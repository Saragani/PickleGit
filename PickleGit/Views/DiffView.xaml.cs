using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class DiffView : UserControl
    {
        public DiffView()
        {
            InitializeComponent();
        }

        private RepositoryViewModel RepoVm => DataContext as RepositoryViewModel;

        // ── Line selection: click/ctrl/shift are native ListView behavior (SelectionMode=Extended);
        // this adds a plain click-drag range-select on top, matching SourceTree's line staging UX.
        // Only Added/Deleted rows participate — context/hunk-header rows are excluded from selection
        // entirely so a drag over them can't be mistaken for "selecting" unchanged text.
        private int _dragAnchorIndex = -1;
        private bool _isDragging;

        /// <summary>Walks up from a hit-test result to the containing ListViewItem, or null when the
        /// point isn't over any row at all — e.g. the scrollbar, or empty space below the last row.
        /// Callers must treat "no container" as "not our concern" (let the event pass through
        /// untouched), not the same as "container found but not a selectable line" (which should
        /// block selection) — conflating the two previously caused a click-drag on the scrollbar
        /// thumb to be silently swallowed by the line-selection logic, since a scrollbar hit resolves
        /// to no ListViewItem and was (wrongly) treated as "not selectable, so mark Handled".</summary>
        private static ListViewItem ListViewItemFromPoint(ListView lv, Point p)
        {
            var element = lv.InputHitTest(p) as DependencyObject;
            while (element != null && !(element is ListViewItem))
            {
                // Inline/Run (word-diff highlighted spans inside a TextBlock) aren't part of the
                // visual tree — VisualTreeHelper throws on them, so walk the logical tree instead.
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }
            return element as ListViewItem;
        }

        private static DiffItem DiffItemFromPoint(ListView lv, Point p) =>
            ListViewItemFromPoint(lv, p)?.Content as DiffItem;

        private static bool IsSelectableLine(DiffItem item) =>
            item != null && item.Kind == DiffItemKind.Line && item.Line != null && item.Line.Kind != DiffLineKind.Context;

        /// <summary>True when the click originated on (or inside) a Button — e.g. the hunk header's
        /// Stage/Discard/Unstage buttons. Those must always reach the Button untouched; marking the
        /// event Handled here (to block selection on non-line rows) would silently swallow the click
        /// before Button's own routed-event handling ever sees it.</summary>
        private static bool IsWithinButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void DiffListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var lv = (ListView)sender;
            var position = e.GetPosition(lv);
            if (IsWithinButton(lv.InputHitTest(position) as DependencyObject)) return;
            var container = ListViewItemFromPoint(lv, position);
            if (container == null) return; // scrollbar, empty space, etc. — not a row, leave it alone
            var item = container.Content as DiffItem;
            if (!IsSelectableLine(item))
            {
                // Context/hunk-header rows never become part of the selection.
                e.Handled = true;
                return;
            }
            // Let native click/Ctrl+click/Shift+click selection proceed unmodified; just remember
            // the anchor in case this turns into a plain (no-modifier) drag.
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                _dragAnchorIndex = lv.Items.IndexOf(item);
                _isDragging = true;
            }
        }

        private void DiffListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var lv = (ListView)sender;
            var hovered = DiffItemFromPoint(lv, e.GetPosition(lv));
            if (hovered == null) return;
            int hoveredIndex = lv.Items.IndexOf(hovered);
            if (hoveredIndex < 0 || _dragAnchorIndex < 0) return;

            int lo = Math.Min(_dragAnchorIndex, hoveredIndex);
            int hi = Math.Max(_dragAnchorIndex, hoveredIndex);
            lv.SelectedItems.Clear();
            for (int i = lo; i <= hi; i++)
            {
                var candidate = lv.Items[i] as DiffItem;
                if (IsSelectableLine(candidate)) lv.SelectedItems.Add(candidate);
            }
        }

        private void DiffListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _dragAnchorIndex = -1;
        }

        private void DiffListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = RepoVm;
            if (vm == null) return;
            var lv = (ListView)sender;
            var selectedLines = lv.SelectedItems.Cast<DiffItem>()
                .Where(IsSelectableLine)
                .Select(i => i.Line);
            vm.UpdateDiffLineSelection(selectedLines);
        }

        // ── Side-by-side: two independent ListViews (Left/Right), each showing only its own side of
        // SideBySideItem. Selection and drag-extend mirror the unified-view logic above, just against
        // SideBySideItem and a per-pane "which side" flag; the resulting DiffLine selection is merged
        // from BOTH panes into the same ViewModel-side set unified mode uses (DiffLine identity is
        // shared across both projections of a hunk).

        private static bool IsSelectableSideBySideLine(SideBySideItem item, bool isLeftPane)
        {
            if (item == null || item.Kind != DiffItemKind.Line) return false;
            var line = isLeftPane ? item.Left : item.Right;
            return line != null && line.Kind != DiffLineKind.Context;
        }

        private static SideBySideItem SideBySideItemFromPoint(ListView lv, Point p) =>
            ListViewItemFromPoint(lv, p)?.Content as SideBySideItem;

        private void SideBySideListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var lv = (ListView)sender;
            bool isLeft = ReferenceEquals(lv, SideBySideLeftListView);
            var position = e.GetPosition(lv);
            if (IsWithinButton(lv.InputHitTest(position) as DependencyObject)) return;
            var container = ListViewItemFromPoint(lv, position);
            if (container == null) return; // scrollbar, empty space, etc. — not a row, leave it alone
            var item = container.Content as SideBySideItem;
            if (!IsSelectableSideBySideLine(item, isLeft))
            {
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                _dragAnchorIndex = lv.Items.IndexOf(item);
                _isDragging = true;
            }
        }

        private void SideBySideListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var lv = (ListView)sender;
            bool isLeft = ReferenceEquals(lv, SideBySideLeftListView);
            var hovered = SideBySideItemFromPoint(lv, e.GetPosition(lv));
            if (hovered == null) return;
            int hoveredIndex = lv.Items.IndexOf(hovered);
            if (hoveredIndex < 0 || _dragAnchorIndex < 0) return;

            int lo = Math.Min(_dragAnchorIndex, hoveredIndex);
            int hi = Math.Max(_dragAnchorIndex, hoveredIndex);
            lv.SelectedItems.Clear();
            for (int i = lo; i <= hi; i++)
            {
                var candidate = lv.Items[i] as SideBySideItem;
                if (IsSelectableSideBySideLine(candidate, isLeft)) lv.SelectedItems.Add(candidate);
            }
        }

        private void SideBySideListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _dragAnchorIndex = -1;
        }

        private void SideBySideListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = RepoVm;
            if (vm == null) return;
            var leftLines = SideBySideLeftListView.SelectedItems.Cast<SideBySideItem>()
                .Where(i => IsSelectableSideBySideLine(i, isLeftPane: true))
                .Select(i => i.Left);
            var rightLines = SideBySideRightListView.SelectedItems.Cast<SideBySideItem>()
                .Where(i => IsSelectableSideBySideLine(i, isLeftPane: false))
                .Select(i => i.Right);
            vm.UpdateDiffLineSelection(leftLines.Concat(rightLines));
        }

        // ── Side-by-side scroll sync: both panes move together (vertically, so rows stay aligned,
        // and horizontally: behavior of mirroring either pane's scrollbar onto the
        // other). Wired from each ListView's own Loaded event, deferred to the DispatcherPriority.Loaded
        // queue slot with an explicit ApplyTemplate() call first — verified empirically that neither
        // Loaded firing nor a later dispatcher callback guarantees the ListView's ControlTemplate
        // (where its internal ScrollViewer lives) has actually been applied yet; ApplyTemplate() forces
        // it immediately so the descendant search below reliably finds a real ScrollViewer.
        private ScrollViewer _leftScroll, _rightScroll;
        private bool _syncingScroll;

        private void SideBySideLeftListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_leftScroll != null) return;
                SideBySideLeftListView.ApplyTemplate();
                _leftScroll = FindDescendant<ScrollViewer>(SideBySideLeftListView);
                if (_leftScroll != null) _leftScroll.ScrollChanged += (s, ev) => SyncScroll(_leftScroll, _rightScroll);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SideBySideRightListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_rightScroll != null) return;
                SideBySideRightListView.ApplyTemplate();
                _rightScroll = FindDescendant<ScrollViewer>(SideBySideRightListView);
                if (_rightScroll != null) _rightScroll.ScrollChanged += (s, ev) => SyncScroll(_rightScroll, _leftScroll);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SyncScroll(ScrollViewer source, ScrollViewer target)
        {
            if (_syncingScroll || target == null) return;
            _syncingScroll = true;
            target.ScrollToVerticalOffset(source.VerticalOffset);
            target.ScrollToHorizontalOffset(source.HorizontalOffset);
            _syncingScroll = false;
        }

        // ── Change-map click-to-jump (Controls/DiffChangeMapControl.cs) ─────────────────────────
        private void UnifiedChangeMap_JumpRequested(object sender, double fraction) =>
            JumpListViewToFraction(UnifiedListView, fraction);

        // Scrolling the left pane triggers the right pane too via the existing scroll-sync wiring
        // above (SyncScroll), so only one ListView needs to be driven directly here.
        private void SideBySideChangeMap_JumpRequested(object sender, double fraction) =>
            JumpListViewToFraction(SideBySideLeftListView, fraction);

        private static void JumpListViewToFraction(ListView lv, double fraction)
        {
            if (lv == null || lv.Items.Count == 0) return;
            int idx = (int)(fraction * lv.Items.Count);
            if (idx < 0) idx = 0;
            if (idx >= lv.Items.Count) idx = lv.Items.Count - 1;
            lv.ScrollIntoView(lv.Items[idx]);
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void DiffSearchToggle_Click(object sender, RoutedEventArgs e)
        {
            var vm = RepoVm;
            if (vm == null) return;
            if (vm.IsDiffSearchOpen) { vm.IsDiffSearchOpen = false; return; }
            vm.IsDiffSearchOpen = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DiffSearchBox.Focus();
                DiffSearchBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void DiffSearchClose_Click(object sender, RoutedEventArgs e)
        {
            var vm = RepoVm;
            if (vm != null) vm.IsDiffSearchOpen = false;
        }

        private void DiffSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            var vm = RepoVm;
            if (vm == null) return;
            if (e.Key == Key.Escape)
            {
                vm.IsDiffSearchOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift) vm.PrevDiffMatchCommand.Execute(null);
                else vm.NextDiffMatchCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
