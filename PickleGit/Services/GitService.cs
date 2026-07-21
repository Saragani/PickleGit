using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using PickleGit.Models;
using PickleGit.Services.Git;

namespace PickleGit.Services
{
    /// <summary>Fast-forward behavior for a merge. Squash merges don't fit libgit2's merge API
    /// and go through the CLI (`git merge --squash`) instead — see RepositoryViewModel.</summary>
    public enum GitMergeMode { Default, NoFastForward, FastForwardOnly }

    public class GitService : IDisposable
    {
        private Repository _repo;
        private string _repoPath;

        /// <summary>Ahead/behind is an uncached LibGit2Sharp divergence walk (git_graph_ahead_behind)
        /// from each local branch's tip to its upstream's tip — expensive for long-diverged branches,
        /// and GetBranches() runs on every refresh (including background watcher/timer refreshes that
        /// touch nothing branch-related). Cache per branch keyed by both tip SHAs so it's only
        /// recomputed when the branch or its upstream actually moved.</summary>
        private readonly Dictionary<string, (string LocalSha, string UpstreamSha, int Ahead, int Behind)> _aheadBehindCache
            = new Dictionary<string, (string, string, int, int)>();

        public string RepoPath => _repoPath;
        public bool IsOpen => _repo != null;

        /// <summary>Serializes every git operation (libgit2 + git.exe) for this repo.</summary>
        public GitExecutor Executor { get; } = new GitExecutor();

        /// <summary>git.exe backend for operations LibGit2Sharp cannot do. Null until a repo is open.</summary>
        public CliGitService Cli { get; private set; }

        public string WorkingDirectory => _repo?.Info.WorkingDirectory;
        public string GitDirectory => _repo?.Info.Path;

        // ── Repository lifecycle ────────────────────────────────────────────

        public bool TryOpen(string path)
        {
            try
            {
                var root = Repository.Discover(path);
                if (root == null) return false;
                _repo?.Dispose();
                _repo = new Repository(root);
                _repoPath = root;
                Cli = new CliGitService(_repo.Info.WorkingDirectory ?? path);
                _aheadBehindCache.Clear();
                return true;
            }
            catch (Exception ex) { AppLog.Warn($"Failed to open repository at '{path}'", ex); return false; }
        }

        /// <summary>
        /// Disposes and reopens the underlying Repository handle. Must be called
        /// after any git.exe operation that mutates refs or the index, because
        /// libgit2 caches ref state and would otherwise serve stale data.
        /// </summary>
        public void Reopen()
        {
            if (_repoPath == null) return;
            _repo?.Dispose();
            _repo = new Repository(_repoPath);
        }

        public static string Clone(string url, string localPath,
            string username, string password,
            IProgress<string> progress = null,
            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken),
            string branch = null)
        {
            var opts = new CloneOptions
            {
                CredentialsProvider = BuildCredHandler(username, password),
                OnProgress = msg => { progress?.Report(msg); return !ct.IsCancellationRequested; },
                OnTransferProgress = tp =>
                {
                    if (tp.TotalObjects > 0)
                        progress?.Report($"Receiving objects: {tp.ReceivedObjects * 100 / tp.TotalObjects}% ({tp.ReceivedObjects}/{tp.TotalObjects})");
                    return !ct.IsCancellationRequested;
                }
            };
            if (!string.IsNullOrWhiteSpace(branch)) opts.BranchName = branch.Trim();
            return Repository.Clone(url, localPath, opts);
        }

        public static void Init(string path) => Repository.Init(path);

        // ── Commits ──────────────────────────────────────────────────────────

        /// <summary>
        /// Single topological walk over all branch tips. While walking, each
        /// commit's <see cref="CommitInfo.RefMask"/> is computed by propagating
        /// branch-tip bits from children to parents (topological order guarantees
        /// children are visited first), so branch-membership queries — e.g. the
        /// smart-visibility filter — need no second walk. The current branch
        /// (or detached HEAD) always owns bit 0.
        /// </summary>
        public CommitHistory GetHistory(int maxCount = 10000, BisectState bisectState = null)
        {
            EnsureOpen();
            var history = new CommitHistory();

            // Seed mask bits at the branch tips. Bit 0 = HEAD/current branch;
            // remaining branches get bits 1..63. Branches past 64 get no bit —
            // only per-branch filtering is affected, and bit 0 is always exact.
            var pendingMasks = new Dictionary<string, ulong>();
            var headTip = _repo.Head?.Tip;
            if (headTip != null)
                pendingMasks[headTip.Sha] = 1UL;

            int nextBit = 1;
            var branchBits = new Dictionary<string, ulong>();
            foreach (var b in _repo.Branches)
            {
                var tipSha = b.Tip?.Sha;
                if (tipSha == null) continue;
                ulong bit;
                if (b.IsCurrentRepositoryHead)
                {
                    bit = 1UL;
                }
                else if (nextBit < 64)
                {
                    bit = 1UL << nextBit++;
                }
                else
                {
                    continue;
                }
                branchBits[b.FriendlyName] = bit;
                pendingMasks.TryGetValue(tipSha, out var existing);
                pendingMasks[tipSha] = existing | bit;
            }
            history.BranchMasks = branchBits;

            var tips = _repo.Branches
                .Select(b => (object)b.Tip)
                .Where(t => t != null)
                .ToList();
            if (headTip != null)
                tips.Add(headTip);

            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = tips
            };

            var refMap = BuildRefMap();

