using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PickleGit.Models;

namespace PickleGit.Controls
{
    /// <summary>
    /// Additive, self-contained cross-line text-selection for one diff ListView pane (Unified,
    /// or one side of Side-by-side) — click-drag across multiple rows' code text to select a
    /// range and Ctrl+C/copy it, entirely independent of the pane's existing gutter-driven line
    /// selection used for partial-hunk staging (see DiffView.xaml.cs, which routes gutter clicks
    /// to that existing logic and content clicks to this controller — nothing here decides that
    /// routing itself).
    ///
    /// Selection is stored as (row index, character offset) pairs rather than any visual state,
    /// so it survives virtualization: rows outside the realized range simply aren't drawn by
    /// <see cref="Recompute"/> until a later call (post-scroll) finds them realized. Copying reads
    /// the underlying model text directly (via the caller-supplied <c>getRowText</c>), which needs
    /// no realization at all.
    /// </summary>
    public sealed class DiffTextSelectionController
    {
        private readonly ListView _listView;
        private readonly Canvas _overlay;
        private readonly Func<object, string> _getRowText;
        private readonly Func<object, int> _getSelectableStart;
        private readonly Func<object, bool> _isRowSelectable;

        private (int Row, int Ch)? _anchor;
        private (int Row, int Ch)? _focus;
        private string _matchQuery;
        private (int Row, int Start, int End)? _matchExcludeSpan;
        private Point _lastMousePosition;
        private int _autoScrollDirection;
        private ScrollViewer _scrollViewer;
        private double? _pendingOffset;
        private readonly DispatcherTimer _autoScrollTimer;

        private const double AutoScrollMargin = 24;
        private const double AutoScrollStep = 28;

        /// <param name="getSelectableStart">Number of leading characters of a row's text that are
        /// never selectable/copyable — e.g. 1 for a diff line, whose model text carries a leading
        /// '+'/'-'/' ' marker character rendered inline as part of "RowText" (see CLAUDE.md), or 0
        /// for a row with no such marker (a hunk header's literal text).</param>
        /// <param name="isRowSelectable">Whether a row can take part in text selection at all — e.g.
        /// false for a diff hunk header ("@@ -12,3 +12,4 @@ ..."), which is metadata rather than
        /// file content. Null (the default) means every row is selectable, unchanged from before this
        /// existed. An unselectable row is skipped entirely: it can't anchor a click-drag, never gets
        /// a highlight rectangle even when a span crosses over it, and its text is left out of a copy
        /// — the row is invisible to this class as far as content goes, only its index still counts
        /// for locating the rows around it.</param>
        public DiffTextSelectionController(ListView listView, Canvas overlay, Func<object, string> getRowText,
            Func<object, int> getSelectableStart, Func<object, bool> isRowSelectable = null)
        {
            _listView = listView;
            _overlay = overlay;
            _getRowText = getRowText;
            _getSelectableStart = getSelectableStart;
            _isRowSelectable = isRowSelectable;
            // DispatcherPriority.Normal (the default) is HIGHER priority than the Render pass that
            // actually applies a ScrollToVerticalOffset request to this non-virtualizing ListView's
            // panel (IsVirtualizing="False", needed for smooth pixel scrolling elsewhere — see
            // CLAUDE.md). At Normal priority, this timer's ticks can fire back-to-back faster than
            // layout ever gets a turn, so several offset requests pile up before the panel's
            // IScrollInfo bookkeeping catches up — confirmed by instrumenting VerticalOffset
            // directly: it periodically snapped back to 0 mid-drag even with cross-pane sync fully
            // disabled, i.e. a single ListView scrolling itself was enough to reproduce it. Running
            // the timer at Render priority instead means each tick waits for the previous layout
            // pass to actually finish first.
            _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _autoScrollTimer.Tick += OnAutoScrollTick;

            // Drag continuation (move/up) is handled off the overlay's own capture below, not the
            // ListView's — see BeginSelection's doc comment for why capture never targets _listView.
            _overlay.PreviewMouseMove += (s, e) => { if (IsSelecting) UpdateDrag(e); };
            _overlay.PreviewMouseLeftButtonUp += (s, e) => { if (IsSelecting) EndSelection(); };
        }

        public bool IsSelecting { get; private set; }
        public bool HasSelection => _anchor != null && _focus != null;

        private int SelectableStart(int rowIndex) => _getSelectableStart(_listView.Items[rowIndex]);
        private bool IsRowSelectable(int rowIndex) => _isRowSelectable == null || _isRowSelectable(_listView.Items[rowIndex]);

        /// <summary>Ctrl+click range-select (set a start point, hold Ctrl, click an end point) —
        /// keeps the existing anchor and only moves focus, unlike BeginSelection which always
        /// starts a fresh anchor at the click point. Behaves like BeginSelection when there's no
        /// existing anchor yet (first click of the pair).</summary>
        public void ExtendTo(ListViewItem container, TextBlock rowText, Point pointInRowText)
        {
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0) return;
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = Math.Max(PlainCharIndexAt(rowText, pointInRowText, textLength), SelectableStart(rowIndex));
            if (_anchor == null) _anchor = (rowIndex, ch);
            _focus = (rowIndex, ch);
            UpdateOccurrenceHighlight();
        }

