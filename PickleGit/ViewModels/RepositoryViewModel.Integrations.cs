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
        /// `git lfs pull`. libgit2 checkout (used by every branch/tag/commit checkout in this app,
        /// via GitService.Checkout et al.) never invokes git-lfs's smudge filter, so switching back
        /// to a branch whose LFS content was already pulled on an earlier visit still needs this
        /// "finish the checkout" step every single time — run it silently before ever bothering the
        /// user with a prompt, so a round-trip through already-fetched branches never nags for a
        /// pull it doesn't actually need. Only objects genuinely missing from the local cache still
        /// need the explicit, user-initiated "Pull Now" (see PromptLfsPullAfterCheckoutAsync).</summary>
        private async Task SmudgeLfsFromLocalCacheAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) return;
            try { await _git.Cli.RunAsync("lfs checkout"); }
            catch (Exception ex) { AppLog.Warn("SmudgeLfsFromLocalCacheAsync failed", ex); }
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
