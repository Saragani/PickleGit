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
    /// <summary>Commit detail panel, multi-select aggregation, cherry-pick/revert, patch export.</summary>
    public partial class RepositoryViewModel
    {
        // ── Branch selection → commit ─────────────────────────────────────────

        private void SelectBranch(object param)
        {
            if (!(param is BranchInfo bi)) return;
            var name = bi.Name;
            var target = GraphNodes.FirstOrDefault(n =>
                n.Commit?.Refs != null &&
                n.Commit.Refs.Any(r => r == name || r == "HEAD -> " + name));
            if (target == null) return;
            SelectedNode = target;
            ScrollToNodeRequested?.Invoke(this, target);
        }

        // ── Detail panels ─────────────────────────────────────────────────────

        private async void LoadCommitDetail(string sha)
        {
            if (!_git.IsOpen) return;
            var commit = GraphNodes.FirstOrDefault(n => n.Commit.Sha == sha)?.Commit;
            if (commit == null) return;
            DetailCommit = commit;
            SelectedFile = null; // drop any file selection carried over from the previous commit
            CommitFiles = new ObservableCollection<FileChange>();
            FlatDiffItems = Array.Empty<DiffItem>();
            SideBySideItems = Array.Empty<SideBySideItem>();
            IsLfsPointerDiff = false;
            IsBinaryDiff = false;
            IsLargeDiffPending = false;
            ShowWorkingDir = false;
            // async void, invoked directly from a selection-changed handler with no caller to
            // observe a Task — without this try/catch a failure here (e.g. a commit that vanished
            // mid-rebase) surfaces as the generic DispatcherUnhandledException crash-style dialog
            // instead of the polished in-place error handling every other operation gets.
            try
            {
                var files = await _git.Executor.RunAsync(() => _git.GetCommitChangedFiles(sha));
                CommitFiles = new ObservableCollection<FileChange>(files);
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Commit Detail", ex.Message);
            }
        }

        // ── Multi-select handling ─────────────────────────────────────────────

        private void OnSelectedNodesChanged()
        {
            SelectedCount = _selectedNodes.Count;
            IsMultiSelection = _selectedNodes.Count > 1;

            // Selecting a commit node always means "browse this commit," which is a different
            // context than an open branch comparison — exit it rather than leaving a stale
            // comparison file list showing alongside a newly-selected commit.
            if (_selectedNodes.Count > 0 && IsComparisonMode) ExitComparisonMode();

            if (_selectedNodes.Count == 0)
            {
                _selectedNode = null;
                RaisePropertyChanged(nameof(SelectedNode));
                // Keep the conflict banner visible even if the synthetic "uncommitted changes"
                // node disappears mid-merge (e.g. a resolved file now matches HEAD exactly).
                if (!HasConflict) ShowWorkingDir = false;
                DetailCommit = null;
                CommitFiles.Clear();
                _aggregatedFiles.Clear();
                RaiseDetailPanelPropertiesChanged();
                return;
            }

            _selectedNode = _selectedNodes[_selectedNodes.Count - 1];
            RaisePropertyChanged(nameof(SelectedNode));

            if (_selectedNodes.Count == 1)
            {
                _aggregatedFiles.Clear();
                var node = _selectedNodes[0];
                if (node.Commit?.IsUncommitted == true) { ShowWorkingDir = true; _ = LoadWorkingDirAsync(); }
                else { ShowWorkingDir = false; LoadCommitDetail(node.Commit.Sha); }
            }
            else
            {
                DetailCommit = null;
                CommitFiles.Clear();
                ShowWorkingDir = false;
                ComputeAggregatedFiles();
            }

            RaiseDetailPanelPropertiesChanged();
        }

        private int _aggregationVersion;

        private async void ComputeAggregatedFiles()
        {
            var version = ++_aggregationVersion;
            var shas = _selectedNodes
                .Where(n => n.Commit?.IsUncommitted != true && n.Commit?.Sha != null)
                .Select(n => n.Commit.Sha)
                .ToList();

            Dictionary<string, (int count, FileChangeKind kind)> map;
            try
            {
                // async void, invoked directly from a selection-changed handler with no caller to
                // observe a Task — without this try/catch a failure partway through aggregating a
                // large multi-select surfaces as the generic crash-style dialog instead of an
                // in-place error.
                map = await _git.Executor.RunAsync(() =>
                {
                    var m = new Dictionary<string, (int count, FileChangeKind kind)>();
                    foreach (var sha in shas)
                    {
                        foreach (var f in _git.GetCommitChangedFiles(sha))
                        {
                            if (m.TryGetValue(f.Path, out var existing))
                                m[f.Path] = (existing.count + 1, f.Kind);
                            else
                                m[f.Path] = (1, f.Kind);
                        }
                    }
                    return m;
                });
            }
            catch (Exception ex)
            {
                if (version == _aggregationVersion) DialogService.ShowError("Files Changed", ex.Message);
                return;
            }

            if (version != _aggregationVersion) return; // selection changed while computing

            _aggregatedFiles.Clear();
            foreach (var kv in map.OrderByDescending(x => x.Value.count).ThenBy(x => x.Key))
            {
                _aggregatedFiles.Add(new AggregatedFileChange
                {
                    Path = kv.Key,
                    CommitCount = kv.Value.count,
                    Kind = kv.Value.kind
                });
            }
        }

        private async Task CherryPickSelectedAsync()
        {
            // Cherry-pick in chronological order (oldest first)
            var ordered = _selectedNodes
                .Where(n => n.Commit?.IsUncommitted != true && n.Commit?.Sha != null)
                .OrderBy(n => n.Commit.AuthorDate)
                .Select(n => n.Commit.Sha)
                .ToList();
            if (ordered.Count == 0) return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunAsync($"Cherry-picking {ordered.Count} commit(s)…", () =>
            {
                foreach (var sha in ordered)
                    _git.CherryPick(sha);
            });
            if (ok) await CaptureUndo(UndoKind.HeadHardMove, $"Cherry-pick {ordered.Count} commit(s)", preHead);
            await LoadWorkingDirAsync();
            await RefreshAsync();
        }

        // ── Patch export / apply ──────────────────────────────────────────────

        /// <summary>`git format-patch -1 &lt;sha&gt; --stdout` → user-chosen .patch file.</summary>
        private async Task SaveCommitAsPatchAsync(object _)
        {
            var commit = SelectedNode?.Commit;
            if (commit == null || commit.IsUncommitted || string.IsNullOrEmpty(commit.Sha)) return;
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError("Save Patch", "This feature requires Git for Windows (git.exe).");
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save commit as patch",
                FileName = commit.Sha.Substring(0, 7) + ".patch",
                Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            var target = dlg.FileName;
            var ok = await RunAsync($"Exporting {commit.Sha.Substring(0, 7)} as patch…", () =>
            {
                var result = _git.Cli.RunAsync($"format-patch -1 {commit.Sha} --stdout")
                    .GetAwaiter().GetResult();
                if (!result.Success) throw new InvalidOperationException(result.ErrorText);
                File.WriteAllText(target, result.StdOut);
            });
            if (ok) StatusMessage = $"Patch saved to {target}";
        }

        /// <summary>`git format-patch -1 &lt;sha&gt; --stdout` for each selected commit, oldest first,
        /// concatenated into one file — a single mailbox-format patch, same as multiple individual
        /// commit patches back to back. ApplyPatchFileAsync's mailbox detection (`git am`) splits this
        /// back into separate commits with original authorship preserved.</summary>
        private async Task SaveSelectedCommitsAsPatchAsync()
        {
            var ordered = _selectedNodes
                .Where(n => n.Commit?.IsUncommitted != true && n.Commit?.Sha != null)
                .OrderBy(n => n.Commit.AuthorDate)
                .Select(n => n.Commit.Sha)
                .ToList();
            if (ordered.Count == 0) return;
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError("Save Patch", "This feature requires Git for Windows (git.exe).");
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save commits as patch",
                FileName = $"{ordered.Count}-commits.patch",
                Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            var target = dlg.FileName;
            var ok = await RunAsync($"Exporting {ordered.Count} commit(s) as patch…", () =>
            {
                var sb = new StringBuilder();
                foreach (var sha in ordered)
                {
                    var result = _git.Cli.RunAsync($"format-patch -1 {sha} --stdout").GetAwaiter().GetResult();
                    if (!result.Success) throw new InvalidOperationException(result.ErrorText);
                    sb.Append(result.StdOut);
                }
                File.WriteAllText(target, sb.ToString());
            });
            if (ok) StatusMessage = $"Patch saved to {target}";
        }

        /// <summary>Applies a .patch file — `git am` for mailbox patches (keeps authorship),
        /// falling back to `git apply` for plain diffs.</summary>
        private async Task ApplyPatchFileAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError("Apply Patch", "This feature requires Git for Windows (git.exe).");
                return;
            }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Apply patch file",
                Filter = "Patch files (*.patch;*.diff)|*.patch;*.diff|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;
            var file = dlg.FileName;
            bool isMailbox;
            try { isMailbox = File.ReadLines(file).FirstOrDefault()?.StartsWith("From ") == true; }
            catch (Exception ex) { DialogService.ShowError("Apply Patch", ex.Message); return; }

            var args = isMailbox
                ? $"am --3way {CliGitService.Quote(file)}"
                : $"apply {CliGitService.Quote(file)}";
            if (await RunCliAsync($"Applying {Path.GetFileName(file)}…", args, "Apply patch"))
            {
                await RefreshAsync();
                await LoadWorkingDirAsync();
            }
        }

        /// <summary>Reverts the selected commits newest-first so each revert applies onto the previous.</summary>
        private async Task RevertSelectedAsync()
        {
            var ordered = _selectedNodes
                .Where(n => n.Commit?.IsUncommitted != true && n.Commit?.Sha != null)
                .OrderByDescending(n => n.Commit.AuthorDate)
                .Select(n => n.Commit.Sha)
                .ToList();
            if (ordered.Count == 0) return;
            if (!DialogService.Confirm("Revert Commits",
                    $"Create {ordered.Count} revert commit(s), newest first?", "Revert"))
                return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunAsync($"Reverting {ordered.Count} commit(s)…", () =>
            {
                foreach (var sha in ordered)
                    _git.Revert(sha);
            });
            if (ok) await CaptureUndo(UndoKind.HeadHardMove, $"Revert {ordered.Count} commit(s)", preHead);
            await LoadWorkingDirAsync();
            await RefreshAsync();
        }
    }
}
