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

        private async Task ResolveConflictSideAsync(FileChange fc, string side)
        {
            var quoted = Services.Git.CliGitService.Quote(fc.Path);
            if (!await RunCliAsync($"Resolving {fc.Path} ({side})…", $"checkout --{side} -- {quoted}", "Resolve conflict"))
                return;
            await RunCliAsync($"Staging {fc.Path}…", $"add -- {quoted}", "Resolve conflict");
            await LoadWorkingDirAsync();
        }

        private async Task MarkResolvedAsync(object param)
        {
            if (!(param is FileChange fc)) return;
            if (!await RunCliAsync($"Marking {fc.Path} resolved…",
                    $"add -- {Services.Git.CliGitService.Quote(fc.Path)}", "Mark resolved"))
                return;
            await LoadWorkingDirAsync();
        }

        private async void OpenMergeEditor(object param)
        {
            var clicked = param as FileChange;
            var conflicted = ConflictedFileChanges.ToList();
            if (conflicted.Count == 0) return;

            var vm = new MergeConflictSessionViewModel(
                conflicted,
                path => GetAbsoluteFilePath(conflicted.FirstOrDefault(f => f.Path == path)),
                async path =>
                {
                    await RunCliAsync($"Staging {path}…",
                        $"add -- {Services.Git.CliGitService.Quote(path)}", "Mark resolved");
                });

            if (clicked != null)
            {
                var match = vm.Files.FirstOrDefault(f => string.Equals(f.Path, clicked.Path, StringComparison.Ordinal));
                if (match != null) vm.SelectedEntry = match;
            }

            var win = new Views.MergeConflictEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            win.ShowDialog();
            await LoadWorkingDirAsync();
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
            string args;
            switch (op)
            {
                case ConflictOperation.Merge: args = "merge --continue"; break;
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
