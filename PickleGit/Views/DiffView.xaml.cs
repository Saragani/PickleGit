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
            _unifiedTextSelection = new DiffTextSelectionController(UnifiedListView, UnifiedTextSelectionOverlay,
                GetUnifiedRowText, GetUnifiedSelectableStart, IsUnifiedRowSelectable);
            _sideBySideLeftTextSelection = new DiffTextSelectionController(SideBySideLeftListView, SideBySideLeftTextSelectionOverlay,
                item => GetSideBySideRowText(item, isLeftPane: true), GetSideBySideSelectableStart, IsSideBySideRowSelectable);
            _sideBySideRightTextSelection = new DiffTextSelectionController(SideBySideRightListView, SideBySideRightTextSelectionOverlay,
                item => GetSideBySideRowText(item, isLeftPane: false), GetSideBySideSelectableStart, IsSideBySideRowSelectable);

            // Selector's own class handler for KeyDown marks Ctrl+A Handled before a plain XAML-attached
            // instance handler (KeyDown="...") ever gets a turn — WPF skips ordinary handlers once
            // Handled is set, even later handlers on the SAME element. Registering with
            // handledEventsToo=true is the only way to still run our own logic afterward; the XAML
            // attribute for Ctrl+C alone was fine since nothing upstream claims that key, but Ctrl+A
            // needs this explicit registration to ever be reached at all.
            var keyHandler = new KeyEventHandler(DiffTextSelection_KeyDown);
            UnifiedListView.AddHandler(KeyDownEvent, keyHandler, true);
            SideBySideLeftListView.AddHandler(KeyDownEvent, keyHandler, true);
            SideBySideRightListView.AddHandler(KeyDownEvent, keyHandler, true);
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

        // A line row's model text (DiffLine.Content) carries a leading '+'/'-'/' ' diff-marker
        // character that WordDiffHighlighter renders inline as the first glyph of "RowText" (see
        // CLAUDE.md) — visually distinct from the line-number gutter, but still not something a
        // user selecting/copying code actually wants. A hunk header's Header text has no such
        // marker, so it alone is fully selectable from character 0.
        private static int GetUnifiedSelectableStart(object item) =>
            (item as DiffItem)?.Kind == DiffItemKind.HunkHeader ? 0 : 1;

        private static int GetSideBySideSelectableStart(object item) =>
            (item as SideBySideItem)?.Kind == DiffItemKind.HunkHeader ? 0 : 1;

        // Hunk headers ("@@ -12,3 +12,4 @@ ...") are metadata about the diff, not file content — a
        // user selecting/copying code doesn't want them anchoring a drag or showing up mid-paste.
        private static bool IsUnifiedRowSelectable(object item) => (item as DiffItem)?.Kind != DiffItemKind.HunkHeader;
        private static bool IsSideBySideRowSelectable(object item) => (item as SideBySideItem)?.Kind != DiffItemKind.HunkHeader;

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

            // Captured synchronously, before this event's ItemsSource swap has had a chance to
            // reach a layout pass — WPF only resets a ScrollViewer's offset to 0 during Arrange,
            // which happens later (Dispatcher render), so reading VerticalOffset here still reflects
            // where the user was scrolled to right before FlatDiffItems/SideBySideItems just got
            // reassigned to a new collection instance (every reload replaces the list wholesale,
            // even a same-file refresh with near-identical content).
            var unifiedOffset = _unifiedScroll?.VerticalOffset ?? 0;
            var sideBySideOffset = _leftScroll?.VerticalOffset ?? 0;

            // Unlike the scroll-reset flag below (which deliberately skips the pre-load empty-clear
            // and only acts once real content arrives), a stale text selection must be dropped
            // immediately on EVERY change, including that first empty clear — the (row, char) pairs
            // it holds are about to reference a list that's being replaced out from under them.
            _unifiedTextSelection.ClearSelection();
            _sideBySideLeftTextSelection.ClearSelection();
            _sideBySideRightTextSelection.ClearSelection();

            var vm = (RepositoryViewModel)sender;
            // FlatDiffItems and SideBySideItems are two separate properties that this same load
            // always reassigns back-to-back (never atomically) — so this handler runs TWICE for one
            // real content arrival: once right after FlatDiffItems is set (SideBySideItems still the
            // stale empty/previous-file array from the load's own initial clear) and once after
            // SideBySideItems catches up. Requiring BOTH non-empty (not just "not both empty") skips
            // that first, transitional call instead of treating it as "content has arrived" and
            // consuming _pendingDiffScrollReset a step early — which left the *second* call falling
            // through to the offset-restore branch and clobbering the just-reset scroll position
            // back to the previous file's offset.
            if (vm.FlatDiffItems.Count == 0 || vm.SideBySideItems.Count == 0) return; // not real content yet

            if (_pendingDiffScrollReset)
            {
                _pendingDiffScrollReset = false;
                ResetDiffScrollToTop();
            }
            else
            {
                // Not a file switch — e.g. staging/unstaging/discarding a hunk while this same
                // file's diff is open re-fetches and reassigns the whole list. Restore the offset
                // the user actually had instead of leaving it at whatever WPF's own ItemsSource-swap
                // layout pass lands on (top).
                RestoreDiffScrollOffset(unifiedOffset, sideBySideOffset);
            }
        }

        private void RestoreDiffScrollOffset(double unifiedOffset, double sideBySideOffset)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _unifiedScroll?.ScrollToVerticalOffset(unifiedOffset);
                _leftScroll?.ScrollToVerticalOffset(sideBySideOffset);
                _rightScroll?.ScrollToVerticalOffset(sideBySideOffset);
            }), System.Windows.Threading.DispatcherPriority.Background);
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
                if (e.ClickCount == 2)
                {
                    _unifiedTextSelection.SelectWordAt(container, rowText, e.GetPosition(rowText));
                    e.Handled = true;
                    return;
                }
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
                // TryGetRowTextForContentClick only checks that the click landed on/right of the
                // row's "RowText" element — a hunk-header row has one too (bound to Header), even
                // though BeginSelection is about to refuse it via its own IsRowSelectable check
                // (hunk headers are excluded, see IsSideBySideRowSelectable below). Clearing the
                // OTHER pane's selection unconditionally, before that refusal happens, meant a
                // stray click on a hunk header — which does nothing at all in the pane it lands in —
                // still destroyed a valid, unrelated selection the user was holding on the other
                // side. Checking the same selectability here first keeps a no-op click a no-op
                // everywhere, not just in the pane it was clicked in.
                if (!IsSideBySideRowSelectable(container.Content as SideBySideItem)) { e.Handled = true; return; }

                // The two panes are independent DiffTextSelectionController instances, so nothing
                // stops both from holding a selection at once by default — but a single logical
                // selection that's either on the left or the right (never both) is what a normal
                // text editor's behavior would lead you to expect. Starting a new one on this side
                // drops whatever was selected on the other.
                SideBySideController(!isLeft).ClearSelection();
                if (e.ClickCount == 2)
                {
                    SideBySideController(isLeft).SelectWordAt(container, rowText, e.GetPosition(rowText));
                    e.Handled = true;
                    return;
                }
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
        private DiffTextSelectionController ResolveTextSelectionController(ListView lv) =>
            ReferenceEquals(lv, UnifiedListView) ? _unifiedTextSelection
                : ReferenceEquals(lv, SideBySideLeftListView) ? _sideBySideLeftTextSelection
                : _sideBySideRightTextSelection;

        /// <summary>The pane's own ScrollViewer, keyed the same way as
        /// <see cref="ResolveTextSelectionController"/> — used so Page Up/Down scrolls exactly the
        /// pane that has focus. Scrolling it (rather than e.g. scrolling all three at once) is
        /// enough to keep every pane in sync: each ScrollViewer's own ScrollChanged handler (wired in
        /// the *_Loaded methods below) already calls SyncScroll to move its sibling(s) to match,
        /// which is the exact same mechanism a mouse-driven scrollbar drag already goes through.</summary>
        private ScrollViewer ResolveScrollViewerFor(ListView lv) =>
            ReferenceEquals(lv, UnifiedListView) ? _unifiedScroll
                : ReferenceEquals(lv, SideBySideLeftListView) ? _leftScroll
                : _rightScroll;

        private void DiffTextSelection_KeyDown(object sender, KeyEventArgs e)
        {
            var lv = (ListView)sender;
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ResolveTextSelectionController(lv).TryCopySelection()) e.Handled = true;
            }
            else if ((e.Key == Key.PageUp || e.Key == Key.PageDown) && Keyboard.Modifiers == ModifierKeys.None)
            {
                var scrollViewer = ResolveScrollViewerFor(lv);
                if (scrollViewer != null)
                {
                    if (e.Key == Key.PageUp) scrollViewer.PageUp(); else scrollViewer.PageDown();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Selector.OnKeyDown (a plain virtual-method override, not routed through
                // ApplicationCommands.SelectAll — an earlier attempt to override via an instance
                // CommandBinding for that command was a no-op, confirming this) has already run its
                // own native "select all rows via SelectedItems" by the time this instance handler
                // gets a turn — class handlers for a bubble event always run before instance handlers
                // on the same element, and marking Handled here wouldn't have stopped it from running
                // in the first place either way. Rather than fight that ordering, undo its visible
                // result (SelectedItems back to empty, dropping the gutter/stage-selection highlight
                // it drives) and apply our own text SelectAll() instead — synchronous, so there's no
                // visible flicker between the two.
                lv.SelectedItems.Clear();
                // Same "only one pane holds a selection at a time" rule the mouse-down path already
                // enforces (see SideBySideListView_PreviewMouseLeftButtonDown) — without this,
                // selecting text in one pane and then pressing Ctrl+A in the other left BOTH panes
                // holding an active, independently highlighted/copyable selection at once.
                if (ReferenceEquals(lv, SideBySideLeftListView)) _sideBySideRightTextSelection.ClearSelection();
                else if (ReferenceEquals(lv, SideBySideRightListView)) _sideBySideLeftTextSelection.ClearSelection();
                ResolveTextSelectionController(lv).SelectAll();
                e.Handled = true;
            }
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
                // DiffSearchBox's Text binding has Delay=150 — force the pending value to commit
                // before navigating, or pressing Enter right after typing (within 150ms) navigates
                // against the previous DiffSearchText instead of what's actually in the box.
                DiffSearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (Keyboard.Modifiers == ModifierKeys.Shift) vm.PrevDiffMatchCommand.Execute(null);
                else vm.NextDiffMatchCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
