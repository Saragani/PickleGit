using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

        private (int Row, int Ch)? _anchor;
        private (int Row, int Ch)? _focus;
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
        public DiffTextSelectionController(ListView listView, Canvas overlay, Func<object, string> getRowText,
            Func<object, int> getSelectableStart)
        {
            _listView = listView;
            _overlay = overlay;
            _getRowText = getRowText;
            _getSelectableStart = getSelectableStart;
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
            if (rowIndex < 0) { e.Handled = true; return; }
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = Math.Max(PlainCharIndexAt(rowText, e.GetPosition(rowText), textLength), SelectableStart(rowIndex));
            _anchor = _focus = (rowIndex, ch);
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
        }

        /// <summary>Drops the current selection entirely — must be called whenever the underlying
        /// row list is about to change (file switch, stage/unstage reload, diff-option toggle, mode
        /// switch): stale (row, char) pairs referencing a list that's being replaced would otherwise
        /// point at the wrong content, or throw once indices run past the new list's length.</summary>
        public void ClearSelection()
        {
            _anchor = null;
            _focus = null;
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
            var brush = _overlay.TryFindResource("TextSelectionBrush") as Brush;
            if (brush == null) return;

            for (int i = lo; i <= hi; i++)
            {
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
                    right = textBottomRight.X;
                }
                else if (i == hi)
                {
                    left = XForCharIndex(textBlock, selectableStart, rowTextLength);
                    right = XForCharIndex(textBlock, hiCh, rowTextLength);
                }
                else
                {
                    left = XForCharIndex(textBlock, selectableStart, rowTextLength);
                    right = textBottomRight.X;
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
            for (int i = lo; i <= hi; i++)
            {
                var text = _getRowText(items[i]) ?? string.Empty;
                int start = SelectableStart(i);
                string line = (lo == hi) ? SafeSubstring(text, Math.Max(loCh, start), hiCh)
                    : i == lo ? SafeSubstring(text, Math.Max(loCh, start), text.Length)
                    : i == hi ? SafeSubstring(text, start, hiCh)
                    : SafeSubstring(text, start, text.Length);
                if (i > lo) sb.Append(Environment.NewLine);
                sb.Append(line);
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
            Recompute();
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
                if (_scrollViewer == null)
                {
                    _listView.ApplyTemplate();
                    _scrollViewer = FindNamedDescendant<ScrollViewer>(_listView, null);
                }
                if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
            }
            else
            {
                _pendingOffset = null;
                _autoScrollTimer.Stop();
            }
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
        /// glyph to snap against). Kept separate from <see cref="XForCharIndex"/> so
        /// <see cref="EstimateCharWidth"/> can measure a raw glyph width without going through the
        /// corrected, mutually-recursive path.
        ///
        /// ch == 0 is short-circuited to return 0 directly, skipping the search — not just a micro-
        /// optimization: this is THE hot path for every non-edge row of a multi-row selection (every
        /// row's left boundary is its SelectableStart, almost always 0), and Recompute() calls this once
        /// per such row on every scroll tick. Running a ~20-iteration search (each iteration walking a
        /// TextRange to reconstruct plain text) per row per tick was measured to make a large selection
        /// visibly lag scrolling and starve the overlay enough that freshly-scrolled-in rows appeared
        /// unhighlighted until a later tick caught up. ch == 0's answer is already known with certainty
        /// (see above), so there is nothing to search for.</summary>
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
        /// before character <paramref name="ch"/> — i.e. <see cref="RawBoundaryX"/> corrected back by
        /// half a character's width for any ch &gt; 0 (see its doc for why that boundary is otherwise
        /// biased left). This being uncorrected was a real, visible bug, not just an internal
        /// implementation detail some earlier comment claimed "cancels out": a SPAN's width (right -
        /// left) is unaffected since both ends share the identical bias, but the span's absolute
        /// position was still shifted left by half a character as a whole — a selection starting right
        /// after a skipped marker character rendered back into that marker, and a selection's right
        /// edge stopped half a character before the actually-included character. There is no direct
        /// "offset → Rect" API usable here in the first place — <c>TextPointer.GetPositionAtOffset</c>
        /// counts "symbols" (which include extra positions at each Run boundary WordDiffHighlighter
        /// introduces), not plain characters, so it doesn't reliably line up with the same
        /// <paramref name="ch"/> this class uses everywhere else.</summary>
        private double XForCharIndex(TextBlock textBlock, int ch, int textLength)
        {
            if (textLength == 0) return textBlock.TransformToVisual(_overlay).Transform(new Point(0, 0)).X;
            ch = Math.Max(0, Math.Min(ch, textLength));
            double x = RawBoundaryX(textBlock, ch, textLength);
            if (ch > 0) x += EstimateCharWidth(textBlock, textLength) / 2;
            return textBlock.TransformToVisual(_overlay).Transform(new Point(x, 0)).X;
        }

        /// <summary>One character's on-screen width for this row, measured directly off
        /// <see cref="RawBoundaryX"/> (not the corrected <see cref="XForCharIndex"/>, which depends on
        /// this method — going through it here would be mutually recursive) — the bias
        /// <see cref="RawBoundaryX"/> documents cancels out in this subtraction regardless, since both
        /// endpoints carry the identical bias. Monospace font throughout this app's diff/conflict text,
        /// so measuring between characters 0 and 1 is representative of any character in the row.</summary>
        private double EstimateCharWidth(TextBlock textBlock, int textLength)
        {
            if (textLength <= 0) return 0;
            return Math.Max(0, RawBoundaryX(textBlock, Math.Min(1, textLength), textLength) - RawBoundaryX(textBlock, 0, textLength));
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
