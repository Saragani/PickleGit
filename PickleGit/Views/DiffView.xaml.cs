using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PickleGit.Controls;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class DiffView : UserControl
    {
        // ── Cross-line text selection (Controls/DiffTextSelectionController.cs) — additive, keeps
        // the existing gutter-driven line-selection/staging code below completely untouched. One
        // controller per pane; DiffListView_PreviewMouseLeftButtonDown/Move/Up route a click to
        // either the existing staging logic (gutter) or the matching controller (everything else),
        // never both.
        private readonly DiffTextSelectionController _unifiedTextSelection;
        private readonly DiffTextSelectionController _sideBySideLeftTextSelection;
        private readonly DiffTextSelectionController _sideBySideRightTextSelection;

        public DiffView()
        {
            InitializeComponent();
            _unifiedTextSelection = new DiffTextSelectionController(UnifiedListView, UnifiedTextSelectionOverlay, GetUnifiedRowText);
            _sideBySideLeftTextSelection = new DiffTextSelectionController(SideBySideLeftListView, SideBySideLeftTextSelectionOverlay,
                item => GetSideBySideRowText(item, isLeftPane: true));
            _sideBySideRightTextSelection = new DiffTextSelectionController(SideBySideRightListView, SideBySideRightTextSelectionOverlay,
                item => GetSideBySideRowText(item, isLeftPane: false));
        }

        private static string GetUnifiedRowText(object item)
        {
            var di = item as DiffItem;
            if (di == null) return null;
            return di.Kind == DiffItemKind.HunkHeader ? di.Header : di.Line?.Content;
        }

        private static string GetSideBySideRowText(object item, bool isLeftPane)
        {
            var sbi = item as SideBySideItem;
            if (sbi == null) return null;
            if (sbi.Kind == DiffItemKind.HunkHeader) return sbi.Header;
            return (isLeftPane ? sbi.Left : sbi.Right)?.Content;
        }

        private RepositoryViewModel RepoVm => DataContext as RepositoryViewModel;

        /// <summary>Decides gutter vs. content by comparing the click's X position against the row's
        /// own "RowText" content element's left edge — not by which specific visual element the
        /// hit-test happened to land on. That distinction matters: the gutter columns' line-number
        /// TextBlocks are right-aligned and narrow (e.g. a single-digit line number in a 36px
        /// column), so blank space to their *left* doesn't hit-test to the TextBlock at all — it
        /// falls through to the row's own background Border. A hit-test-identity check would then
        /// wrongly classify that blank gutter-column space as content. Comparing X position against
        /// RowText's actual rendered left edge is robust to that regardless of padding/alignment,
        /// and naturally makes an entire hunk-header row "content" (no gutter — buttons are already
        /// excluded earlier via IsWithinButton) since RowText's left edge sits at the row's own left
        /// edge there.</summary>
        private static bool TryGetRowTextForContentClick(ListView lv, ListViewItem container, Point positionInListView, out TextBlock rowText)
        {
            rowText = DiffTextSelectionController.FindNamedDescendant<TextBlock>(container, "RowText");
            if (rowText == null) return false;
            var rowTextLeft = rowText.TransformToAncestor(lv).Transform(new Point(0, 0)).X;
            return positionInListView.X >= rowTextLeft;
        }

        // ── Scroll-to-top on file switch ──────────────────────────────────────────────────────
        // DiffFileSwitched fires synchronously from SelectedFile's setter, well before the new
        // file's diff has actually loaded (that load is fire-and-forget and awaits a git call).
        // Scrolling right away would just act on the OLD content a moment before it's cleared, so
        // instead this arms a pending flag and defers the actual scroll until FlatDiffItems /
        // SideBySideItems / BlameLines next change to something non-empty — i.e. the new file's
        // content actually arriving, not the interim clear-to-empty every load starts with (both
        // fire a PropertyChanged for the same property, so the empty one must NOT consume the flag
        // or the later real one would be silently missed).
        private bool _pendingDiffScrollReset;

        private void DiffView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is RepositoryViewModel oldVm)
            {
                oldVm.DiffFileSwitched -= OnDiffFileSwitched;
                oldVm.PropertyChanged -= OnRepoVmPropertyChangedForScrollReset;
                oldVm.BlameLines.CollectionChanged -= OnBlameLinesChangedForScrollReset;
            }
            if (e.NewValue is RepositoryViewModel newVm)
            {
                newVm.DiffFileSwitched += OnDiffFileSwitched;
                newVm.PropertyChanged += OnRepoVmPropertyChangedForScrollReset;
                newVm.BlameLines.CollectionChanged += OnBlameLinesChangedForScrollReset;
            }
        }

        private void OnDiffFileSwitched(object sender, EventArgs e) => _pendingDiffScrollReset = true;

        private void OnRepoVmPropertyChangedForScrollReset(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RepositoryViewModel.FlatDiffItems) &&
                e.PropertyName != nameof(RepositoryViewModel.SideBySideItems))
                return;
            // Unlike the scroll-reset flag below (which deliberately skips the pre-load empty-clear
            // and only acts once real content arrives), a stale text selection must be dropped
            // immediately on EVERY change, including that first empty clear — the (row, char) pairs
            // it holds are about to reference a list that's being replaced out from under them.
            _unifiedTextSelection.ClearSelection();
            _sideBySideLeftTextSelection.ClearSelection();
            _sideBySideRightTextSelection.ClearSelection();

            if (!_pendingDiffScrollReset) return;
            var vm = (RepositoryViewModel)sender;
            if (vm.FlatDiffItems.Count == 0 && vm.SideBySideItems.Count == 0) return; // the pre-load clear, not real content yet
            _pendingDiffScrollReset = false;
            ResetDiffScrollToTop();
        }

        private void OnBlameLinesChangedForScrollReset(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!_pendingDiffScrollReset || RepoVm == null || RepoVm.BlameLines.Count == 0) return;
            _pendingDiffScrollReset = false;
            ResetDiffScrollToTop();
        }

        /// <summary>ScrollIntoView(Items[0]) needs no captured ScrollViewer reference (unlike the
        /// side-by-side scroll-sync below), and for the first item it always lands at the very top
        /// since nothing above it could ever be "more in view". Deferred a tick so the ListView has
        /// already re-virtualized against the just-updated ItemsSource.</summary>
        private void ResetDiffScrollToTop()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollListToTop(UnifiedListView);
                ScrollListToTop(SideBySideLeftListView);
                ScrollListToTop(SideBySideRightListView);
                ScrollListToTop(BlameListView);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void ScrollListToTop(ListView lv)
        {
            if (lv != null && lv.Items.Count > 0) lv.ScrollIntoView(lv.Items[0]);
        }

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

            if (TryGetRowTextForContentClick(lv, container, position, out var rowText))
            {
                _unifiedTextSelection.BeginSelection(e, container, rowText);
                return;
            }

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
            if (_unifiedTextSelection.IsSelecting)
            {
                _unifiedTextSelection.UpdateDrag(e);
                return;
            }
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
            if (_unifiedTextSelection.IsSelecting) { _unifiedTextSelection.EndSelection(); return; }
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

        private DiffTextSelectionController SideBySideController(bool isLeft) =>
            isLeft ? _sideBySideLeftTextSelection : _sideBySideRightTextSelection;

        private void SideBySideListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var lv = (ListView)sender;
            bool isLeft = ReferenceEquals(lv, SideBySideLeftListView);
            var position = e.GetPosition(lv);
            if (IsWithinButton(lv.InputHitTest(position) as DependencyObject)) return;
            var container = ListViewItemFromPoint(lv, position);
            if (container == null) return; // scrollbar, empty space, etc. — not a row, leave it alone

            if (TryGetRowTextForContentClick(lv, container, position, out var rowText))
            {
                SideBySideController(isLeft).BeginSelection(e, container, rowText);
                return;
            }

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
            var lv = (ListView)sender;
            bool isLeft = ReferenceEquals(lv, SideBySideLeftListView);
            var controller = SideBySideController(isLeft);
            if (controller.IsSelecting)
            {
                controller.UpdateDrag(e);
                return;
            }
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
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
            var lv = (ListView)sender;
            bool isLeft = ReferenceEquals(lv, SideBySideLeftListView);
            var controller = SideBySideController(isLeft);
            if (controller.IsSelecting) { controller.EndSelection(); return; }
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
        private ScrollViewer _leftScroll, _rightScroll, _unifiedScroll;
        private bool _syncingScroll;

        private void UnifiedListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_unifiedScroll != null) return;
                UnifiedListView.ApplyTemplate();
                _unifiedScroll = FindDescendant<ScrollViewer>(UnifiedListView);
                if (_unifiedScroll != null) _unifiedScroll.ScrollChanged += (s, ev) => _unifiedTextSelection.Recompute();
                UnifiedListView.SizeChanged += (s, ev) => _unifiedTextSelection.Recompute();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SideBySideLeftListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_leftScroll != null) return;
                SideBySideLeftListView.ApplyTemplate();
                _leftScroll = FindDescendant<ScrollViewer>(SideBySideLeftListView);
                if (_leftScroll != null)
                    _leftScroll.ScrollChanged += (s, ev) => { SyncScroll(_leftScroll, _rightScroll); _sideBySideLeftTextSelection.Recompute(); };
                SideBySideLeftListView.SizeChanged += (s, ev) => _sideBySideLeftTextSelection.Recompute();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SideBySideRightListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_rightScroll != null) return;
                SideBySideRightListView.ApplyTemplate();
                _rightScroll = FindDescendant<ScrollViewer>(SideBySideRightListView);
                if (_rightScroll != null)
                {
                    _rightScroll.ScrollChanged += (s, ev) => SyncScroll(_rightScroll, _leftScroll);
                    _rightScroll.ScrollChanged += (s, ev) => _sideBySideRightTextSelection.Recompute();
                }
                SideBySideRightListView.SizeChanged += (s, ev) => _sideBySideRightTextSelection.Recompute();
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

        // ── Cross-line text-selection copy (Ctrl+C / context-menu "Copy") ───────────────────────
        private void DiffTextSelection_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;
            var lv = (ListView)sender;
            var controller = ReferenceEquals(lv, UnifiedListView) ? _unifiedTextSelection
                : ReferenceEquals(lv, SideBySideLeftListView) ? _sideBySideLeftTextSelection
                : _sideBySideRightTextSelection;
            if (controller.TryCopySelection()) e.Handled = true;
        }

        private void UnifiedCopySelection_Click(object sender, RoutedEventArgs e) => _unifiedTextSelection.TryCopySelection();
        private void SideBySideLeftCopySelection_Click(object sender, RoutedEventArgs e) => _sideBySideLeftTextSelection.TryCopySelection();
        private void SideBySideRightCopySelection_Click(object sender, RoutedEventArgs e) => _sideBySideRightTextSelection.TryCopySelection();

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
