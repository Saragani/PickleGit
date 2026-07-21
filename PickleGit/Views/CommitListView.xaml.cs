using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PickleGit.Behaviors;
using PickleGit.Controls;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class CommitListView : UserControl
    {
        private ScrollViewer _listScrollViewer;
        private ScrollContentPresenter _listContentPresenter;
        private ScrollBar _verticalScrollBar;

        public CommitListView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _listScrollViewer = FindDescendant<ScrollViewer>(CommitList);
            if (_listScrollViewer != null)
            {
                _listContentPresenter = FindDescendant<ScrollContentPresenter>(_listScrollViewer);
                _verticalScrollBar = FindDescendant<ScrollBar>(
                    _listScrollViewer,
                    scrollBar => scrollBar.Orientation == Orientation.Vertical);

                _listScrollViewer.ScrollChanged -= OnListScrollChanged;
                _listScrollViewer.ScrollChanged += OnListScrollChanged;
            }

            UpdateCommitListViewportWidth();
        }

        private void OnListScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0)
                HeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset);

            UpdateCommitListViewportWidth();
        }

        // ── Commit search / filter ────────────────────────────────────────────

        private RepositoryViewModel RepoVm => DataContext as RepositoryViewModel;

        /// <summary>Opens the filter bar and focuses its input (Ctrl+F entry point).</summary>
        public void OpenSearch()
        {
            var vm = RepoVm;
            if (vm == null) return;
            vm.IsSearchOpen = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        /// <summary>Moves keyboard focus into the commit list (Ctrl+1) so arrow keys navigate commits.</summary>
        public void FocusList()
        {
            if (CommitList.Items.Count == 0) { CommitList.Focus(); return; }
            if (CommitList.SelectedIndex < 0) CommitList.SelectedIndex = 0;
            var item = CommitList.ItemContainerGenerator.ContainerFromIndex(
                Math.Max(0, CommitList.SelectedIndex)) as System.Windows.Controls.ListViewItem;
            if (item != null) item.Focus();
            else CommitList.Focus();
        }

        private void SearchToggle_Click(object sender, RoutedEventArgs e)
        {
            var vm = RepoVm;
            if (vm == null) return;
            if (vm.IsSearchOpen) vm.IsSearchOpen = false;
            else OpenSearch();
        }

        private void CloseSearch_Click(object sender, RoutedEventArgs e)
        {
            var vm = RepoVm;
            if (vm != null) vm.IsSearchOpen = false;
        }

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                var vm = RepoVm;
                if (vm != null) vm.IsSearchOpen = false;
                CommitList.Focus();
                e.Handled = true;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateCommitListViewportWidth));
        }

        private void UpdateCommitListViewportWidth()
        {
            var vm = AppVm;
            if (vm == null)
                return;

            var width = GetCommitListContentViewportWidth();

            if (!double.IsNaN(width) && !double.IsInfinity(width))
                vm.CommitListViewportWidth = width;
        }

        private double GetCommitListContentViewportWidth()
        {
            if (_listContentPresenter != null && _listContentPresenter.ActualWidth > 0)
                return _listContentPresenter.ActualWidth;

            if (_listScrollViewer != null && _listScrollViewer.ViewportWidth > 0)
                return _listScrollViewer.ViewportWidth;

            var width = CommitList.ActualWidth;
            if (_verticalScrollBar != null &&
                _verticalScrollBar.Visibility == Visibility.Visible &&
                _verticalScrollBar.ActualWidth > 0)
            {
                width -= _verticalScrollBar.ActualWidth;
            }

            return Math.Max(0, width);
        }

        private static T FindDescendant<T>(
            DependencyObject parent,
            Predicate<T> predicate = null) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found && (predicate == null || predicate(found))) return found;
                var result = FindDescendant<T>(child, predicate);
                if (result != null) return result;
            }
            return null;
        }

        private AppViewModel AppVm => Window.GetWindow(this)?.DataContext as AppViewModel;

        private void ResizeBranchTag_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthBranchTag = Math.Max(
                AppViewModel.MinColWidthBranchTag,
                AppVm.ColWidthBranchTag + e.HorizontalChange);

        private void ResizeGraph_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthGraph = Math.Max(
                AppViewModel.MinColWidthGraph,
                AppVm.ColWidthGraph + e.HorizontalChange);

        private void ResizeCommitDesc_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthCommitDesc = Math.Max(
                AppViewModel.MinColWidthCommitDesc,
                AppVm.ColWidthCommitDesc + e.HorizontalChange);

        private void ResizeAuthor_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthAuthor = Math.Max(
                AppViewModel.MinColWidthAuthor,
                AppVm.ColWidthAuthor + e.HorizontalChange);

        private void ResizeDateTime_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthDateTime = Math.Max(
                AppViewModel.MinColWidthDateTime,
                AppVm.ColWidthDateTime + e.HorizontalChange);

        private void ResizeSha_DragDelta(object sender, DragDeltaEventArgs e)
            => AppVm.ColWidthSha = Math.Max(
                AppViewModel.MinColWidthSha,
                AppVm.ColWidthSha + e.HorizontalChange);

        private void GearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        // ── Drag-and-drop: drag a commit row out onto a branch in the sidebar (cherry-pick) ──

        private Point _commitDragStart;
        private string _commitDragCandidateSha;

        private void CommitRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _commitDragStart = e.GetPosition(null);
            _commitDragCandidateSha = (sender as FrameworkElement)?.DataContext is GraphNode node &&
                node.Commit != null && !node.Commit.IsUncommitted && !node.Commit.IsStash
                ? node.Commit.Sha
                : null;
        }

        private void CommitRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_commitDragCandidateSha == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _commitDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _commitDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (!(sender is FrameworkElement fe)) return;

            var sha = _commitDragCandidateSha;
            _commitDragCandidateSha = null;
            var data = new DataObject(DragDropFormats.CommitSha, sha);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }

        // ── Branch/tag hover: dim commits unrelated to the hovered ref (1s delay, 0.25s fade) ──

        private DispatcherTimer _refHoverTimer;

        private static string NormalizeRefName(string refName) =>
            refName != null && refName.StartsWith("HEAD -> ") ? refName.Substring(8) : refName;

        private void RefLabel_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!(sender is RefLabel label) || string.IsNullOrEmpty(label.RefName)) return;
            var name = NormalizeRefName(label.RefName);
            // Tags aren't branch tips — they have no mask bit, so there's nothing to dim by.
            if (name.StartsWith("tag: ")) return;
            var vm = RepoVm;
            if (vm == null || !vm.BranchMasks.TryGetValue(name, out var bit)) return;

            _refHoverTimer?.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                ApplyBranchDim(bit);
            };
            _refHoverTimer = timer;
            timer.Start();
        }

        private void RefLabel_MouseLeave(object sender, MouseEventArgs e)
        {
            _refHoverTimer?.Stop();
            _refHoverTimer = null;
            ClearBranchDim();
        }

        private void ApplyBranchDim(ulong bit)
        {
            for (int i = 0; i < CommitList.Items.Count; i++)
            {
                if (!(CommitList.Items[i] is GraphNode node) || node.Commit == null) continue;
                if (!(CommitList.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem container)) continue;
                var related = (node.Commit.RefMask & bit) != 0;
                AnimateOpacity(container, related ? 1.0 : 0.5);
            }
        }

        private void ClearBranchDim()
        {
            for (int i = 0; i < CommitList.Items.Count; i++)
            {
                if (CommitList.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem container)
                    AnimateOpacity(container, 1.0);
            }
        }

        private static void AnimateOpacity(UIElement el, double to)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromSeconds(0.25));
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // ── Row hover: reveal ghost badges for branches this commit belongs to but doesn't
        //    already display (Feature B) — only on the hovered row, no delay/animation. ──

        private static Grid FindRowRoot(DependencyObject start)
        {
            var cur = start;
            while (cur != null)
            {
                if (cur is Grid g && g.Name == "RowRoot") return g;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        private static ItemsControl FindGhostRefsHost(DependencyObject start)
        {
            var rowRoot = FindRowRoot(start);
            return rowRoot != null
                ? FindDescendant<ItemsControl>(rowRoot, ic => ic.Name == "GhostRefsHost")
                : null;
        }

        private void RowContent_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is GraphNode node) || node.Commit == null) return;
            var vm = RepoVm;
            var ghostHost = FindGhostRefsHost(fe);
            if (vm == null || ghostHost == null) return;

            var shown = new HashSet<string>(node.Commit.Refs.Select(NormalizeRefName));
            ghostHost.ItemsSource = vm.BranchMasks
                .Where(kv => (node.Commit.RefMask & kv.Value) != 0 && !shown.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();
        }

        private void RowContent_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement fe)) return;
            var ghostHost = FindGhostRefsHost(fe);
            if (ghostHost != null) ghostHost.ItemsSource = null;
        }
    }
}
