using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.Services.Git;

namespace PickleGit.ViewModels
{
    /// <summary>Git LFS, submodules and worktrees.</summary>
    public partial class RepositoryViewModel
    {
        private int _lfsUnpulledCount;
        /// <summary>Count of LFS-tracked files currently sitting as raw pointer text in the working
        /// tree — drives the "Pull LFS objects" suggestion banner and PullLfsObjectsCommand's
        /// CanExecute. 0 hides the banner. See RefreshLfsStatusAsync.</summary>
        public int LfsUnpulledCount
        {
            get => _lfsUnpulledCount;
            private set
            {
                if (Set(ref _lfsUnpulledCount, value))
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task TrackWithLfsAsync(FileChange fc)
        {
            if (fc == null) return;
            var ext = Path.GetExtension(fc.Path);
            var suggested = string.IsNullOrEmpty(ext) ? fc.Path : "*" + ext;
            var pattern = DialogService.Prompt("Track with Git LFS",
                "Glob pattern to track with Git LFS (added to .gitattributes):", suggested);
            if (string.IsNullOrWhiteSpace(pattern)) return;

            if (!await RunCliAsync($"Tracking {pattern} with Git LFS…",
                    $"lfs track {CliGitService.Quote(pattern)}", "Git LFS"))
                return;
            await _git.Executor.RunAsync(() => _git.StageFile(".gitattributes"));
            await LoadWorkingDirAsync();
        }

        /// <summary>Cheap, network-free scan for LFS-tracked files that are still raw pointer text
        /// after a checkout/pull/stash-restore — replaces the old silent, best-effort `git lfs pull`
        /// auto-run (which fetched over the network on every such operation and swallowed failures
        /// into a log line only). `git lfs ls-files --json`'s "checkout" field tells us, per file,
        /// whether the working tree currently holds the real content (true) or just the pointer
        /// (false) — verified against a real repo in both states rather than assumed, since a
        /// same-named "downloaded" field in that JSON turned out to mean something different
        /// (whether it was ever fetched from a remote this session, not whether the working copy is
        /// smudged right now). Populates <see cref="LfsUnpulledCount"/> so the UI can offer a "Pull
        /// LFS objects" button; the actual pull only runs when the user clicks it (see
        /// PullLfsObjectsCommand), which goes through the normal RunCliAsync path and so gets
        /// progress-bar and error-dialog handling for free — no bespoke handling needed here.</summary>
        private async Task RefreshLfsStatusAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable || !RepoUsesLfs())
            {
                LfsUnpulledCount = 0;
                return;
            }
            try
            {
                var result = await _git.Cli.RunAsync("lfs ls-files --json");
                if (!result.Success) { LfsUnpulledCount = 0; return; }
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(result.StdOut);
                int count = 0;
                if (parsed["files"] is Newtonsoft.Json.Linq.JArray files)
                {
                    foreach (var f in files)
                    {
                        var checkoutToken = f["checkout"];
                        if (checkoutToken != null && (bool)checkoutToken == false)
                            count++;
                    }
                }
                LfsUnpulledCount = count;
            }
            catch (Exception ex)
            {
                AppLog.Warn("RefreshLfsStatusAsync failed", ex);
                LfsUnpulledCount = 0;
            }
        }

        /// <summary>Shown right after a checkout/tag-checkout/remote-branch-checkout whose result
        /// left LFS-tracked files as un-smudged pointer text — the passive "Pull LFS objects"
        /// banner (RefreshLfsStatusAsync) only renders inside the Diff/Commit-detail panels, so
        /// it's easy to miss immediately after switching branches if neither panel happens to be
        /// open. This surfaces the same information as an unmissable popup, mirroring how clone
        /// already makes LFS data expectations obvious. Pulling itself still only happens if the
        /// user opts in here (or later via the banner) — never automatically/silently, per the
        /// deliberate move away from the old background auto-pull.</summary>
        private async Task PromptLfsPullAfterCheckoutAsync()
        {
            var count = LfsUnpulledCount;
            var noun = count == 1 ? "file" : "files";
            if (DialogService.Confirm("Git LFS",
                    $"{count} {noun} tracked by Git LFS haven't been downloaded yet for this checkout.",
                    "Pull Now", cancelText: "Later"))
                await PullLfsObjectsAsync();
        }

