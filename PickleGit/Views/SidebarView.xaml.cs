using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
                    RebuildRows();
                    break;
            }
        }

        /// <summary>Case-insensitive filter typed into the search box at the top of the sidebar;
        /// null/empty means "show everything" (the original, unfiltered behavior).</summary>
        private string _searchText;

        private void RebuildRows()
        {
            var rows = new List<SidebarRow>();
            var search = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
            if (_repo != null)
            {
                bool localExpanded = _appVm?.SidebarLocalBranchesExpanded ?? true;
                var localRows = new List<SidebarRow>();
                bool localMatched = AppendBranchRows(localRows, _repo.LocalBranchTree, 1, SidebarRowKind.LocalBranchGroup, SidebarRowKind.LocalBranchLeaf, search);
                if (search == null)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.LocalBranchesHeader, IsExpanded = localExpanded });
                    if (localExpanded) rows.AddRange(localRows);
                }
                else if (localMatched)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.LocalBranchesHeader, IsExpanded = true });
                    rows.AddRange(localRows);
                }

                bool remoteExpanded = _appVm?.SidebarRemoteBranchesExpanded ?? true;
                var remoteBranchRows = new List<SidebarRow>();
                bool remoteMatched = AppendBranchRows(remoteBranchRows, _repo.RemoteBranchTree, 1, SidebarRowKind.RemoteBranchGroup, SidebarRowKind.RemoteBranchLeaf, search);
                if (search == null)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.RemoteBranchesHeader, IsExpanded = remoteExpanded });
                    if (remoteExpanded) rows.AddRange(remoteBranchRows);
                }
                else if (remoteMatched)
                {
                    rows.Add(new SidebarRow { Kind = SidebarRowKind.RemoteBranchesHeader, IsExpanded = true });
                    rows.AddRange(remoteBranchRows);
                }

                AddSimpleSection(rows, _repo.Tags, t => t.Name,
                    SidebarRowKind.TagsHeader, SidebarRowKind.Tag, _appVm?.SidebarTagsExpanded ?? false, search);

                AddSimpleSection(rows, _repo.Stashes, s => s.Message,
                    SidebarRowKind.StashesHeader, SidebarRowKind.Stash, _appVm?.SidebarStashesExpanded ?? false, search);

                AddSimpleSection(rows, _repo.Remotes, r => r.Name,
                    SidebarRowKind.RemotesHeader, SidebarRowKind.Remote, _appVm?.SidebarRemotesExpanded ?? false, search);

                if (_repo.Hosting?.HasHostingProvider == true)
                    AddSimpleSection(rows, _repo.Hosting.PullRequests, pr => pr.Title,
                        SidebarRowKind.PullRequestsHeader, SidebarRowKind.PullRequest, _pullRequestsExpanded, search);

                if (_repo.Submodules?.Count > 0)
                    AddSimpleSection(rows, _repo.Submodules, sm => sm.Name,
                        SidebarRowKind.SubmodulesHeader, SidebarRowKind.Submodule, _submodulesExpanded, search);

                if (_repo.Worktrees?.Count > 0)
                    AddSimpleSection(rows, _repo.Worktrees, wt => wt.Name,
                        SidebarRowKind.WorktreesHeader, SidebarRowKind.Worktree, _worktreesExpanded, search);
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

        /// <summary>Flattens a branch tree into rows. When <paramref name="search"/> is null, behaves
        /// exactly as before (groups always shown, children only when expanded). When searching, a
        /// group is only included if its own name or some descendant matches — and once included, its
        /// children are always shown (search results shouldn't stay hidden behind IsExpanded=false).
        /// Returns whether anything was added, so callers can decide whether to show the section's
        /// header at all while searching.</summary>
        private static bool AppendBranchRows(
            List<SidebarRow> rows,
            IEnumerable<BranchNodeViewModel> nodes,
            int indentLevel,
            SidebarRowKind groupKind,
            SidebarRowKind leafKind,
            string search)
        {
            bool anyAdded = false;
            foreach (var node in nodes)
            {
                if (node.IsGroup)
                {
                    if (search == null)
                    {
                        rows.Add(new SidebarRow { Kind = groupKind, IndentLevel = indentLevel, Payload = node });
                        anyAdded = true;
                        if (node.IsExpanded)
                            AppendBranchRows(rows, node.Children, indentLevel + 1, groupKind, leafKind, search);
                    }
                    else
                    {
                        var childRows = new List<SidebarRow>();
                        bool childMatched = AppendBranchRows(childRows, node.Children, indentLevel + 1, groupKind, leafKind, search);
                        bool nameMatches = node.DisplayName != null &&
                            node.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (childMatched || nameMatches)
                        {
                            rows.Add(new SidebarRow { Kind = groupKind, IndentLevel = indentLevel, Payload = node });
                            rows.AddRange(childRows);
                            anyAdded = true;
                        }
                    }
                }
                else
                {
                    bool include = search == null || (node.DisplayName != null &&
                        node.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (include)
                    {
                        rows.Add(new SidebarRow { Kind = leafKind, IndentLevel = indentLevel, Payload = node });
                        anyAdded = true;
                    }
                }
            }
            return anyAdded;
        }

        /// <summary>Shared logic for the non-branch sections (Tags/Stashes/Remotes/Pull
        /// Requests/Submodules/Worktrees): unfiltered, the header always shows and children show
        /// only when expanded (original behavior); while searching, the header shows only if at
        /// least one item's text matches, and matches are always shown regardless of the section's
        /// own expand/collapse state.</summary>
        private static void AddSimpleSection<T>(
            List<SidebarRow> rows, IEnumerable<T> items, Func<T, string> textOf,
            SidebarRowKind headerKind, SidebarRowKind leafKind, bool expanded, string search)
        {
            var leaves = (items ?? Enumerable.Empty<T>())
                .Where(x => search == null || (textOf(x) ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => new SidebarRow { Kind = leafKind, IndentLevel = 1, Payload = x })
                .ToList();
            if (search == null)
            {
                rows.Add(new SidebarRow { Kind = headerKind, IsExpanded = expanded });
                if (expanded) rows.AddRange(leaves);
            }
            else if (leaves.Count > 0)
            {
                rows.Add(new SidebarRow { Kind = headerKind, IsExpanded = true });
                rows.AddRange(leaves);
            }
        }

        private void SidebarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SidebarSearchBox.Text;
            RebuildRows();
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
