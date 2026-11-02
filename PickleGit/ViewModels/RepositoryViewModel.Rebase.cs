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
    /// <summary>Rebase (plain + interactive), conflict resolution and undo.</summary>
    public partial class RepositoryViewModel
    {
        // ── Rebase ───────────────────────────────────────────────────────────

        private void RebaseOntoBranch(object param)
        {
            if (!(param is BranchInfo bi)) return;
            _ = RebaseOntoAsync(bi.Name, bi.Name);
        }

        private void RebaseOntoCommit(object _)
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (string.IsNullOrEmpty(sha)) return;
            _ = RebaseOntoAsync(sha, sha.Substring(0, 7));
        }

        private async Task RebaseOntoAsync(string target, string label)
        {
            if (!DialogService.Confirm("Rebase",
                    $"Rebase '{CurrentBranch}' onto {label}?\n\n" +
                    "This rewrites commit history. If this branch was already pushed, " +
                    "you will need to force-push afterward.",
                    "Rebase"))
                return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunCliAllowingConflictAsync($"Rebasing onto {label}…",
                $"rebase --autostash {Services.Git.CliGitService.Quote(target)}", "Rebase");
            if (ok)
            {
                var conflict = await _git.Executor.RunAsync(() => _git.GetConflictState());
                if (!conflict.HasConflicts)
                    await CaptureUndo(UndoKind.HeadHardMove, $"Rebase onto {label}", preHead);
            }
            await LoadWorkingDirAsync();
            await RefreshAsync();
        }

        private void InteractiveRebaseOntoBranch(object param)
        {
            if (!(param is BranchInfo bi)) return;
            _ = OpenInteractiveRebaseAsync(bi.Name, bi.Name);
        }

        private void InteractiveRebaseOntoCommit(object _)
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (string.IsNullOrEmpty(sha)) return;
            _ = OpenInteractiveRebaseAsync(sha, sha.Substring(0, 7));
        }

        private async Task OpenInteractiveRebaseAsync(string target, string label)
        {
            var vm = new InteractiveRebaseViewModel(_git, target, label);
            var win = new Views.InteractiveRebaseDialog { DataContext = vm, Owner = Application.Current.MainWindow };
            if (win.ShowDialog() == true)
                await StartInteractiveRebaseAsync(target, label, vm.Items.ToList());
        }

        /// <summary>Runs `git rebase -i` with a pre-built todo (see Services/Git/RebaseTodoBuilder.cs
        /// for the GIT_SEQUENCE_EDITOR / MSYS-sh quoting details).</summary>
        private async Task StartInteractiveRebaseAsync(string target, string label, IReadOnlyList<Models.RebaseTodoItem> items)
        {
            var sequenceEditor = RebaseTodoBuilder.BuildSequenceEditor(items);
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunCliAllowingConflictAsync($"Rebasing (interactive) onto {label}…",
                $"rebase -i --autostash {CliGitService.Quote(target)}", "Interactive Rebase",
                env: new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = sequenceEditor });
            if (ok)
            {
                var conflict = await _git.Executor.RunAsync(() => _git.GetConflictState());
                if (!conflict.HasConflicts)
                    await CaptureUndo(UndoKind.HeadHardMove, $"Interactive rebase onto {label}", preHead);
            }
            await LoadWorkingDirAsync();
            await RefreshAsync();
        }

        // ── Conflict resolution ───────────────────────────────────────────────

        /// <summary>The conflicted files a context-menu resolve command applies to: every selected
        /// Unstaged file when the right-clicked row is part of a multi-selection (the same rule
        /// stage/unstage/discard use, see StagingService.ResolveTargets), otherwise just the clicked
        /// row — filtered to conflicted files, since a selection can mix in ordinary changes.</summary>
        private List<FileChange> ConflictTargets(object param) =>
            StagingService.ResolveTargets(_selectedWorkingFiles, param)
                .Where(f => f.Kind == FileChangeKind.Conflicted)
                .ToList();

        /// <summary>Quoted path lists for git, split by GitService.ChunkPathsByLength so each command
        /// line stays under Windows' 32,767-character CreateProcess limit (a whole folder can exceed it).</summary>
        private static IEnumerable<string> QuotedPathChunks(IEnumerable<FileChange> files) =>
            GitService.ChunkPathsByLength(files.Select(f => f.Path).ToList())
                .Select(batch => string.Join(" ", batch.Select(Services.Git.CliGitService.Quote)));

        /// <summary>Asks before a resolve action that will touch more than one file: checkout --ours/
        /// --theirs overwrites each working file (including any hand-resolution in progress) and `git add`
        /// stages it, so a folder or multi-selection must not do that silently. A single file keeps the
        /// old no-prompt behavior unless it still contains conflict markers (Mark Resolved only).</summary>
        private async Task<bool> ConfirmResolveAsync(List<FileChange> files, string action, bool checkMarkers)
        {
            var withMarkers = new List<FileChange>();
            if (checkMarkers)
            {
                var abs = files.ToDictionary(f => f, f => GetAbsoluteFilePath(f));
                withMarkers = await Task.Run(() => files.Where(f => ContainsConflictMarkers(abs[f])).ToList());
            }
            if (files.Count <= 1 && withMarkers.Count == 0) return true;

            const int maxListed = 10;
            var sb = new StringBuilder();
            sb.Append(files.Count == 1 ? $"{action} for {files[0].Path}?" : $"{action} for these {files.Count} files?");
            if (files.Count > 1)
            {
                sb.Append("\n\n").Append(string.Join("\n", files.Take(maxListed).Select(f => f.Path)));
                if (files.Count > maxListed) sb.Append($"\n…and {files.Count - maxListed} more");
            }
            if (withMarkers.Count > 0)
                sb.Append(files.Count == 1
                    ? "\n\nIt still contains conflict markers (<<<<<<<), which would be staged as-is."
                    : $"\n\n{withMarkers.Count} of them still contain conflict markers (<<<<<<<), which would be staged as-is.");
            return DialogService.Confirm(action, sb.ToString(), action, danger: true);
        }

        private static bool ContainsConflictMarkers(string absolutePath)
        {
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return false;
                if (new FileInfo(absolutePath).Length > 20L * 1024 * 1024) return false; // not worth scanning
                foreach (var line in File.ReadLines(absolutePath))
                    if (line.StartsWith("<<<<<<< ", StringComparison.Ordinal)) return true;
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Scanning {absolutePath} for conflict markers failed", ex);
                return false;
            }
        }

        private static string DescribeTargets(List<FileChange> files) =>
            files.Count == 1 ? files[0].Path : $"{files.Count} files";

        private async Task ResolveConflictSideAsync(object param, string side)
        {
            var targets = ConflictTargets(param);
            if (targets.Count == 0) return;

            // A delete/modify conflict where the chosen side has no version of the file can't be
            // resolved with `checkout --ours/--theirs` (git errors out), and one such path would
            // fail the whole batched command — leave those to the merge editor and say so.
            var missing = targets.Where(f => side == "ours" ? f.OursMissing : f.TheirsMissing).ToList();
            var resolvable = targets.Except(missing).ToList();

            if (resolvable.Count > 0)
            {
                if (!await ConfirmResolveAsync(resolvable, side == "ours" ? "Take Ours" : "Take Theirs", checkMarkers: false))
                    return;
                var label = DescribeTargets(resolvable);
                foreach (var quoted in QuotedPathChunks(resolvable))
                {
                    if (!await RunCliAsync($"Resolving {label} ({side})…", $"checkout --{side} -- {quoted}", "Resolve conflict")
                        || !await RunCliAsync($"Staging {label}…", $"add -- {quoted}", "Resolve conflict"))
                        break;
                }
                await LoadWorkingDirAsync();
            }

            if (missing.Count > 0)
                DialogService.ShowError("Resolve conflict",
                    $"These files have no \"{side}\" version (deleted on that side), so they were left unresolved. " +
                    "Use the merge editor to keep or delete them:\n\n" + string.Join("\n", missing.Select(f => f.Path)));
        }

        private async Task MarkResolvedAsync(object param)
        {
            var targets = ConflictTargets(param);
            if (targets.Count == 0) return;
            if (!await ConfirmResolveAsync(targets, "Mark Resolved", checkMarkers: true)) return;
            foreach (var quoted in QuotedPathChunks(targets))
            {
                if (!await RunCliAsync($"Marking {DescribeTargets(targets)} resolved…", $"add -- {quoted}", "Mark resolved"))
                    break;
            }
            await LoadWorkingDirAsync();
        }

        /// <summary>Undoes a resolution: puts a file back into a raw, unmerged conflict state
        /// (conflict markers back in the working tree, stage 1/2/3 back in the index), whether it's
        /// currently staged (already resolved+`git add`-ed) or unstaged. `git add` collapses a
        /// conflicted path's stage 1/2/3 index entries into one stage-0 entry irreversibly — plain
        /// git has no "undo" for that — so this re-derives the same three-way conflict from scratch:
        /// finds the merge-base of HEAD and the operation's "theirs" ref (<see cref="TheirsRefFor"/>),
        /// reads each side's blob for this path via `ls-tree`, restages them at conflict stages 1/2/3
        /// via `update-index --index-info`, then `checkout -m` to write fresh conflict markers into
        /// the working tree from those stages — the same plumbing git's own merge machinery uses
        /// when a conflict first occurs, so it reproduces the same markers rather than an
        /// independently-diffed approximation.</summary>
        private async Task MarkUnresolvedAsync(object param)
        {
            if (!(param is FileChange fc)) return;
            var theirsRef = TheirsRefFor(ConflictInfo?.Operation ?? ConflictOperation.None);
            if (theirsRef == null)
            {
                DialogService.ShowError("Mark Unresolved",
                    "Restoring conflict markers isn't supported for a plain rebase — there's no single tracked \"theirs\" commit to recompute the conflict from.");
                return;
            }
            if (fc.OursMissing || fc.TheirsMissing)
            {
                DialogService.ShowError("Mark Unresolved",
                    "This was an add/delete conflict, not a three-way content conflict, so its original state can't be reconstructed automatically.");
                return;
            }

            var path = fc.Path;
            var ok = await RunAsync($"Restoring conflict markers in {path}…", () =>
            {
                var quotedPath = Services.Git.CliGitService.Quote(path);

                var baseResult = _git.Cli.RunAsync($"merge-base HEAD {theirsRef}").GetAwaiter().GetResult();
                if (!baseResult.Success)
                    throw new InvalidOperationException("Could not find a common ancestor commit: " + baseResult.ErrorText);
                var baseRef = baseResult.StdOut.Trim();

                // The three ls-tree reads are independent of each other (once baseRef is known) —
                // kick all three git.exe processes off together instead of waiting for one to exit
                // before starting the next. Safe to run concurrently: each is a read-only subprocess,
                // not a LibGit2Sharp call, so this doesn't violate the "one thread touches _repo"
                // rule — only the results are consumed back on this (executor) thread below.
                var baseTreeTask = _git.Cli.RunAsync($"ls-tree {baseRef} -- {quotedPath}");
                var oursTreeTask = _git.Cli.RunAsync("ls-tree HEAD -- " + quotedPath);
                var theirsTreeTask = _git.Cli.RunAsync($"ls-tree {theirsRef} -- {quotedPath}");
                Task.WaitAll(baseTreeTask, oursTreeTask, theirsTreeTask);

                string ParseTreeEntry(Task<GitCliResult> resultTask, string treeish)
                {
                    var r = resultTask.Result;
                    if (!r.Success || string.IsNullOrWhiteSpace(r.StdOut))
                        throw new InvalidOperationException($"'{path}' does not exist at {treeish} — can't reconstruct a three-way conflict for it.");
                    // ls-tree line shape: "<mode> blob <sha>\t<path>"
                    var line = r.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    var tabIdx = line.IndexOf('\t');
                    var parts = (tabIdx >= 0 ? line.Substring(0, tabIdx) : line).Split(' ');
                    if (parts.Length < 3)
                        throw new InvalidOperationException($"Unexpected 'git ls-tree' output for '{path}' at {treeish}: \"{line}\"");
                    return parts[0] + " " + parts[2]; // "<mode> <sha>"
                }

                var baseEntry = ParseTreeEntry(baseTreeTask, baseRef);
                var oursEntry = ParseTreeEntry(oursTreeTask, "HEAD");
                var theirsEntry = ParseTreeEntry(theirsTreeTask, theirsRef);

                var indexInfo =
                    $"{baseEntry} 1\t{path}\n" +
                    $"{oursEntry} 2\t{path}\n" +
                    $"{theirsEntry} 3\t{path}\n";

                var updateResult = _git.Cli.RunAsync("update-index --index-info",
                    new Services.Git.GitCliOptions { StdIn = indexInfo }).GetAwaiter().GetResult();
                if (!updateResult.Success)
                    throw new InvalidOperationException("Failed to restore conflict stages: " + updateResult.ErrorText);

                var checkoutResult = _git.Cli.RunAsync($"checkout -m -- {quotedPath}").GetAwaiter().GetResult();
                if (!checkoutResult.Success)
                    throw new InvalidOperationException("Failed to write conflict markers: " + checkoutResult.ErrorText);

                _git.Reopen();
            });
            if (!ok) return;

            await LoadWorkingDirAsync();
            await RefreshAsync();
        }

        /// <summary>Theirs-side ref for each conflict-producing operation, mirroring the raw
        /// state-file reads GetConflictState() already does — not read via LibGit2Sharp anywhere
        /// else in this codebase, but the same ref names resolve the same way through it. Plain
        /// rebase has no equivalent single ref (the "theirs" commit changes every step and isn't
        /// tracked by a named ref this codebase already reads), so it's left out — the editor
        /// just won't show a theirs commit header for a rebase conflict.</summary>
        private static string TheirsRefFor(ConflictOperation op)
        {
            switch (op)
            {
                case ConflictOperation.Merge: return "MERGE_HEAD";
                case ConflictOperation.CherryPick: return "CHERRY_PICK_HEAD";
                case ConflictOperation.Revert: return "REVERT_HEAD";
                default: return null;
            }
        }

        /// <summary>True from the first click until the merge editor closes. OpenMergeEditor awaits
        /// executor/CLI work before ShowDialog, so without this a double-click (or any click while
        /// it loads) starts a second invocation and opens a second window.</summary>
        private bool _openingMergeEditor;

        private async void OpenMergeEditor(object param)
        {
            if (_openingMergeEditor) return;
            _openingMergeEditor = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await OpenMergeEditorCoreAsync(param);
            }
            finally
            {
                _openingMergeEditor = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task OpenMergeEditorCoreAsync(object param)
        {
            var clicked = param as FileChange;
            var conflicted = ConflictedFileChanges.ToList();
            if (conflicted.Count == 0) return;

            var theirsRef = TheirsRefFor(ConflictInfo?.Operation ?? ConflictOperation.None);
            var (oursBranch, oursCommit, theirsCommit) = await _git.Executor.RunAsync(() =>
                (_git.GetCurrentBranch(), _git.GetCommit("HEAD"), theirsRef != null ? _git.GetCommit(theirsRef) : null));

            // Fetch the real common-ancestor blob straight from git's index for every content-
            // conflicted file, so the Result pane can show real base text for an unresolved block
            // even when the working file itself only has plain (non-diff3) markers — see
            // CliGitService.GetConflictAncestorTextAsync. Existence conflicts (add/delete) have no
            // content to parse at all, so skip those. A failed or unavailable fetch just leaves
            // that file out of the dictionary — MergeConflictFileViewModel falls back to today's
            // placeholder for it exactly as before.
            var ancestorTextByPath = new Dictionary<string, string>();
            var contentConflicted = conflicted.Where(f => !f.OursMissing && !f.TheirsMissing).ToList();
            var ancestorResults = await Task.WhenAll(contentConflicted.Select(async f =>
                (f.Path, Text: await _git.Cli.GetConflictAncestorTextAsync(f.Path))));
            foreach (var (path, text) in ancestorResults)
                if (text != null) ancestorTextByPath[path] = text;

            // Raw index-stage bytes, so the editor can rebuild conflicts git wouldn't text-merge
            // (NUL-containing files such as UTF-16 get no conflict markers — see ThreeWayMerge).
            // GetConflictStageBytes returns null (reading nothing) for ordinary text conflicts, so only
            // binary-to-git files are loaded at all.
            var stagesByPath = await _git.Executor.RunAsync(() =>
            {
                var map = new Dictionary<string, ConflictStages>();
                foreach (var f in contentConflicted)
                {
                    try
                    {
                        var stages = _git.GetConflictStageBytes(f.Path);
                        if (stages != null) map[f.Path] = stages;
                    }
                    catch (Exception ex) { AppLog.Error($"Reading conflict stages failed for {f.Path}", ex); }
                }
                return map;
            });
            // Decode + three-way merge is pure CPU/file work (no repository access), so it runs on the
            // thread pool rather than the git executor, keeping the UI responsive for large files.
            var absByPath = stagesByPath.Keys.ToDictionary(p => p, p => GetAbsoluteFilePath(conflicted.First(f => f.Path == p)));
            var rebuiltByPath = await Task.Run(() => stagesByPath.ToDictionary(
                kv => kv.Key, kv => RebuiltConflict.Prepare(kv.Value, absByPath[kv.Key])));

            var vm = new MergeConflictSessionViewModel(
                conflicted,
                path => GetAbsoluteFilePath(conflicted.FirstOrDefault(f => f.Path == path)),
                async path =>
                {
                    await RunCliAsync($"Staging {path}…",
                        $"add -- {Services.Git.CliGitService.Quote(path)}", "Mark resolved");
                },
                oursBranch, oursCommit, ConflictInfo?.SourceDescription, theirsCommit, ancestorTextByPath, rebuiltByPath);

            if (clicked != null)
            {
                var match = vm.Files.FirstOrDefault(f => string.Equals(f.Path, clicked.Path, StringComparison.Ordinal));
                if (match != null) vm.SelectedEntry = match;
            }

            // Suppress() must span the whole dialog (ShowDialog pumps the dispatcher for as long as
            // it's open, giving RepositoryWatcher's 400ms-debounced refresh plenty of opportunity to
            // fire and race this method's own post-close LoadWorkingDirAsync/selection sync — the
            // same class of race Staging.cs's StageFileAsync/UnstageFileAsync already guards against
            // for exactly this reason, previously missing here), not just the resolve-and-stage CLI
            // calls the dialog triggers along the way. Left unsuppressed, a stray watcher refresh
            // could rebuild WorkingDirFiles/StagedFiles concurrently with this method's own rebuild,
            // leaving the just-resolved file's selection reconciled against two different snapshots
            // and stranding a stale FileChange in one collection's selection.
            using (_watcher?.Suppress())
            {
                var win = new Views.MergeConflictEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
                win.ShowDialog();
                await LoadWorkingDirAsync();
                await SyncDiffPaneAfterFileListChangeAsync();
            }
        }

        private async Task ContinueOperationAsync()
        {
            var op = ConflictInfo?.Operation ?? ConflictOperation.None;
            if (op == ConflictOperation.None) return;
            if (ConflictInfo.ConflictedFiles.Count > 0)
            {
                DialogService.ShowError("Continue", "Resolve all conflicted files before continuing.");
                return;
            }
            if (op == ConflictOperation.Merge)
            {
                if (string.IsNullOrWhiteSpace(CommitMessage))
                {
                    var conflictedPaths = await _git.Executor.RunAsync(() => _git.GetMergeConflictedFilePaths());
                    var sourceDesc = ConflictInfo.SourceDescription;
                    var sb = new StringBuilder(string.IsNullOrEmpty(sourceDesc) ? "Merge" : sourceDesc);
                    if (conflictedPaths.Count > 0)
                    {
                        sb.Append("\n\nResolved conflicts in:\n");
                        foreach (var path in conflictedPaths) sb.Append("  ").Append(path).Append('\n');
                    }
                    CommitMessage = sb.ToString().TrimEnd('\n');
                }
                return;
            }

            string args;
            switch (op)
            {
                case ConflictOperation.CherryPick: args = "cherry-pick --continue"; break;
                case ConflictOperation.Revert: args = "revert --continue"; break;
                case ConflictOperation.Rebase: args = "rebase --continue"; break;
                default: return;
            }
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (await RunCliAsync("Continuing…", args, "Continue"))
            {
                await CaptureUndo(UndoKind.HeadHardMove, $"Continue {op.ToString().ToLowerInvariant()}", preHead);
                await LoadWorkingDirAsync();
                await RefreshAsync();
            }
        }

        private async Task AbortOperationAsync()
        {
            var op = ConflictInfo?.Operation ?? ConflictOperation.None;
            if (op == ConflictOperation.None) return;
            string args;
            switch (op)
            {
                case ConflictOperation.Merge: args = "merge --abort"; break;
                case ConflictOperation.CherryPick: args = "cherry-pick --abort"; break;
                case ConflictOperation.Revert: args = "revert --abort"; break;
                case ConflictOperation.Rebase: args = "rebase --abort"; break;
                default: return;
            }
            if (!DialogService.Confirm("Abort",
                    $"Abort the {op.ToString().ToLowerInvariant()} and restore the previous state?",
                    "Abort", danger: true))
                return;
            if (await RunCliAsync("Aborting…", args, "Abort"))
            {
                await LoadWorkingDirAsync();
                await RefreshAsync();
            }
        }

        // ── Undo ──────────────────────────────────────────────────────────────

        /// <summary>Records the last mutating op's HEAD movement so it can be reversed via UndoCommand.
        /// Reads the post-op HEAD via the executor — never touch the Repository handle directly
        /// from the UI thread, since RunAsync's watcher suppression has already ended by the time
        /// this runs and a concurrent refresh may be using the handle on the executor thread.</summary>
        private async Task CaptureUndo(UndoKind kind, string description, string preHeadSha)
        {
            if (!_git.IsOpen || string.IsNullOrEmpty(preHeadSha)) return;
            var postHeadSha = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (string.IsNullOrEmpty(postHeadSha)) return;
            if (string.Equals(preHeadSha, postHeadSha, StringComparison.OrdinalIgnoreCase)) return;
            LastUndo = new UndoEntry
            {
                Kind = kind,
                Description = description,
                PreHeadSha = preHeadSha,
                PostHeadSha = postHeadSha
            };
        }

        private async Task CaptureCheckoutUndo(string description, string preHeadSha, string preBranchName)
        {
            if (!_git.IsOpen || string.IsNullOrEmpty(preHeadSha)) return;
            var postHeadSha = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (string.IsNullOrEmpty(postHeadSha) || string.Equals(preHeadSha, postHeadSha, StringComparison.OrdinalIgnoreCase))
                return;
            LastUndo = new UndoEntry
            {
                Kind = UndoKind.Checkout,
                Description = description,
                PreHeadSha = preHeadSha,
                PostHeadSha = postHeadSha,
                BranchName = preBranchName
            };
        }

        /// <summary>Reads HEAD sha + current branch (null if detached) as one atomic executor read.</summary>
        private async Task<(string head, string branch)> CaptureCheckoutStateAsync()
        {
            return await _git.Executor.RunAsync(() =>
            {
                var head = _git.GetHeadSha();
                var branch = _git.IsHeadDetached ? null : _git.GetCurrentBranch();
                return (head, branch);
            });
        }

        private async Task UndoLastAsync()
        {
            var entry = LastUndo;
            if (entry == null) return;

            if (entry.Kind == UndoKind.HeadHardMove)
            {
                var status = await _git.Executor.RunAsync(() => _git.GetWorkingDirectoryStatus());
                if (status.Count > 0 && !DialogService.Confirm("Undo",
                        $"Undo '{entry.Description}'?\n\nThis will discard any uncommitted changes made since.",
                        "Undo", danger: true))
                    return;
            }
            else if (!DialogService.Confirm("Undo", $"Undo '{entry.Description}'?", "Undo"))
            {
                return;
            }

            LastUndo = null;
            await RunThenRefreshWorkingDir($"Undoing: {entry.Description}…", () => UndoService.Undo(_git, entry));
        }
    }
}