        /// <summary>Materializes LFS-tracked files that are still raw pointer text using ONLY
        /// already-cached local objects (`git lfs checkout`) — no network access at all, unlike
        /// `git lfs pull`. Checkout (both the CLI path, which runs with GIT_LFS_SKIP_SMUDGE=1, and
        /// the libgit2 fallback, which never invokes git-lfs's smudge filter at all) leaves
        /// LFS-tracked content as raw pointer text, so switching back to a branch whose LFS content
        /// was already pulled on an earlier visit still needs this "finish the checkout" step every
        /// single time — run it silently before ever bothering the user with a prompt, so a
        /// round-trip through already-fetched branches never nags for a pull it doesn't actually
        /// need. Only objects genuinely missing from the local cache still need the explicit,
        /// user-initiated "Pull Now" (see PromptLfsPullAfterCheckoutAsync).
        /// <paramref name="paths"/> restricts the smudge to just those files (glob-matched, but a
        /// literal path is also a valid exact-match glob) instead of scanning/smudging every
        /// LFS-tracked file in the repo — see RefreshLfsStatusForCheckoutAsync. A checkout that
        /// touches thousands of files can produce a path list well past Windows' ~32K
        /// command-line limit (confirmed against a real repo — see chat: a single unbatched
        /// invocation hit 67,623 characters and failed with Win32Exception "The filename or
        /// extension is too long"), so the list is chunked via <see cref="ChunkPathsByLength"/>
        /// into multiple invocations instead of one.</summary>
        private async Task SmudgeLfsFromLocalCacheAsync(IEnumerable<string> paths = null)
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) return;
            try
            {
                if (paths == null) { await _git.Cli.RunAsync("lfs checkout"); return; }
                var list = paths as IReadOnlyCollection<string> ?? paths.ToList();
                if (list.Count == 0) return;
                foreach (var batch in ChunkPathsByLength(list))
                    await _git.Cli.RunAsync("lfs checkout -- " + string.Join(" ", batch.Select(CliGitService.Quote)));
            }
            catch (Exception ex) { AppLog.Warn("SmudgeLfsFromLocalCacheAsync failed", ex); }
        }

        /// <summary>Splits a path list into batches whose joined length stays comfortably under
        /// Windows' ~32,767-character command-line limit — see SmudgeLfsFromLocalCacheAsync and
        /// GetUnpulledLfsPathsAsync, the two callers that turn a path list into a single git-lfs
        /// command-line argument.</summary>
        private const int PathArgBudgetChars = 20000;
        private static IEnumerable<List<string>> ChunkPathsByLength(IReadOnlyCollection<string> paths)
        {
            var batch = new List<string>();
            int len = 0;
            foreach (var p in paths)
            {
                var addLen = p.Length + 1;
                if (batch.Count > 0 && len + addLen > PathArgBudgetChars)
                {
                    yield return batch;
                    batch = new List<string>();
                    len = 0;
                }
                batch.Add(p);
                len += addLen;
            }
            if (batch.Count > 0) yield return batch;
        }

        /// <summary>Checkout-scoped LFS follow-up. Earlier versions of this detected still-pointer
        /// files by asking git-lfs itself (`git lfs ls-files --json`, scoped to the checkout's
        /// changed paths) — real-repo benchmarking (34K files, LFS-heavy — see chat) showed that
        /// alone cost ~35-50s (two git-lfs subprocess round-trips over ~1000 changed paths, chunked),
        /// which is *more* than git.exe's entire native checkout of the same branches (~17s). The
        /// fix isn't a smarter git-lfs query — it's not asking git-lfs at all when we don't need to.
        /// Checkout (GitService.Checkout et al.) now runs through git.exe with its real smudge
        /// filter active, so it already materializes every LFS object it can from the local cache
        /// as part of the checkout itself; this method's job shrinks to "did anything NOT get
        /// smudged" (offline, object missing upstream, or the libgit2 fallback path, which never
        /// smudges at all). That's answered with a plain local file-header read — a real git-lfs
        /// pointer file's content always starts with the fixed, documented signature checked by
        /// LooksLikeLfsPointer (confirmed against a real pointer file — see chat) — instead of a
        /// git-lfs process call, so the common "checkout already smudged everything" case costs a
        /// handful of tiny file reads, not a single subprocess spawn.
        /// LfsUnpulledCount (the repo-wide total the persistent "Pull LFS objects" banner reads) is
        /// adjusted by the before/after delta among just the changed paths rather than re-deriving
        /// the whole-repo total from scratch — an approximation (it can't know those paths' state
        /// under the OLD tree), same as before; drift self-corrects at the next full rescan.</summary>
        private async Task RefreshLfsStatusForCheckoutAsync(string preHeadSha)
        {
            if (!RepoUsesLfs())
            {
                LfsUnpulledCount = 0;
                return;
            }
            if (string.IsNullOrEmpty(preHeadSha)) { await RefreshLfsStatusAsync(); return; }
            try
            {
                var postHeadSha = await _git.Executor.RunAsync(() => _git.GetHeadSha());
                if (string.IsNullOrEmpty(postHeadSha) ||
                    string.Equals(preHeadSha, postHeadSha, StringComparison.Ordinal))
                    return; // HEAD didn't move (e.g. re-checking out the already-current branch) — nothing to re-check.

                var changedPaths = await _git.Executor.RunAsync(
                    () => _git.GetChangedFiles(preHeadSha, postHeadSha).Select(f => f.Path).ToList());
                if (changedPaths.Count == 0) return;

                var workDir = _git.WorkingDirectory;
                var stillPointer = await _git.Executor.RunAsync(() =>
                    changedPaths.Where(p => LooksLikeLfsPointer(Path.Combine(workDir, p))).ToList());
                if (stillPointer.Count == 0) return;

                if (_git.Cli == null || !_git.Cli.IsAvailable)
                {
                    // No git.exe at all — nothing can smudge these; just report the count.
                    LfsUnpulledCount += stillPointer.Count;
                    return;
                }

                await SmudgeLfsFromLocalCacheAsync(stillPointer);
                var stillUnresolved = await _git.Executor.RunAsync(() =>
                    stillPointer.Where(p => LooksLikeLfsPointer(Path.Combine(workDir, p))).ToList());
                AppLog.Info($"RefreshLfsStatusForCheckoutAsync: {changedPaths.Count} changed paths, " +
                    $"{stillPointer.Count} still pointer before local-cache smudge, " +
                    $"{stillUnresolved.Count} still pointer after, LfsUnpulledCount {LfsUnpulledCount} -> " +
                    $"{Math.Max(0, LfsUnpulledCount - (stillPointer.Count - stillUnresolved.Count))}");
                LfsUnpulledCount = Math.Max(0, LfsUnpulledCount - (stillPointer.Count - stillUnresolved.Count));
            }
            catch (Exception ex)
            {
                AppLog.Warn("RefreshLfsStatusForCheckoutAsync failed, falling back to a full rescan", ex);
                await RefreshLfsStatusAsync();
            }
        }

        /// <summary>True when <paramref name="absolutePath"/>'s content starts with the git-lfs
        /// pointer-file signature ("version https://git-lfs.github.com/spec/v1") — confirmed
        /// byte-for-byte against a real pointer file (see chat), not assumed from memory. Reads
        /// only the first few dozen bytes, so this is a cheap local check even across hundreds of
        /// files — no git-lfs process involved at all.</summary>
        private static bool LooksLikeLfsPointer(string absolutePath)
        {
            const string Signature = "version https://git-lfs.github.com/spec/v1";
            try
            {
                using (var stream = File.OpenRead(absolutePath))
                {
                    var buffer = new byte[Signature.Length];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read < Signature.Length) return false;
                    return Encoding.ASCII.GetString(buffer, 0, read) == Signature;
                }
            }
            catch (IOException) { return false; } // deleted/renamed/locked — not a pointer we can fix here
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Runs the actual `git lfs pull` — only ever user-initiated (the "Pull LFS
        /// objects" banner button), unlike the old automatic background pull this replaced.</summary>
        private async Task PullLfsObjectsAsync()
        {
            if (!await RunCliAsync("Pulling LFS objects…", "lfs pull", "Git LFS")) return;
            await RefreshLfsStatusAsync();
            // A commit's diff always shows the pointer (it diffs git's own object store, which
            // never holds anything but the pointer for an LFS path — pulling only affects the
            // working tree) so that banner is expected to persist; reloading here just clears the
            // "Pull LFS objects" button/suggestion state once there's nothing left to pull, and
            // picks up real working-tree content for the working-directory view.
            if (ShowWorkingDir) await LoadWorkingDirAsync();
            else if (DetailCommit != null) LoadCommitDetail(DetailCommit.Sha);
        }

        /// <summary>Cheap check for whether this repo declares any Git LFS filters, so the LFS fetch
        /// step above can skip entirely (no process spawn, no network) for the common non-LFS repo.</summary>
        private bool RepoUsesLfs()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(RepoPath, ".gitattributes"),
                    Path.Combine(RepoPath, ".git", "info", "attributes"),
                };
                foreach (var p in candidates)
                {
                    if (File.Exists(p) &&
                        File.ReadAllText(p).IndexOf("filter=lfs", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { /* best-effort detection only */ }
            return false;
        }

        // ── Submodules ───────────────────────────────────────────────────────

        public async Task LoadSubmodulesAsync()
        {
            if (!_git.IsOpen) return;
            var list = await _git.Executor.RunAsync(() => _git.GetSubmodules());
            Submodules = new ObservableCollection<SubmoduleInfo>(list);
        }

        private async Task InitAllSubmodulesAsync()
        {
            if (await RunCliAsync("Initializing submodules…", "submodule update --init --recursive", "Submodules"))
                await LoadSubmodulesAsync();
        }

        private async Task UpdateSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (await RunCliAsync($"Updating {sm.Name}…",
                    $"submodule update --init -- {CliGitService.Quote(sm.Path)}", "Submodule update"))
                await LoadSubmodulesAsync();
        }

        /// <summary>`git submodule add &lt;url&gt; [path]` — path defaults to the repo name.</summary>
        private async Task AddSubmoduleAsync()
        {
            var url = DialogService.Prompt("Add Submodule", "Repository URL of the submodule:", "", "Next");
            if (string.IsNullOrWhiteSpace(url)) return;
            var defaultPath = Path.GetFileNameWithoutExtension(url.TrimEnd('/'));
            var path = DialogService.Prompt("Add Submodule", "Path inside this repository:", defaultPath ?? "", "Add");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (await RunCliAsync($"Adding submodule {path}…",
                    $"submodule add {CliGitService.Quote(url.Trim())} {CliGitService.Quote(path.Trim())}",
                    "Add submodule"))
            {
                await LoadSubmodulesAsync();
                await LoadWorkingDirAsync();
                await RefreshAsync();
            }
        }

        /// <summary>`git submodule sync` then update — re-reads the URL from .gitmodules.</summary>
        private async Task SyncSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (!await RunCliAsync($"Syncing {sm.Name}…",
                    $"submodule sync -- {CliGitService.Quote(sm.Path)}", "Submodule sync"))
                return;
            if (await RunCliAsync($"Updating {sm.Name}…",
                    $"submodule update --init -- {CliGitService.Quote(sm.Path)}", "Submodule update"))
                await LoadSubmodulesAsync();
        }

        private async Task DeinitSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (!DialogService.Confirm("Deinit Submodule",
                    $"Deinitialize '{sm.Name}'?\n\nIts working directory content is removed (the entry stays in .gitmodules; local changes inside it are lost).",
                    "Deinit", danger: true))
                return;
            if (await RunCliAsync($"Deinitializing {sm.Name}…",
                    $"submodule deinit -f -- {CliGitService.Quote(sm.Path)}", "Submodule deinit"))
                await LoadSubmodulesAsync();
        }

        // ── Worktrees ────────────────────────────────────────────────────────

        public async Task LoadWorktreesAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) { Worktrees = new ObservableCollection<WorktreeInfo>(); return; }
            var result = await _git.Cli.RunAsync("worktree list --porcelain");
            Worktrees = new ObservableCollection<WorktreeInfo>(result.Success
                ? WorktreeService.ParsePorcelain(result.StdOut)
                : Enumerable.Empty<WorktreeInfo>());
        }

        private async Task AddWorktreeAsync()
        {
            var branch = DialogService.Prompt("Add Worktree", "Branch name for the new worktree:", "");
            if (string.IsNullOrWhiteSpace(branch)) return;
            var parentDir = Path.GetDirectoryName(RepoPath.TrimEnd('\\', '/'));
            var suggested = Path.Combine(parentDir ?? RepoPath, $"{RepoName}-{branch.Replace('/', '-')}");
            var path = DialogService.Prompt("Add Worktree", "Path for the new worktree:", suggested);
            if (string.IsNullOrWhiteSpace(path)) return;

            var branchExists = LocalBranches.Any(b => string.Equals(b.Name, branch, StringComparison.OrdinalIgnoreCase));
            var args = WorktreeService.BuildAddArgs(path, branch, branchExists);

            if (await RunCliAsync($"Adding worktree at {path}…", args, "Add worktree"))
                await LoadWorktreesAsync();
        }

        private async Task RemoveWorktreeAsync(WorktreeInfo wt)
        {
            if (wt == null || wt.IsMain) return;
            if (!DialogService.Confirm("Remove Worktree",
                    $"Remove the worktree at '{wt.Path}'? This deletes its working directory.", "Remove"))
                return;
            if (await RunCliAsync($"Removing worktree {wt.Name}…",
                    WorktreeService.BuildRemoveArgs(wt.Path), "Remove worktree"))
                await LoadWorktreesAsync();
        }
    }
}