        /// <summary>Double-click word-select: expands the click point outward to the run of word
        /// characters (letters, digits, underscore) surrounding it and selects exactly that run —
        /// same anchor/focus mechanics as a plain click, just pre-expanded to word boundaries rather
        /// than collapsed to a single point. A click that lands between words (whitespace/punctuation)
        /// selects that single character instead of leaving a confusing zero-width no-op. Also feeds
        /// <see cref="UpdateOccurrenceHighlight"/> — double-click-to-select-a-word is exactly the
        /// gesture a normal code editor uses to also light up every other occurrence of that word.</summary>
        public void SelectWordAt(ListViewItem container, TextBlock rowText, Point pointInRowText)
        {
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0 || !IsRowSelectable(rowIndex)) return;
            string text = _getRowText(_listView.Items[rowIndex]) ?? string.Empty;
            int textLength = text.Length;
            int selectableStart = SelectableStart(rowIndex);
            int ch = Math.Max(PlainCharIndexAt(rowText, pointInRowText, textLength), selectableStart);

            int start = ch, end = ch;
            while (start > selectableStart && IsWordChar(text[start - 1])) start--;
            while (end < textLength && IsWordChar(text[end])) end++;
            // Clicked on a lone punctuation/space character — select it alone rather than leaving a
            // zero-width no-op. A click resolving to exactly textLength (past the last character,
            // e.g. double-clicking beyond a line's trailing punctuation) can't extend forward, so
            // fall back to extending backward instead of silently producing no selection at all.
            if (start == end)
            {
                if (end < textLength) end++;
                else if (start > selectableStart) start--;
            }

            _anchor = (rowIndex, start);
            _focus = (rowIndex, end);
            _listView.Focus();
            UpdateOccurrenceHighlight();
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Decides which term (if any) gets the "highlight every other occurrence in this
        /// pane" treatment in <see cref="Recompute"/>: the current selection's own text, trimmed of
        /// surrounding whitespace, but only when the selection sits entirely on one row (a multi-row
        /// selection is prose/code being copied, not a single token being inspected) and the trimmed
        /// text is more than one character (skips a bare single-character selection, which would
        /// otherwise light up nearly the whole pane on any common letter). Also records the trimmed
        /// span's own (row, start, end) in <see cref="_matchExcludeSpan"/> — <em>not</em> the raw,
        /// untrimmed selection bounds — so <see cref="DrawOccurrenceHighlights"/> can recognize the
        /// primary selection as one of its own matches and skip redrawing over it; comparing against
        /// the untrimmed bounds instead let a selection with leading/trailing whitespace (e.g.
        /// " foo ") never match its own trimmed "foo" span, double-drawing the wrong brush color on
        /// top of the user's own selection. Called on every selection-changing action, including
        /// every drag tick (not just once a gesture finishes) per the live "highlight while
        /// selecting" behavior — scanning every realized row's text on each tick is the accepted
        /// cost of that, bounded by pane size the same way Recompute's own primary-highlight loop
        /// already is.</summary>
        private void UpdateOccurrenceHighlight()
        {
            _matchQuery = null;
            _matchExcludeSpan = null;
            if (_anchor != null && _focus != null)
            {
                GetOrderedRange(out int lo, out int loCh, out int hi, out int hiCh);
                if (lo == hi && IsRowSelectable(lo))
                {
                    string text = _getRowText(_listView.Items[lo]) ?? string.Empty;
                    string rawSelected = SafeSubstring(text, loCh, hiCh);
                    string trimmed = rawSelected.Trim();
                    if (trimmed.Length > 1)
                    {
                        _matchQuery = trimmed;
                        int leadingTrimmed = rawSelected.Length - rawSelected.TrimStart().Length;
                        int start = loCh + leadingTrimmed;
                        _matchExcludeSpan = (lo, start, start + trimmed.Length);
                    }
                }
            }
            Recompute();
        }

        /// <summary>Ctrl+A — selects the entire pane's text, first row through last.</summary>
        public void SelectAll()
        {
            if (_listView.Items.Count == 0) return;
            int lastIndex = _listView.Items.Count - 1;
            int lastLen = (_getRowText(_listView.Items[lastIndex]) ?? string.Empty).Length;
            _anchor = (0, SelectableStart(0));
            _focus = (lastIndex, lastLen);
            Recompute();
        }

