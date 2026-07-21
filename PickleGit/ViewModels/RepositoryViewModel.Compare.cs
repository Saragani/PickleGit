using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;

namespace PickleGit.ViewModels
{
    /// <summary>Ad-hoc "diff against current branch" comparison — a third mutually-exclusive
    /// right-panel state alongside DetailCommit (single commit) and ShowWorkingDir (staging area).
    /// Reuses the existing CommitFiles list + DiffView/FlatDiffItems pipeline; only the two
    /// endpoint shas and their display names are new state.</summary>
    public partial class RepositoryViewModel
    {
        private string _comparisonBaseSha;
        private string _comparisonTargetSha;

        /// <summary>True while a branch comparison is open. Checked first in SelectedFile's setter
        /// (ahead of DetailCommit/ShowWorkingDir) and in ShowDiffInsteadOfCommits.</summary>
        public bool IsComparisonMode => _comparisonBaseSha != null && _comparisonTargetSha != null;

        private string _comparisonBaseName;
        public string ComparisonBaseName { get => _comparisonBaseName; private set => Set(ref _comparisonBaseName, value); }

        private string _comparisonTargetName;
        public string ComparisonTargetName { get => _comparisonTargetName; private set => Set(ref _comparisonTargetName, value); }

        /// <summary>Drives the "Files changed" label + list in CommitDetailView's commit-detail
        /// grid — shown for either a single selected commit or an open comparison, both of which
        /// populate the same CommitFiles collection.</summary>
        public bool ShowCommitFilesPanel => DetailCommit != null || IsComparisonMode;

        public ICommand CompareWithCurrentCommand { get; private set; }
        public ICommand ExitComparisonCommand { get; private set; }

        private void InitializeCompareCommands()
        {
            CompareWithCurrentCommand = new RelayCommand(param => _ = CompareWithCurrentAsync(param), _ => HasRepo);
            ExitComparisonCommand = new RelayCommand(() => { SelectedFile = null; ExitComparisonMode(); });
        }

        private async Task CompareWithCurrentAsync(object param)
        {
            if (!(param is BranchInfo bi) || bi.IsHead || string.IsNullOrEmpty(bi.TipSha)) return;
            var currentHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (string.IsNullOrEmpty(currentHead)) return;

            DetailCommit = null;
            ShowWorkingDir = false;
            SelectedFile = null;
            if (_selectedNodes.Count > 0) _selectedNodes.Clear(); // comparison is its own context, not a node selection

            _comparisonBaseSha = currentHead;
            _comparisonTargetSha = bi.TipSha;
            ComparisonBaseName = CurrentBranch ?? "current";
            ComparisonTargetName = bi.DisplayName;
            RaisePropertyChanged(nameof(IsComparisonMode));
            RaisePropertyChanged(nameof(ShowCommitFilesPanel));
            RaisePropertyChanged(nameof(DiffPaneHeaderText));
            RaiseDetailPanelPropertiesChanged();

            var files = await _git.Executor.RunAsync(() => _git.GetChangedFiles(currentHead, bi.TipSha));
            CommitFiles = new ObservableCollection<FileChange>(files);
        }

        /// <summary>Mirrors LoadDiffAsync (commit-vs-parent) but for two arbitrary endpoints — no
        /// image-diff handling since that needs its own two-sha GetImageDiff overload, out of scope
        /// for v1; an image file here just falls through to GetFileDiff's binary-comparison path.</summary>
        private async Task LoadComparisonDiffAsync(string shaA, string shaB, string filePath)
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
            // Invoked fire-and-forget; without this try/catch an exception here is an unobserved
            // task exception — the pane (already cleared above) just stays blank with no trace.
            try
            {
                var diff = await _git.Executor.RunAsync(() => _git.GetFileDiff(shaA, shaB, filePath));
                ApplyDiffResult(diff);
            }
            catch (Exception ex)
            {
                DialogService.ShowError("Diff", ex.Message);
            }
        }

        private void ExitComparisonMode()
        {
            if (!IsComparisonMode) return;
            _comparisonBaseSha = null;
            _comparisonTargetSha = null;
            ComparisonBaseName = null;
            ComparisonTargetName = null;
            CommitFiles = new ObservableCollection<FileChange>();
            RaisePropertyChanged(nameof(IsComparisonMode));
            RaisePropertyChanged(nameof(ShowCommitFilesPanel));
            RaisePropertyChanged(nameof(DiffPaneHeaderText));
            RaiseDetailPanelPropertiesChanged();
        }
    }
}
