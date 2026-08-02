using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    /// <summary>Blame and per-file history, rendered inline in the main diff pane (DiffView) as
    /// toggle buttons alongside Unified/Side-by-side, rather than the
    /// separate popup window this used to be (Views/FileHistoryWindow.xaml, retired).</summary>
    public partial class RepositoryViewModel
    {
        private DiffPaneMode _diffPaneMode = DiffPaneMode.Diff;
        public DiffPaneMode DiffPaneMode
        {
            get => _diffPaneMode;
            private set
            {
                if (!Set(ref _diffPaneMode, value)) return;
                RaiseDiffModeChanged();
                RaisePropertyChanged(nameof(ShowBlame));
                RaisePropertyChanged(nameof(ShowHistoryCommitList));
            }
        }

        private bool _isBlameContent;
        /// <summary>Only meaningful within History mode —
        /// Blame is not a separate top-level mode): toggles the main content between the selected
        /// commit's diff (false, default) and a full blame of the file as of that commit (true).</summary>
        public bool IsBlameContent
        {
            get => _isBlameContent;
            set
            {
                if (!Set(ref _isBlameContent, value)) return;
                RaisePropertyChanged(nameof(ShowBlame));
                RaiseDiffModeChanged();
                if (value) _ = LoadBlameForHistoryAsync(SelectedFileHistoryCommit?.Sha, SelectedFileHistoryCommit?.HistoryPath ?? _blameHistoryFilePath);
            }
        }

        /// <summary>Sets IsBlameContent without triggering a (possibly premature, stale-commit) blame
        /// load — used by EnterHistoryModeAsync, which selects the first history commit right after,
        /// and that SelectedFileHistoryCommit assignment is what actually triggers the correct load.</summary>
        private void SetBlameContentSilently(bool value)
        {
            if (!Set(ref _isBlameContent, value, nameof(IsBlameContent))) return;
            RaisePropertyChanged(nameof(ShowBlame));
            RaiseDiffModeChanged();
        }

        /// <summary>True when the blame view should be shown instead of the normal diff content —
        /// only reachable within History mode (see IsBlameContent).</summary>
        public bool ShowBlame => DiffPaneMode == DiffPaneMode.History && IsBlameContent;

        /// <summary>True when the file-history commit list column should be shown alongside the main
        /// content — either a commit's diff or, when IsBlameContent, that commit's blame (History
        /// reuses the same Unified/Side-by-side rendering for whichever commit is selected — see
        /// SelectedFileHistoryCommit).</summary>
        public bool ShowHistoryCommitList => DiffPaneMode == DiffPaneMode.History;

        private string _blameHistoryFilePath;

        public ObservableCollection<BlameLine> BlameLines { get; } = new ObservableCollection<BlameLine>();
        public ObservableCollection<CommitInfo> FileHistoryCommits { get; } = new ObservableCollection<CommitInfo>();

        private CommitInfo _selectedFileHistoryCommit;
        public CommitInfo SelectedFileHistoryCommit
        {
            get => _selectedFileHistoryCommit;
            set
            {
                if (!Set(ref _selectedFileHistoryCommit, value) || value == null) return;
                // HistoryPath is the path the file actually had AT this commit (see CommitInfo) —
                // falls back to the path the history list was opened with only when unset (the
                // libgit2 QueryBy fallback path in GetFileHistory doesn't populate it). Using the
                // wrong (later, post-rename) path here is exactly what made Blame/diff come back
                // empty for commits from before a rename.
                var pathAtCommit = value.HistoryPath ?? _blameHistoryFilePath;
                if (IsBlameContent) _ = LoadBlameForHistoryAsync(value.Sha, pathAtCommit);
                else _ = LoadDiffAsync(value.Sha, pathAtCommit);
            }
        }

        public ICommand ShowDiffModeCommand { get; private set; }
        public ICommand ShowBlameModeCommand { get; private set; }
        public ICommand ShowHistoryModeCommand { get; private set; }
        public ICommand ShowCommitDiffContentCommand { get; private set; }
        public ICommand ShowBlameContentCommand { get; private set; }

        private void InitializeBlameCommands()
        {
            ShowDiffModeCommand = new RelayCommand(() => DiffPaneMode = DiffPaneMode.Diff, () => HasRepo);
            // "Blame" from a file's context menu — jumps straight into History mode with blame content
            // active (blame is reached by navigating history, not a separate mode).
            ShowBlameModeCommand = new RelayCommand(param => _ = EnterHistoryModeAsync(ResolveFilePath(param), blameContent: true), _ => HasRepo);
            ShowHistoryModeCommand = new RelayCommand(param => _ = EnterHistoryModeAsync(ResolveFilePath(param)), _ => HasRepo);
            // In-panel sub-toggle, only shown while already in History mode (see DiffView.xaml).
            ShowCommitDiffContentCommand = new RelayCommand(() =>
            {
                IsBlameContent = false;
                if (SelectedFileHistoryCommit != null)
                    _ = LoadDiffAsync(SelectedFileHistoryCommit.Sha, SelectedFileHistoryCommit.HistoryPath ?? _blameHistoryFilePath);
            }, () => HasRepo);
            ShowBlameContentCommand = new RelayCommand(() => IsBlameContent = true, () => HasRepo);
        }

        private static string ResolveFilePath(object param) =>
            (param as FileChange)?.Path ?? (param as AggregatedFileChange)?.Path;

        /// <summary>Blame/History show a file outside the working-dir-diff context (a plain
        /// working-tree read, or an arbitrary historical commit's diff) — clear any stale
        /// working-dir-diff state so hunk/line staging (which only makes sense for the actual
        /// current working-dir diff) doesn't stay incorrectly enabled from whatever was viewed
        /// immediately before switching modes.</summary>
        private void ClearWorkingDirDiffState()
        {
            _currentDiffFile = null;
            IsDiffFromStagedFile = false;
            RaisePropertyChanged(nameof(IsWorkingDirDiff));
            RaisePropertyChanged(nameof(CanPartialStage));
        }

        /// <summary>Bumped by every EnterHistoryModeAsync/LoadBlameForHistoryAsync call and captured
        /// at the start of each — clicking History/Blame/a history row repeatedly (as this feature
        /// invites) fires a new fire-and-forget load before an earlier one's git call has returned;
        /// without this guard, whichever overlapping call happens to finish LAST wins regardless of
        /// which one the user actually asked for last, and a slow stale response can wipe out a
        /// newer, already-correct one right after it rendered ("click Blame -> nothing visible" could
        /// really be a same-frame stale response clobbering a good one, not an empty result at all).</summary>
        private int _historyRequestSeq;

        /// <summary>Loads blame for the file as of a specific commit (navigating History while
        /// IsBlameContent is active — see SelectedFileHistoryCommit/IsBlameContent), or the working
        /// tree when sha is null. <paramref name="path"/> must be the path the file actually had AT
        /// that commit (see CommitInfo.HistoryPath) — using the current/later path for a commit from
        /// before a rename looks up a file that doesn't exist yet at that point in history and comes
        /// back empty.</summary>
        private async Task LoadBlameForHistoryAsync(string sha, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var seq = ++_historyRequestSeq;
            ClearWorkingDirDiffState();
            BlameLines.Clear();
            List<BlameLine> blame;
            try
            {
                blame = await _git.Executor.RunAsync(() => _git.GetBlame(path, sha));
            }
            catch (Exception ex)
            {
                if (seq == _historyRequestSeq) DialogService.ShowError("Blame", ex.Message);
                return;
            }
            if (seq != _historyRequestSeq) return; // superseded by a newer History/Blame click
            string lastSha = null;
            bool alt = false;
            foreach (var b in blame)
            {
                if (b.Sha != lastSha) { alt = !alt; lastSha = b.Sha; }
                b.IsBandAlt = alt;
                BlameLines.Add(b);
            }
            // Navigating between history commits while already in Blame mode swaps BlameLines'
            // content without a DiffPaneMode/IsBlameContent change, so RaiseDiffModeChanged (which
            // would otherwise catch this) never fires here — recompute Find matches directly
            // against the freshly-loaded content instead of leaving stale ones from the prior commit.
            RecomputeDiffSearch();
        }

        private async Task EnterHistoryModeAsync(string path, bool blameContent = false)
        {
            if (string.IsNullOrEmpty(path)) return;
            var seq = ++_historyRequestSeq;
            _blameHistoryFilePath = path;
            DiffPaneMode = DiffPaneMode.History;
            SetBlameContentSilently(blameContent); // SelectedFileHistoryCommit (below) triggers the actual load
            ClearWorkingDirDiffState();
            FileHistoryCommits.Clear();
            List<CommitInfo> history;
            try
            {
                history = await _git.Executor.RunAsync(() => _git.GetFileHistory(path));
            }
            catch (Exception ex)
            {
                if (seq == _historyRequestSeq) DialogService.ShowError("File History", ex.Message);
                return;
            }
            if (seq != _historyRequestSeq) return; // superseded by a newer History/Blame click
            foreach (var c in history) FileHistoryCommits.Add(c);
            if (FileHistoryCommits.Count > 0) SelectedFileHistoryCommit = FileHistoryCommits[0];
        }
    }
}