        /// <summary>Arrow-key caret movement, like a normal text editor: Left/Right by character
        /// (wrapping to the adjacent row at a line boundary), Up/Down to the same column in the
        /// neighboring row (clamped to that row's length). Only moves the existing caret/selection
        /// focus — a no-op (returns false) until a click has established one. Shift held extends
        /// the selection from the current anchor instead of collapsing it to the new point, same
        /// as BeginSelection/ExtendTo's own anchor-preserving convention.</summary>
        public bool MoveCaret(Key key, bool extendSelection)
        {
            if (_focus == null || _listView.Items.Count == 0) return false;
            var (row, ch) = _focus.Value;
            // Same stale-index defense as Recompute() — _focus's row can outlive a shrunk Items
            // collection if it changed size before ClearSelection() ran.
            if (row >= _listView.Items.Count) { _anchor = null; _focus = null; return false; }
            string rowText = _getRowText(_listView.Items[row]) ?? string.Empty;

            switch (key)
            {
                case Key.Left:
                    if (ch > SelectableStart(row)) ch--;
                    else if (row > 0) { row--; rowText = _getRowText(_listView.Items[row]) ?? string.Empty; ch = rowText.Length; }
                    else return true;
                    break;
                case Key.Right:
                    if (ch < rowText.Length) ch++;
                    else if (row < _listView.Items.Count - 1) { row++; ch = SelectableStart(row); }
                    else return true;
                    break;
                case Key.Up:
                    if (row == 0) return true;
                    row--;
                    ch = Math.Max(SelectableStart(row), Math.Min(ch, (_getRowText(_listView.Items[row]) ?? string.Empty).Length));
                    break;
                case Key.Down:
                    if (row >= _listView.Items.Count - 1) return true;
                    row++;
                    ch = Math.Max(SelectableStart(row), Math.Min(ch, (_getRowText(_listView.Items[row]) ?? string.Empty).Length));
                    break;
                default:
                    return false;
            }

            _focus = (row, ch);
            if (!extendSelection) _anchor = _focus;
            // caret moved — any occurrence highlight from the prior selection no longer applies
            _matchQuery = null;
            _matchExcludeSpan = null;
            Recompute();
            if (_listView.Items.Count > row) _listView.ScrollIntoView(_listView.Items[row]);
            return true;
        }