            int count = 0;
            foreach (var commit in _repo.Commits.QueryBy(filter))
            {
                if (count++ >= maxCount) { history.ReachedLimit = true; break; }
                var info = MapCommit(commit, refMap);
                if (bisectState != null && bisectState.InProgress)
                    info.BisectMark = ClassifyBisectMark(commit.Sha, bisectState);
                if (pendingMasks.TryGetValue(commit.Sha, out var mask))
                {
                    pendingMasks.Remove(commit.Sha);
                    info.RefMask = mask;
                    if (mask != 0)
                    {
                        foreach (var p in commit.Parents)
                        {
                            pendingMasks.TryGetValue(p.Sha, out var pm);
                            pendingMasks[p.Sha] = pm | mask;
                        }
                    }
                }
                history.Commits.Add(info);
            }
            return history;
        }

        private static BisectMark ClassifyBisectMark(string sha, BisectState s)
        {
            if (sha == s.CurrentSha) return BisectMark.Current;
            if (sha == s.BadSha) return BisectMark.Bad;
            if (s.GoodShas.Contains(sha)) return BisectMark.Good;
            if (s.SkippedShas.Contains(sha)) return BisectMark.Skip;
            return BisectMark.None;
        }

        private Dictionary<string, List<string>> BuildRefMap()
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var branch in _repo.Branches)
            {
                if (branch.Tip == null) continue;
                var sha = branch.Tip.Sha;
                if (!map.ContainsKey(sha)) map[sha] = new List<string>();
                var label = branch.IsRemote ? branch.FriendlyName : branch.FriendlyName;
                if (branch.IsCurrentRepositoryHead) label = "HEAD -> " + label;
                map[sha].Add(label);
            }
            foreach (var tag in _repo.Tags)
            {
                var sha = (tag.Target as Commit)?.Sha ?? tag.Target?.Sha;
                if (sha == null) continue;
                if (!map.ContainsKey(sha)) map[sha] = new List<string>();
                map[sha].Add("tag: " + tag.FriendlyName);
            }
            return map;
        }

        private static CommitInfo MapCommit(Commit c, Dictionary<string, List<string>> refMap)
        {
            refMap.TryGetValue(c.Sha, out var refs);
            return new CommitInfo
            {
                Sha = c.Sha,
                Message = c.Message,
                AuthorName = c.Author.Name,
                AuthorEmail = c.Author.Email,
                AuthorDate = c.Author.When,
                CommitterName = c.Committer.Name,
                CommitterEmail = c.Committer.Email,
                CommitterDate = c.Committer.When,
                ParentShas = c.Parents.Select(p => p.Sha).ToList(),
                Refs = refs ?? new List<string>()
            };
        }

        // ── Branches ──────────────────────────────────────────────────────────

        public List<BranchInfo> GetBranches()
        {
            EnsureOpen();
            var list = new List<BranchInfo>();
            var remoteNames = new HashSet<string>(
                _repo.Network.Remotes.Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var b in _repo.Branches.OrderBy(b => b.FriendlyName))
            {
                if (b.IsRemote && !remoteNames.Contains(b.RemoteName ?? string.Empty))
                    continue;

                var info = new BranchInfo
                {
                    Name = b.FriendlyName,
                    FullName = b.CanonicalName,
                    IsRemote = b.IsRemote,
                    IsHead = b.IsCurrentRepositoryHead,
                    RemoteName = b.RemoteName,
                    TipSha = b.Tip?.Sha
                };
                if (b.TrackingDetails != null && !b.IsRemote)
                {
                    info.TrackedBranchName = b.TrackedBranch?.FriendlyName;
                    var localSha = b.Tip?.Sha;
                    var upstreamSha = b.TrackedBranch?.Tip?.Sha;
                    if (_aheadBehindCache.TryGetValue(b.CanonicalName, out var cached)
                        && cached.LocalSha == localSha && cached.UpstreamSha == upstreamSha)
                    {
                        info.AheadBy = cached.Ahead;
                        info.BehindBy = cached.Behind;
                    }
                    else
                    {
                        var ahead = b.TrackingDetails.AheadBy ?? 0;
                        var behind = b.TrackingDetails.BehindBy ?? 0;
                        info.AheadBy = ahead;
                        info.BehindBy = behind;
                        _aheadBehindCache[b.CanonicalName] = (localSha, upstreamSha, ahead, behind);
                    }
                }
                list.Add(info);
            }
            return list;
        }

        public void CreateBranch(string name, string startPoint = null)
        {
            EnsureOpen();
            var target = startPoint != null
                ? _repo.Lookup<Commit>(startPoint)
                : _repo.Head.Tip;
            _repo.CreateBranch(name, target);
        }

        public void Checkout(string branchName)
        {
            EnsureOpen();
            var branch = _repo.Branches[branchName];
            if (branch == null) throw new InvalidOperationException($"Branch '{branchName}' not found.");
            Commands.Checkout(_repo, branch);
        }

        public void DeleteBranch(string branchName, bool force = false)
        {
            EnsureOpen();
            var branch = _repo.Branches[branchName];
            if (branch == null) throw new InvalidOperationException($"Branch '{branchName}' not found.");
            if (!force && !IsBranchMergedInternal(branch))
                throw new InvalidOperationException(
                    $"Branch '{branchName}' is not fully merged into the current branch.");
            _repo.Branches.Remove(branch);
        }

        /// <summary>True when the branch tip is reachable from HEAD (safe to delete).</summary>
        public bool IsBranchMerged(string branchName)
        {
            EnsureOpen();
            var branch = _repo.Branches[branchName];
            return branch == null || IsBranchMergedInternal(branch);
        }

        private bool IsBranchMergedInternal(Branch branch)
        {
            var tip = branch?.Tip;
            var head = _repo.Head?.Tip;
            if (tip == null) return true;
            if (head == null) return false;
            var mergeBase = _repo.ObjectDatabase.FindMergeBase(head, tip);
            return mergeBase?.Sha == tip.Sha;
        }

        public void RenameBranch(string oldName, string newName)
        {
            EnsureOpen();
            var branch = _repo.Branches[oldName];
            if (branch == null) throw new InvalidOperationException($"Branch '{oldName}' not found.");
            _repo.Branches.Rename(branch, newName);
        }

        /// <summary>Creates a local tracking branch for a remote branch (if needed) and checks it out.</summary>
        public void CheckoutRemoteBranch(string remoteBranchName)
        {
            EnsureOpen();
            var remoteBranch = _repo.Branches[remoteBranchName];
            if (remoteBranch == null || !remoteBranch.IsRemote)
                throw new InvalidOperationException($"Remote branch '{remoteBranchName}' not found.");

            var idx = remoteBranchName.IndexOf('/');
            var localName = idx >= 0 ? remoteBranchName.Substring(idx + 1) : remoteBranchName;

            var local = _repo.Branches[localName];
            if (local == null || local.IsRemote)
            {
                local = _repo.CreateBranch(localName, remoteBranch.Tip);
                _repo.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
            }
            Commands.Checkout(_repo, local);
        }

        /// <summary>Checks out a commit directly (detached HEAD).</summary>
        public void CheckoutCommit(string sha)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(sha)
                ?? throw new InvalidOperationException($"Commit {sha} not found.");
            Commands.Checkout(_repo, commit);
        }

        public void ResetTo(string sha, string mode)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(sha)
                ?? throw new InvalidOperationException($"Commit {sha} not found.");
            ResetMode resetMode;
            switch (mode)
            {
                case "soft": resetMode = ResetMode.Soft; break;
                case "hard": resetMode = ResetMode.Hard; break;
                default:     resetMode = ResetMode.Mixed; break;
            }
            _repo.Reset(resetMode, commit);
        }

        public void Revert(string sha)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(sha)
                ?? throw new InvalidOperationException($"Commit {sha} not found.");
            var sig = _repo.Config.BuildSignature(DateTimeOffset.Now)
                      ?? new Signature("PickleGit", "picklegit@localhost", DateTimeOffset.Now);
            var result = _repo.Revert(commit, sig);
            if (result.Status == RevertStatus.NothingToRevert)
                throw new InvalidOperationException("Nothing to revert — the changes are already absent.");
            // Conflicts are left in the index/working tree and surfaced via GetConflictState().
        }

        public MergeResult Merge(string branchName, string authorName, string authorEmail,
            GitMergeMode mode = GitMergeMode.Default)
        {
            EnsureOpen();
            var branch = _repo.Branches[branchName];
            if (branch == null) throw new InvalidOperationException($"Branch '{branchName}' not found.");
            var sig = new Signature(authorName, authorEmail, DateTimeOffset.Now);
            var opts = new MergeOptions();
            switch (mode)
            {
                case GitMergeMode.NoFastForward: opts.FastForwardStrategy = FastForwardStrategy.NoFastForward; break;
                case GitMergeMode.FastForwardOnly: opts.FastForwardStrategy = FastForwardStrategy.FastForwardOnly; break;
            }
            return _repo.Merge(branch, sig, opts);
        }

        // ── Staging & Commits ─────────────────────────────────────────────────

        public List<FileChange> GetWorkingDirectoryStatus()
        {
            EnsureOpen();
            var status = _repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                IncludeIgnored = false,
            });
            var result = new List<FileChange>();
            foreach (var entry in status)
            {
                // A file staged and then modified again has both index AND workdir state — emit both
                var s = BuildFileChange(entry, staged: true);
                if (s != null) result.Add(s);
                var w = BuildFileChange(entry, staged: false);
                if (w != null) result.Add(w);
            }

            if (result.Any(f => f.Kind == FileChangeKind.Conflicted))
            {
                var conflictsByPath = _repo.Index.Conflicts
                    .Select(c => new { Path = c.Ours?.Path ?? c.Theirs?.Path ?? c.Ancestor?.Path, Conflict = c })
                    .Where(x => x.Path != null)
                    .ToDictionary(x => x.Path, x => x.Conflict);

                foreach (var f in result.Where(f => f.Kind == FileChangeKind.Conflicted))
                {
                    if (!conflictsByPath.TryGetValue(f.Path, out var conflict)) continue;
                    f.OursMissing = conflict.Ours == null;
                    f.TheirsMissing = conflict.Theirs == null;
                }
            }

            return result;
        }

        private static FileChange BuildFileChange(StatusEntry entry, bool staged)
        {
            var s = entry.State;
            if (s == FileStatus.Ignored) return null;

            FileChangeKind kind;
            if (staged)
            {
                if      ((s & FileStatus.NewInIndex)       != 0) kind = FileChangeKind.Added;
                else if ((s & FileStatus.ModifiedInIndex)  != 0) kind = FileChangeKind.Modified;
                else if ((s & FileStatus.DeletedFromIndex) != 0) kind = FileChangeKind.Deleted;
                else if ((s & FileStatus.RenamedInIndex)   != 0) kind = FileChangeKind.Renamed;
                else return null;
            }
            else
            {
                if      ((s & FileStatus.NewInWorkdir)       != 0) kind = FileChangeKind.Untracked;
                else if ((s & FileStatus.ModifiedInWorkdir)  != 0) kind = FileChangeKind.Modified;
                else if ((s & FileStatus.DeletedFromWorkdir) != 0) kind = FileChangeKind.Deleted;
                else if ((s & FileStatus.RenamedInWorkdir)   != 0) kind = FileChangeKind.Renamed;
                else if ((s & FileStatus.Conflicted)         != 0) kind = FileChangeKind.Conflicted;
                else return null;
            }

            return new FileChange
            {
                Path = entry.FilePath,
                OldPath = staged
                    ? entry.HeadToIndexRenameDetails?.OldFilePath
                    : entry.IndexToWorkDirRenameDetails?.OldFilePath,
                Kind = kind,
                IsStaged = staged
            };
        }

        public void StageFile(string filePath)
        {
            EnsureOpen();
            Commands.Stage(_repo, filePath);
        }

        public void UnstageFile(string filePath)
        {
            EnsureOpen();
            Commands.Unstage(_repo, filePath);
        }

        public void StageFiles(IEnumerable<string> filePaths)
        {
            EnsureOpen();
            Commands.Stage(_repo, filePaths);
        }

        public void UnstageFiles(IEnumerable<string> filePaths)
        {
            EnsureOpen();
            Commands.Unstage(_repo, filePaths);
        }

        public void StageAll()
        {
            EnsureOpen();
            Commands.Stage(_repo, "*");
        }

        public void UnstageAll()
        {
            EnsureOpen();
            Commands.Unstage(_repo, "*");
        }

        /// <summary>Reads a boolean git config value (repo, then global, then system scope).</summary>
        public bool GetConfigBool(string key, bool defaultValue = false)
        {
            EnsureOpen();
            try { return _repo.Config.Get<bool>(key)?.Value ?? defaultValue; }
            catch { return defaultValue; }
        }

        /// <summary>Reads a string git config value (repo, then global, then system scope).</summary>
        public string GetConfigString(string key, string defaultValue = null)
        {
            EnsureOpen();
            try { return _repo.Config.Get<string>(key)?.Value ?? defaultValue; }
            catch { return defaultValue; }
        }

        /// <summary>Resolves and reads the repo's commit message template (`commit.template`), if configured.</summary>
        public string GetCommitTemplate()
        {
            var path = GetConfigString("commit.template");
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (path.StartsWith("~/") || path.StartsWith("~\\"))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(_repo.Info.WorkingDirectory, path);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch { return null; }
        }

        public Commit CreateCommit(string message, string authorName, string authorEmail,
            bool amend = false, bool signOff = false)
        {
            EnsureOpen();
            var sig = new Signature(authorName, authorEmail, DateTimeOffset.Now);
            if (signOff)
            {
                var trailer = $"Signed-off-by: {authorName} <{authorEmail}>";
                if (message.IndexOf(trailer, StringComparison.Ordinal) < 0)
                    message = message.TrimEnd() + "\n\n" + trailer;
            }
            return _repo.Commit(message, sig, sig,
                new CommitOptions { AmendPreviousCommit = amend });
        }

        public string GetHeadCommitMessage()
        {
            EnsureOpen();
            return _repo.Head?.Tip?.Message ?? string.Empty;
        }

        // ── Diff ──────────────────────────────────────────────────────────────

        /// <summary>Default hunk context (matches git's own default of 3 lines). "Show entire file"
        /// passes a very large value instead so hunks merge into (near-)whole-file context — see
        /// RepositoryViewModel.ShowEntireFile.</summary>
        public const int DefaultContextLines = 3;
        public const int FullFileContextLines = 1_000_000;

        public FileDiffResult GetFileDiff(string commitSha, string filePath, int contextLines = DefaultContextLines)
        {
            EnsureOpen();
            var compareOptions = new CompareOptions { ContextLines = contextLines };
            Patch patch;
            if (commitSha == null)
            {
                patch = _repo.Diff.Compare<Patch>(
                    _repo.Head?.Tip?.Tree,
                    DiffTargets.WorkingDirectory,
                    new[] { filePath }, null, compareOptions);
            }
            else
            {
                var commit = _repo.Lookup<Commit>(commitSha);
                if (commit == null) return new FileDiffResult();
                var parent = commit.Parents.FirstOrDefault();
                patch = _repo.Diff.Compare<Patch>(
                    parent?.Tree, commit.Tree, new[] { filePath }, null, compareOptions);
            }
            var result = ParsePatchEntries(patch);
            Highlighting.SyntaxHighlighter.Apply(result.Hunks, filePath);
            return result;
        }

        /// <summary>Diffs one file between two arbitrary commits/branch tips — a direct two-dot
        /// tree comparison (not merge-base-relative), i.e. exactly what "git diff shaA shaB -- path"
        /// shows. Used by the branch "Diff against current branch" comparison view.</summary>
        public FileDiffResult GetFileDiff(string shaA, string shaB, string filePath, int contextLines = DefaultContextLines)
        {
            EnsureOpen();
            var commitA = _repo.Lookup<Commit>(shaA);
            var commitB = _repo.Lookup<Commit>(shaB);
            if (commitA == null || commitB == null) return new FileDiffResult();
            var patch = _repo.Diff.Compare<Patch>(commitA.Tree, commitB.Tree, new[] { filePath },
                null, new CompareOptions { ContextLines = contextLines });
            var result = ParsePatchEntries(patch);
            Highlighting.SyntaxHighlighter.Apply(result.Hunks, filePath);
            return result;
        }

        /// <summary>Unstaged changes for one file — index vs working directory (what "git diff &lt;path&gt;" shows).</summary>
        public FileDiffResult GetUnstagedFileDiff(string filePath, int contextLines = DefaultContextLines)
        {
            EnsureOpen();
            var patch = _repo.Diff.Compare<Patch>(new[] { filePath }, includeUntracked: true,
                explicitPathsOptions: null, compareOptions: new CompareOptions { ContextLines = contextLines });
            var result = ParsePatchEntries(patch);
            Highlighting.SyntaxHighlighter.Apply(result.Hunks, filePath);
            return result;
        }

        /// <summary>Staged changes for one file — HEAD vs index (what "git diff --cached &lt;path&gt;" shows).</summary>
        public FileDiffResult GetStagedFileDiff(string filePath, int contextLines = DefaultContextLines)
        {
            EnsureOpen();
            var patch = _repo.Diff.Compare<Patch>(_repo.Head?.Tip?.Tree, DiffTargets.Index, new[] { filePath },
                null, new CompareOptions { ContextLines = contextLines });
            var result = ParsePatchEntries(patch);
            Highlighting.SyntaxHighlighter.Apply(result.Hunks, filePath);
            return result;
        }

        /// <summary>Whitespace-ignoring diff via `git diff -w` — libgit2 0.27 exposes no
        /// whitespace option, so this path requires git.exe. Runs on the executor thread,
        /// where blocking on the CLI task is the established pattern.</summary>
        public FileDiffResult GetFileDiffIgnoreWhitespace(string commitSha, string filePath, bool staged, int contextLines = DefaultContextLines)
        {
            EnsureOpen();
            if (Cli == null || !Cli.IsAvailable)
                throw new InvalidOperationException("Ignoring whitespace requires git.exe (not found).");

            string args;
            var quoted = Git.CliGitService.Quote(filePath);
            var uFlag = $"-U{contextLines}";
            if (commitSha == null)
                args = staged ? $"diff --cached -w {uFlag} -- {quoted}" : $"diff -w {uFlag} -- {quoted}";
            else
            {
                var commit = _repo.Lookup<Commit>(commitSha);
                var parent = commit?.Parents.FirstOrDefault();
                args = parent != null
                    ? $"diff -w {uFlag} {parent.Sha} {commitSha} -- {quoted}"
                    : $"show --format= -w {uFlag} {commitSha} -- {quoted}";
            }
            var cliResult = Cli.RunCheckedAsync(args).GetAwaiter().GetResult();
            var result = new FileDiffResult { Hunks = ParsePatch(cliResult.StdOut) };
            if (result.Hunks.Count == 0 &&
                cliResult.StdOut.IndexOf("Binary files ", StringComparison.Ordinal) >= 0)
                result.IsBinary = true;
            foreach (var hunk in result.Hunks)
                ComputeWordDiffsForHunk(hunk);
            Highlighting.SyntaxHighlighter.Apply(result.Hunks, filePath);
            return result;
        }

        private static FileDiffResult ParsePatchEntries(Patch patch)
        {
            var result = new FileDiffResult();
            if (patch == null) return result;
            foreach (var entry in patch)
            {
                if (entry.IsBinaryComparison) result.IsBinary = true;
                result.Hunks.AddRange(ParsePatch(entry.Patch));
            }
            foreach (var hunk in result.Hunks)
                ComputeWordDiffsForHunk(hunk);
            // A binary comparison that somehow produced hunks (e.g. forced text) is shown as text
            if (result.Hunks.Count > 0) result.IsBinary = false;
            return result;
        }

        private static List<DiffHunk> ParsePatch(string patchText)
        {
            var hunks = new List<DiffHunk>();
            if (string.IsNullOrEmpty(patchText)) return hunks;

            DiffHunk current = null;
            int oldLine = 0, newLine = 0;

            // Split() on a string ending with '\n' yields a trailing "" element that isn't real
            // patch data — without dropping it, it gets stored as a bogus empty context line on
            // the last hunk, corrupting its line count for hunk/line staging (git apply then
            // rejects the whole patch with "patch does not apply").
            var rawLines = patchText.Split('\n');
            var lineCount = rawLines.Length;
            if (lineCount > 0 && rawLines[lineCount - 1].Length == 0) lineCount--;

            for (int i = 0; i < lineCount; i++)
            {
                var line = rawLines[i].TrimEnd('\r');
                if (line.StartsWith("@@"))
                {
                    current = new DiffHunk { Header = line };
                    hunks.Add(current);
                    // parse @@ -a,b +c,d @@
                    var parts = line.Split(' ');
                    if (parts.Length >= 3)
                    {
                        var oldPart = parts[1].TrimStart('-').Split(',');
                        var newPart = parts[2].TrimStart('+').Split(',');
                        int.TryParse(oldPart[0], out oldLine);
                        int.TryParse(newPart[0], out newLine);
                        current.OldStart = oldLine;
                        current.NewStart = newLine;
                    }
                    continue;
                }
                if (current == null) continue;
                if (line.StartsWith("+") && !line.StartsWith("+++"))
                    current.Lines.Add(new DiffLine { Content = line, Kind = DiffLineKind.Added, NewLineNumber = newLine++ });
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                    current.Lines.Add(new DiffLine { Content = line, Kind = DiffLineKind.Deleted, OldLineNumber = oldLine++ });
                else if (line.StartsWith("\\"))
                    // "\ No newline at end of file" — must be preserved, not dropped, or a patch
                    // rebuilt from these Lines for hunk/line staging silently loses the marker and
                    // either gets rejected by `git apply` or applies while adding a newline byte
                    // the file never had.
                    current.Lines.Add(new DiffLine { Content = line, Kind = DiffLineKind.Header });
                else
                    current.Lines.Add(new DiffLine { Content = line, Kind = DiffLineKind.Context, OldLineNumber = oldLine++, NewLineNumber = newLine++ });
            }
            return hunks;
        }

        // ── Image diff ───────────────────────────────────────────────────────

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };

        public static bool IsImagePath(string filePath) =>
            !string.IsNullOrEmpty(filePath) && ImageExtensions.Contains(Path.GetExtension(filePath));

        /// <summary>Old/new raw bytes for an image file changed in a commit (relative to its first parent).</summary>
        public (byte[] Old, byte[] New) GetImageDiff(string commitSha, string filePath)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(commitSha);
            if (commit == null) return (null, null);
            var parent = commit.Parents.FirstOrDefault();
            return (parent != null ? GetBlobBytesAt(parent, filePath) : null, GetBlobBytesAt(commit, filePath));
        }

        /// <summary>Old/new raw bytes for an unstaged image change — index vs working directory.</summary>
        public (byte[] Old, byte[] New) GetUnstagedImageDiff(string filePath)
        {
            EnsureOpen();
            var oldBytes = GetIndexBlobBytes(filePath);
            var fullPath = Path.Combine(_repo.Info.WorkingDirectory, filePath.Replace('/', Path.DirectorySeparatorChar));
            var newBytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            return (oldBytes, newBytes);
        }

        /// <summary>Old/new raw bytes for a staged image change — HEAD vs index.</summary>
        public (byte[] Old, byte[] New) GetStagedImageDiff(string filePath)
        {
            EnsureOpen();
            var oldBytes = _repo.Head?.Tip != null ? GetBlobBytesAt(_repo.Head.Tip, filePath) : null;
            var newBytes = GetIndexBlobBytes(filePath);
            return (oldBytes, newBytes);
        }

        private static byte[] GetBlobBytesAt(Commit commit, string filePath)
        {
            var entry = commit?[filePath];
            if (!(entry?.Target is Blob blob)) return null;
            using (var stream = blob.GetContentStream())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private byte[] GetIndexBlobBytes(string filePath)
        {
            var entry = _repo.Index?[filePath];
            if (entry == null) return null;
            var blob = _repo.Lookup<Blob>(entry.Id);
            if (blob == null) return null;
            using (var stream = blob.GetContentStream())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ── Word-level intra-line diff ───────────────────────────────────────

        private static readonly System.Text.RegularExpressions.Regex WordTokenRegex =
            new System.Text.RegularExpressions.Regex(@"\w+|\s+|[^\w\s]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private const int MaxWordDiffTokens = 300;

        /// <summary>Pairs adjacent deleted/added line runs within a hunk and highlights the differing word spans.</summary>
        private static void ComputeWordDiffsForHunk(DiffHunk hunk)
        {
            var lines = hunk.Lines;
            int i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind != DiffLineKind.Deleted) { i++; continue; }
                int delStart = i;
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Deleted) i++;
                int delCount = i - delStart;
                int addStart = i;
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Added) i++;
                int addCount = i - addStart;
                int pairCount = Math.Min(delCount, addCount);
                for (int k = 0; k < pairCount; k++)
                    ComputeWordDiff(lines[delStart + k], lines[addStart + k]);
            }
        }

        private static List<string> Tokenize(string s)
        {
            var result = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in WordTokenRegex.Matches(s))
                result.Add(m.Value);
            return result;
        }

        private static void ComputeWordDiff(DiffLine delLine, DiffLine addLine)
        {
            var oldContent = delLine.Content.Length > 0 ? delLine.Content.Substring(1) : delLine.Content;
            var newContent = addLine.Content.Length > 0 ? addLine.Content.Substring(1) : addLine.Content;
            var oldTokens = Tokenize(oldContent);
            var newTokens = Tokenize(newContent);
            if (oldTokens.Count > MaxWordDiffTokens || newTokens.Count > MaxWordDiffTokens) return;

            int n = oldTokens.Count, m = newTokens.Count;
            var dp = new int[n + 1, m + 1];
            for (int oi2 = n - 1; oi2 >= 0; oi2--)
                for (int ni2 = m - 1; ni2 >= 0; ni2--)
                    dp[oi2, ni2] = oldTokens[oi2] == newTokens[ni2]
                        ? dp[oi2 + 1, ni2 + 1] + 1
                        : Math.Max(dp[oi2 + 1, ni2], dp[oi2, ni2 + 1]);

            var oldSpans = new List<DiffHighlightSpan>();
            var newSpans = new List<DiffHighlightSpan>();
            int oi = 0, ni = 0, oPos = 0, nPos = 0;
            int oUnmatchedStart = -1, nUnmatchedStart = -1;
            while (oi < n && ni < m)
            {
                if (oldTokens[oi] == newTokens[ni] && dp[oi, ni] == dp[oi + 1, ni + 1] + 1)
                {
                    if (oUnmatchedStart >= 0) { oldSpans.Add(new DiffHighlightSpan(oUnmatchedStart, oPos - oUnmatchedStart)); oUnmatchedStart = -1; }
                    if (nUnmatchedStart >= 0) { newSpans.Add(new DiffHighlightSpan(nUnmatchedStart, nPos - nUnmatchedStart)); nUnmatchedStart = -1; }
                    oPos += oldTokens[oi].Length; nPos += newTokens[ni].Length;
                    oi++; ni++;
                }
                else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
                {
                    if (oUnmatchedStart < 0) oUnmatchedStart = oPos;
                    oPos += oldTokens[oi].Length; oi++;
                }
                else
                {
                    if (nUnmatchedStart < 0) nUnmatchedStart = nPos;
                    nPos += newTokens[ni].Length; ni++;
                }
            }
            while (oi < n) { if (oUnmatchedStart < 0) oUnmatchedStart = oPos; oPos += oldTokens[oi].Length; oi++; }
            while (ni < m) { if (nUnmatchedStart < 0) nUnmatchedStart = nPos; nPos += newTokens[ni].Length; ni++; }
            if (oUnmatchedStart >= 0) oldSpans.Add(new DiffHighlightSpan(oUnmatchedStart, oPos - oUnmatchedStart));
            if (nUnmatchedStart >= 0) newSpans.Add(new DiffHighlightSpan(nUnmatchedStart, nPos - nUnmatchedStart));

            // Shift by 1 to account for the leading -/+ character stripped above
            delLine.HighlightSpans = oldSpans.Select(s => new DiffHighlightSpan(s.Start + 1, s.Length)).ToList();
            addLine.HighlightSpans = newSpans.Select(s => new DiffHighlightSpan(s.Start + 1, s.Length)).ToList();
        }

        // ── Remotes / Push / Pull ─────────────────────────────────────────────

        public List<RemoteInfo> GetRemotes()
        {
            EnsureOpen();
            return _repo.Network.Remotes
                .Select(r => new RemoteInfo { Name = r.Name, Url = r.Url, PushUrl = r.PushUrl })
                .ToList();
        }

        public void AddRemote(string name, string url)
        {
            EnsureOpen();
            _repo.Network.Remotes.Add(name, url);
        }

        public void UpdateRemote(string name, string url)
        {
            EnsureOpen();
            if (_repo.Network.Remotes[name] == null)
                throw new InvalidOperationException($"Remote '{name}' not found.");
            _repo.Network.Remotes.Update(name, r => r.Url = url);
        }

        public void RemoveRemote(string name)
        {
            EnsureOpen();
            _repo.Network.Remotes.Remove(name);
        }

        // ── Submodules ───────────────────────────────────────────────────────

        public List<Models.SubmoduleInfo> GetSubmodules()
        {
            EnsureOpen();
            var list = new List<Models.SubmoduleInfo>();
            foreach (var sm in _repo.Submodules)
            {
                SubmoduleStatus status;
                try { status = sm.RetrieveStatus(); }
                catch { status = SubmoduleStatus.WorkDirUninitialized; }

                list.Add(new Models.SubmoduleInfo
                {
                    Name = sm.Name,
                    Path = sm.Path,
                    Url = sm.Url,
                    IsInitialized = !status.HasFlag(SubmoduleStatus.WorkDirUninitialized),
                    IsDirty = status.HasFlag(SubmoduleStatus.WorkDirModified) ||
                              status.HasFlag(SubmoduleStatus.WorkDirFilesModified) ||
                              status.HasFlag(SubmoduleStatus.WorkDirFilesIndexDirty) ||
                              status.HasFlag(SubmoduleStatus.WorkDirFilesUntracked)
                });
            }
            return list;
        }

        public void Fetch(string remoteName, string username, string password,
            IProgress<string> progress = null, bool prune = false,
            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            EnsureOpen();
            var remote = _repo.Network.Remotes[remoteName];
            if (remote == null) throw new InvalidOperationException($"Remote '{remoteName}' not found.");
            int lastPct = -1;
            var opts = new FetchOptions
            {
                CredentialsProvider = BuildCredHandler(username, password),
                // Returning false from a progress callback is libgit2's cancellation mechanism
                OnProgress = msg => { progress?.Report(msg); return !ct.IsCancellationRequested; },
                OnTransferProgress = tp =>
                {
                    if (tp.TotalObjects > 0)
                    {
                        var pct = tp.ReceivedObjects * 100 / tp.TotalObjects;
                        if (pct != lastPct) // throttle: this callback fires per object batch
                        {
                            lastPct = pct;
                            progress?.Report($"Receiving objects: {pct}% ({tp.ReceivedObjects}/{tp.TotalObjects})");
                        }
                    }
                    return !ct.IsCancellationRequested;
                },
                Prune = prune
            };
            var refSpecs = remote.FetchRefSpecs.Select(r => r.Specification);
            Commands.Fetch(_repo, remoteName, refSpecs, opts, null);
        }

        public void Pull(string authorName, string authorEmail,
            string username, string password,
            IProgress<string> progress = null,
            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            EnsureOpen();
            var sig = new Signature(authorName, authorEmail, DateTimeOffset.Now);
            var opts = new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    CredentialsProvider = BuildCredHandler(username, password),
                    OnProgress = msg => { progress?.Report(msg); return !ct.IsCancellationRequested; },
                    OnTransferProgress = tp => !ct.IsCancellationRequested
                }
            };
            Commands.Pull(_repo, sig, opts);
        }

        public void Push(string remoteName, string branchName,
            string username, string password,
            IProgress<string> progress = null,
            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            EnsureOpen();
            var branch = _repo.Branches[branchName];
            if (branch == null) throw new InvalidOperationException($"Branch '{branchName}' not found.");
            var opts = new PushOptions
            {
                CredentialsProvider = BuildCredHandler(username, password),
                OnPushStatusError = err => throw new InvalidOperationException(err.Message),
                OnPushTransferProgress = (current, total, bytes) => !ct.IsCancellationRequested
            };

            if (branch.TrackedBranch == null)
            {
                // New branch with no upstream — push explicitly and set tracking so
                // future push/pull work and ahead/behind indicators appear.
                var remote = _repo.Network.Remotes[remoteName]
                    ?? throw new InvalidOperationException($"Remote '{remoteName}' not found.");
                progress?.Report($"Pushing new branch {branchName} and setting upstream…");
                _repo.Network.Push(remote, $"{branch.CanonicalName}:{branch.CanonicalName}", opts);
                _repo.Branches.Update(branch,
                    b => b.Remote = remoteName,
                    b => b.UpstreamBranch = branch.CanonicalName);
            }
            else
            {
                _repo.Network.Push(branch, opts);
            }
        }

        public void PushTag(string tagName, string remoteName,
            string username, string password)
        {
            EnsureOpen();
            var remote = _repo.Network.Remotes[remoteName]
                ?? throw new InvalidOperationException($"Remote '{remoteName}' not found.");
            var opts = new PushOptions
            {
                CredentialsProvider = BuildCredHandler(username, password),
                OnPushStatusError = err => throw new InvalidOperationException(err.Message)
            };
            _repo.Network.Push(remote, $"refs/tags/{tagName}", opts);
        }

        // ── Tags ──────────────────────────────────────────────────────────────

        public List<TagInfo> GetTags()
        {
            EnsureOpen();
            return _repo.Tags
                .OrderByDescending(t => (t.Target as Commit)?.Author.When ?? DateTimeOffset.MinValue)
                .Select(t => new TagInfo
                {
                    Name = t.FriendlyName,
                    TargetSha = t.Target?.Sha,
                    IsAnnotated = t.IsAnnotated,
                    Message = (t.Target as TagAnnotation)?.Message
                }).ToList();
        }

        public void DeleteTag(string name)
        {
            EnsureOpen();
            _repo.Tags.Remove(name);
        }

        /// <summary>Checks out the commit a tag points to (detached HEAD). Peels annotated tags.</summary>
        public void CheckoutTag(string tagName)
        {
            EnsureOpen();
            var tag = _repo.Tags[tagName]
                ?? throw new InvalidOperationException($"Tag '{tagName}' not found.");
            var commit = tag.PeeledTarget as Commit
                ?? throw new InvalidOperationException($"Tag '{tagName}' does not point to a commit.");
            Commands.Checkout(_repo, commit);
        }

        public void CreateTag(string name, string sha = null, string message = null)
        {
            EnsureOpen();
            var target = sha != null ? _repo.Lookup<GitObject>(sha) : _repo.Head.Tip;
            if (message != null)
            {
                var sig = _repo.Config.BuildSignature(DateTimeOffset.Now)
                          ?? new Signature("PickleGit", "picklegit@localhost", DateTimeOffset.Now);
                _repo.ApplyTag(name, target.Sha, sig, message);
            }
            else
            {
                _repo.ApplyTag(name, target.Sha);
            }
        }

        // ── Reflog ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses .git/logs/HEAD directly — LibGit2Sharp 0.27's reflog API doesn't expose
        /// enough (no reliable old/new sha pairing). Most-recent entry first (HEAD@{0}).
        /// </summary>
        public List<Models.ReflogEntry> GetReflog(int maxEntries = 200)
        {
            EnsureOpen();
            var result = new List<Models.ReflogEntry>();
            var path = Path.Combine(GitDirectory, "logs", "HEAD");
            string[] lines;
            try { lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
            catch (Exception ex) { AppLog.Warn("Failed to read reflog", ex); return result; }

            for (int i = lines.Length - 1, idx = 0; i >= 0 && idx < maxEntries; i--)
            {
                var line = lines[i];
                var tabIdx = line.IndexOf('\t');
                if (tabIdx < 0) continue;
                var header = line.Substring(0, tabIdx);
                var message = line.Substring(tabIdx + 1);
                try
                {
                    var sp1 = header.IndexOf(' ');
                    var oldSha = header.Substring(0, sp1);
                    var rest = header.Substring(sp1 + 1);
                    var sp2 = rest.IndexOf(' ');
                    var newSha = rest.Substring(0, sp2);
                    rest = rest.Substring(sp2 + 1);
                    var lastSp = rest.LastIndexOf(' ');
                    rest = rest.Substring(0, lastSp); // drop trailing timezone offset
                    var secondLastSp = rest.LastIndexOf(' ');
                    var tsStr = rest.Substring(secondLastSp + 1);
                    if (!long.TryParse(tsStr, out var unixTs)) continue;

                    result.Add(new Models.ReflogEntry
                    {
                        Index = idx,
                        OldSha = oldSha,
                        NewSha = newSha,
                        Message = message,
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTs)
                    });
                    idx++;
                }
                catch { /* skip a malformed line rather than aborting the whole reflog */ }
            }
            return result;
        }

        // ── Stash ─────────────────────────────────────────────────────────────

        public List<StashInfo> GetStashes()
        {
            EnsureOpen();
            return _repo.Stashes
                .Select((s, i) => new StashInfo
                {
                    Index = i,
                    Message = string.IsNullOrWhiteSpace(s.Message) ? s.FriendlyName : s.Message,
                    Sha = s.WorkTree?.Sha
                }).ToList();
        }

        public void Stash(string message, string authorName, string authorEmail, bool keepIndex = false)
        {
            EnsureOpen();
            var sig = new Signature(authorName, authorEmail, DateTimeOffset.Now);
            var modifiers = StashModifiers.IncludeUntracked;
            if (keepIndex) modifiers |= StashModifiers.KeepIndex;
            _repo.Stashes.Add(sig, message, modifiers);
        }

        public void ApplyStash(int index)
        {
            EnsureOpen();
            _repo.Stashes.Apply(index);
        }

        public void DropStash(int index)
        {
            EnsureOpen();
            _repo.Stashes.Remove(index);
        }

        public void DiscardFile(string filePath, FileChangeKind kind)
        {
            EnsureOpen();
            if (kind == FileChangeKind.Untracked)
            {
                var workDir = _repo.Info.WorkingDirectory.TrimEnd('\\', '/');
                var absPath = Path.Combine(workDir, filePath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(absPath))
                    Directory.Delete(absPath, recursive: true);
                else if (File.Exists(absPath))
                    File.Delete(absPath);
                return;
            }
            _repo.CheckoutPaths("HEAD", new[] { filePath },
                new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        }

        /// <summary>Discards multiple working-dir changes in one pass — untracked files/dirs are deleted
        /// directly, tracked files are restored from HEAD in a single CheckoutPaths call.</summary>
        public void DiscardFiles(IEnumerable<FileChange> files)
        {
            EnsureOpen();
            var workDir = _repo.Info.WorkingDirectory.TrimEnd('\\', '/');
            var trackedPaths = new List<string>();
            foreach (var fc in files)
            {
                if (fc.Kind == FileChangeKind.Untracked)
                {
                    var absPath = Path.Combine(workDir, fc.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(absPath)) Directory.Delete(absPath, recursive: true);
                    else if (File.Exists(absPath)) File.Delete(absPath);
                }
                else
                {
                    trackedPaths.Add(fc.Path);
                }
            }
            if (trackedPaths.Count > 0)
                _repo.CheckoutPaths("HEAD", trackedPaths, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        }

        public void PopStash(int index = 0)
        {
            EnsureOpen();
            _repo.Stashes.Pop(index);
        }

        // ── Cherry-pick ───────────────────────────────────────────────────────

        public void CherryPick(string sha)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(sha)
                ?? throw new InvalidOperationException($"Commit {sha} not found.");
            var identity = new Identity(
                _repo.Config.Get<string>("user.name")?.Value ?? "Unknown",
                _repo.Config.Get<string>("user.email")?.Value ?? "unknown@example.com");
            _repo.CherryPick(commit, new Signature(identity, System.DateTimeOffset.Now));
            // Conflicts are left in the index/working tree and surfaced via GetConflictState().
        }

        // ── Conflict resolution ──────────────────────────────────────────────

        /// <summary>
        /// Detects an in-progress merge/cherry-pick/revert/rebase from .git state files
        /// (LibGit2Sharp exposes none of this) plus the conflicted paths from the index.
        /// </summary>
        public ConflictState GetConflictState()
        {
            EnsureOpen();
            var gitDir = GitDirectory;
            var state = new ConflictState();

            var rebaseMerge = Path.Combine(gitDir, "rebase-merge");
            var rebaseApply = Path.Combine(gitDir, "rebase-apply");
            if (Directory.Exists(rebaseMerge))
            {
                state.Operation = ConflictOperation.Rebase;
                state.RebaseStepCurrent = ReadIntFile(Path.Combine(rebaseMerge, "msgnum"));
                state.RebaseStepTotal = ReadIntFile(Path.Combine(rebaseMerge, "end"));
                state.SourceDescription = ReadFirstLine(Path.Combine(rebaseMerge, "onto-name"))
                    ?? ReadFirstLine(Path.Combine(rebaseMerge, "head-name"));
            }
            else if (Directory.Exists(rebaseApply))
            {
                state.Operation = ConflictOperation.Rebase;
                state.RebaseStepCurrent = ReadIntFile(Path.Combine(rebaseApply, "next"));
                state.RebaseStepTotal = ReadIntFile(Path.Combine(rebaseApply, "last"));
            }
            else if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            {
                state.Operation = ConflictOperation.Merge;
                state.SourceDescription = ReadMergeSourceDescription(gitDir);
            }
            else if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
            {
                state.Operation = ConflictOperation.CherryPick;
                state.SourceDescription = ShortShaOrNull(ReadFirstLine(Path.Combine(gitDir, "CHERRY_PICK_HEAD")));
            }
            else if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
            {
                state.Operation = ConflictOperation.Revert;
                state.SourceDescription = ShortShaOrNull(ReadFirstLine(Path.Combine(gitDir, "REVERT_HEAD")));
            }

            if (state.Operation != ConflictOperation.None)
            {
                state.ConflictedFiles = _repo.Index.Conflicts
                    .Select(c => c.Ours?.Path ?? c.Theirs?.Path ?? c.Ancestor?.Path)
                    .Where(p => p != null)
                    .Distinct()
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            return state;
        }

        private static string ShortShaOrNull(string sha)
            => string.IsNullOrEmpty(sha) ? null : sha.Substring(0, Math.Min(7, sha.Length));

        private static readonly System.Text.RegularExpressions.Regex BisectLogEntryRegex =
            new System.Text.RegularExpressions.Regex(@"^# (?<verb>bad|good|skip): \[(?<sha>[0-9a-f]{40})\]");

        /// <summary>
        /// Detects an in-progress `git bisect` session from `.git/BISECT_START` + `BISECT_LOG`
        /// (LibGit2Sharp exposes none of this, same as GetConflictState). Read-only reconstruction —
        /// RevisionsLeft/StepsRemaining/Found/FirstBadSha are NOT recoverable from files (git only
        /// prints them on the triggering command's own stdout), so they're left at their "unknown"
        /// defaults here; the ViewModel fills them in live from each step's CLI result and holds
        /// them in memory for the rest of the session.
        /// </summary>
        public BisectState GetBisectState()
        {
            EnsureOpen();
            var gitDir = GitDirectory;
            var state = new BisectState();
            if (!File.Exists(Path.Combine(gitDir, "BISECT_START"))) return state;

            state.InProgress = true;
            state.CurrentSha = GetHeadSha();

            var logPath = Path.Combine(gitDir, "BISECT_LOG");
            if (File.Exists(logPath))
            {
                foreach (var line in File.ReadLines(logPath))
                {
                    var m = BisectLogEntryRegex.Match(line);
                    if (!m.Success) continue;
                    var sha = m.Groups["sha"].Value;
                    switch (m.Groups["verb"].Value)
                    {
                        case "bad": state.BadSha = sha; break;   // last one wins — the current bad boundary
                        case "good": if (!state.GoodShas.Contains(sha)) state.GoodShas.Add(sha); break;
                        case "skip": if (!state.SkippedShas.Contains(sha)) state.SkippedShas.Add(sha); break;
                    }
                }
            }
            return state;
        }

        private static int ReadIntFile(string path)
        {
            try { return int.TryParse(File.ReadAllText(path).Trim(), out var n) ? n : 0; }
            catch { return 0; }
        }

        private static string ReadFirstLine(string path)
        {
            try { return File.Exists(path) ? File.ReadLines(path).FirstOrDefault() : null; }
            catch { return null; }
        }

        private static string ReadMergeSourceDescription(string gitDir)
        {
            var msg = ReadFirstLine(Path.Combine(gitDir, "MERGE_MSG"));
            if (!string.IsNullOrEmpty(msg)) return msg.Trim();
            return ShortShaOrNull(ReadFirstLine(Path.Combine(gitDir, "MERGE_HEAD")));
        }

        // ── Commit detail ─────────────────────────────────────────────────────

        public List<FileChange> GetCommitChangedFiles(string sha)
        {
            EnsureOpen();
            var commit = _repo.Lookup<Commit>(sha);
            if (commit == null) return new List<FileChange>();
            var parent = commit.Parents.FirstOrDefault();
            var changes = _repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree);
            return changes.Select(c => new FileChange
            {
                Path = c.Path,
                OldPath = c.OldPath,
                Kind = MapChangeKind(c.Status)
            }).ToList();
        }

        /// <summary>Changed files between two arbitrary commits/branch tips — a direct two-dot tree
        /// comparison (not merge-base-relative), for the branch "Diff against current branch" view.</summary>
        public List<FileChange> GetChangedFiles(string shaA, string shaB)
        {
            EnsureOpen();
            var commitA = _repo.Lookup<Commit>(shaA);
            var commitB = _repo.Lookup<Commit>(shaB);
            if (commitA == null || commitB == null) return new List<FileChange>();
            var changes = _repo.Diff.Compare<TreeChanges>(commitA.Tree, commitB.Tree);
            return changes.Select(c => new FileChange
            {
                Path = c.Path,
                OldPath = c.OldPath,
                Kind = MapChangeKind(c.Status)
            }).ToList();
        }

        private static FileChangeKind MapChangeKind(ChangeKind k)
        {
            switch (k)
            {
                case ChangeKind.Added: return FileChangeKind.Added;
                case ChangeKind.Deleted: return FileChangeKind.Deleted;
                case ChangeKind.Modified: return FileChangeKind.Modified;
                case ChangeKind.Renamed: return FileChangeKind.Renamed;
                case ChangeKind.Copied: return FileChangeKind.Copied;
                default: return FileChangeKind.Modified;
            }
        }

        /// <summary>Commits reachable from <paramref name="toInclusiveSha"/> but not from
        /// <paramref name="fromExclusiveSha"/>, oldest first — the same order `git rebase -i`
        /// lists its todo in.</summary>
        public List<CommitInfo> GetCommitRange(string fromExclusiveSha, string toInclusiveSha)
        {
            EnsureOpen();
            var toCommit = _repo.Lookup<Commit>(toInclusiveSha);
            if (toCommit == null) return new List<CommitInfo>();
            var fromCommit = _repo.Lookup<Commit>(fromExclusiveSha);

            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = toCommit
            };
            if (fromCommit != null) filter.ExcludeReachableFrom = fromCommit;

            var refMap = BuildRefMap();
            var list = _repo.Commits.QueryBy(filter).Select(c => MapCommit(c, refMap)).ToList();
            list.Reverse();
            return list;
        }

        // ── File history & blame ─────────────────────────────────────────────

        /// <summary>Commits that touched this path, most recent first.</summary>
        public List<CommitInfo> GetFileHistory(string filePath, int maxCount = 500)
        {
            EnsureOpen();

            // Prefer `git log --follow` (rename-aware — libgit2's QueryBy stops at renames)
            if (Cli != null && Cli.IsAvailable)
            {
                try { return GetFileHistoryCli(filePath, maxCount); }
                catch { /* fall through to the libgit2 walk */ }
            }

            var refMap = BuildRefMap();
            var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
            var result = new List<CommitInfo>();
            int count = 0;
            foreach (var entry in _repo.Commits.QueryBy(filePath, filter))
            {
                if (count++ >= maxCount) break;
                result.Add(MapCommit(entry.Commit, refMap));
            }
            return result;
        }

        /// <summary>Rename-following file history via `git log --follow`; \x1f as field separator.</summary>
        private List<CommitInfo> GetFileHistoryCli(string filePath, int maxCount)
        {
            var args = $"log --follow -{maxCount} --format=%H%x1f%an%x1f%ae%x1f%aI%x1f%B%x1e -- "
                       + Git.CliGitService.Quote(filePath);
            var cliResult = Cli.RunCheckedAsync(args).GetAwaiter().GetResult();
            var result = new List<CommitInfo>();
            foreach (var record in cliResult.StdOut.Split('\x1e'))
            {
                var fields = record.TrimStart('\n', '\r').Split('\x1f');
                if (fields.Length < 5 || fields[0].Length < 40) continue;
                DateTimeOffset.TryParse(fields[3], out var when);
                result.Add(new CommitInfo
                {
                    Sha = fields[0].Trim(),
                    AuthorName = fields[1],
                    AuthorEmail = fields[2],
                    AuthorDate = when,
                    Message = fields[4].TrimEnd('\n', '\r')
                });
            }
            return result;
        }

        /// <summary>Per-line blame for the file's current working-tree content.</summary>
        public List<BlameLine> GetBlame(string filePath, string sha = null)
        {
            EnsureOpen();
            BlameHunkCollection hunks;
            string[] contentLines;
            if (!string.IsNullOrEmpty(sha))
            {
                // Blame as of an arbitrary historical commit (History mode navigating commits) —
                // read the file's content from that commit's tree, not the working tree, since the
                // two can differ arbitrarily far apart.
                var commit = _repo.Lookup<Commit>(sha);
                hunks = _repo.Blame(filePath, new BlameOptions { StartingAt = commit });
                var blobBytes = GetBlobBytesAt(commit, filePath);
                contentLines = blobBytes != null
                    ? Encoding.UTF8.GetString(blobBytes).Replace("\r\n", "\n").Split('\n')
                    : Array.Empty<string>();
            }
            else
            {
                hunks = _repo.Blame(filePath);
                var fullPath = Path.Combine(_repo.Info.WorkingDirectory,
                    filePath.Replace('/', Path.DirectorySeparatorChar));
                contentLines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : Array.Empty<string>();
            }

            var result = new List<BlameLine>();
            foreach (var hunk in hunks)
            {
                for (int i = 0; i < hunk.LineCount; i++)
                {
                    int idx = hunk.FinalStartLineNumber + i;
                    result.Add(new BlameLine
                    {
                        LineNumber = idx + 1,
                        Content = idx < contentLines.Length ? contentLines[idx] : string.Empty,
                        Sha = hunk.FinalCommit?.Sha,
                        AuthorName = hunk.FinalCommit?.Author.Name ?? string.Empty,
                        AuthorDate = hunk.FinalCommit?.Author.When ?? default,
                        MessageShort = FirstLine(hunk.FinalCommit?.Message)
                    });
                }
            }
            return result;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var idx = s.IndexOf('\n');
            return (idx >= 0 ? s.Substring(0, idx) : s).Trim();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static CredentialsHandler BuildCredHandler(string username, string password)
        {
            return (url, usernameFromUrl, types) =>
            {
                if ((types & SupportedCredentialTypes.UsernamePassword) != 0)
                {
                    return new UsernamePasswordCredentials
                    {
                        Username = !string.IsNullOrEmpty(username) ? username : (usernameFromUrl ?? string.Empty),
                        Password = password ?? string.Empty
                    };
                }
                // Corporate servers using Windows Negotiate (Kerberos/NTLM)
                return new DefaultCredentials();
            };
        }

        private void EnsureOpen()
        {
            if (_repo == null) throw new InvalidOperationException("No repository is open.");
        }

        public string GetCurrentBranch()
        {
            EnsureOpen();
            if (_repo.Info.IsHeadDetached)
            {
                var sha = _repo.Head?.Tip?.Sha;
                return sha != null
                    ? "detached @ " + sha.Substring(0, Math.Min(7, sha.Length))
                    : "HEAD (detached)";
            }
            return _repo.Head?.FriendlyName ?? "HEAD";
        }

        public bool IsHeadDetached
        {
            get { EnsureOpen(); return _repo.Info.IsHeadDetached; }
        }

        public string GetHeadSha()
        {
            EnsureOpen();
            return _repo.Head?.Tip?.Sha;
        }

        /// <summary>Tip SHA of any branch by friendly name (e.g. "origin/main"), or null.</summary>
        public string GetBranchTipSha(string branchName)
        {
            EnsureOpen();
            return _repo.Branches[branchName]?.Tip?.Sha;
        }

        public void Dispose()
        {
            Executor.Dispose();
            _repo?.Dispose();
            _repo = null;
        }
    }
}
