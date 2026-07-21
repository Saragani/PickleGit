using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PickleGit.Behaviors;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class SidebarView : UserControl
    {
        // ── Flattening: the sidebar is one flat, virtualized ListView (see plan/CLAUDE.md notes
        // on the TreeView it replaced). Rows are rebuilt into _rows whenever the underlying data
        // or any expand/collapse state changes; collapsing a section/folder simply omits its rows
        // rather than visually hiding an already-realized subtree. ─────────────────────────────

        private readonly ObservableCollection<SidebarRow> _rows = new ObservableCollection<SidebarRow>();
        private RepositoryViewModel _repo;
        private AppViewModel _appVm;

        // Sections with no persisted expand state today (matches the TreeView defaults they replace).
        private bool _pullRequestsExpanded = true;
        private bool _submodulesExpanded;
        private bool _worktreesExpanded;

        private static readonly HashSet<string> s_structuralRepoProps = new HashSet<string>
        {
            nameof(RepositoryViewModel.LocalBranchTree),
            nameof(RepositoryViewModel.RemoteBranchTree),
            nameof(RepositoryViewModel.Tags),
            nameof(RepositoryViewModel.Stashes),
            nameof(RepositoryViewModel.Remotes),
            nameof(RepositoryViewModel.Reflog),
            nameof(RepositoryViewModel.Submodules),
            nameof(RepositoryViewModel.Worktrees),
        };

        public SidebarView()
        {
            InitializeComponent();
            SidebarList.ItemsSource = _rows;
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var newAppVm = Window.GetWindow(this)?.DataContext as AppViewModel;
            if (!ReferenceEquals(newAppVm, _appVm))
            {
                if (_appVm != null) _appVm.PropertyChanged -= OnAppVmPropertyChanged;
                _appVm = newAppVm;
                if (_appVm != null) _appVm.PropertyChanged += OnAppVmPropertyChanged;
            }
            RebuildRows();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_appVm != null) _appVm.PropertyChanged -= OnAppVmPropertyChanged;
            _appVm = null;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_repo != null)
            {
                _repo.PropertyChanged -= OnRepoPropertyChanged;
                if (_repo.Hosting != null) _repo.Hosting.PropertyChanged -= OnHostingPropertyChanged;
            }

            _repo = e.NewValue as RepositoryViewModel;

            if (_repo != null)
            {
                _repo.PropertyChanged += OnRepoPropertyChanged;
                if (_repo.Hosting != null) _repo.Hosting.PropertyChanged += OnHostingPropertyChanged;
            }

            RebuildRows();
        }

        private void OnRepoPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || s_structuralRepoProps.Contains(e.PropertyName))
                RebuildRows();
        }

        private void OnHostingPropertyChanged(object sender, PropertyChangedEventArgs e) => RebuildRows();

        private void OnAppVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppViewModel.SidebarLocalBranchesExpanded):
                case nameof(AppViewModel.SidebarRemoteBranchesExpanded):
                case nameof(AppViewModel.SidebarTagsExpanded):
                case nameof(AppViewModel.SidebarStashesExpanded):
                case nameof(AppViewModel.SidebarRemotesExpanded):
                case nameof(AppViewModel.SidebarReflogExpanded):
                    RebuildRows();
                    break;
            }
        }

        private void RebuildRows()
        {
            var rows = new List<SidebarRow>();
            if (_repo != null)
            {
                bool localExpanded = _appVm?.SidebarLocalBranchesExpanded ?? true;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.LocalBranchesHeader, IsExpanded = localExpanded });
                if (localExpanded)
                    AppendBranchRows(rows, _repo.LocalBranchTree, 1, SidebarRowKind.LocalBranchGroup, SidebarRowKind.LocalBranchLeaf);

                bool remoteExpanded = _appVm?.SidebarRemoteBranchesExpanded ?? true;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.RemoteBranchesHeader, IsExpanded = remoteExpanded });
                if (remoteExpanded)
                    AppendBranchRows(rows, _repo.RemoteBranchTree, 1, SidebarRowKind.RemoteBranchGroup, SidebarRowKind.RemoteBranchLeaf);

                bool tagsExpanded = _appVm?.SidebarTagsExpanded ?? false;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.TagsHeader, IsExpanded = tagsExpanded });
                if (tagsExpanded)
                    foreach (var tag in _repo.Tags)
                        rows.Add(new SidebarRow { Kind = SidebarRowKind.Tag, IndentLevel = 1, Payload = tag });

                bool stashesExpanded = _appVm?.SidebarStashesExpanded ?? false;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.StashesHeader, IsExpanded = stashesExpanded });
                if (stashesExpanded)
                    foreach (var stash in _repo.Stashes)
                        rows.Add(new SidebarRow { Kind = SidebarRowKind.Stash, IndentLevel = 1, Payload = stash });

                bool remotesExpanded = _appVm?.SidebarRemotesExpanded ?? false;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.RemotesHeader, IsExpanded = remotesExpanded });
                if (remotesExpanded)
                    foreach (var remote in _repo.Remotes)
                        rows.Add(new SidebarRow { Kind = SidebarRowKind.Remote, IndentLevel = 1, Payload = remote });

                bool reflogExpanded = _appVm?.SidebarReflogExpanded ?? false;
                rows.Add(new SidebarRow { Kind = SidebarRowKind.ReflogHeader, IsExpanded = reflogExpanded });
                if (reflogExpanded)
                    foreach (var entry in _repo.Reflog)
                        rows.Add(new SidebarRow { Kind = SidebarRowKind.Reflog, IndentLevel = 1, Payload = entry });

                if (_repo.Hosting?.HasHostingProvider == true)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.PullRequestsHeader, IsExpanded = _pullRequestsExpanded });
                    if (_pullRequestsExpanded)
                        foreach (var pr in _repo.Hosting.PullRequests)
                            rows.Add(new SidebarRow { Kind = SidebarRowKind.PullRequest, IndentLevel = 1, Payload = pr });
                }

                if (_repo.Submodules?.Count > 0)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.SubmodulesHeader, IsExpanded = _submodulesExpanded });
                    if (_submodulesExpanded)
                        foreach (var sm in _repo.Submodules)
                            rows.Add(new SidebarRow { Kind = SidebarRowKind.Submodule, IndentLevel = 1, Payload = sm });
                }

                if (_repo.Worktrees?.Count > 0)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.WorktreesHeader, IsExpanded = _worktreesExpanded });
                    if (_worktreesExpanded)
                        foreach (var wt in _repo.Worktrees)
                            rows.Add(new SidebarRow { Kind = SidebarRowKind.Worktree, IndentLevel = 1, Payload = wt });
                }
            }

            // Preserve selection across a rebuild (the same BranchNodeViewModel instance may
            // reappear at a different index once ahead/behind or other data has changed).
            var previouslySelected = (SidebarList.SelectedItem as SidebarRow)?.Payload;

            _rows.Clear();
            foreach (var row in rows) _rows.Add(row);

            if (previouslySelected != null)
            {
                foreach (var row in _rows)
                {
                    if (ReferenceEquals(row.Payload, previouslySelected))
                    {
                        SidebarList.SelectedItem = row;
                        break;
                    }
                }
            }
        }

        private static void AppendBranchRows(
            List<SidebarRow> rows,
            IEnumerable<BranchNodeViewModel> nodes,
            int indentLevel,
            SidebarRowKind groupKind,
            SidebarRowKind leafKind)
        {
            foreach (var node in nodes)
            {
                if (node.IsGroup)
                {
                    rows.Add(new SidebarRow { Kind = groupKind, IndentLevel = indentLevel, Payload = node });
                    if (node.IsExpanded)
                        AppendBranchRows(rows, node.Children, indentLevel + 1, groupKind, leafKind);
                }
                else
                {
                    rows.Add(new SidebarRow { Kind = leafKind, IndentLevel = indentLevel, Payload = node });
                }
            }
        }

        // ── Section header expand/collapse ──────────────────────────────────────────────────

        private void ToggleLocalBranchesHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarLocalBranchesExpanded = !_appVm.SidebarLocalBranchesExpanded;
            RebuildRows();
        }

        private void ToggleRemoteBranchesHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarRemoteBranchesExpanded = !_appVm.SidebarRemoteBranchesExpanded;
            RebuildRows();
        }

        private void ToggleTagsHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarTagsExpanded = !_appVm.SidebarTagsExpanded;
            RebuildRows();
        }

        private void ToggleStashesHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarStashesExpanded = !_appVm.SidebarStashesExpanded;
            RebuildRows();
        }

        private void ToggleRemotesHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarRemotesExpanded = !_appVm.SidebarRemotesExpanded;
            RebuildRows();
        }

        private void ToggleReflogHeader(object sender, MouseButtonEventArgs e)
        {
            if (_appVm != null) _appVm.SidebarReflogExpanded = !_appVm.SidebarReflogExpanded;
            RebuildRows();
        }

        private void TogglePullRequestsHeader(object sender, MouseButtonEventArgs e)
        {
            _pullRequestsExpanded = !_pullRequestsExpanded;
            RebuildRows();
        }

        private void ToggleSubmodulesHeader(object sender, MouseButtonEventArgs e)
        {
            _submodulesExpanded = !_submodulesExpanded;
            RebuildRows();
        }

        private void ToggleWorktreesHeader(object sender, MouseButtonEventArgs e)
        {
            _worktreesExpanded = !_worktreesExpanded;
            RebuildRows();
        }

        private void ToggleBranchGroup(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BranchNodeViewModel node && node.IsGroup)
            {
                node.IsExpanded = !node.IsExpanded;
                RebuildRows();
            }
        }

        // ── Selection: only leaf branch rows drive SelectBranchCommand ───────────────────────

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SidebarList.SelectedItem is SidebarRow row
                && (row.Kind == SidebarRowKind.LocalBranchLeaf || row.Kind == SidebarRowKind.RemoteBranchLeaf)
                && row.Payload is BranchNodeViewModel node)
            {
                _repo?.SelectBranchCommand.Execute(node.BranchInfo);
            }
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        // ── Drag-and-drop: drag a branch (or a commit, from CommitListView) onto a branch ──

        private RepositoryViewModel RepoVm => DataContext as RepositoryViewModel;

        private Point _branchDragStart;
        private BranchInfo _branchDragCandidate;

        private void BranchNode_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _branchDragStart = e.GetPosition(null);
            _branchDragCandidate = (sender as FrameworkElement)?.DataContext is BranchNodeViewModel node && !node.IsGroup
                ? node.BranchInfo
                : null;
        }

        private void BranchNode_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_branchDragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _branchDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _branchDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (!(sender is FrameworkElement fe)) return;

            var source = _branchDragCandidate;
            _branchDragCandidate = null;
            var data = new DataObject(DragDropFormats.BranchName, source.Name);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
        }

        private void BranchNode_Drop(object sender, DragEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is BranchNodeViewModel targetNode) || targetNode.IsGroup)
                return;
            var vm = RepoVm;
            var targetName = targetNode.BranchInfo?.Name;
            if (vm == null || string.IsNullOrEmpty(targetName)) return;

            if (e.Data.GetDataPresent(DragDropFormats.BranchName))
            {
                var sourceName = e.Data.GetData(DragDropFormats.BranchName) as string;
                if (string.IsNullOrEmpty(sourceName) || string.Equals(sourceName, targetName, StringComparison.Ordinal))
                    return;
                ShowBranchDropMenu(fe, sourceName, targetName, vm);
            }
            else if (e.Data.GetDataPresent(DragDropFormats.CommitSha))
            {
                var sha = e.Data.GetData(DragDropFormats.CommitSha) as string;
                if (string.IsNullOrEmpty(sha)) return;
                ShowCommitDropMenu(fe, sha, targetName, vm);
            }
        }

        private static void ShowBranchDropMenu(FrameworkElement anchor, string sourceName, string targetName, RepositoryViewModel vm)
        {
            var menu = new ContextMenu { PlacementTarget = anchor };
            var merge = new MenuItem { Header = $"Merge '{sourceName}' into '{targetName}'" };
            merge.Click += (s, e) => vm.DragMergeBranches(sourceName, targetName);
            var rebase = new MenuItem { Header = $"Rebase '{targetName}' onto '{sourceName}'" };
            rebase.Click += (s, e) => vm.DragRebaseBranch(targetName, sourceName);
            menu.Items.Add(merge);
            menu.Items.Add(rebase);
            if (vm.Hosting.HasHostingProvider)
            {
                var createPr = new MenuItem { Header = $"Create Pull Request '{sourceName}' → '{targetName}'…" };
                createPr.Click += (s, e) => vm.Hosting.DragCreatePullRequest(sourceName, targetName);
                menu.Items.Add(new Separator());
                menu.Items.Add(createPr);
            }
            menu.IsOpen = true;
        }

        private static void ShowCommitDropMenu(FrameworkElement anchor, string sha, string targetName, RepositoryViewModel vm)
        {
            var shortSha = sha.Substring(0, Math.Min(7, sha.Length));
            var menu = new ContextMenu { PlacementTarget = anchor };
            var pick = new MenuItem { Header = $"Cherry-pick {shortSha} onto '{targetName}'" };
            pick.Click += (s, e) => vm.DragCherryPick(sha, targetName);
            menu.Items.Add(pick);
            menu.IsOpen = true;
        }
    }
}
