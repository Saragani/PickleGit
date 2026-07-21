using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.Services.Git;

namespace PickleGit.ViewModels
{
    public partial class RepositoryViewModel : BaseViewModel, IDisposable
    {
        private readonly GitService _git = new GitService();
        private readonly StagingService _staging;

        // ── Repository state ──────────────────────────────────────────────────
        private string _repoPath;
        private string _currentBranch;
        private string _statusMessage = "Ready";
        private bool _isBusy;

        public string RepoPath { get => _repoPath; private set => Set(ref _repoPath, value); }
        public string RepoName => RepoPath != null ? Path.GetFileName(RepoPath.TrimEnd('\\', '/')) : "(no repository)";
        public string CurrentBranch { get => _currentBranch; private set => Set(ref _currentBranch, value); }
        public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }
        public bool IsBusy
        {
            get => _isBusy;
            set { if (Set(ref _isBusy, value)) RaisePropertyChanged(nameof(ShowEmptyState)); }
        }
        public bool HasRepo => _git.IsOpen;

        // ── Graph data ────────────────────────────────────────────────────────
        private ObservableCollection<GraphNode> _graphNodes = new ObservableCollection<GraphNode>();
        public ObservableCollection<GraphNode> GraphNodes
        {
            get => _graphNodes;
            private set
            {
                if (Set(ref _graphNodes, value))
                {
                    RaisePropertyChanged(nameof(ShowEmptyState));
                    RaisePropertyChanged(nameof(EmptyStateText));
                }
            }
        }

        /// <summary>Mask bit for each branch (by friendly name), from the last history walk —
        /// used by CommitListView's branch/tag hover-dim and row-hover ghost-badge features.</summary>
        public Dictionary<string, ulong> BranchMasks { get; private set; } = new Dictionary<string, ulong>();

        /// <summary>Shows a friendly message in the commit-list area instead of a silent blank list.</summary>
        public bool ShowEmptyState => IsLoaded && !IsBusy && _graphNodes.Count == 0;

        public string EmptyStateText => !string.IsNullOrWhiteSpace(_searchText)
            ? "No commits match your search."
            : "No commits yet — make your first commit to see it here.";

