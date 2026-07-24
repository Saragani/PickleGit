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
    /// <summary>Working-directory status, stage/unstage, commit, stash and file utilities.</summary>
    public partial class RepositoryViewModel
    {
        // ── Staging ───────────────────────────────────────────────────────────

        public async Task LoadWorkingDirAsync()
        {
            if (!_git.IsOpen) return;
            var changes = await _git.Executor.RunAsync(() => _git.GetWorkingDirectoryStatus());
            await RefreshConflictStateAsync();
            ApplyWorkingDirStatus(changes, updateGraph: false);
        }

        private bool _commitTemplateChecked;

        /// <summary>Pre-fills an empty commit message from the repo's `commit.template`, once per tab.</summary>
        private async Task TryPrefillCommitTemplateAsync()
        {
            if (_commitTemplateChecked || !string.IsNullOrEmpty(CommitMessage)) return;
            _commitTemplateChecked = true;
            var template = await _git.Executor.RunAsync(() => _git.GetCommitTemplate());
            if (!string.IsNullOrEmpty(template) && string.IsNullOrEmpty(CommitMessage))
                CommitMessage = template.Replace("\r\n", "\n").TrimEnd('\n');
        }

        /// <summary>Watcher-driven light refresh: status lists + the uncommitted graph node only. Only
        /// re-syncs the diff pane when the file lists actually changed — the watcher fires on ANY workdir
        /// write anywhere in the repo, and re-pointing the diff pane unconditionally on every tick would
        /// otherwise drop the current selection (see ApplyWorkingDirStatus) even for unrelated files.</summary>
        private async Task RefreshWorkingDirStatusAsync()
        {
            if (!_git.IsOpen || RepoDirectoryMissing()) return;
            var changes = await _git.Executor.RunAsync(() => _git.GetWorkingDirectoryStatus());
            await RefreshConflictStateAsync();
            if (ApplyWorkingDirStatus(changes, updateGraph: true))
                await SyncDiffPaneAfterFileListChangeAsync();
        }

        private async Task RefreshConflictStateAsync()
        {
            var conflict = await _git.Executor.RunAsync(() => _git.GetConflictState());
            ConflictInfo = conflict;
            if (conflict.HasConflicts) ShowWorkingDir = true;
        }

        private static string BuildFileChangeKey(FileChange f) =>
            string.Concat(f.Path, "", f.OldPath, "", f.Kind, "",
                f.IsStaged, "", f.LinesAdded, "", f.LinesDeleted);

        /// <summary>Rebuilds WorkingDirFiles/StagedFiles from fresh status, unless the new list is
        /// content-equivalent to what's already shown (cheap key comparison) — skipping the rebuild in
        /// that case avoids dropping the current ListView selection on every watcher tick for unrelated
        /// workdir writes. Returns true when the lists were actually rebuilt, so callers know whether the
        /// diff pane/selection needs re-syncing afterward.</summary>
        private bool ApplyWorkingDirStatus(List<FileChange> changes, bool updateGraph)
        {
            var currentKeys = new HashSet<string>(WorkingDirFiles.Concat(StagedFiles).Select(BuildFileChangeKey));
            var newKeys = new HashSet<string>(changes.Select(BuildFileChangeKey));
            bool contentChanged = !newKeys.SetEquals(currentKeys);

            if (contentChanged)
            {
                // FileChange has no Equals override (by design — reference identity is relied on
                // elsewhere, e.g. _currentDiffFile), so re-find the multi-selection by Path afterward,
                // the same way SyncDiffPaneAfterFileListChangeAsync already does for the single selection.
                var selectedWorkingPaths = new HashSet<string>(_selectedWorkingFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
                var selectedStagedPaths = new HashSet<string>(_selectedStagedFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

                var cmp = BuildFileComparer();
                var working = changes.Where(f => !f.IsStaged).ToList();
                working.Sort(cmp);
                var staged = changes.Where(f => f.IsStaged).ToList();
                staged.Sort(cmp);
                WorkingDirFiles = new ObservableCollection<FileChange>(working);
                StagedFiles = new ObservableCollection<FileChange>(staged);

                _selectedWorkingFiles.Clear();
                foreach (var f in WorkingDirFiles.Where(f => selectedWorkingPaths.Contains(f.Path)))
                    _selectedWorkingFiles.Add(f);
                _selectedStagedFiles.Clear();
                foreach (var f in StagedFiles.Where(f => selectedStagedPaths.Contains(f.Path)))
                    _selectedStagedFiles.Add(f);

                RebuildFileTreeRows();
                RaisePropertyChanged(nameof(ConflictedFileChanges));
                // Stage All/Unstage All/discard-all's CanExecute depends on these lists' Count.
                // CommandManager only re-queries ICommand.CanExecute on certain input events (mouse
                // move, keyboard, focus change) — without an explicit nudge here, a button can render
                // as disabled (dim, per the ToolbarButton IsEnabled=False trigger) for a moment after
                // this list changes, until the next incidental input event, even though clicking it
                // would already work (WPF re-checks CanExecute synchronously right before Execute).
                CommandManager.InvalidateRequerySuggested();
            }

            if (updateGraph)
            {
                var has = changes.Count > 0;
                if (has != _hasUncommittedChanges)
                {
                    _hasUncommittedChanges = has;
                    ApplyFilter(); // add/remove the uncommitted node without a full walk
                }
            }
            return contentChanged;
        }

        /// <summary>Builds the ordering for the current <see cref="FileSortMode"/> — shared by
        /// ApplyWorkingDirStatus (fresh status) and ReapplyFileOrder/RebuildFileTreeRows (re-sorting
        /// already-loaded files with no new git call needed).</summary>
        private Comparison<FileChange> BuildFileComparer()
        {
            switch (FileSortMode)
            {
                case FileSortMode.PathAsc:
                    return (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                case FileSortMode.PathDesc:
                    return (a, b) => string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase);
                case FileSortMode.NameAsc:
                    return (a, b) => string.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path), StringComparison.OrdinalIgnoreCase);
                case FileSortMode.NameDesc:
                    return (a, b) => string.Compare(Path.GetFileName(b.Path), Path.GetFileName(a.Path), StringComparison.OrdinalIgnoreCase);
                case FileSortMode.Status:
                default:
                    return (a, b) =>
                    {
                        var byKind = ((int)a.Kind).CompareTo((int)b.Kind);
                        return byKind != 0 ? byKind : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                    };
            }
        }

        /// <summary>Re-sorts the already-loaded WorkingDirFiles/StagedFiles in place when the user
        /// changes FileSortMode — no fresh status read needed, the FileChange objects are unchanged.</summary>
        private void ReapplyFileOrder()
        {
            var cmp = BuildFileComparer();
            var selectedWorkingPaths = new HashSet<string>(_selectedWorkingFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var selectedStagedPaths = new HashSet<string>(_selectedStagedFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

            var working = WorkingDirFiles.ToList();
            working.Sort(cmp);
            var staged = StagedFiles.ToList();
            staged.Sort(cmp);
            WorkingDirFiles = new ObservableCollection<FileChange>(working);
            StagedFiles = new ObservableCollection<FileChange>(staged);

            _selectedWorkingFiles.Clear();
            foreach (var f in WorkingDirFiles.Where(f => selectedWorkingPaths.Contains(f.Path)))
                _selectedWorkingFiles.Add(f);
            _selectedStagedFiles.Clear();
            foreach (var f in StagedFiles.Where(f => selectedStagedPaths.Contains(f.Path)))
                _selectedStagedFiles.Add(f);

            RebuildFileTreeRows();
        }

        /// <summary>Rebuilds the flattened folder-tree rows for both panels' Tree view mode. Cheap
        /// enough (file counts are small) to always keep both up to date regardless of which view
        /// mode is currently active, so switching Flat→Tree never shows a stale tree.</summary>
        private void RebuildFileTreeRows()
        {
            var cmp = BuildFileComparer();
            StagedFileTreeRows = new ObservableCollection<FileTreeRow>(FileTreeBuilder.Build(StagedFiles, cmp, _expandedStagedTreeFolders));
            WorkingFileTreeRows = new ObservableCollection<FileTreeRow>(FileTreeBuilder.Build(WorkingDirFiles, cmp, _expandedWorkingTreeFolders));
        }

        private async Task StageFileAsync(object param)
        {
            var targets = StagingService.ResolveTargets(_selectedWorkingFiles, param);
            if (targets.Count == 0) return;
            using (_watcher?.Suppress())
                await _staging.StageAsync(targets.Select(f => f.Path));
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        private async Task UnstageFileAsync(object param)
        {
            var targets = StagingService.ResolveTargets(_selectedStagedFiles, param);
            if (targets.Count == 0) return;
            using (_watcher?.Suppress())
                await _staging.UnstageAsync(targets.Select(f => f.Path));
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        /// <summary>Stages everything, unless 2+ files are multi-selected — then only those.
        /// Mirrors UnstageAllAsync and DiscardAllOrSelectedAsync; a single selected file does not
        /// narrow the scope, since a lone selection is just as likely to be plain navigation
        /// (viewing that file's diff) as a deliberate "act on just this one" gesture.</summary>
        private async Task StageAllAsync()
        {
            if (_selectedWorkingFiles.Count >= 2)
            {
                var targets = _selectedWorkingFiles.ToList();
                using (_watcher?.Suppress())
                    await _staging.StageAsync(targets.Select(f => f.Path));
            }
            else
            {
                await RunAsync("Staging all…", () => _git.StageAll());
            }
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        private async Task UnstageAllAsync()
        {
            if (_selectedStagedFiles.Count >= 2)
            {
                var targets = _selectedStagedFiles.ToList();
                using (_watcher?.Suppress())
                    await _staging.UnstageAsync(targets.Select(f => f.Path));
            }
            else
            {
                await RunAsync("Unstaging all…", () => _git.UnstageAll());
            }
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        /// <summary>Blocks commit-creating operations that would otherwise fall back to the bogus
        /// "User &lt;user@example.com&gt;" identity when no git config/override exists.</summary>
        private bool EnsureIdentity()
        {
            if (!string.IsNullOrWhiteSpace(AuthorName) && !string.IsNullOrWhiteSpace(AuthorEmail))
                return true;
            DialogService.ShowError("No Author Identity",
                "No author name/email is configured for this repository.\n\n" +
                "Set one in Settings → Profile (default or per-repository), or run:\n" +
                "  git config user.name \"Your Name\"\n" +
                "  git config user.email you@example.com");
            return false;
        }

        private void OpenIdentitySettings()
        {
            var owner = Application.Current.MainWindow;
            if (!(owner?.DataContext is AppViewModel app)) return;
            new Views.SettingsWindow(app, "Profile") { Owner = owner }.ShowDialog();
        }

        private async Task<bool> CommitAsync()
        {
            if (string.IsNullOrWhiteSpace(CommitMessage)) return false;
            if (!EnsureIdentity()) return false;
            var amend = IsAmend;
            var signOff = SignOff;
            if (amend && !DialogService.Confirm("Amend Commit",
                    "Rewrite the last commit with the staged changes and this message?\n\n" +
                    "If the commit was already pushed, you will need to force-push.",
                    "Amend"))
                return false;
            var message = CommitMessage;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());

            var shouldSign = AppSettings.LoadGpgSignCommits() ||
                await _git.Executor.RunAsync(() => _git.GetConfigBool("commit.gpgsign"));

            bool ok;
            if (shouldSign && _git.Cli != null && _git.Cli.IsAvailable)
            {
                var author = $"{AuthorName ?? "User"} <{AuthorEmail ?? "user@example.com"}>";
                var args = "commit -S -F -" +
                    (amend ? " --amend" : "") +
                    (signOff ? " --signoff" : "") +
                    $" --author={CliGitService.Quote(author)}";
                ok = await RunCliAsync(amend ? "Amending (signed)…" : "Committing (signed)…", args, "Commit", message);
            }
            else
            {
                ok = await RunAsync(amend ? "Amending…" : "Committing…", () =>
                    _git.CreateCommit(message,
                        AuthorName ?? "User", AuthorEmail ?? "user@example.com",
                        amend, signOff));
            }
            if (ok)
            {
                CommitMessage = string.Empty;
                IsAmend = false;
                await CaptureUndo(UndoKind.Commit, amend ? "Amend commit" : "Commit", preHead);
            }
            await LoadWorkingDirAsync();
            await RefreshAsync();
            return ok;
        }

        // ── Stash ─────────────────────────────────────────────────────────────

        /// <summary>Stashes only the selected working-dir files (`git stash push -u -- &lt;paths&gt;`).</summary>
        private async Task StashSelectedFilesAsync(object param)
        {
            var targets = StagingService.ResolveTargets(_selectedWorkingFiles, param);
            if (targets.Count == 0) return;
            var dlg = new Views.Dialogs.TextPromptDialog
            {
                Owner = Application.Current.MainWindow,
                DialogTitle = "Stash Selected Files",
                HeaderText = $"Stash {targets.Count} file(s)",
                PromptText = "Stash message (optional). Only the selected files are stashed (including untracked).",
                InputText = "WIP on " + CurrentBranch,
                OkText = "Stash",
                RequireInput = false
            };
            if (dlg.ShowDialog() != true) return;
            var msg = dlg.InputText;
            var paths = string.Join(" ", targets.Select(t => CliGitService.Quote(t.Path)));
            var msgArg = string.IsNullOrWhiteSpace(msg) ? "" : $"-m {CliGitService.Quote(msg)} ";
            if (await RunCliAsync("Stashing selected files…",
                    $"stash push -u {msgArg}-- {paths}", "Stash files"))
            {
                await RefreshAsync();
                await LoadWorkingDirAsync();
            }
        }

        private async Task StashAsync()
        {
            if (!EnsureIdentity()) return;
            var dlg = new Views.Dialogs.TextPromptDialog
            {
                Owner = Application.Current.MainWindow,
                DialogTitle = "Stash Changes",
                HeaderText = "Stash Changes",
                PromptText = "Stash message (optional). Untracked files will also be stashed and removed from disk.",
                InputText = "WIP on " + CurrentBranch,
                OkText = "Stash",
                RequireInput = false,
                CheckboxText = "Keep staged changes in the index (--keep-index)"
            };
            if (dlg.ShowDialog() != true) return;
            var msg = dlg.InputText;
            var keepIndex = dlg.IsCheckboxChecked;
            await RunThenRefreshWorkingDir("Stashing…", () =>
                _git.Stash(msg, AuthorName ?? "User", AuthorEmail ?? "user@example.com", keepIndex));
        }

        private void PopStash(object _)
            => _ = RunThenRefreshWorkingDir("Popping stash…", () => _git.PopStash());

        private void ApplyStash(object param, bool pop)
        {
            if (!(param is StashInfo si)) return;
            var verb = pop ? "Popping" : "Applying";
            _ = RunThenRefreshWorkingDir($"{verb} stash…", () =>
            {
                if (pop) _git.PopStash(si.Index);
                else _git.ApplyStash(si.Index);
            });
        }

        private void DropStash(object param)
        {
            if (!(param is StashInfo si)) return;
            if (DialogService.Confirm("Drop Stash",
                    $"Drop stash '{si.Message}'?\nIts changes will be lost permanently.",
                    "Drop", danger: true))
                _ = RunThenRefresh("Dropping stash…", () => _git.DropStash(si.Index));
        }

        /// <summary>Shows a stash's changed files in the detail panel (like a commit preview).</summary>
        private async void SelectStash(object param)
        {
            if (!(param is StashInfo si) || string.IsNullOrEmpty(si.Sha)) return;
            ShowWorkingDir = false;
            DetailCommit = new CommitInfo
            {
                Sha = si.Sha,
                Message = si.Message,
                AuthorName = "(stash)",
                AuthorEmail = string.Empty,
                AuthorDate = DateTimeOffset.Now,
                IsStash = true
            };
            CommitFiles = new ObservableCollection<FileChange>();
            FlatDiffItems = Array.Empty<DiffItem>();
            SideBySideItems = Array.Empty<SideBySideItem>();
            IsLfsPointerDiff = false;
            IsBinaryDiff = false;
            IsLargeDiffPending = false;
            var sha = si.Sha;
            // async void, invoked directly from a selection-changed handler with no caller to
            // observe a Task — without this try/catch a failure here surfaces as the generic
            // crash-style dialog instead of an in-place error.
            try
            {
                var files = await _git.Executor.RunAsync(() => _git.GetCommitChangedFiles(sha));
                if (DetailCommit?.Sha == sha)
                    CommitFiles = new ObservableCollection<FileChange>(files);
            }
            catch (Exception ex)
            {
                if (DetailCommit?.Sha == sha) DialogService.ShowError("Stash", ex.Message);
            }
        }

        // ── File operations ───────────────────────────────────────────────────

        private string GetAbsoluteFilePath(object param)
        {
            var rel = (param as FileChange)?.Path ?? (param as AggregatedFileChange)?.Path;
            if (string.IsNullOrEmpty(rel)) return null;
            var workDir = _git.WorkingDirectory ?? RepoPath;
            if (string.IsNullOrEmpty(workDir)) return null;
            return Path.Combine(workDir.TrimEnd('\\', '/'),
                rel.Replace('/', Path.DirectorySeparatorChar));
        }

        private void OpenFile(object param)
        {
            var abs = GetAbsoluteFilePath(param);
            if (abs == null || !File.Exists(abs)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(abs) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Open File", ex.Message);
            }
        }

        private void RevealFile(object param)
        {
            var abs = GetAbsoluteFilePath(param);
            if (abs == null) return;
            try
            {
                if (File.Exists(abs) || Directory.Exists(abs))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{abs}\"");
                else
                    System.Diagnostics.Process.Start("explorer.exe",
                        $"\"{Path.GetDirectoryName(abs)}\"");
            }
            catch { }
        }

        private void CopyFilePath(object param)
        {
            var abs = GetAbsoluteFilePath(param);
            if (abs != null) CopyToClipboard(abs);
        }

        private async void AddToGitignore(object param)
        {
            var rel = (param as FileChange)?.Path;
            if (string.IsNullOrEmpty(rel)) return;
            var workDir = _git.WorkingDirectory ?? RepoPath;
            if (string.IsNullOrEmpty(workDir)) return;
            try
            {
                var gitignore = Path.Combine(workDir.TrimEnd('\\', '/'), ".gitignore");
                var entry = "/" + rel;
                var needsLeadingNewline = File.Exists(gitignore) &&
                    File.ReadAllText(gitignore) is string existing &&
                    existing.Length > 0 && !existing.EndsWith("\n");
                File.AppendAllText(gitignore, (needsLeadingNewline ? Environment.NewLine : string.Empty)
                    + entry + Environment.NewLine);
                StatusMessage = $"Added {entry} to .gitignore";
            }
            catch (Exception ex)
            {
                DialogService.ShowError(".gitignore", ex.Message);
                return;
            }
            await RefreshWorkingDirStatusAsync();
        }

    }
}