        /// <summary>Starts a new selection at the given row/content element. Called by the view's
        /// mouse-down handler once it has already decided this is a content-region click (not the
        /// gutter). Captures the mouse — on the overlay Canvas, deliberately NOT on the ListView
        /// itself — so drag/release keep routing here even if the cursor leaves this ListView's
        /// bounds, e.g. dragging across a side-by-side GridSplitter into the other pane, which as a
        /// side effect keeps the two panes' selections independent for free.
        ///
        /// Capturing on _listView (a ListBox) instead was tried first and is what caused a real,
        /// visible bug: ListBox.OnMouseMove has its own built-in auto-scroll/navigate-to-item logic
        /// that runs whenever Mouse.Captured == this ListBox, regardless of who called CaptureMouse
        /// or why. With no valid "current item" context for it to navigate from (nothing here uses
        /// native ListBox selection), it fell through to jumping the ScrollViewer straight to the
        /// very start of the list (offset 0) mid-drag — confirmed via a live call stack showing
        /// ListBox.OnMouseMove -> ItemsControl.DoAutoScroll -> NavigateByLine -> NavigateByLineInternal
        /// -> NavigateToStartInternal. Capturing on the overlay Canvas instead (a plain Canvas, never
        /// a ListBox) makes that condition impossible to satisfy, unconditionally.</summary>
        public void BeginSelection(MouseButtonEventArgs e, ListViewItem container, TextBlock rowText)
        {
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0 || !IsRowSelectable(rowIndex)) { e.Handled = true; return; }
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = Math.Max(PlainCharIndexAt(rowText, e.GetPosition(rowText), textLength), SelectableStart(rowIndex));
            _anchor = _focus = (rowIndex, ch);
            // A fresh drag starting clears any occurrence highlight left over from the previous
            // selection immediately, rather than leaving it visible (and increasingly stale) until
            // this new gesture finishes — EndSelection recomputes it for the new selection.
            _matchQuery = null;
            _matchExcludeSpan = null;
            IsSelecting = true;
            _listView.Focus();
            // UIElement.CaptureMouse()/Mouse.Capture() silently fails (returns false, capture stays
            // wherever it was — nobody, typically) for an element with IsHitTestVisible=False, which
            // the overlay normally is (see its XAML comment — it must let clicks pass through to the
            // ListView underneath when a drag ISN'T active). Flip it on only for the capture's
            // lifetime; EndSelection flips it back off. Without this, capture never actually landed on
            // the overlay at all, so as soon as the drag cursor left the ListView's own hit-test
            // bounds (e.g. past the pane's top/bottom edge while trying to trigger auto-scroll, or
            // across the GridSplitter into the other pane), no further move events reached UpdateDrag
            // — the selection simply stopped growing past whatever row the cursor last crossed while
            // still inside the pane.
            _overlay.IsHitTestVisible = true;
            Mouse.Capture(_overlay);
            e.Handled = true;
            Recompute();
        }

        public void UpdateDrag(MouseEventArgs e)
        {
            if (!IsSelecting) return;
            _lastMousePosition = e.GetPosition(_listView);
            UpdateFocusFromListViewPoint(_lastMousePosition);
            UpdateAutoScrollState(_lastMousePosition);
        }

        public void EndSelection()
        {
            if (!IsSelecting) return;
            IsSelecting = false;
            _autoScrollDirection = 0;
            _autoScrollTimer.Stop();
            if (Mouse.Captured == _overlay) Mouse.Capture(null);
            _overlay.IsHitTestVisible = false;
            UpdateOccurrenceHighlight();
        }

        /// <summary>Drops the current selection entirely — must be called whenever the underlying
        /// row list is about to change (file switch, stage/unstage reload, diff-option toggle, mode
        /// switch): stale (row, char) pairs referencing a list that's being replaced would otherwise
        /// point at the wrong content, or throw once indices run past the new list's length.</summary>
        public void ClearSelection()
        {
            _anchor = null;
            _focus = null;
            _matchQuery = null;
            _matchExcludeSpan = null;
            _overlay.Children.Clear();
        }

        /// <summary>Redraws the highlight overlay from the current anchor/focus. Safe to call anytime
        /// (scroll, resize, drag update, or externally after any layout change) — a no-op when there's
        /// no active selection. Only realized rows are drawn; unrealized ones are simply skipped and
        /// get their turn on this pane's next ScrollChanged/Recompute once they're realized.</summary>
        public void Recompute()
        {
            _overlay.Children.Clear();
            if (_anchor == null || _focus == null) return;
            GetOrderedRange(out int lo, out int loCh, out int hi, out int hiCh);
            // _anchor/_focus are (row, char) indices captured at some earlier point — the
            // underlying Items collection can be replaced wholesale with a shorter list (file
            // switch, reload) without ClearSelection() reaching this controller first (confirmed via
            // a user-reported IndexOutOfRangeException: a tab-switch layout pass invoked this
            // through a ScrollChanged handler with a now-out-of-range row index). Treat an
            // out-of-range anchor/focus as stale and drop it rather than indexing past the list.
            if (lo < 0 || hi >= _listView.Items.Count)
            {
                _anchor = null;
                _focus = null;
                _matchQuery = null;
                _matchExcludeSpan = null;
                return;
            }
            var brush = _overlay.TryFindResource("TextSelectionBrush") as Brush;
            if (brush == null) return;

            // A "rest of the line" span's right edge (below) is otherwise RowText's own ActualWidth
            // transformed into overlay space — correct for a row that fits within the pane, but for
            // one wide enough to need horizontal scrolling (see the ClipToBounds comment on the
            // Canvas itself in DiffView.xaml/MergeConflictEditorWindow.xaml) that can be far wider
            // than the pane's viewport. ClipToBounds alone only stops it at the overlay's own edge,
            // which spans this ListView's FULL control width — content column AND the vertical
            // scrollbar's own reserved column (see DarkListView's ControlTemplate: content and
            // scrollbar are sibling Grid columns, both inside the same outer Border the overlay
            // Canvas is sized to match) — so without an explicit cap here the highlight visibly
            // painted straight through the scrollbar's track instead of stopping at its left edge.
            var scrollViewer = ResolveScrollViewer();
            double? contentRightEdge = scrollViewer != null && scrollViewer.ViewportWidth > 0
                ? _listView.TransformToVisual(_overlay).Transform(new Point(0, 0)).X + scrollViewer.ViewportWidth
                : (double?)null;

            for (int i = lo; i <= hi; i++)
            {
                if (!IsRowSelectable(i)) continue; // e.g. a hunk header crossed by a spanning selection
                var container = _listView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null || !container.IsArrangeValid) continue;
                var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
                if (textBlock == null) continue;

                double left, right;
                var topLeft = container.TransformToVisual(_overlay).Transform(new Point(0, 0));
                var bottomRight = container.TransformToVisual(_overlay).Transform(new Point(container.ActualWidth, container.ActualHeight));
                // A full-line span's end X must stay within the "RowText" TextBlock's own bounds,
                // not the row container's — the container's left edge sits at the row's true x=0,
                // which for a side-by-side line row is the line-number gutter column
                // (Grid.Column="0", 36px), one column to the left of where the code text actually
                // starts. Using the container's edge here drew the highlight bleeding leftward over
                // the gutter (and, for the hunk-header row, past its own padding) instead of
                // stopping exactly where the visible text starts/ends. The start X is computed via
                // XForCharIndex below instead (skipping the row's own marker character too).
                var textBottomRight = textBlock.TransformToVisual(_overlay).Transform(new Point(textBlock.ActualWidth, textBlock.ActualHeight));

                int rowTextLength = (_getRowText(_listView.Items[i]) ?? string.Empty).Length;
                // A row's own text starts with its unselectable marker character(s) (see the
                // constructor doc on getSelectableStart) — a "whole line" span (every row except
                // the drag's own start/end row) must start after that marker too, not at plain
                // character 0, or the '+'/'-'/' ' glyph itself would sit inside the highlight.
                int selectableStart = SelectableStart(i);

                if (i == lo && i == hi)
                {
                    left = XForCharIndex(textBlock, Math.Max(loCh, selectableStart), rowTextLength);
                    right = XForCharIndex(textBlock, hiCh, rowTextLength);
                }
                else if (i == lo)
                {
                    left = XForCharIndex(textBlock, Math.Max(loCh, selectableStart), rowTextLength);
                    right = contentRightEdge.HasValue ? Math.Min(textBottomRight.X, contentRightEdge.Value) : textBottomRight.X;
                }
                else if (i == hi)
                {
                    left = XForCharIndex(textBlock, selectableStart, rowTextLength);
                    right = XForCharIndex(textBlock, hiCh, rowTextLength);
                }
                else
                {
                    left = XForCharIndex(textBlock, selectableStart, rowTextLength);
                    right = contentRightEdge.HasValue ? Math.Min(textBottomRight.X, contentRightEdge.Value) : textBottomRight.X;
                }

                if (right < left) { var t = left; left = right; right = t; }

                // A zero-length selection (click without drag, or the first click of a Ctrl+click
                // pair) has no highlight to show — draw a thin blinking-caret-style line instead
                // so "where did my click land" is never invisible, matching a normal text editor.
                // (XForCharIndex already returns each boundary's true on-screen position — see its
                // own doc — so the caret needs no further correction beyond that.)
                bool isCaret = lo == hi && loCh == hiCh;
                double caretLeft = left;

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = isCaret ? 1.5 : Math.Max(0, right - left),
                    Height = Math.Max(0, bottomRight.Y - topLeft.Y),
                    Fill = isCaret ? (_overlay.TryFindResource("AccentBrush") as Brush ?? brush) : brush
                };
                Canvas.SetLeft(rect, isCaret ? caretLeft - 0.75 : left);
                Canvas.SetTop(rect, topLeft.Y);
                _overlay.Children.Add(rect);
            }

            DrawOccurrenceHighlights();
        }

        /// <summary>Draws a lighter overlay rectangle over every occurrence of <see cref="_matchQuery"/>
        /// across every currently-realized row — the "highlight other occurrences of the selected
        /// text" behavior. Skips the exact span the primary selection itself already covers (passed in
        /// as <see cref="_matchExcludeSpan"/>, set alongside <see cref="_matchQuery"/> in
        /// <see cref="UpdateOccurrenceHighlight"/>) so that span isn't double-drawn under two
        /// different brushes. Runs over every row in the pane, not just the selection's own range —
        /// unlike the primary-selection loop above, occurrences can appear anywhere.</summary>
        private void DrawOccurrenceHighlights()
        {
            if (string.IsNullOrEmpty(_matchQuery)) return;
            var matchBrush = _overlay.TryFindResource("TextMatchHighlightBrush") as Brush;
            if (matchBrush == null) return;

            for (int i = 0; i < _listView.Items.Count; i++)
            {
                if (!IsRowSelectable(i)) continue;
                var container = _listView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container == null || !container.IsArrangeValid) continue;
                var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
                if (textBlock == null) continue;

                string rowText = _getRowText(_listView.Items[i]) ?? string.Empty;
                int searchFrom = SelectableStart(i);
                int idx;
                while ((idx = rowText.IndexOf(_matchQuery, searchFrom, StringComparison.Ordinal)) >= 0)
                {
                    int matchEnd = idx + _matchQuery.Length;
                    searchFrom = idx + 1;
                    bool isPrimarySpan = _matchExcludeSpan.HasValue && _matchExcludeSpan.Value.Row == i &&
                        _matchExcludeSpan.Value.Start == idx && _matchExcludeSpan.Value.End == matchEnd;
                    if (isPrimarySpan) continue;

                    var topLeft = container.TransformToVisual(_overlay).Transform(new Point(0, 0));
                    var bottomRight = container.TransformToVisual(_overlay).Transform(new Point(container.ActualWidth, container.ActualHeight));
                    double left = XForCharIndex(textBlock, idx, rowText.Length);
                    double right = XForCharIndex(textBlock, matchEnd, rowText.Length);
                    if (right < left) { var t = left; left = right; right = t; }

                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = Math.Max(0, right - left),
                        Height = Math.Max(0, bottomRight.Y - topLeft.Y),
                        Fill = matchBrush
                    };
                    Canvas.SetLeft(rect, left);
                    Canvas.SetTop(rect, topLeft.Y);
                    _overlay.Children.Add(rect);
                }
            }
        }

        /// <summary>Reconstructs the selected text from the model (not the realized visuals — full
        /// text is available regardless of virtualization) and puts it on the clipboard. Returns false
        /// (and leaves the clipboard untouched) when there's nothing selected or the copy fails.</summary>
        public bool TryCopySelection()
        {
            if (_anchor == null || _focus == null) return false;
            GetOrderedRange(out int lo, out int loCh, out int hi, out int hiCh);
            var items = _listView.Items;
            if (lo < 0 || hi >= items.Count) return false;

            var sb = new StringBuilder();
            bool appendedAny = false;
            for (int i = lo; i <= hi; i++)
            {
                if (!IsRowSelectable(i)) continue; // e.g. a hunk header — left out of the copied text entirely
                var text = _getRowText(items[i]) ?? string.Empty;
                int start = SelectableStart(i);
                string line = (lo == hi) ? SafeSubstring(text, Math.Max(loCh, start), hiCh)
                    : i == lo ? SafeSubstring(text, Math.Max(loCh, start), text.Length)
                    : i == hi ? SafeSubstring(text, start, hiCh)
                    : SafeSubstring(text, start, text.Length);
                if (appendedAny) sb.Append(Environment.NewLine);
                sb.Append(line);
                appendedAny = true;
            }
            if (sb.Length == 0) return false;
            try { Clipboard.SetText(sb.ToString()); return true; }
            catch { return false; }
        }

        // ── Hit-testing / geometry helpers ──────────────────────────────────────────────────────

        private void UpdateFocusFromListViewPoint(Point pointInListView)
        {
            // InputHitTest returns nothing for a point outside _listView's own rectangle — and a
            // real drag gesture aimed at the top/bottom auto-scroll margin very easily overshoots
            // past the ListView's actual edge (the margin is a zone just inside the edge, not a
            // hard stop). Clamping into the ListView's own bounds before hit-testing means the query
            // always lands on the nearest real row (the first/last one) instead of hitting nothing
            // — without this, the selection simply stopped growing the instant the cursor drifted
            // outside the pane during auto-scroll, even though the timer kept scrolling content.
            var clamped = new Point(
                Math.Max(0, Math.Min(pointInListView.X, _listView.ActualWidth - 1)),
                Math.Max(0, Math.Min(pointInListView.Y, _listView.ActualHeight - 1)));
            var hit = _listView.InputHitTest(clamped) as DependencyObject;
            var container = FindAncestor<ListViewItem>(hit);
            if (container == null) return; // over the scrollbar, or past the last row — leave focus as-is
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0) return;
            var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
            if (textBlock == null) return;
            var pointInText = _listView.TranslatePoint(clamped, textBlock);
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = Math.Max(PlainCharIndexAt(textBlock, pointInText, textLength), SelectableStart(rowIndex));
            _focus = (rowIndex, ch);
            // Live occurrence highlighting: recompute on every drag tick (not just at mouse-up) so
            // matches light up as the selection grows, the same way the primary highlight itself
            // already updates live. UpdateOccurrenceHighlight calls Recompute() itself.
            UpdateOccurrenceHighlight();
        }

        private void UpdateAutoScrollState(Point pointInListView)
        {
            int direction;
            if (pointInListView.Y < AutoScrollMargin) direction = -1;
            else if (pointInListView.Y > _listView.ActualHeight - AutoScrollMargin) direction = 1;
            else direction = 0;

            _autoScrollDirection = direction;
            if (direction != 0)
            {
                ResolveScrollViewer();
                if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
            }
            else
            {
                _pendingOffset = null;
                _autoScrollTimer.Stop();
            }
        }

        /// <summary>Finds and caches this pane's ScrollViewer (template part of the ListView) —
        /// needed both for auto-scroll-while-dragging and, in <see cref="Recompute"/>, to cap a
        /// "rest of the line" highlight at the actual visible content width. ApplyTemplate() forces
        /// the template to exist first; without it the descendant search can come up empty (see
        /// CLAUDE.md's "ApplyTemplate() may still be needed after Loaded").</summary>
        private ScrollViewer ResolveScrollViewer()
        {
            if (_scrollViewer == null)
            {
                _listView.ApplyTemplate();
                _scrollViewer = FindNamedDescendant<ScrollViewer>(_listView, null);
            }
            return _scrollViewer;
        }

        // ScrollViewer.ScrollToVerticalOffset only REQUESTS a scroll — the actual Measure/Arrange
        // pass that applies it happens on a later, independent dispatcher pass. On this ListView
        // (IsVirtualizing="False", every row always realized, needed for smooth pixel scrolling —
        // see CLAUDE.md), issuing repeated requests during auto-scroll-while-dragging without ever
        // waiting for that pass to land let them race: instrumentation confirmed VerticalOffset
        // would intermittently snap back to 0 between ticks even though ExtentHeight/ViewportHeight
        // stayed perfectly constant throughout (ruling out a measurement/extent glitch) — something
        // in the deferred layout pass was corrupting the ScrollViewer's own bookkeeping. Forcing
        // that pass to complete synchronously via UpdateLayout() right after each request — and
        // re-issuing if it didn't land where asked — eliminates the race entirely: verified across
        // repeated 3-second holds with zero deviation from the intended offset on any tick.
        private void OnAutoScrollTick(object sender, EventArgs e)
        {
            if (_autoScrollDirection == 0 || _scrollViewer == null) { _autoScrollTimer.Stop(); return; }
            if (_pendingOffset == null) _pendingOffset = _scrollViewer.VerticalOffset;
            _pendingOffset = Math.Max(0, _pendingOffset.Value + _autoScrollDirection * AutoScrollStep);
            double lastActual = double.NaN;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _scrollViewer.ScrollToVerticalOffset(_pendingOffset.Value);
                _listView.UpdateLayout();
                var actual = _scrollViewer.VerticalOffset;
                if (Math.Abs(actual - _pendingOffset.Value) < 0.5) break; // matched what we asked for
                if (Math.Abs(actual - lastActual) < 0.5) break; // stable but short of it — a real clamp (end of content), not the transient race
                lastActual = actual;
            }
            // The cursor is stationary on screen but content just scrolled underneath it — re-running
            // the same hit-test at the same screen point naturally discovers whichever row/character
            // has scrolled into that position, with no manual coordinate translation needed.
            UpdateFocusFromListViewPoint(_lastMousePosition);
        }

        private void GetOrderedRange(out int lo, out int loCh, out int hi, out int hiCh)
        {
            var a = _anchor.Value;
            var f = _focus.Value;
            if (a.Row < f.Row) { lo = a.Row; loCh = a.Ch; hi = f.Row; hiCh = f.Ch; }
            else if (f.Row < a.Row) { lo = f.Row; loCh = f.Ch; hi = a.Row; hiCh = a.Ch; }
            else { lo = hi = a.Row; loCh = Math.Min(a.Ch, f.Ch); hiCh = Math.Max(a.Ch, f.Ch); }
        }

        /// <summary>Converts a point (in <paramref name="textBlock"/>'s own coordinate space) to a
        /// plain-text character offset. Deliberately does NOT use <c>TextBlock.Text</c> — verified
        /// empirically (per this project's own "don't guess, verify" lesson in CLAUDE.md) that it
        /// reads back empty for a TextBlock whose content was built via direct <c>Inlines</c>
        /// manipulation, which is exactly how <see cref="Behaviors.WordDiffHighlighter"/> (syntax +
        /// word-diff coloring) populates every diff row here. <see cref="TextRange"/> between two
        /// <see cref="TextPointer"/>s of the SAME TextBlock, by contrast, does correctly reconstruct
        /// the plain text regardless of how many colored <c>Run</c> spans it's split across, so all
        /// offset arithmetic here is anchored to that instead. <paramref name="textLength"/> is the
        /// row's plain text length from the underlying model (the authoritative source, sidestepping
        /// the Text-property gap entirely) — used only to clamp the result.</summary>
        private static int PlainCharIndexAt(TextBlock textBlock, Point pointInTextBlock, int textLength)
        {
            if (textLength == 0) return 0;
            var pointer = textBlock.GetPositionFromPoint(pointInTextBlock, true);
            if (pointer == null) return textLength;
            var count = new TextRange(textBlock.ContentStart, pointer).Text.Length;
            return Math.Max(0, Math.Min(count, textLength));
        }

        /// <summary>Binary-searches, via <see cref="PlainCharIndexAt"/>, the local (textBlock-space) X
        /// where its nearest-boundary snap flips from ch-1 to ch. For ch &gt; 0 that's the MIDPOINT of
        /// character ch-1's glyph, not its true right edge — half a character width to the left of the
        /// real boundary (unambiguous only at ch == 0, the text's own start, which has no preceding
        /// glyph to snap against — short-circuited below for exactly that reason, not just as a micro-
        /// optimization). Only called for ch &gt; 1 now (see <see cref="XForCharIndex"/>): the common
        /// ch &lt;= 1 case — every non-edge row of a multi-row selection's own boundary — is resolved by
        /// arithmetic off <see cref="EstimateCharWidth"/> instead, since running this ~20-iteration
        /// search (each iteration walking a TextRange to reconstruct plain text) once per row on every
        /// scroll tick measurably lagged scrolling for a large selection.</summary>
        private static double RawBoundaryX(TextBlock textBlock, int ch, int textLength)
        {
            ch = Math.Max(0, Math.Min(ch, textLength));
            if (ch == 0) return 0;
            double lo = 0, hi = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : 20000;
            double midY = Math.Max(textBlock.ActualHeight / 2, 1);
            for (int iter = 0; iter < 30 && hi - lo > 0.25; iter++)
            {
                double mid = (lo + hi) / 2;
                int idxAtMid = PlainCharIndexAt(textBlock, new Point(mid, midY), textLength);
                if (idxAtMid < ch) lo = mid; else hi = mid;
            }
            return hi;
        }

        /// <summary>Finds the true on-screen X coordinate (in overlay space) of the boundary just
        /// before character <paramref name="ch"/>. For ch &lt;= 1 (character 0's own start, or the
        /// boundary right after it) this is pure arithmetic — <paramref name="ch"/> * the row's
        /// per-character width, no <see cref="RawBoundaryX"/> search at all — since this is THE hot
        /// path: every non-edge row of a multi-row selection's "whole line" span starts at that row's
        /// SelectableStart (0 or 1), once per such row on every scroll tick. For ch &gt; 1 (the rare
        /// case of the drag's own start/end row, wherever the user actually clicked) this falls back to
        /// <see cref="RawBoundaryX"/> corrected back by half a character's width, since that search's
        /// nearest-boundary snap is biased left by exactly that much for any ch &gt; 0 (see its own doc).
        /// This correction being missing was a real, visible bug, not just an internal detail some
        /// earlier comment claimed "cancels out": a SPAN's width (right - left) is unaffected since both
        /// ends share the identical bias, but the span's absolute position was still shifted left by half
        /// a character as a whole — a selection starting right after a skipped marker character rendered
        /// back into that marker, and a selection's right edge stopped half a character before the
        /// actually-included character. There is no direct "offset → Rect" API usable here in the first
        /// place — <c>TextPointer.GetPositionAtOffset</c> counts "symbols" (which include extra positions
        /// at each Run boundary WordDiffHighlighter introduces), not plain characters, so it doesn't
        /// reliably line up with the same <paramref name="ch"/> this class uses everywhere else.</summary>
        private double XForCharIndex(TextBlock textBlock, int ch, int textLength)
        {
            if (textLength == 0) return textBlock.TransformToVisual(_overlay).Transform(new Point(0, 0)).X;
            ch = Math.Max(0, Math.Min(ch, textLength));
            double charWidth = EstimateCharWidth(textBlock);
            double x = ch <= 1 ? ch * charWidth : RawBoundaryX(textBlock, ch, textLength) + charWidth / 2;
            return textBlock.TransformToVisual(_overlay).Transform(new Point(x, 0)).X;
        }

        /// <summary>One character's on-screen width for this row, via direct <see cref="FormattedText"/>
        /// measurement (same pattern as <see cref="CommitGraphControl"/>) rather than
        /// <see cref="RawBoundaryX"/>'s hit-test search — O(1) instead of a ~20-iteration search each
        /// walking a TextRange to reconstruct plain text, which measurably lagged scrolling once a
        /// multi-row selection existed (every row's boundary needs this). Monospace font throughout
        /// this app's diff/conflict text, so any single character is representative of the row.</summary>
        internal static double EstimateCharWidth(TextBlock textBlock)
        {
            var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(textBlock).PixelsPerDip;
            var ft = new FormattedText("0", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, textBlock.FontSize, Brushes.Black, dpi);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>Scrolls <paramref name="listView"/> horizontally so the current Find match — an
        /// exact (Start, Length) character range within the matched row's own "RowText" element,
        /// per <see cref="PickleGit.ViewModels.PaneFindState.CurrentMatchRange"/> — is actually
        /// visible. The vertical <c>ScrollIntoView</c> every ScrollToMatchRequested handler already
        /// calls says nothing about the horizontal offset, so a match far to the right of a long
        /// line stays off-screen with no further action. Deferred to
        /// <see cref="DispatcherPriority.Loaded"/> since the target row's container may not exist
        /// yet immediately after ScrollIntoView returns for a recycling-virtualized ListView — but
        /// that deferral alone isn't enough for a pane whose ancestor started
        /// <c>Visibility="Collapsed"</c> (e.g. Side-by-side before its first toggle): neither
        /// <c>Loaded</c> firing nor a same-priority dispatcher callback reliably guarantees the
        /// ListView's own ControlTemplate (where its internal ScrollViewer lives) or its items'
        /// containers have actually been realized yet (see CLAUDE.md's "ApplyTemplate() may still
        /// be needed after Loaded" — the exact same gap this file's own DiffView.xaml.cs callers
        /// already work around for their ScrollViewer lookups). ApplyTemplate() + UpdateLayout()
        /// below force both before searching, the same fix already applied there.</summary>
        internal static void ScrollToHighlightRangeHorizontally(ListView listView, object item, DiffHighlightSpan? range)
        {
            if (range == null || item == null) return;
            listView.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                listView.ApplyTemplate();
                listView.UpdateLayout();
                var container = listView.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
                if (container == null) return;
                var rowText = FindNamedDescendant<TextBlock>(container, "RowText");
                var scrollViewer = FindNamedDescendant<ScrollViewer>(listView, null);
                if (rowText == null || scrollViewer == null || scrollViewer.ScrollableWidth <= 0) return;

                var charWidth = EstimateCharWidth(rowText);
                var rowTextLeftContent = rowText.TransformToAncestor(listView).Transform(new Point(0, 0)).X
                    + scrollViewer.HorizontalOffset;
                var matchStart = rowTextLeftContent + range.Value.Start * charWidth;
                var matchEnd = rowTextLeftContent + (range.Value.Start + range.Value.Length) * charWidth;
                var viewLeft = scrollViewer.HorizontalOffset;
                var viewRight = viewLeft + scrollViewer.ViewportWidth;
                const double margin = 24;

                if (matchStart < viewLeft)
                    scrollViewer.ScrollToHorizontalOffset(Math.Max(0, matchStart - margin));
                else if (matchEnd > viewRight)
                    scrollViewer.ScrollToHorizontalOffset(matchEnd - scrollViewer.ViewportWidth + margin);
            }));
        }

        private static string SafeSubstring(string text, int start, int end)
        {
            start = Math.Max(0, Math.Min(start, text.Length));
            end = Math.Max(0, Math.Min(end, text.Length));
            if (end < start) { var t = start; start = end; end = t; }
            return text.Substring(start, end - start);
        }

        internal static T FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            if (root is T match && (name == null || match.Name == name)) return match;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindNamedDescendant<T>(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
            {
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return d as T;
        }
    }
}