        private GraphNode _selectedNode;
        public GraphNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (!Set(ref _selectedNode, value)) return;
                // Sync SelectedNodes to this single item without re-running the full
                // detail-loading logic (OnSelectedNodesChanged handles that via CollectionChanged).
                _syncingFromSelectedNode = true;
                _selectedNodes.Clear();
                if (value != null) _selectedNodes.Add(value);
                _syncingFromSelectedNode = false;
                OnSelectedNodesChanged();
            }
        }

        // ── Multi-select ──────────────────────────────────────────────────────
        private bool _syncingFromSelectedNode;
        private readonly ObservableCollection<GraphNode> _selectedNodes = new ObservableCollection<GraphNode>();
        public ObservableCollection<GraphNode> SelectedNodes => _selectedNodes;

        private bool _isMultiSelection;
        public bool IsMultiSelection
        {
            get => _isMultiSelection;
            private set
            {
                if (Set(ref _isMultiSelection, value))
                    RaiseDetailPanelPropertiesChanged();
            }
        }

        private int _selectedCount;
        public int SelectedCount { get => _selectedCount; private set => Set(ref _selectedCount, value); }

        private readonly ObservableCollection<AggregatedFileChange> _aggregatedFiles = new ObservableCollection<AggregatedFileChange>();
        public ObservableCollection<AggregatedFileChange> AggregatedFiles => _aggregatedFiles;

        // ── Sidebar data ──────────────────────────────────────────────────────
        private ObservableCollection<BranchInfo> _localBranches = new ObservableCollection<BranchInfo>();
        private ObservableCollection<BranchInfo> _remoteBranches = new ObservableCollection<BranchInfo>();
        private ObservableCollection<TagInfo> _tags = new ObservableCollection<TagInfo>();
        private ObservableCollection<StashInfo> _stashes = new ObservableCollection<StashInfo>();
        private ObservableCollection<RemoteInfo> _remotes = new ObservableCollection<RemoteInfo>();
        private ObservableCollection<ReflogEntry> _reflog = new ObservableCollection<ReflogEntry>();
        private ObservableCollection<BranchNodeViewModel> _localBranchTree = new ObservableCollection<BranchNodeViewModel>();
        private ObservableCollection<BranchNodeViewModel> _remoteBranchTree = new ObservableCollection<BranchNodeViewModel>();

        public ObservableCollection<BranchInfo> LocalBranches { get => _localBranches; private set => Set(ref _localBranches, value); }
        public ObservableCollection<BranchInfo> RemoteBranches { get => _remoteBranches; private set => Set(ref _remoteBranches, value); }
        public ObservableCollection<TagInfo> Tags { get => _tags; private set => Set(ref _tags, value); }
        public ObservableCollection<StashInfo> Stashes { get => _stashes; private set => Set(ref _stashes, value); }
        public ObservableCollection<RemoteInfo> Remotes { get => _remotes; private set => Set(ref _remotes, value); }
        public ObservableCollection<ReflogEntry> Reflog { get => _reflog; private set => Set(ref _reflog, value); }
        public ObservableCollection<BranchNodeViewModel> LocalBranchTree  { get => _localBranchTree;  private set => Set(ref _localBranchTree,  value); }
        public ObservableCollection<BranchNodeViewModel> RemoteBranchTree { get => _remoteBranchTree; private set => Set(ref _remoteBranchTree, value); }

        // ── Hosting integration (Phase 6) ───────────────────────────────────────
        /// <summary>Sub-VM owning provider detection, the PR list and PR commands (XAML binds via Hosting.*).</summary>
        public HostingViewModel Hosting { get; }

        // ── Submodules / Worktrees (Phase 6) ────────────────────────────────────
        private ObservableCollection<SubmoduleInfo> _submodules = new ObservableCollection<SubmoduleInfo>();
        public ObservableCollection<SubmoduleInfo> Submodules { get => _submodules; private set => Set(ref _submodules, value); }
        private ObservableCollection<WorktreeInfo> _worktrees = new ObservableCollection<WorktreeInfo>();
        public ObservableCollection<WorktreeInfo> Worktrees { get => _worktrees; private set => Set(ref _worktrees, value); }

        /// <summary>Raised to ask the app shell to open a path (a worktree) as a new tab.</summary>
        public event System.Action<string> RequestOpenRepoInNewTab;

        public event System.EventHandler<GraphNode> ScrollToNodeRequested;

        // ── Commit detail ─────────────────────────────────────────────────────
        private CommitInfo _detailCommit;
        private ObservableCollection<FileChange> _commitFiles = new ObservableCollection<FileChange>();
        private FileChange _selectedFile;
        private IReadOnlyList<DiffItem> _flatDiffItems = Array.Empty<DiffItem>();

        public CommitInfo DetailCommit
        {
            get => _detailCommit;
            private set
            {
                if (Set(ref _detailCommit, value))
                {
                    RaiseDetailPanelPropertiesChanged();
                    SignatureStatus = null;
                    if (value?.Sha != null && !value.IsUncommitted && !value.IsStash)
                        _ = LoadSignatureStatusAsync(value.Sha);
                }
            }
        }

        private string _signatureStatus;
        /// <summary>GPG signature line for the detail pane ("✔ Signed — good (name)"), or null when
        /// the commit is unsigned / git.exe is unavailable.</summary>
        public string SignatureStatus { get => _signatureStatus; private set => Set(ref _signatureStatus, value); }

        private async Task LoadSignatureStatusAsync(string sha)
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) return;
            try
            {
                var result = await _git.Cli.RunAsync($"log -1 --format=%G?%x1f%GS {sha}");
                if (!result.Success || _detailCommit?.Sha != sha) return; // stale — selection moved on
                var parts = result.StdOut.Trim().Split('\x1f');
                var code = parts.Length > 0 ? parts[0] : "N";
                var signer = parts.Length > 1 ? parts[1].Trim() : null;
                switch (code)
                {
                    case "G": SignatureStatus = $"✔ Signed — good{(string.IsNullOrEmpty(signer) ? "" : $" ({signer})")}"; break;
                    case "U": SignatureStatus = $"✔ Signed — good, untrusted key{(string.IsNullOrEmpty(signer) ? "" : $" ({signer})")}"; break;
                    case "B": SignatureStatus = "✖ Signed — BAD signature"; break;
                    case "E": SignatureStatus = "Signed — cannot be verified (missing key?)"; break;
                    case "X": case "Y": SignatureStatus = "Signed — expired signature/key"; break;
                    case "R": SignatureStatus = "Signed — revoked key"; break;
                    default: SignatureStatus = null; break; // N = unsigned
                }
            }
            catch (Exception ex) { AppLog.Warn("Signature check failed", ex); }
        }
        public ObservableCollection<FileChange> CommitFiles { get => _commitFiles; private set => Set(ref _commitFiles, value); }
        public FileChange SelectedFile
        {
            get => _selectedFile;
            set
            {
                // Staged/Unstaged ListViews both bind SelectedItem to this same shared property.
                // Clearing the *other* list's SelectedItems (mutual-exclusion sync, below) makes WPF
                // null that list's own SelectedItem, which — via the shared TwoWay binding — would
                // otherwise clobber the selection we just set from the list the user actually clicked.
                if (_syncingFileSelection && value == null) return;
                if (!Set(ref _selectedFile, value)) return;
                RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
                RaisePropertyChanged(nameof(DiffPaneHeaderText));
                if (value == null) return;
                DiffPaneMode = DiffPaneMode.Diff; // picking a file from the list always returns to plain diff view
                if (IsComparisonMode) _ = LoadComparisonDiffAsync(_comparisonBaseSha, _comparisonTargetSha, value.Path);
                else if (_detailCommit != null) _ = LoadDiffAsync(_detailCommit.Sha, value.Path);
                else if (ShowWorkingDir) _ = LoadWorkingDiffAsync(value);
            }
        }

        /// <summary>Drives the middle-pane swap between the commit graph and the file diff:
        /// A single selected file replaces the commit list with its diff; no selection, or
        /// 2+ files multi-selected in the working-dir lists, shows the commit list instead. Guarded on
        /// ShowWorkingDir/DetailCommit (not just SelectedFile != null) since both are reset to null/false
        /// on every selection-context change, so a stale SelectedFile from a just-abandoned context can't
        /// leak through and wrongly show its diff.</summary>
        public bool ShowDiffInsteadOfCommits
        {
            get
            {
                if (SelectedFile == null) return false;
                if (IsComparisonMode) return true;
                if (ShowWorkingDir) return SelectedDiscardableFilesCount == 1;
                return DetailCommit != null;
            }
        }

        /// <summary>DiffView's header text — distinguishes an ad-hoc branch comparison from a
        /// normal single-commit/working-dir diff, since both drive the same pane.</summary>
        public string DiffPaneHeaderText => IsComparisonMode
            ? $"Comparing {ComparisonTargetName} → {ComparisonBaseName}: {SelectedFile?.Path}"
            : $"Diff: {SelectedFile?.Path}";
        public IReadOnlyList<DiffItem> FlatDiffItems
        {
            get => _flatDiffItems;
            // Any diff (re)load invalidates the hunk-navigation cursor and any line selection —
            // the old DiffLine instances no longer exist in the newly-loaded set.
            private set
            {
                if (!Set(ref _flatDiffItems, value)) return;
                _currentHunkIndex = -1;
                _selectedLines.Clear();
                UnifiedRowKinds = value.Select(i => i.Kind == DiffItemKind.Line ? i.Line.Kind : DiffLineKind.Header).ToArray();
            }
        }

        private IReadOnlyList<DiffLineKind> _unifiedRowKinds = Array.Empty<DiffLineKind>();
        /// <summary>One entry per FlatDiffItems row (Header for hunk-header rows), feeding
        /// DiffChangeMapControl's change-location strip next to the unified diff's scrollbar.</summary>
        public IReadOnlyList<DiffLineKind> UnifiedRowKinds { get => _unifiedRowKinds; private set => Set(ref _unifiedRowKinds, value); }

        // ── Lazy load state ───────────────────────────────────────────────────
        private bool _isLoaded;
        public bool IsLoaded => _isLoaded;

        // ── Smart branch filter / history state ──────────────────────────────
        // Branch membership comes from CommitInfo.RefMask (bit 0 = current branch),
        // computed during the single history walk — no second walk needed.
        private List<CommitInfo> _allCommits = new List<CommitInfo>();
        private bool _hasUncommittedChanges;
        private int _commitLimit = AppSettings.LoadCommitLimit();
        private string _lastRefreshSignature;
        private RepositoryWatcher _watcher;

        private bool _hasMoreCommits;
        public bool HasMoreCommits { get => _hasMoreCommits; private set => Set(ref _hasMoreCommits, value); }

        // ── Disk cache ────────────────────────────────────────────────────────
        private string _cacheFile;

        private bool _smartBranchVisibility;
        public bool SmartBranchVisibility
        {
            get => _smartBranchVisibility;
            set
            {
                if (Set(ref _smartBranchVisibility, value) && _git.IsOpen)
                    ApplyFilter();
            }
        }

        // ── Working directory / staging ───────────────────────────────────────
        private bool _showWorkingDir;
        private ObservableCollection<FileChange> _workingDirFiles = new ObservableCollection<FileChange>();
        private ObservableCollection<FileChange> _stagedFiles = new ObservableCollection<FileChange>();
        private readonly ObservableCollection<FileChange> _selectedWorkingFiles = new ObservableCollection<FileChange>();
        private readonly ObservableCollection<FileChange> _selectedStagedFiles = new ObservableCollection<FileChange>();
        private bool _syncingFileSelection;
        private string _commitMessage;
        private string _authorName;
        private string _authorEmail;

        public bool ShowWorkingDir
        {
            get => _showWorkingDir;
            set
            {
                if (Set(ref _showWorkingDir, value))
                {
                    // The detail panel shows either a single commit or the working directory,
                    // never both — a stale DetailCommit left over from a previously-viewed commit
                    // makes SelectedFile's setter diff the wrong thing (that old commit, not the
                    // working tree) against whatever file gets clicked next, silently rendering an
                    // unrelated (often empty-looking) diff instead of an error.
                    if (value) DetailCommit = null;
                    RaiseDetailPanelPropertiesChanged();
                }
            }
        }

        private bool _isAmend;
        public bool IsAmend
        {
            get => _isAmend;
            set
            {
                if (!Set(ref _isAmend, value)) return;
                if (value && string.IsNullOrWhiteSpace(CommitMessage))
                    _ = PrefillAmendMessageAsync();
            }
        }

        private bool _signOff;
        public bool SignOff { get => _signOff; set => Set(ref _signOff, value); }

        // ── Conflicts ──────────────────────────────────────────────────────────
        private ConflictState _conflictInfo = new ConflictState();
        public ConflictState ConflictInfo
        {
            get => _conflictInfo;
            private set { if (Set(ref _conflictInfo, value)) RaisePropertyChanged(nameof(HasConflict)); }
        }
        public bool HasConflict => _conflictInfo?.HasConflicts == true;

        // ── Undo ──────────────────────────────────────────────────────────────
        private UndoEntry _lastUndo;
        public UndoEntry LastUndo
        {
            get => _lastUndo;
            private set { if (Set(ref _lastUndo, value)) RaisePropertyChanged(nameof(CanUndo)); }
        }
        public bool CanUndo => _lastUndo != null;

        private async Task PrefillAmendMessageAsync()
        {
            var msg = await _git.Executor.RunAsync(() => _git.GetHeadCommitMessage());
            if (IsAmend && string.IsNullOrWhiteSpace(CommitMessage))
                CommitMessage = msg?.TrimEnd();
        }

        // ── Commit search / filter ────────────────────────────────────────────
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) ApplyFilter(); }
        }

        private bool _isSearchOpen;
        public bool IsSearchOpen
        {
            get => _isSearchOpen;
            set
            {
                if (Set(ref _isSearchOpen, value) && !value)
                    SearchText = string.Empty;
            }
        }
        public ObservableCollection<FileChange> WorkingDirFiles { get => _workingDirFiles; private set => Set(ref _workingDirFiles, value); }
        public IEnumerable<FileChange> ConflictedFileChanges => _workingDirFiles.Where(f => f.Kind == FileChangeKind.Conflicted);
        public ObservableCollection<FileChange> StagedFiles { get => _stagedFiles; private set => Set(ref _stagedFiles, value); }

        /// <summary>Bound to UnstagedListView via ListViewMultiSelectBehavior — the current multi-selection there.</summary>
        public ObservableCollection<FileChange> SelectedWorkingFiles => _selectedWorkingFiles;
        /// <summary>Bound to StagedListView via ListViewMultiSelectBehavior — the current multi-selection there.</summary>
        public ObservableCollection<FileChange> SelectedStagedFiles => _selectedStagedFiles;
        /// <summary>Combined selection count across both lists — drives the single discard button's
        /// tooltip/badge, since that button now acts on whichever files (staged or unstaged) are selected.</summary>
        public int SelectedDiscardableFilesCount => _selectedWorkingFiles.Count + _selectedStagedFiles.Count;
        public string CommitMessage
        {
            get => _commitMessage;
            set
            {
                if (Set(ref _commitMessage, value))
                {
                    RaisePropertyChanged(nameof(CommitSubjectLength));
                }
            }
        }

        /// <summary>Length of the commit message's first line — 50-char subject guide.</summary>
        public int CommitSubjectLength => (CommitMessage ?? string.Empty).Split('\n')[0].TrimEnd('\r').Length;
        public string AuthorName { get => _authorName; set => Set(ref _authorName, value); }
        public string AuthorEmail { get => _authorEmail; set => Set(ref _authorEmail, value); }

        // ── Credentials ───────────────────────────────────────────────────────
        public string RemoteUsername { get; set; }
        public string RemotePassword { get; set; }
        private bool _credentialsFromDialog;
        private bool _forceCredentialDialog;

        public bool HasDetailPanel => ShowWorkingDir || IsMultiSelection || DetailCommit != null || IsComparisonMode || HasBisect;
        public GridLength DetailPanelWidth => HasDetailPanel ? new GridLength(350) : new GridLength(0);
        // A plain MinWidth="350" on the column would defeat the width-0 collapse above whenever
        // the panel is hidden (ColumnDefinition.MinWidth always wins over a smaller bound Width),
        // permanently reserving 350px of blank space — so the minimum has to collapse right along
        // with the width.
        public double DetailPanelMinWidth => HasDetailPanel ? 350 : 0;
        public GridLength DetailSplitterWidth => HasDetailPanel ? new GridLength(5) : new GridLength(0);

        private readonly HashSet<string> _collapsedBranchNodes = new HashSet<string>();
        private bool _hasSavedBranchNodeState;
        private System.Windows.Threading.DispatcherTimer _refreshTimer;

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand RefreshCommand { get; }
        public ICommand FetchCommand { get; }
        public ICommand PullCommand { get; }
        public ICommand PushCommand { get; }
        public ICommand CreateBranchCommand { get; }
        public ICommand CheckoutBranchCommand { get; }
        public ICommand DeleteBranchCommand { get; }
        public ICommand FetchBranchCommand { get; }
        public ICommand MergeBranchCommand { get; }
        public ICommand MergeBranchNoFfCommand { get; }
        public ICommand MergeBranchFfOnlyCommand { get; }
        public ICommand MergeBranchSquashCommand { get; }
        public ICommand CreateTagCommand { get; }
        public ICommand SelectBranchCommand { get; }
        public ICommand StageFileCommand { get; }
        public ICommand UnstageFileCommand { get; }
        public ICommand StageAllCommand { get; }
        public ICommand UnstageAllCommand { get; }
        public ICommand CommitCommand { get; }
        public ICommand OpenIdentitySettingsCommand { get; }
        public ICommand StashCommand { get; }
        public ICommand StashSelectedFilesCommand { get; }
        public ICommand PopStashCommand { get; }
        public ICommand ShowWorkingDirCommand { get; }
        public ICommand ShowCommitDetailCommand { get; }
        public ICommand CherryPickRangeCommand { get; }
        public ICommand RevertRangeCommand { get; }
        public ICommand SaveCommitAsPatchCommand { get; }
        public ICommand SaveCommitsAsPatchCommand { get; }
        public ICommand ApplyPatchFileCommand { get; }
        public ICommand DiscardFileCommand { get; }
        public ICommand DiscardAllOrSelectedCommand { get; }
        public ICommand DiscardStagedFileCommand { get; }
        public ICommand LoadMoreCommitsCommand { get; }
        public ICommand CommitAndPushCommand { get; }
        public ICommand ForcePushCommand { get; }
        public ICommand PullRebaseCommand { get; }
        public ICommand FetchPruneCommand { get; }
        public ICommand FetchAllCommand { get; }
        public ICommand CheckoutRemoteBranchCommand { get; }
        public ICommand RenameBranchCommand { get; }
        public ICommand CheckoutCommitCommand { get; }
        public ICommand ResetToCommitCommand { get; }
        public ICommand RevertCommitCommand { get; }
        public ICommand CopyShaCommand { get; }
        public ICommand CopyCommitMessageCommand { get; }
        public ICommand ApplyStashCommand { get; }
        public ICommand PopStashItemCommand { get; }
        public ICommand DropStashCommand { get; }
        public ICommand SelectStashCommand { get; }
        public ICommand DeleteTagCommand { get; }
        public ICommand DeleteTagOnRemoteCommand { get; }
        public ICommand DeleteRemoteBranchCommand { get; }
        public ICommand PushTagCommand { get; }
        public ICommand CheckoutTagCommand { get; }
        public ICommand AddRemoteCommand { get; }
        public ICommand EditRemoteCommand { get; }
        public ICommand RemoveRemoteCommand { get; }
        public ICommand CopyBranchNameCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand RevealFileCommand { get; }
        public ICommand CopyFilePathCommand { get; }
        public ICommand AddToGitignoreCommand { get; }
        public ICommand ContinueOperationCommand { get; }
        public ICommand AbortOperationCommand { get; }
        public ICommand TakeOursCommand { get; }
        public ICommand TakeTheirsCommand { get; }
        public ICommand MarkResolvedCommand { get; }
        public ICommand OpenMergeEditorCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand StageHunkOrLinesCommand { get; }
        public ICommand DiscardHunkOrLinesCommand { get; }
        public ICommand UnstageHunkOrLinesCommand { get; }
        public ICommand NextHunkCommand { get; }
        public ICommand PrevHunkCommand { get; }
        public ICommand CloseDiffPaneCommand { get; }
        public ICommand RebaseOntoBranchCommand { get; }
        public ICommand RebaseOntoCommitCommand { get; }
        public ICommand InteractiveRebaseOntoBranchCommand { get; }
        public ICommand InteractiveRebaseOntoCommitCommand { get; }
        public ICommand CheckoutReflogEntryCommand { get; }
        public ICommand ResetToReflogEntryCommand { get; }
        public ICommand InitAllSubmodulesCommand { get; }
        public ICommand UpdateSubmoduleCommand { get; }
        public ICommand AddSubmoduleCommand { get; }
        public ICommand SyncSubmoduleCommand { get; }
        public ICommand DeinitSubmoduleCommand { get; }
        public ICommand OpenSubmoduleAsTabCommand { get; }
        public ICommand AddWorktreeCommand { get; }
        public ICommand RemoveWorktreeCommand { get; }
        public ICommand OpenWorktreeAsTabCommand { get; }
        public ICommand TrackWithLfsCommand { get; }
        public ICommand InstallLfsCommand { get; }

        public RepositoryViewModel()
        {
            _staging = new StagingService(_git);
            Hosting = new HostingViewModel(this);
            _selectedNodes.CollectionChanged += (_, __) =>
            {
                if (!_syncingFromSelectedNode) OnSelectedNodesChanged();
            };
            _selectedWorkingFiles.CollectionChanged += (_, __) =>
            {
                // Staged and Unstaged selection are mutually exclusive — selecting in one clears
                // the other, so stage/discard actions never act on an ambiguous cross-list selection.
                if (!_syncingFileSelection && _selectedWorkingFiles.Count > 0 && _selectedStagedFiles.Count > 0)
                {
                    _syncingFileSelection = true;
                    _selectedStagedFiles.Clear();
                    _syncingFileSelection = false;
                }
                RaisePropertyChanged(nameof(SelectedDiscardableFilesCount));
                RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
            };
            _selectedStagedFiles.CollectionChanged += (_, __) =>
            {
                if (!_syncingFileSelection && _selectedStagedFiles.Count > 0 && _selectedWorkingFiles.Count > 0)
                {
                    _syncingFileSelection = true;
                    _selectedWorkingFiles.Clear();
                    _syncingFileSelection = false;
                }
                RaisePropertyChanged(nameof(SelectedDiscardableFilesCount));
                RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
            };
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => HasRepo);
            FetchCommand = new RelayCommand(async () => await FetchAsync(), () => HasRepo);
            PullCommand = new RelayCommand(async () => await PullAsync(), () => HasRepo);
            PushCommand = new RelayCommand(async () => await PushAsync(), () => HasRepo);
            FetchPruneCommand = new RelayCommand(async () => await FetchAsync(prune: true), () => HasRepo);
            FetchAllCommand = new RelayCommand(async () => await FetchAsync(allRemotes: true), () => HasRepo);
            PullRebaseCommand = new RelayCommand(async () => await PullRebaseAsync(), () => HasRepo);
            ForcePushCommand = new RelayCommand(async () => await ForcePushAsync(),
                () => HasRepo && CurrentBranch != null && !CurrentBranch.StartsWith("detached"));
            CreateBranchCommand = new RelayCommand(CreateBranch, _ => HasRepo);
            CheckoutBranchCommand = new RelayCommand(CheckoutBranch, _ => HasRepo);
            FetchBranchCommand = new RelayCommand(param => _ = FetchBranchAsync(param),
                param => HasRepo && param is BranchInfo bi && !bi.IsRemote && !bi.IsHead
                         && !string.IsNullOrEmpty(bi.TrackedBranchName));
            DeleteBranchCommand = new RelayCommand(DeleteBranch, _ => HasRepo);
            CancelOperationCommand = new RelayCommand(CancelOperation, () => CanCancel);
            LoadLargeDiffCommand = new RelayCommand(LoadLargeDiff, () => IsLargeDiffPending);
            NextDiffMatchCommand = new RelayCommand(() => NavigateDiffMatch(+1));
            PrevDiffMatchCommand = new RelayCommand(() => NavigateDiffMatch(-1));
            CloseDiffPaneCommand = new RelayCommand(() => { SelectedFile = null; ExitComparisonMode(); });
            InitializeCompareCommands();
            InitializeBisectCommands();
            InitializeBlameCommands();
            MergeBranchCommand = new RelayCommand(MergeBranch, _ => HasRepo);
            MergeBranchNoFfCommand = new RelayCommand(MergeBranchNoFf, _ => HasRepo);
            MergeBranchFfOnlyCommand = new RelayCommand(MergeBranchFfOnly, _ => HasRepo);
            MergeBranchSquashCommand = new RelayCommand(MergeBranchSquash, _ => HasRepo);
            RenameBranchCommand = new RelayCommand(RenameBranch, _ => HasRepo);
            CheckoutRemoteBranchCommand = new RelayCommand(CheckoutRemoteBranch, _ => HasRepo);
            CreateTagCommand = new RelayCommand(CreateTag, _ => HasRepo);
            SelectBranchCommand = new RelayCommand(SelectBranch, _ => HasRepo);
            CheckoutCommitCommand = new RelayCommand(CheckoutCommit,
                _ => HasRepo && SelectedNode?.Commit?.IsUncommitted != true);
            ResetToCommitCommand = new RelayCommand(ResetToCommit,
                _ => HasRepo && SelectedNode?.Commit?.IsUncommitted != true);
            RevertCommitCommand = new RelayCommand(RevertCommit,
                _ => HasRepo && SelectedNode?.Commit?.IsUncommitted != true);
            CopyShaCommand = new RelayCommand(_ => CopyToClipboard(SelectedNode?.Commit?.Sha),
                _ => SelectedNode?.Commit?.Sha != null);
            CopyCommitMessageCommand = new RelayCommand(_ => CopyToClipboard(SelectedNode?.Commit?.Message),
                _ => SelectedNode?.Commit?.Message != null);
            ApplyStashCommand = new RelayCommand(p => ApplyStash(p, pop: false), _ => HasRepo);
            PopStashItemCommand = new RelayCommand(p => ApplyStash(p, pop: true), _ => HasRepo);
            DropStashCommand = new RelayCommand(DropStash, _ => HasRepo);
            SelectStashCommand = new RelayCommand(SelectStash, _ => HasRepo);
            DeleteTagCommand = new RelayCommand(DeleteTag, _ => HasRepo);
            DeleteTagOnRemoteCommand = new RelayCommand(DeleteTagOnRemote, _ => HasRepo);
            DeleteRemoteBranchCommand = new RelayCommand(DeleteRemoteBranch, _ => HasRepo);
            PushTagCommand = new RelayCommand(p => _ = PushTagAsync(p), _ => HasRepo);
            CheckoutTagCommand = new RelayCommand(CheckoutTag, _ => HasRepo);
            AddRemoteCommand = new RelayCommand(AddRemote, _ => HasRepo);
            EditRemoteCommand = new RelayCommand(EditRemote, _ => HasRepo);
            RemoveRemoteCommand = new RelayCommand(RemoveRemote, _ => HasRepo);
            CopyBranchNameCommand = new RelayCommand(
                p => CopyToClipboard((p as BranchInfo)?.Name), _ => HasRepo);
            OpenFileCommand = new RelayCommand(OpenFile, _ => HasRepo);
            RevealFileCommand = new RelayCommand(RevealFile, _ => HasRepo);
            CopyFilePathCommand = new RelayCommand(CopyFilePath, _ => HasRepo);
            AddToGitignoreCommand = new RelayCommand(AddToGitignore, _ => HasRepo);
            StageFileCommand = new RelayCommand(param => _ = StageFileAsync(param), _ => HasRepo);
            UnstageFileCommand = new RelayCommand(param => _ = UnstageFileAsync(param), _ => HasRepo);
            StageAllCommand = new RelayCommand(async () => await StageAllAsync(), () => HasRepo && WorkingDirFiles.Count > 0);
            UnstageAllCommand = new RelayCommand(async () => await UnstageAllAsync(), () => HasRepo && StagedFiles.Count > 0);
            CommitCommand = new RelayCommand(async () => await CommitAsync(),
                () => HasRepo && !string.IsNullOrWhiteSpace(CommitMessage) && (StagedFiles.Count > 0 || IsAmend));
            OpenIdentitySettingsCommand = new RelayCommand(OpenIdentitySettings);
            CommitAndPushCommand = new RelayCommand(
                async () => { if (await CommitAsync()) await PushAsync(); },
                () => HasRepo && !string.IsNullOrWhiteSpace(CommitMessage) && (StagedFiles.Count > 0 || IsAmend));
            StashCommand = new RelayCommand(async () => await StashAsync(), () => HasRepo);
            StashSelectedFilesCommand = new RelayCommand(async p => await StashSelectedFilesAsync(p), _ => HasRepo);
            PopStashCommand = new RelayCommand(PopStash, _ => HasRepo && Stashes.Count > 0);
            ShowWorkingDirCommand = new RelayCommand(() =>
            {
                ShowWorkingDir = true;
                _ = LoadWorkingDirAsync();
                _ = TryPrefillCommitTemplateAsync();
            });
            ShowCommitDetailCommand = new RelayCommand(() => ShowWorkingDir = false);
            RevertRangeCommand = new RelayCommand(() => _ = RevertSelectedAsync(),
                () => HasRepo && IsMultiSelection);
            SaveCommitAsPatchCommand = new RelayCommand(p => _ = SaveCommitAsPatchAsync(p), _ => HasRepo);
            SaveCommitsAsPatchCommand = new RelayCommand(() => _ = SaveSelectedCommitsAsPatchAsync(),
                () => HasRepo && IsMultiSelection);
            ApplyPatchFileCommand = new RelayCommand(() => _ = ApplyPatchFileAsync(), () => HasRepo);
            CherryPickRangeCommand = new RelayCommand(() => _ = CherryPickSelectedAsync(),
                () => HasRepo && SelectedCount > 0);
            DiscardFileCommand = new RelayCommand(param => _ = DiscardFileAsync(param), _ => HasRepo);
            DiscardAllOrSelectedCommand = new RelayCommand(() => _ = DiscardAllOrSelectedAsync(),
                () => HasRepo && (WorkingDirFiles.Count > 0 || StagedFiles.Count > 0));
            DiscardStagedFileCommand = new RelayCommand(param => _ = DiscardStagedFileAsync(param), _ => HasRepo);
            LoadMoreCommitsCommand = new RelayCommand(
                async () => { _commitLimit *= 2; await RefreshAsync(force: true); },
                () => HasRepo && HasMoreCommits && !IsBusy);
            ContinueOperationCommand = new RelayCommand(async () => await ContinueOperationAsync(),
                () => HasConflict && ConflictInfo.ConflictedFiles.Count == 0 && !IsBusy);
            AbortOperationCommand = new RelayCommand(async () => await AbortOperationAsync(),
                () => HasConflict && !IsBusy);
            TakeOursCommand = new RelayCommand(
                p => { if (p is FileChange fc) _ = ResolveConflictSideAsync(fc, "ours"); }, _ => HasRepo);
            TakeTheirsCommand = new RelayCommand(
                p => { if (p is FileChange fc) _ = ResolveConflictSideAsync(fc, "theirs"); }, _ => HasRepo);
            MarkResolvedCommand = new RelayCommand(p => _ = MarkResolvedAsync(p), _ => HasRepo);
            OpenMergeEditorCommand = new RelayCommand(OpenMergeEditor, _ => HasRepo);
            UndoCommand = new RelayCommand(async () => await UndoLastAsync(), () => CanUndo && !IsBusy);
            StageHunkOrLinesCommand = new RelayCommand(p => _ = StageHunkOrLinesAsync(p), _ => HasRepo);
            DiscardHunkOrLinesCommand = new RelayCommand(p => _ = DiscardHunkOrLinesAsync(p), _ => HasRepo);
            UnstageHunkOrLinesCommand = new RelayCommand(p => _ = UnstageHunkOrLinesAsync(p), _ => HasRepo);
            NextHunkCommand = new RelayCommand(() => NavigateHunk(1), () => HasRepo);
            PrevHunkCommand = new RelayCommand(() => NavigateHunk(-1), () => HasRepo);
            ShowUnifiedDiffCommand = new RelayCommand(() => IsSideBySide = false);
            ShowSideBySideDiffCommand = new RelayCommand(() => IsSideBySide = true);
            RebaseOntoBranchCommand = new RelayCommand(RebaseOntoBranch, _ => HasRepo);
            RebaseOntoCommitCommand = new RelayCommand(RebaseOntoCommit,
                _ => HasRepo && SelectedNode?.Commit?.IsUncommitted != true);
            InteractiveRebaseOntoBranchCommand = new RelayCommand(InteractiveRebaseOntoBranch, _ => HasRepo);
            InteractiveRebaseOntoCommitCommand = new RelayCommand(InteractiveRebaseOntoCommit,
                _ => HasRepo && SelectedNode?.Commit?.IsUncommitted != true);
            CheckoutReflogEntryCommand = new RelayCommand(CheckoutReflogEntry, _ => HasRepo);
            ResetToReflogEntryCommand = new RelayCommand(ResetToReflogEntry, _ => HasRepo);
            InitAllSubmodulesCommand = new RelayCommand(async () => await InitAllSubmodulesAsync(), () => HasRepo);
            UpdateSubmoduleCommand = new RelayCommand(async p => await UpdateSubmoduleAsync(p as SubmoduleInfo), _ => HasRepo);
            AddSubmoduleCommand = new RelayCommand(async () => await AddSubmoduleAsync(), () => HasRepo);
            SyncSubmoduleCommand = new RelayCommand(async p => await SyncSubmoduleAsync(p as SubmoduleInfo), _ => HasRepo);
            DeinitSubmoduleCommand = new RelayCommand(async p => await DeinitSubmoduleAsync(p as SubmoduleInfo), _ => HasRepo);
            OpenSubmoduleAsTabCommand = new RelayCommand(p =>
            {
                if (p is SubmoduleInfo sm) RequestOpenRepoInNewTab?.Invoke(Path.Combine(RepoPath, sm.Path));
            });
            AddWorktreeCommand = new RelayCommand(async () => await AddWorktreeAsync(), () => HasRepo);
            RemoveWorktreeCommand = new RelayCommand(async p => await RemoveWorktreeAsync(p as WorktreeInfo), _ => HasRepo);
            OpenWorktreeAsTabCommand = new RelayCommand(p =>
            {
                if (p is WorktreeInfo wt) RequestOpenRepoInNewTab?.Invoke(wt.Path);
            });
            TrackWithLfsCommand = new RelayCommand(async p => await TrackWithLfsAsync(p as FileChange), _ => HasRepo);
            InstallLfsCommand = new RelayCommand(async () => await RunCliAsync("Installing Git LFS…", "lfs install", "Git LFS"), () => HasRepo);
        }

        // ── Repository operations ─────────────────────────────────────────────

        public async Task OpenRepoAsync(string path)
        {
            await RunAsync($"Opening {path}…", () =>
            {
                if (!_git.TryOpen(path))
                    throw new InvalidOperationException("Not a valid Git repository.");
            });
            if (_git.IsOpen)
            {
                RepoPath = path;
                _cacheFile = GetCacheFile(path);
                _collapsedBranchNodes.Clear();
                var collapsedNodes = AppSettings.LoadCollapsedBranchNodes(path);
                _hasSavedBranchNodeState = collapsedNodes != null;
                if (collapsedNodes != null)
                {
                    foreach (var key in collapsedNodes)
                        _collapsedBranchNodes.Add(key);
                }
                RaisePropertyChanged(nameof(RepoName));
                RaisePropertyChanged(nameof(HasRepo));
                await LoadIdentityAsync(path);
                // Commits are loaded lazily via EnsureLoadedAsync when the tab becomes active
            }
        }

        /// <summary>Prefills AuthorName/AuthorEmail: per-repo override, else this repo's git config, else the global default identity.</summary>
        private async Task LoadIdentityAsync(string path)
        {
            var (repoName, repoEmail) = AppSettings.LoadRepoIdentity(path);
            if (!string.IsNullOrEmpty(repoName) || !string.IsNullOrEmpty(repoEmail))
            {
                AuthorName = repoName;
                AuthorEmail = repoEmail;
                return;
            }
            var (cfgName, cfgEmail) = await _git.Executor.RunAsync(() =>
                (_git.GetConfigString("user.name"), _git.GetConfigString("user.email")));
            if (!string.IsNullOrEmpty(cfgName) || !string.IsNullOrEmpty(cfgEmail))
            {
                AuthorName = cfgName;
                AuthorEmail = cfgEmail;
                return;
            }
            var (defName, defEmail) = AppSettings.LoadDefaultIdentity();
            AuthorName = defName;
            AuthorEmail = defEmail;
        }

        public async Task LoadSidebarAsync()
        {
            if (!_git.IsOpen) return;

            await RunAsync("Loading repository branches...", () =>
            {
                var branch = _git.GetCurrentBranch();
                var branches = _git.GetBranches();
                var tags = _git.GetTags();
                var stashes = _git.GetStashes();
                var remotes = _git.GetRemotes();
                var reflog = _git.GetReflog();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var local = branches.Where(b => !b.IsRemote).ToList();
                    var remote = FilterRemoteBranches(branches, remotes);

                    LocalBranches = new ObservableCollection<BranchInfo>(local);
                    RemoteBranches = new ObservableCollection<BranchInfo>(remote);
                    LocalBranchTree = BuildBranchTree(local, "local");
                    RemoteBranchTree = BuildBranchTree(remote, "remote");
                    Hosting.UpdateBranchPrBadges();
                    Tags = new ObservableCollection<TagInfo>(tags);
                    Stashes = new ObservableCollection<StashInfo>(stashes);
                    Remotes = new ObservableCollection<RemoteInfo>(remotes);
                    Reflog = new ObservableCollection<ReflogEntry>(reflog);
                    CurrentBranch = branch;
                });
            });
        }

        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded || !_git.IsOpen) return;
            _isLoaded = true;

            // Load cache and working-dir status in parallel so the uncommitted-changes
            // node appears immediately without waiting for the full refresh.
            var cacheTask  = Task.Run(() => LoadCache());
            var statusTask = _git.Executor.RunAsync(() => _git.GetWorkingDirectoryStatus());
            await Task.WhenAll(cacheTask, statusTask);
            var cache       = cacheTask.Result;
            var liveChanges = statusTask.Result.Count > 0;

            if (cache?.Commits?.Count > 0)
            {
                _hasUncommittedChanges = liveChanges;
                var displayList = BuildDisplayList(cache.Commits, liveChanges);
                var nodes = await Task.Run(() => GraphLayout.Compute(displayList));
                ApplyCacheToUI(cache, nodes, liveChanges);
                StatusMessage = "Refreshing…";
            }

            await RefreshAsync();
            StartWatcher();
            StartAutoRefresh();
            _ = Hosting.LoadPullRequestsAsync(); // fire-and-forget network call — never blocks tab load
            _ = LoadSubmodulesAsync();
            _ = LoadWorktreesAsync();
            _ = LoadCommitTemplateAsync();
        }

        /// <summary>Pre-fills the commit box from `commit.template` when one is configured and the box is empty.</summary>
        private async Task LoadCommitTemplateAsync()
        {
            try
            {
                var path = await _git.Executor.RunAsync(() => _git.GetConfigString("commit.template"));
                if (string.IsNullOrWhiteSpace(path)) return;
                if (path.StartsWith("~"))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        path.TrimStart('~', '/', '\\'));
                if (!File.Exists(path)) return;
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(CommitMessage) && !string.IsNullOrWhiteSpace(text))
                    CommitMessage = text;
            }
            catch (Exception ex) { AppLog.Warn("commit.template load failed", ex); }
        }

        public async Task CloneRepoAsync(string url, string localPath, string username, string password,
            string branch = null, bool recurseSubmodules = false)
        {
            // Submodule recursion needs git.exe on both paths — libgit2sharp 0.27's
            // RecurseSubmodules is unreliable with credentials, so prefer the CLI when asked.
            if ((GitCli.IsSshUrl(url) || recurseSubmodules) && GitCli.IsGitAvailable)
            {
                var parentDir = Path.GetDirectoryName(localPath.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(parentDir)) parentDir = Environment.CurrentDirectory;
                var extra = (recurseSubmodules ? " --recurse-submodules" : "") +
                            (!string.IsNullOrWhiteSpace(branch) ? $" --branch {CliGitService.Quote(branch.Trim())}" : "");
                await RunAsync($"Cloning {url}…", () =>
                {
                    var result = GitCli.RunAsync(parentDir,
                        $"clone{extra} {CliGitService.Quote(url)} {CliGitService.Quote(localPath)}",
                        new GitCliOptions { Progress = new Progress<string>(ReportProgress) }, OpToken)
                        .GetAwaiter().GetResult();
                    if (!result.Success)
                        throw new InvalidOperationException(result.ErrorText);
                });
            }
            else
            {
                await RunAsync($"Cloning {url}…", () =>
                {
                    GitService.Clone(url, localPath, username, password,
                        new Progress<string>(ReportProgress), OpToken, branch);
                });
            }
            await OpenRepoAsync(localPath);
            if (_git.IsOpen)
            {
                RemoteUsername = username;
                RemotePassword = password;
                await RefreshAsync();
                SaveCredentials(); // Remotes are populated after refresh
            }
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        public Task RefreshAsync() => RefreshAsync(force: false);

        public async Task RefreshAsync(bool force)
        {
            if (!_git.IsOpen || RepoDirectoryMissing()) return;
            _isLoaded = true;
            // Called both standalone (watcher tick, failsafe timer, initial load — none of which
            // are already inside a busy scope) and as the last step of a RunThenRefresh* sequence
            // (which already holds the busy scope). In the latter case, go through RunWorkAsync
            // directly so refresh doesn't re-trip the "already busy" guard or drop IsBusy back to
            // false before the whole sequence is actually done (see TryEnterBusyScope).
            Action work = () =>
            {
                var branch   = _git.GetCurrentBranch();
                var headSha  = _git.GetHeadSha();
                var bisect   = _git.GetBisectState();
                var history  = _git.GetHistory(_commitLimit, bisect);
                var branches = _git.GetBranches();
                var tags     = _git.GetTags();
                var stashes  = _git.GetStashes();
                var remotes  = _git.GetRemotes();
                var status   = _git.GetWorkingDirectoryStatus();
                var conflict = _git.GetConflictState();

                // Conflict/bisect state are cheap (a few file reads) — refresh every cycle
                // regardless of the signature-skip below, so neither banner ever goes stale.
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConflictInfo = conflict;
                    // GetBisectState() can't recover RevisionsLeft/StepsRemaining/Found from files
                    // (git only prints them on the triggering step's own stdout) — if HEAD hasn't
                    // moved since the last known step, carry those numbers forward instead of
                    // flashing "resuming…" on every incidental refresh between bisect steps.
                    if (bisect.InProgress && _bisectInfo?.CurrentSha == bisect.CurrentSha)
                    {
                        bisect.RevisionsLeft = _bisectInfo.RevisionsLeft;
                        bisect.StepsRemaining = _bisectInfo.StepsRemaining;
                        bisect.Found = _bisectInfo.Found;
                        bisect.FirstBadSha = _bisectInfo.FirstBadSha;
                        bisect.FirstBadSummary = _bisectInfo.FirstBadSummary;
                    }
                    BisectInfo = bisect;
                    if (conflict.HasConflicts) ShowWorkingDir = true;
                    // Always resync from this same status snapshot, not just during conflicts —
                    // otherwise the "Uncommitted changes" node (driven by `hasChanges` below, from
                    // this same `status`) and the Staged/Unstaged file lists (otherwise only kept
                    // current by LoadWorkingDirAsync/RefreshWorkingDirStatusAsync's own, separately
                    // timed status snapshot) can disagree about whether there are any changes.
                    ApplyWorkingDirStatus(status, updateGraph: false);
                });

                // Skip the graph/UI/cache rebuild when the repo state is identical —
                // rebuilding GraphNodes resets scroll and selection, and auto-refresh
                // ticks would otherwise pay the full cost for nothing.
                var signature = ComputeRefreshSignature(branch, headSha, history, branches, tags, stashes, status);
                if (!force && signature == _lastRefreshSignature)
                    return;

                var commits = history.Commits;
                var hasChanges = status.Count > 0;
                var filtered = FilterCommits(commits);

                // Compute graph on background thread — not on the UI thread
                // (uncommitted node is hidden while a search filter is active)
                var displayList = BuildDisplayList(filtered,
                    hasChanges && string.IsNullOrWhiteSpace(_searchText));
                var nodes = GraphLayout.Compute(displayList);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var savedSha = _selectedNode?.Commit?.Sha;

                    _allCommits            = commits;
                    _hasUncommittedChanges = hasChanges;
                    HasMoreCommits         = history.ReachedLimit;
                    BranchMasks            = history.BranchMasks;

                    GraphNodes = new ObservableCollection<GraphNode>(nodes);
                    var local  = branches.Where(b => !b.IsRemote).ToList();
                    var remote = FilterRemoteBranches(branches, remotes);
                    LocalBranches    = new ObservableCollection<BranchInfo>(local);
                    RemoteBranches   = new ObservableCollection<BranchInfo>(remote);
                    LocalBranchTree  = BuildBranchTree(local, "local");
                    RemoteBranchTree = BuildBranchTree(remote, "remote");
                    Hosting.UpdateBranchPrBadges();
                    Tags    = new ObservableCollection<TagInfo>(tags);
                    Stashes = new ObservableCollection<StashInfo>(stashes);
                    Remotes = new ObservableCollection<RemoteInfo>(remotes);
                    CurrentBranch = branch;

                    if (savedSha != null)
                    {
                        var restored = nodes.FirstOrDefault(n => n.Commit?.Sha == savedSha);
                        if (restored != null)
                        {
                            SelectedNode = restored;
                            // Rebuilding GraphNodes resets the ListView scroll — bring the
                            // restored selection back into view (no-op if already visible)
                            ScrollToNodeRequested?.Invoke(this, restored);
                        }
                    }
                });

                _lastRefreshSignature = signature;

                // Persist to disk so next open can show data instantly
                SaveCache(new RepoCache
                {
                    Commits               = commits,
                    Branches              = branches,
                    Tags                  = tags,
                    Stashes               = stashes,
                    Remotes               = remotes,
                    CurrentBranch         = branch,
                    HasUncommittedChanges = hasChanges,
                    ReachedLimit          = history.ReachedLimit
                });
            };

            if (IsBusy)
                await RunWorkAsync("Refreshing…", work);
            else
                await RunAsync("Refreshing…", work);
        }

        /// <summary>
        /// Cheap fingerprint of everything the UI renders — when unchanged between
        /// refreshes, the graph recompute, UI rebuild and cache write are skipped.
        /// </summary>
        private static string ComputeRefreshSignature(string branch, string headSha,
            CommitHistory history, List<BranchInfo> branches, List<TagInfo> tags,
            List<StashInfo> stashes, List<FileChange> status)
        {
            var sb = new StringBuilder(512);
            sb.Append(branch).Append('|').Append(headSha)
              .Append('|').Append(history.Commits.Count)
              .Append('|').Append(history.ReachedLimit ? '1' : '0');
            foreach (var b in branches)
                sb.Append('|').Append(b.FullName).Append('=').Append(b.TipSha)
                  .Append('^').Append(b.AheadBy).Append(',').Append(b.BehindBy);
            foreach (var t in tags)
                sb.Append('|').Append(t.Name).Append('=').Append(t.TargetSha);
            foreach (var s in stashes)
                sb.Append('|').Append(s.Sha);
            foreach (var f in status)
                sb.Append('|').Append(f.Path).Append(':').Append((int)f.Kind).Append(f.IsStaged ? 'S' : 'W');
            return sb.ToString();
        }

        private List<CommitInfo> BuildDisplayList(List<CommitInfo> commits, bool hasUncommittedChanges)
        {
            if (!hasUncommittedChanges || commits.Count == 0) return commits;
            var list = new List<CommitInfo>(commits.Count + 1);
            list.Add(new CommitInfo
            {
                Sha = string.Empty,
                Message = "Uncommitted changes",
                AuthorName = string.Empty,
                AuthorEmail = string.Empty,
                AuthorDate = DateTimeOffset.Now,
                ParentShas = new List<string> { commits[0].Sha },
                IsUncommitted = true
            });
            list.AddRange(commits);
            return list;
        }

        /// <summary>Applies the smart-visibility mask and the search text to the full commit list.</summary>
        private List<CommitInfo> FilterCommits(List<CommitInfo> commits)
        {
            IEnumerable<CommitInfo> seq = commits;
            if (_smartBranchVisibility)
                seq = seq.Where(c => (c.RefMask & CommitHistory.CurrentBranchMask) != 0);
            var search = _searchText?.Trim();
            if (!string.IsNullOrEmpty(search))
                seq = seq.Where(c =>
                    (c.Message != null && c.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.AuthorName != null && c.AuthorName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.AuthorEmail != null && c.AuthorEmail.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.Sha != null && c.Sha.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            return ReferenceEquals(seq, commits) ? commits : seq.ToList();
        }

        private int _searchMatchCount;
        public int SearchMatchCount { get => _searchMatchCount; private set => Set(ref _searchMatchCount, value); }

        /// <summary>"N matches" label next to the search box; hidden while the box is empty.</summary>
        public bool ShowSearchMatchCount => !string.IsNullOrWhiteSpace(_searchText);

        private void ApplyFilter()
        {
            if (_allCommits.Count == 0) return;
            var searching = !string.IsNullOrWhiteSpace(_searchText);
            var filtered = FilterCommits(_allCommits);
            SearchMatchCount = filtered.Count;
            RaisePropertyChanged(nameof(ShowSearchMatchCount));
            var displayList = BuildDisplayList(filtered, _hasUncommittedChanges && !searching);
            Task.Run(() =>
            {
                // Search results are a discontiguous subset — use the cheap single-lane layout
                var nodes = searching ? GraphLayout.ComputeFlat(displayList) : GraphLayout.Compute(displayList);
                Application.Current.Dispatcher.Invoke(() =>
                    GraphNodes = new ObservableCollection<GraphNode>(nodes));
            });
        }

        // ── Cache ─────────────────────────────────────────────────────────────

        private sealed class RepoCache
        {
            public List<CommitInfo> Commits { get; set; }
            public List<BranchInfo> Branches { get; set; }
            public List<TagInfo> Tags { get; set; }
            public List<StashInfo> Stashes { get; set; }
            public List<RemoteInfo> Remotes { get; set; }
            public string CurrentBranch { get; set; }
            public bool HasUncommittedChanges { get; set; }
            public bool ReachedLimit { get; set; }
        }

        private static string GetCacheFile(string repoPath)
        {
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(repoPath.ToUpperInvariant()));
            var key = BitConverter.ToString(hash, 0, 8).Replace("-", "");
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PickleGit", "cache");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, key + ".json");
        }

        private RepoCache LoadCache()
        {
            try
            {
                if (_cacheFile == null || !File.Exists(_cacheFile)) return null;
                return JsonConvert.DeserializeObject<RepoCache>(File.ReadAllText(_cacheFile));
            }
            catch { return null; }
        }

        private void SaveCache(RepoCache cache)
        {
            try
            {
                if (_cacheFile == null) return;
                File.WriteAllText(_cacheFile, JsonConvert.SerializeObject(cache));
            }
            catch { }
        }

        private void ApplyCacheToUI(RepoCache cache, List<GraphNode> nodes, bool? liveHasChanges = null)
        {
            _allCommits            = cache.Commits  ?? new List<CommitInfo>();
            _hasUncommittedChanges = liveHasChanges ?? cache.HasUncommittedChanges;
            HasMoreCommits         = cache.ReachedLimit;

            GraphNodes = new ObservableCollection<GraphNode>(nodes);
            var local  = (cache.Branches ?? new List<BranchInfo>()).Where(b => !b.IsRemote).ToList();
            var remote = FilterRemoteBranches(cache.Branches, cache.Remotes);
            LocalBranches    = new ObservableCollection<BranchInfo>(local);
            RemoteBranches   = new ObservableCollection<BranchInfo>(remote);
            LocalBranchTree  = BuildBranchTree(local, "local");
            RemoteBranchTree = BuildBranchTree(remote, "remote");
            Hosting.UpdateBranchPrBadges();
            Tags    = new ObservableCollection<TagInfo>  (cache.Tags    ?? new List<TagInfo>());
            Stashes = new ObservableCollection<StashInfo>(cache.Stashes ?? new List<StashInfo>());
            Remotes = new ObservableCollection<RemoteInfo>(cache.Remotes ?? new List<RemoteInfo>());
            CurrentBranch = cache.CurrentBranch;
        }

        // ── External-change detection ─────────────────────────────────────────

        private void StartWatcher()
        {
            if (_watcher != null || !_git.IsOpen) return;
            try
            {
                _watcher = new RepositoryWatcher(_git.WorkingDirectory, _git.GitDirectory);
                _watcher.Changed += OnRepoChangedExternally;
            }
            catch { _watcher = null; }
        }

        private void OnRepoChangedExternally(RepoChangeKind kind)
        {
            // Raised on a threadpool thread — marshal to the dispatcher
            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!_git.IsOpen || IsBusy) return;
                try
                {
                    if (kind == RepoChangeKind.Refs)
                        await RefreshAsync();
                    else
                        await RefreshWorkingDirStatusAsync();
                }
                catch { }
            }));
        }

        // 5-minute failsafe poll — the watcher is the primary refresh trigger, but
        // FileSystemWatcher can silently miss events on network drives.
        private void StartAutoRefresh()
        {
            if (_refreshTimer != null) return;
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (!_git.IsOpen || IsBusy) return;
                try { await RefreshAsync(); }
                catch { }
            };
            _refreshTimer.Start();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Like RunAsync, but keeps the busy scope held continuously across the primary
        /// work plus a trailing refresh, so the "Working" indicator doesn't drop out and back in
        /// between the two steps (each of which used to be its own separately-guarded RunAsync
        /// call).</summary>
        private async Task<bool> RunThenRefresh(string status, Action work)
        {
            if (!TryEnterBusyScope()) return false;
            try
            {
                var ok = await RunWorkAsync(status, work);
                await RefreshAsync();
                return ok;
            }
            finally { IsBusy = false; }
        }

        /// <summary>Like RunThenRefresh, for ops that also change the working directory (reset, revert, stash).</summary>
        private async Task<bool> RunThenRefreshWorkingDir(string status, Action work)
        {
            if (!TryEnterBusyScope()) return false;
            try
            {
                var ok = await RunWorkAsync(status, work);
                await LoadWorkingDirAsync();
                await RefreshAsync();
                return ok;
            }
            finally { IsBusy = false; }
        }

        /// <summary>Like RunThenRefresh, for checkouts — also re-smudges any now-stale LFS pointer files
        /// before refreshing (see TryLfsCheckoutAsync).</summary>
        private async Task<bool> RunThenRefreshCheckout(string status, Action work)
        {
            if (!TryEnterBusyScope()) return false;
            try
            {
                var ok = await RunWorkAsync(status, work);
                if (ok) await TryLfsCheckoutAsync();
                await RefreshAsync();
                return ok;
            }
            finally { IsBusy = false; }
        }

        // ── Operation cancellation ────────────────────────────────────────────
        // One CTS per running operation. CLI paths pass the token to git.exe (the process is
        // killed on cancel); libgit2 network ops poll it from their transfer-progress callbacks.
        private System.Threading.CancellationTokenSource _opCts;
        public bool CanCancel => _opCts != null;
        public ICommand CancelOperationCommand { get; private set; }

        /// <summary>The running operation's token ('None' when idle). Safe to capture inside work lambdas.</summary>
        private System.Threading.CancellationToken OpToken => _opCts?.Token ?? System.Threading.CancellationToken.None;

        private void CancelOperation()
        {
            try { _opCts?.Cancel(); } catch { }
            StatusMessage = "Cancelling…";
        }

        // ── Determinate progress ──────────────────────────────────────────────
        private static readonly System.Text.RegularExpressions.Regex PercentRegex =
            new System.Text.RegularExpressions.Regex(@"(\d{1,3})%",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private int _progressPercent = -1;
        /// <summary>0–100 while a transfer reports percentages; -1 hides the bar.</summary>
        public int ProgressPercent
        {
            get => _progressPercent;
            private set { if (Set(ref _progressPercent, value)) RaisePropertyChanged(nameof(HasProgress)); }
        }
        public bool HasProgress => _progressPercent >= 0;

        /// <summary>Routes a progress message to the status bar and mines any "NN%" out of it
        /// for the determinate progress bar (git emits these on clone/fetch/push transfers).</summary>
        private void ReportProgress(string message)
        {
            StatusMessage = message;
            var m = PercentRegex.Match(message ?? string.Empty);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var pct) && pct >= 0 && pct <= 100)
                ProgressPercent = pct;
        }

        /// <summary>Claims the single busy scope, or reports it's already held and refuses.
        /// Most commands only gate their CanExecute on HasRepo, not !IsBusy, so this check — not
        /// those bindings — is what actually stops a second overlapping call from clobbering the
        /// first operation's state. Callers must reset <see cref="IsBusy"/> to false themselves
        /// (in a finally block) once their whole sequence — one step or several chained ones — is
        /// done, so a multi-step sequence (see RunThenRefresh*) can stay continuously "Working"
        /// instead of flickering between steps.</summary>
        private bool TryEnterBusyScope()
        {
            if (IsBusy)
            {
                StatusMessage = "Another operation is already in progress.";
                return false;
            }
            IsBusy = true;
            return true;
        }

        private async Task<bool> RunAsync(string status, Action work)
        {
            if (!TryEnterBusyScope()) return false;
            try { return await RunWorkAsync(status, work); }
            finally { IsBusy = false; }
        }

        /// <summary>Runs one unit of git work and reports/classifies failures. Does not touch
        /// <see cref="IsBusy"/> — the caller (either <see cref="RunAsync"/> for a single step, or
        /// a RunThenRefresh* helper chaining several steps under one busy scope) owns that.</summary>
        private async Task<bool> RunWorkAsync(string status, Action work)
        {
            StatusMessage = status;
            var suppression = _watcher?.Suppress();
            _opCts = new System.Threading.CancellationTokenSource();
            RaisePropertyChanged(nameof(CanCancel));
            try
            {
                // All git work runs on the dedicated executor thread — never the UI thread
                await _git.Executor.RunAsync(work);
                StatusMessage = "Ready";
                return true;
            }
            catch (Exception ex)
            {
                if (_opCts.IsCancellationRequested)
                {
                    StatusMessage = "Operation cancelled";
                    return false;
                }
                var msg = ex.Message;
                // libgit2 reports genuine credential rejection as "authentication replays" — the
                // server kept rejecting the same credentials until libgit2 gave up. Purge them and
                // force a fresh prompt next attempt.
                if (msg.IndexOf("authentication replays", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Purge the stale PickleGit entry so it won't shadow a fresh credential
                    var failedUser = RemoteUsername;
                    var failedUrl = Remotes.FirstOrDefault()?.Url;
                    RemoteUsername = null;
                    RemotePassword = null;
                    // Force the dialog on the next attempt — skip GCM/git-credential-fill which
                    // would just replay the same bad token
                    _forceCredentialDialog = true;
                    if (!string.IsNullOrEmpty(failedUser) && !string.IsNullOrEmpty(failedUrl))
                    {
                        try
                        {
                            if (Uri.TryCreate(failedUrl, UriKind.Absolute, out var failedUri))
                                Services.CredentialStore.Delete(failedUri.Host, failedUser);
                        }
                        catch { }
                    }
                    msg = "Authentication failed. Check that your username and password (or app password) are correct.";
                }
                // "Too many redirects" on its own (not paired with "authentication replays") is
                // libgit2's other trigger for the same underlying cap, but it's frequently a genuine
                // HTTP redirect loop (misconfigured remote URL, corporate proxy/SSO) rather than a
                // bad password — treating it as a credential failure would wrongly delete a correct
                // saved credential and blame the wrong thing. Point at the network/URL instead, and
                // leave any saved credential untouched.
                else if (msg.IndexOf("too many redirects", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    msg = "Too many redirects. This usually means the remote URL is wrong (e.g. an http:// " +
                          "URL that keeps redirecting to https://) or a proxy/VPN is intercepting the " +
                          "request — check the remote URL and any proxy settings. If your credentials were " +
                          "actually the problem, you'll be prompted to re-enter them next attempt.";
                }
                // SSH runs with GIT_TERMINAL_PROMPT=0, so agent/key problems fail without any
                // prompt — translate the common ones into something actionable.
                else if (msg.IndexOf("Permission denied (publickey", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    msg = "SSH authentication failed (publickey).\n\n" +
                          "• Make sure your key is loaded: run `ssh-add ~\\.ssh\\id_ed25519` (start the " +
                          "\"OpenSSH Authentication Agent\" Windows service first if needed).\n" +
                          "• Passphrase-protected keys must be in the agent — PickleGit cannot prompt for a passphrase.\n" +
                          "• Confirm the public key is registered with the server.";
                }
                else if (msg.IndexOf("Host key verification failed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    msg = "SSH host key verification failed.\n\n" +
                          "The server isn't in your known_hosts yet, and PickleGit cannot show the interactive " +
                          "accept prompt. Run any git command against this remote once from a terminal " +
                          "(e.g. `git fetch`) and accept the host key, then retry here.";
                }
                StatusMessage = $"Error: {msg}";
                DialogService.ShowError("Operation Failed", msg, ex.ToString());
                return false;
            }
            finally
            {
                suppression?.Dispose();
                var cts = _opCts;
                _opCts = null;
                cts?.Dispose();
                RaisePropertyChanged(nameof(CanCancel));
                ProgressPercent = -1;
            }
        }

        private ObservableCollection<BranchNodeViewModel> BuildBranchTree(
            IEnumerable<BranchInfo> branches,
            string scope)
        {
            return BranchNodeViewModel.Build(
                branches,
                scope,
                _hasSavedBranchNodeState ? _collapsedBranchNodes : null,
                OnBranchNodeExpansionChanged);
        }

        private static List<BranchInfo> FilterRemoteBranches(
            IEnumerable<BranchInfo> branches,
            IEnumerable<RemoteInfo> remotes)
        {
            var remoteNames = new HashSet<string>(
                (remotes ?? Enumerable.Empty<RemoteInfo>()).Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);

            return (branches ?? Enumerable.Empty<BranchInfo>())
                .Where(b => b.IsRemote &&
                    (remoteNames.Count == 0 || remoteNames.Contains(b.RemoteName ?? string.Empty)))
                .ToList();
        }

        private void OnBranchNodeExpansionChanged(BranchNodeViewModel node)
        {
            if (node == null || !node.IsGroup || string.IsNullOrEmpty(node.ExpansionKey))
                return;

            if (!node.IsExpanded)
                _collapsedBranchNodes.Add(node.ExpansionKey);
            else
                _collapsedBranchNodes.Remove(node.ExpansionKey);

            _hasSavedBranchNodeState = true;
            AppSettings.SaveCollapsedBranchNodes(RepoPath, _collapsedBranchNodes);
        }

        public void Dispose()
        {
            StopRepoMonitoring();
            _git.Dispose();
        }

        /// <summary>
        /// True (and stops the watcher/auto-refresh timer) when the repo's working directory no
        /// longer exists — e.g. its worktree was removed while this tab was still open elsewhere.
        /// Prevents a flood of "reference HEAD not found" error dialogs on every refresh tick.
        /// </summary>
        private bool RepoDirectoryMissing()
        {
            if (string.IsNullOrEmpty(RepoPath) || Directory.Exists(RepoPath)) return false;
            StopRepoMonitoring();
            return true;
        }

        private void StopRepoMonitoring()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer = null;
            }
            if (_watcher != null)
            {
                _watcher.Changed -= OnRepoChangedExternally;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private void RaiseDetailPanelPropertiesChanged()
        {
            RaisePropertyChanged(nameof(HasDetailPanel));
            RaisePropertyChanged(nameof(DetailPanelWidth));
            RaisePropertyChanged(nameof(DetailPanelMinWidth));
            RaisePropertyChanged(nameof(DetailSplitterWidth));
            RaisePropertyChanged(nameof(ShowDiffInsteadOfCommits));
            RaisePropertyChanged(nameof(ShowCommitFilesPanel));
        }
    }
}
