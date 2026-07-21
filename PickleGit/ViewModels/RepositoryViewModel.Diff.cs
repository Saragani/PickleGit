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
    /// <summary>Diff loading and rendering state, hunk/line staging, diff search and navigation.</summary>
    public partial class RepositoryViewModel
    {
        private async Task LoadDiffAsync(string sha, string filePath)
        {
            FlatDiffItems = Array.Empty<DiffItem>();
            SideBySideItems = Array.Empty<SideBySideItem>();
            _currentDiffFile = null;
            IsDiffFromStagedFile = false;
            IsLfsPointerDiff = false;
            IsBinaryDiff = false;
            IsLargeDiffPending = false;
            IsImageDiff = false;
            OldImage = null;
            NewImage = null;
            RaisePropertyChanged(nameof(IsWorkingDirDiff));
            RaisePropertyChanged(nameof(CanPartialStage));
            if (!_git.IsOpen) return;
            // This is invoked fire-and-forget from SelectedFile's setter (and elsewhere); an
            // unhandled exception here would be an unobserved task exception — no dialog, no
            // crash, the pane just stays blank (it was already cleared above) with zero trace.
            try
            {
                if (GitService.IsImagePath(filePath))
                {
                    var images = await _git.Executor.RunAsync(() => _git.GetImageDiff(sha, filePath));
                    if ((images.Old?.Length ?? 0) > LargeImageByteThreshold ||
                        (images.New?.Length ?? 0) > LargeImageByteThreshold)
                    {
                        IsBinaryDiff = true; // too big to decode — show the binary notice instead
                        return;
                    }
                    OldImage = BytesToImage(images.Old);
                    NewImage = BytesToImage(images.New);
                    IsImageDiff = true;
                    return;
                }
                var contextLines = ContextLines;
                var diff = await _git.Executor.RunAsync(() =>
                    IgnoreWhitespace && _git.Cli != null && _git.Cli.IsAvailable
                        ? _git.GetFileDiffIgnoreWhitespace(sha, filePath, false, contextLines)
                        : _git.GetFileDiff(sha, filePath, contextLines));
                ApplyDiffResult(diff);
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Diff", ex.Message);
            }
        }

        private bool _isSideBySide;
        public bool IsSideBySide
        {
            get => _isSideBySide;
            set { if (Set(ref _isSideBySide, value)) RaiseDiffModeChanged(); }
        }

        private bool _ignoreWhitespace = AppSettings.LoadDiffIgnoreWhitespace();
        /// <summary>Diffs run through `git diff -w` when set. Partial staging is disabled while
        /// active — whitespace-stripped patches would not apply cleanly.</summary>
        public bool IgnoreWhitespace
        {
            get => _ignoreWhitespace;
            set
            {
                if (!Set(ref _ignoreWhitespace, value)) return;
                AppSettings.SaveDiffIgnoreWhitespace(value);
                RaisePropertyChanged(nameof(CanPartialStage));
                _ = ReloadCurrentDiffOrCommitDiffAsync();
            }
        }

        /// <summary>Hunk/line staging is only offered for working-dir diffs produced without -w.</summary>
        public bool CanPartialStage => IsWorkingDirDiff && !IgnoreWhitespace && !IsConflictedFileDiff;

        private bool _showEntireFile = AppSettings.LoadDiffShowEntireFile();
        /// <summary>Toggle, orthogonal to Unified/Side-by-side: shows the whole file
        /// as context around each change (large CompareOptions.ContextLines/-U value) instead of just
        /// the surrounding hunk lines.</summary>
        public bool ShowEntireFile
        {
            get => _showEntireFile;
            set
            {
                if (!Set(ref _showEntireFile, value)) return;
                AppSettings.SaveDiffShowEntireFile(value);
                _ = ReloadCurrentDiffOrCommitDiffAsync();
            }
        }

        private int ContextLines => ShowEntireFile ? GitService.FullFileContextLines : GitService.DefaultContextLines;

        private async Task ReloadCurrentDiffOrCommitDiffAsync()
        {
            if (_currentDiffFile != null) { await ReloadCurrentDiffAsync(); return; }
            if (DiffPaneMode == DiffPaneMode.History && !IsBlameContent && SelectedFileHistoryCommit != null)
            {
                await LoadDiffAsync(SelectedFileHistoryCommit.Sha, _blameHistoryFilePath);
                return;
            }
            if (_detailCommit != null && SelectedFile != null)
                await LoadDiffAsync(_detailCommit.Sha, SelectedFile.Path);
        }

        public ICommand ShowUnifiedDiffCommand { get; }
        public ICommand ShowSideBySideDiffCommand { get; }

        private IReadOnlyList<SideBySideItem> _sideBySideItems = Array.Empty<SideBySideItem>();
        public IReadOnlyList<SideBySideItem> SideBySideItems
        {
            get => _sideBySideItems;
            private set
            {
                if (!Set(ref _sideBySideItems, value)) return;
                // One mark per row for the change-map strip. A modified line pairs a Deleted (left)
                // with an Added (right) in the same row — Added wins arbitrarily since the map is a
                // single column, not per-pane; it still lands on the right row either way.
                SideBySideRowKinds = value.Select(i =>
                {
                    if (i.Kind != DiffItemKind.Line) return DiffLineKind.Header;
                    if (i.Right?.Kind == DiffLineKind.Added) return DiffLineKind.Added;
                    if (i.Left?.Kind == DiffLineKind.Deleted) return DiffLineKind.Deleted;
                    return DiffLineKind.Context;
                }).ToArray();
            }
        }

        private IReadOnlyList<DiffLineKind> _sideBySideRowKinds = Array.Empty<DiffLineKind>();
        /// <summary>One entry per SideBySideItems row, feeding DiffChangeMapControl's change-location
        /// strip next to the side-by-side diff's scrollbar.</summary>
        public IReadOnlyList<DiffLineKind> SideBySideRowKinds { get => _sideBySideRowKinds; private set => Set(ref _sideBySideRowKinds, value); }

        private static List<SideBySideItem> BuildSideBySideItems(List<DiffHunk> hunks)
        {
            var result = new List<SideBySideItem>();
            foreach (var hunk in hunks)
            {
                result.Add(new SideBySideItem { Kind = DiffItemKind.HunkHeader, Header = hunk.Header, Hunk = hunk });
                var lines = hunk.Lines;
                int i = 0;
                while (i < lines.Count)
                {
                    if (lines[i].Kind == DiffLineKind.Context)
                    {
                        result.Add(new SideBySideItem { Kind = DiffItemKind.Line, Left = lines[i], Right = lines[i] });
                        i++;
                        continue;
                    }
                    int delStart = i;
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Deleted) i++;
                    int delCount = i - delStart;
                    int addStart = i;
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Added) i++;
                    int addCount = i - addStart;
                    int max = Math.Max(delCount, addCount);
                    for (int k = 0; k < max; k++)
                    {
                        result.Add(new SideBySideItem
                        {
                            Kind = DiffItemKind.Line,
                            Left = k < delCount ? lines[delStart + k] : null,
                            Right = k < addCount ? lines[addStart + k] : null
                        });
                    }
                }
            }
            return result;
        }

        private bool _isImageDiff;
        /// <summary>True when the selected diff is an image file — DiffView shows an old/new preview instead of text.</summary>
        public bool IsImageDiff
        {
            get => _isImageDiff;
            private set { if (Set(ref _isImageDiff, value)) RaiseDiffModeChanged(); }
        }

        private void RaiseDiffModeChanged()
        {
            RaisePropertyChanged(nameof(ShowUnifiedDiff));
            RaisePropertyChanged(nameof(ShowSideBySideDiff));
        }

        /// <summary>True when the unified (single-column) diff list should be shown — also used by
        /// History mode (reusing the same diff rendering for whichever commit is selected), so this
        /// only excludes blame content, not History mode itself.</summary>
        public bool ShowUnifiedDiff => !ShowBlame && !IsImageDiff && !IsSideBySide;

        /// <summary>True when the side-by-side (old | new) diff list should be shown.</summary>
        public bool ShowSideBySideDiff => !ShowBlame && !IsImageDiff && IsSideBySide;

        private System.Windows.Media.Imaging.BitmapImage _oldImage;
        public System.Windows.Media.Imaging.BitmapImage OldImage { get => _oldImage; private set => Set(ref _oldImage, value); }

        private System.Windows.Media.Imaging.BitmapImage _newImage;
        public System.Windows.Media.Imaging.BitmapImage NewImage { get => _newImage; private set => Set(ref _newImage, value); }

        private static System.Windows.Media.Imaging.BitmapImage BytesToImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }

        private static List<DiffItem> BuildFlatDiffItems(List<DiffHunk> hunks)
        {
            var flat = new List<DiffItem>(hunks.Sum(h => h.Lines.Count + 1));
            foreach (var hunk in hunks)
            {
                flat.Add(new DiffItem { Kind = DiffItemKind.HunkHeader, Header = hunk.Header, Hunk = hunk });
                foreach (var line in hunk.Lines)
                    flat.Add(new DiffItem { Kind = DiffItemKind.Line, Line = line, Hunk = hunk });
            }
            return flat;
        }

        private bool _isLfsPointerDiff;
        /// <summary>True when the diff being shown is a Git LFS pointer file — the raw pointer text
        /// (oid/size) is meaningless to a user, so DiffView shows a badge instead.</summary>
        public bool IsLfsPointerDiff { get => _isLfsPointerDiff; private set => Set(ref _isLfsPointerDiff, value); }

        private bool _isBinaryDiff;
        /// <summary>True when the current file compares as binary (and isn't a previewable image) —
        /// DiffView shows a "binary file" notice instead of a blank pane.</summary>
        public bool IsBinaryDiff { get => _isBinaryDiff; private set => Set(ref _isBinaryDiff, value); }

        private bool _isConflictedFileDiff;
        /// <summary>True when the selected file has unmerged index entries (a live merge/rebase/cherry-pick
        /// conflict) — LibGit2Sharp's Diff.Compare can't produce a patch against an unmerged path (it silently
        /// returns zero hunks), so DiffView shows a conflict notice + "Open Merge Editor" button instead of a
        /// blank pane.</summary>
        public bool IsConflictedFileDiff { get => _isConflictedFileDiff; private set => Set(ref _isConflictedFileDiff, value); }

        // ── Large-diff guard ─────────────────────────────────────────────────
        private const int LargeDiffLineThreshold = 15000;
        private const int LargeImageByteThreshold = 25 * 1024 * 1024;
        private FileDiffResult _pendingLargeDiff;

        private bool _isLargeDiffPending;
        /// <summary>True when the diff exceeded the render threshold — a banner offers to load it anyway.</summary>
        public bool IsLargeDiffPending { get => _isLargeDiffPending; private set => Set(ref _isLargeDiffPending, value); }

        private string _largeDiffInfo;
        public string LargeDiffInfo { get => _largeDiffInfo; private set => Set(ref _largeDiffInfo, value); }

        public ICommand LoadLargeDiffCommand { get; private set; }

        /// <summary>Applies a parsed diff to the panes, or parks it behind the large-diff banner.</summary>
        private void ApplyDiffResult(FileDiffResult diff, bool bypassSizeGuard = false)
        {
            var totalLines = diff.Hunks.Sum(h => h.Lines.Count);
            if (!bypassSizeGuard && totalLines > LargeDiffLineThreshold)
            {
                _pendingLargeDiff = diff;
                LargeDiffInfo = $"Large diff ({totalLines:N0} lines) — not rendered automatically.";
                IsLargeDiffPending = true;
                FlatDiffItems = Array.Empty<DiffItem>();
                SideBySideItems = Array.Empty<SideBySideItem>();
                IsBinaryDiff = false; IsLfsPointerDiff = false;
                return;
            }
            _pendingLargeDiff = null;
            IsLargeDiffPending = false;
            FlatDiffItems = BuildFlatDiffItems(diff.Hunks);
            SideBySideItems = BuildSideBySideItems(diff.Hunks);
            IsLfsPointerDiff = DetectLfsPointer(diff.Hunks);
            IsBinaryDiff = diff.IsBinary;
            if (IsDiffSearchOpen) RecomputeDiffSearch(); // matches referenced the old items
        }

        private void LoadLargeDiff()
        {
            var pending = _pendingLargeDiff;
            if (pending != null) ApplyDiffResult(pending, bypassSizeGuard: true);
        }

        private static bool DetectLfsPointer(List<DiffHunk> hunks)
        {
            const string signature = "git-lfs.github.com/spec";
            foreach (var h in hunks)
                foreach (var l in h.Lines)
                    if (l.Content != null && l.Content.IndexOf(signature, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            return false;
        }

        // ── Working-directory diff (staged / unstaged files) — hunk & line staging ─

        private FileChange _currentDiffFile;

        private bool _isDiffFromStagedFile;
        public bool IsDiffFromStagedFile { get => _isDiffFromStagedFile; private set => Set(ref _isDiffFromStagedFile, value); }

        /// <summary>True when the diff panel is showing a staged/unstaged working-dir file (hunk/line actions apply).</summary>
        public bool IsWorkingDirDiff => _currentDiffFile != null;

        private async Task LoadWorkingDiffAsync(FileChange fc)
        {
            FlatDiffItems = Array.Empty<DiffItem>();
            SideBySideItems = Array.Empty<SideBySideItem>();
            DetailCommit = null;
            _currentDiffFile = fc;
            IsDiffFromStagedFile = fc.IsStaged;
            IsLfsPointerDiff = false;
            IsBinaryDiff = false;
            IsLargeDiffPending = false;
            IsImageDiff = false;
            IsConflictedFileDiff = false;
            OldImage = null;
            NewImage = null;
            RaisePropertyChanged(nameof(IsWorkingDirDiff));
            RaisePropertyChanged(nameof(CanPartialStage));
            if (!_git.IsOpen) return;
            if (fc.Kind == FileChangeKind.Conflicted)
            {
                IsConflictedFileDiff = true;
                RaisePropertyChanged(nameof(CanPartialStage));
                return;
            }
            // Invoked fire-and-forget from SelectedFile's setter; without this try/catch an
            // exception here is an unobserved task exception — the pane (already cleared above)
            // just stays blank with no dialog and no trace.
            try
            {
                if (GitService.IsImagePath(fc.Path))
                {
                    var images = await _git.Executor.RunAsync(() =>
                        fc.IsStaged ? _git.GetStagedImageDiff(fc.Path) : _git.GetUnstagedImageDiff(fc.Path));
                    if (!ReferenceEquals(_currentDiffFile, fc)) return;
                    if ((images.Old?.Length ?? 0) > LargeImageByteThreshold ||
                        (images.New?.Length ?? 0) > LargeImageByteThreshold)
                    {
                        IsBinaryDiff = true; // too big to decode — show the binary notice instead
                        return;
                    }
                    OldImage = BytesToImage(images.Old);
                    NewImage = BytesToImage(images.New);
                    IsImageDiff = true;
                    return;
                }
                // Untracked files never appear in `git diff -w`, so they always use the libgit2 path
                var useW = IgnoreWhitespace && _git.Cli != null && _git.Cli.IsAvailable
                    && fc.Kind != FileChangeKind.Untracked;
                var contextLines = ContextLines;
                var diff = await _git.Executor.RunAsync(() =>
                    useW ? _git.GetFileDiffIgnoreWhitespace(null, fc.Path, fc.IsStaged, contextLines)
                         : fc.IsStaged ? _git.GetStagedFileDiff(fc.Path, contextLines) : _git.GetUnstagedFileDiff(fc.Path, contextLines));
                if (!ReferenceEquals(_currentDiffFile, fc)) return; // a newer selection has already superseded this one
                ApplyDiffResult(diff);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_currentDiffFile, fc)) DialogService.ShowError("Diff", ex.Message);
            }
        }

        /// <summary>Re-fetches the diff for whatever file/direction is currently shown, after a hunk/line action.</summary>
        private async Task ReloadCurrentDiffAsync()
        {
            if (_currentDiffFile == null) return;
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        /// <summary>
        /// Re-points the diff pane at the file it was showing after WorkingDirFiles/StagedFiles have just
        /// been rebuilt with fresh FileChange instances (every Stage/Unstage/Discard does this). Without this,
        /// a stage/unstage/discard of a file whose diff is currently open leaves FlatDiffItems showing stale
        /// hunks — checking a line and clicking Discard/Stage Selected then builds a patch from content that
        /// no longer matches the working tree, and `git apply` fails with "patch does not apply".
        /// </summary>
        private async Task SyncDiffPaneAfterFileListChangeAsync()
        {
            if (_currentDiffFile == null) return;
            var path = _currentDiffFile.Path;
            var staged = _currentDiffFile.IsStaged;
            var refreshed = (staged ? StagedFiles : WorkingDirFiles)
                .FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            // The ListView's SelectedItem binding drops the stale FileChange reference back to null once
            // the ItemsSource is replaced — restore it directly (bypassing the SelectedFile setter, which
            // would re-trigger a redundant load).
            if (refreshed != null)
            {
                _selectedFile = refreshed;
                RaisePropertyChanged(nameof(SelectedFile));
                RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
                await LoadWorkingDiffAsync(refreshed);
            }
            else
            {
                _selectedFile = null;
                RaisePropertyChanged(nameof(SelectedFile));
                RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
                FlatDiffItems = Array.Empty<DiffItem>();
                SideBySideItems = Array.Empty<SideBySideItem>();
                IsLfsPointerDiff = false;
                IsBinaryDiff = false;
                IsLargeDiffPending = false;
                _currentDiffFile = null;
                RaisePropertyChanged(nameof(IsWorkingDirDiff));
                RaisePropertyChanged(nameof(CanPartialStage));
            }
        }

        // ── Line selection (click/shift/drag in the diff ListView(s)) ──────────
        // Selection lives here, not on DiffItem — the ListView's own SelectedItems is the source of
        // truth for "is this row highlighted"; this set is what hunk-scoped staging/discard reads.
        private readonly HashSet<DiffLine> _selectedLines = new HashSet<DiffLine>();

        /// <summary>Called by DiffView's code-behind whenever a diff ListView's selection changes —
        /// the unified list, or the merged selection across the side-by-side pair (see
        /// DiffView.xaml.cs; DiffLine identity is shared across both projections of the same hunks,
        /// so a line selected in either view is attributable to its hunk regardless of origin).</summary>
        public void UpdateDiffLineSelection(IEnumerable<DiffLine> selected)
        {
            _selectedLines.Clear();
            foreach (var line in selected) _selectedLines.Add(line);
            RecomputeHunkSelectionFlags();
        }

        private void RecomputeHunkSelectionFlags()
        {
            var seen = new HashSet<DiffHunk>();
            foreach (var item in FlatDiffItems)
            {
                var hunk = item.Hunk;
                if (hunk == null || !seen.Add(hunk)) continue;
                hunk.HasLineSelection = hunk.Lines.Any(l => l.Kind != DiffLineKind.Context && _selectedLines.Contains(l));
            }
        }

        private Task StageHunkOrLinesAsync(object param) => HunkOrLinesPatchAsync(param, StagingPatchOp.Stage, "Staging…", "Stage");

        private async Task DiscardHunkOrLinesAsync(object param)
        {
            if (!(param is DiffHunk hunk)) return;
            var message = hunk.HasLineSelection
                ? "Discard the selected lines?\nThis cannot be undone."
                : "Discard this hunk's changes?\nThis cannot be undone.";
            if (!DialogService.Confirm("Discard", message, "Discard", danger: true)) return;
            await HunkOrLinesPatchAsync(param, StagingPatchOp.Discard, "Discarding…", "Discard");
        }

        private Task UnstageHunkOrLinesAsync(object param) => HunkOrLinesPatchAsync(param, StagingPatchOp.Unstage, "Unstaging…", "Unstage");

        /// <summary>Stages/discards/unstages just the hunk's currently-selected lines when it has a
        /// selection, otherwise the whole hunk — mirrors SourceTree's per-hunk button behavior.</summary>
        private async Task HunkOrLinesPatchAsync(object param, StagingPatchOp op, string status, string featureName)
        {
            if (!(param is DiffHunk hunk) || _currentDiffFile == null) return;
            string patch;
            if (hunk.HasLineSelection)
            {
                var selected = new HashSet<DiffLine>(hunk.Lines.Where(l => l.Kind != DiffLineKind.Context && _selectedLines.Contains(l)));
                patch = StagingService.BuildLinePatch(_currentDiffFile, new List<(DiffHunk, HashSet<DiffLine>)> { (hunk, selected) }, op);
            }
            else
            {
                patch = StagingService.BuildHunkPatch(_currentDiffFile, hunk);
            }
            if (await RunCliAsync(status, StagingService.ApplyArgs(op), featureName, patch))
                await ReloadCurrentDiffAsync();
        }

        // ── Diff navigation (next/prev hunk) ────────────────────────────────────

        /// <summary>Payload is a DiffItem (unified) or SideBySideItem hunk header — MainWindow
        /// scrolls whichever diff ListView holds items of that type.</summary>
        public event EventHandler<object> ScrollToDiffItemRequested;

        // ── Find in diff ─────────────────────────────────────────────────────

        private bool _isDiffSearchOpen;
        public bool IsDiffSearchOpen
        {
            get => _isDiffSearchOpen;
            set { if (Set(ref _isDiffSearchOpen, value) && !value) DiffSearchText = string.Empty; }
        }

        private string _diffSearchText;
        public string DiffSearchText
        {
            get => _diffSearchText;
            set { if (Set(ref _diffSearchText, value)) RecomputeDiffSearch(); }
        }

        private List<DiffItem> _diffMatches = new List<DiffItem>();
        private int _diffMatchPos = -1;

        private string _diffSearchStatus;
        public string DiffSearchStatus { get => _diffSearchStatus; private set => Set(ref _diffSearchStatus, value); }

        public ICommand NextDiffMatchCommand { get; private set; }
        public ICommand PrevDiffMatchCommand { get; private set; }

        private void RecomputeDiffSearch()
        {
            _diffMatches.Clear();
            _diffMatchPos = -1;
            var term = _diffSearchText?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                foreach (var item in FlatDiffItems)
                    if (item.Kind == DiffItemKind.Line && item.Line?.Content != null &&
                        item.Line.Content.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        _diffMatches.Add(item);
            }
            DiffSearchStatus = string.IsNullOrEmpty(term) ? null
                : _diffMatches.Count == 0 ? "0 matches" : $"{_diffMatches.Count} matches";
            if (_diffMatches.Count > 0) NavigateDiffMatch(+1);
        }

        private void NavigateDiffMatch(int direction)
        {
            if (_diffMatches.Count == 0) return;
            _diffMatchPos = ((_diffMatchPos + direction) % _diffMatches.Count + _diffMatches.Count) % _diffMatches.Count;
            DiffSearchStatus = $"{_diffMatchPos + 1} of {_diffMatches.Count}";
            ScrollToDiffItemRequested?.Invoke(this, _diffMatches[_diffMatchPos]);
        }
        private int _currentHunkIndex = -1;

        private void NavigateHunk(int direction)
        {
            var items = FlatDiffItems;
            var hunkIndices = new List<int>();
            for (int i = 0; i < items.Count; i++)
                if (items[i].Kind == DiffItemKind.HunkHeader) hunkIndices.Add(i);
            if (hunkIndices.Count == 0) return;

            int pos = hunkIndices.IndexOf(_currentHunkIndex);
            int newPos = pos < 0
                ? (direction > 0 ? 0 : hunkIndices.Count - 1)
                : ((pos + direction) % hunkIndices.Count + hunkIndices.Count) % hunkIndices.Count;
            _currentHunkIndex = hunkIndices[newPos];

            if (ShowSideBySideDiff)
            {
                // Hunk headers appear in the same order in both lists — scroll the Nth one there
                var sbsHeader = SideBySideItems.Where(i => i.Kind == DiffItemKind.HunkHeader)
                                               .ElementAtOrDefault(newPos);
                if (sbsHeader != null) { ScrollToDiffItemRequested?.Invoke(this, sbsHeader); return; }
            }
            ScrollToDiffItemRequested?.Invoke(this, items[_currentHunkIndex]);
        }

        // ── Discard ───────────────────────────────────────────────────────────
        // Discarding a staged file works the same way as an unstaged one: DiscardFiles ultimately
        // checks the path out from HEAD into both the index and working tree (libgit2
        // CheckoutPaths), so it reverts the staged change too, not just the working-dir copy.

        /// <summary>Shared confirm+execute tail for every discard entry point below.</summary>
        private async Task ConfirmAndDiscardAsync(List<FileChange> targets, string allFilesMessage, bool discardingSelection)
        {
            if (targets.Count == 0) return;
            string message;
            if (!discardingSelection)
                message = allFilesMessage;
            else if (targets.Count == 1)
                message = $"Discard changes to '{targets[0].Path}'?\nThis cannot be undone.";
            else
                message = $"Discard changes to {targets.Count} files?\nThis cannot be undone.";
            if (AppSettings.LoadConfirmBeforeDiscard() && !DialogService.Confirm("Discard Changes",
                    message, "Discard", danger: true))
                return;
            await RunAsync("Discarding changes…", () => _git.DiscardFiles(targets));
            await LoadWorkingDirAsync();
            await SyncDiffPaneAfterFileListChangeAsync();
        }

        /// <summary>Context-menu/row "Discard changes" for an unstaged file (or the current
        /// multi-selection, if the clicked row is part of it).</summary>
        private Task DiscardFileAsync(object param)
            => ConfirmAndDiscardAsync(StagingService.ResolveTargets(_selectedWorkingFiles, param),
                allFilesMessage: null, discardingSelection: true);

        /// <summary>Context-menu/row "Discard changes" for a staged file — same semantics as
        /// DiscardFileAsync, just resolved against the staged list/selection.</summary>
        private Task DiscardStagedFileAsync(object param)
            => ConfirmAndDiscardAsync(StagingService.ResolveTargets(_selectedStagedFiles, param),
                allFilesMessage: null, discardingSelection: true);

        /// <summary>The single bulk discard button next to "Unstaged Files": acts on whichever files
        /// are multi-selected across EITHER list (staged or unstaged) once 2+ are selected combined,
        /// otherwise discards every change — staged and unstaged alike (matches StageAllAsync's same
        /// "a lone selection doesn't narrow scope" rule).</summary>
        private Task DiscardAllOrSelectedAsync()
        {
            var combinedSelection = _selectedWorkingFiles.Concat(_selectedStagedFiles).ToList();
            bool discardingSelection = combinedSelection.Count >= 2;
            var targets = discardingSelection ? combinedSelection : WorkingDirFiles.Concat(StagedFiles).ToList();
            return ConfirmAndDiscardAsync(targets,
                "Discard all changes?\nThis cannot be undone.", discardingSelection);
        }
    }
}
