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

        private void SelectTag(object param)
        {
            if (!(param is TagInfo ti)) return;
            var target = GraphNodes.FirstOrDefault(n => n.Commit?.Sha == ti.TargetSha);
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
            // Any commit-selection change invalidates the multi-select file pick and its range diff
            // (done before IsMultiSelection flips, so SelectedFile's setter doesn't see a mixed state).
            ClearAggregatedFileSelection();
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
                CommitFiles = new ObservableCollection<FileChange>();
                _aggregatedFiles.Clear();
                RebuildAggregatedFileTreeRows();
                RaiseDetailPanelPropertiesChanged();
                return;
            }

            _selectedNode = _selectedNodes[_selectedNodes.Count - 1];
            RaisePropertyChanged(nameof(SelectedNode));

            if (_selectedNodes.Count == 1)
            {
                _aggregatedFiles.Clear();
                RebuildAggregatedFileTreeRows();
                var node = _selectedNodes[0];
                if (node.Commit?.IsUncommitted == true) { ShowWorkingDir = true; _ = LoadWorkingDirAsync(); }
                else { ShowWorkingDir = false; LoadCommitDetail(node.Commit.Sha); }
            }
            else
            {
                DetailCommit = null;
                CommitFiles = new ObservableCollection<FileChange>();
                ShowWorkingDir = false;
                // A file picked in the previous context (e.g. an unstaged file under the WIP node)
                // must not carry over — it would keep its working-tree diff live under multi-select.
                SelectedFile = null;
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
            _aggregatedShas = shas;

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

            var ordered = map.Select(kv => new AggregatedFileChange
            {
                Path = kv.Key,
                CommitCount = kv.Value.count,
                Kind = kv.Value.kind
            }).ToList();
            ordered.Sort(BuildAggregatedFileComparer());

            _aggregatedFiles.Clear();
            foreach (var f in ordered) _aggregatedFiles.Add(f);
            RebuildAggregatedFileTreeRows();
        }

        // ── Multi-select "Files changed" selection ────────────────────────────

        private AggregatedFileChange _selectedAggregatedFile;
        /// <summary>Flat-mode selection in the multi-select "Files changed" panel. Null from the view
        /// (an ItemsSource rebuild dropping the row) is ignored — it's cleared by a commit-selection
        /// change or by SelectedFile moving off the aggregated pick (DropAggregatedRowSelection).</summary>
        public AggregatedFileChange SelectedAggregatedFile
        {
            get => _selectedAggregatedFile;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedAggregatedFile)) return;
                SelectAggregatedFile(value);
            }
        }

        private AggregatedFileTreeRow _selectedAggregatedFilesTreeRow;
        /// <summary>Tree-mode selection in the multi-select "Files changed" panel; folder rows don't
        /// select a file.</summary>
        public AggregatedFileTreeRow SelectedAggregatedFilesTreeRow
        {
            get => _selectedAggregatedFilesTreeRow;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedAggregatedFilesTreeRow)) return;
                if (value.Kind != FileTreeRowKind.File || value.File == null)
                {
                    Set(ref _selectedAggregatedFilesTreeRow, value);
                    return;
                }
                SelectAggregatedFile(value.File);
            }
        }

        // The FileChange handed to SelectedFile for the current aggregated selection — lets a commit
        // selection change clear SelectedFile only when it came from this panel.
        private FileChange _aggregatedSelectedFileChange;

        private void SelectAggregatedFile(AggregatedFileChange file)
        {
            // Keep the Flat and Tree lists pointing at the same file so toggling FileViewMode agrees.
            Set(ref _selectedAggregatedFile, file, nameof(SelectedAggregatedFile));
            Set(ref _selectedAggregatedFilesTreeRow,
                AggregatedFilesTreeRows?.FirstOrDefault(r => r.Kind == FileTreeRowKind.File && ReferenceEquals(r.File, file)),
                nameof(SelectedAggregatedFilesTreeRow));
            _aggregatedSelectedFileChange = new FileChange { Path = file.Path, Kind = file.Kind };
            SelectedFile = _aggregatedSelectedFileChange;
        }

        /// <summary>True when <see cref="SelectedFile"/> is the file picked from the multi-select panel
        /// (not a leftover from a previous selection context).</summary>
        private bool IsAggregatedFileSelected =>
            _aggregatedSelectedFileChange != null && ReferenceEquals(_selectedFile, _aggregatedSelectedFileChange);

        /// <summary>Un-highlights the multi-select panel's row so the same file can be clicked again —
        /// called whenever SelectedFile moves off the aggregated pick (closing the diff, etc.).</summary>
        private void DropAggregatedRowSelection()
        {
            _aggregatedSelectedFileChange = null;
            Set(ref _selectedAggregatedFile, null, nameof(SelectedAggregatedFile));
            Set(ref _selectedAggregatedFilesTreeRow, null, nameof(SelectedAggregatedFilesTreeRow));
        }

        private void ClearAggregatedFileSelection()
        {
            ++_rangeDiffVersion; // drop any in-flight range diff
            if (IsAggregatedFileSelected)
            {
                SelectedFile = null;
                FlatDiffItems = Array.Empty<DiffItem>();
                SideBySideItems = Array.Empty<SideBySideItem>();
            }
            DropAggregatedRowSelection();
        }

        // SHAs the aggregated "Files changed" panel was built from. The range diff uses this snapshot,
        // not _selectedNodes, because a background refresh's ReselectSameCommit can collapse
        // _selectedNodes to one node while the panel (and SelectedCount) still show the multi-select.
        private List<string> _aggregatedShas = new List<string>();

        private int _rangeDiffVersion;

        /// <summary>Multi-select file diff: the oldest selected commit's parent → the newest selected
        /// commit, by graph order (not date — a rebased commit's dates can disagree with its position).
        /// A non-contiguous selection deliberately includes the unselected commits in between.</summary>
        private async Task LoadRangeDiffAsync(string filePath)
        {
            var version = ++_rangeDiffVersion;
            var selected = new HashSet<string>(_aggregatedShas);
            // GraphNodes is ordered newest-first: the first selected hit is the newest, the last the oldest.
            var ordered = GraphNodes.Where(n => n.Commit?.Sha != null && selected.Contains(n.Commit.Sha)).ToList();

            FlatDiffItems = Array.Empty<DiffItem>();
            SideBySideItems = Array.Empty<SideBySideItem>();
            _currentDiffFile = null;
            SetCurrentDecodedDiff(null);
            IsDiffFromStagedFile = false;
            IsConflictedFileDiff = false;
            IsLfsPointerDiff = false;
            IsBinaryDiff = false;
            IsLargeDiffPending = false;
            IsImageDiff = false;
            OldImage = null;
            NewImage = null;
            RaisePropertyChanged(nameof(IsWorkingDirDiff));
            RaisePropertyChanged(nameof(CanPartialStage));
            if (!_git.IsOpen || ordered.Count == 0) return;

            var newestSha = ordered[0].Commit.Sha;
            var oldestSha = ordered[ordered.Count - 1].Commit.Sha;
            var contextLines = ContextLines;
            // Invoked fire-and-forget from SelectedFile's setter — without this try/catch an exception
            // is an unobserved task exception and the (already cleared) pane just stays blank.
            try
            {
                var diff = await _git.Executor.RunAsync(() => _git.GetRangeFileDiff(oldestSha, newestSha, filePath, contextLines));
                if (version != _rangeDiffVersion) return; // selection moved on while loading
                ApplyDiffResult(diff);
            }
            catch (Exception ex)
            {
                if (version == _rangeDiffVersion) DialogService.ShowError("Diff", ex.Message);
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
                Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*",
                InitialDirectory = RepoPath
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
                Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*",
                InitialDirectory = RepoPath
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
