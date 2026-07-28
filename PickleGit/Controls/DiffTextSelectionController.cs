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

        private (int Row, int Ch)? _anchor;
        private (int Row, int Ch)? _focus;
        private Point _lastMousePosition;
        private int _autoScrollDirection;
        private ScrollViewer _scrollViewer;
        private readonly DispatcherTimer _autoScrollTimer;

        private const double AutoScrollMargin = 24;
        private const double AutoScrollStep = 28;

        public DiffTextSelectionController(ListView listView, Canvas overlay, Func<object, string> getRowText)
        {
            _listView = listView;
            _overlay = overlay;
            _getRowText = getRowText;
            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _autoScrollTimer.Tick += OnAutoScrollTick;
        }

        public bool IsSelecting { get; private set; }
        public bool HasSelection => _anchor != null && _focus != null;

        /// <summary>Starts a new selection at the given row/content element. Called by the view's
        /// mouse-down handler once it has already decided this is a content-region click (not the
        /// gutter). Captures the mouse so drag/release keep routing here even if the cursor leaves
        /// this ListView's bounds — e.g. dragging across a side-by-side GridSplitter into the other
        /// pane, which as a side effect keeps the two panes' selections independent for free.</summary>
        public void BeginSelection(MouseButtonEventArgs e, ListViewItem container, TextBlock rowText)
        {
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0) { e.Handled = true; return; }
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = PlainCharIndexAt(rowText, e.GetPosition(rowText), textLength);
            _anchor = _focus = (rowIndex, ch);
            IsSelecting = true;
            _listView.Focus();
            _listView.CaptureMouse();
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
            if (_listView.IsMouseCaptured) _listView.ReleaseMouseCapture();
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

                double left, right;
                var topLeft = container.TransformToVisual(_overlay).Transform(new Point(0, 0));
                var bottomRight = container.TransformToVisual(_overlay).Transform(new Point(container.ActualWidth, container.ActualHeight));

                int rowTextLength = (_getRowText(_listView.Items[i]) ?? string.Empty).Length;

                if (i == lo && i == hi)
                {
                    var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
                    if (textBlock == null) continue;
                    left = XForCharIndex(textBlock, loCh, rowTextLength);
                    right = XForCharIndex(textBlock, hiCh, rowTextLength);
                }
                else if (i == lo)
                {
                    var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
                    left = textBlock != null ? XForCharIndex(textBlock, loCh, rowTextLength) : topLeft.X;
                    right = bottomRight.X;
                }
                else if (i == hi)
                {
                    var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
                    left = topLeft.X;
                    right = textBlock != null ? XForCharIndex(textBlock, hiCh, rowTextLength) : bottomRight.X;
                }
                else
                {
                    left = topLeft.X;
                    right = bottomRight.X;
                }

                if (right < left) { var t = left; left = right; right = t; }
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(0, right - left),
                    Height = Math.Max(0, bottomRight.Y - topLeft.Y),
                    Fill = brush
                };
                Canvas.SetLeft(rect, left);
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
                string line = (lo == hi) ? SafeSubstring(text, loCh, hiCh)
                    : i == lo ? SafeSubstring(text, loCh, text.Length)
                    : i == hi ? SafeSubstring(text, 0, hiCh)
                    : text;
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
            var hit = _listView.InputHitTest(pointInListView) as DependencyObject;
            var container = FindAncestor<ListViewItem>(hit);
            if (container == null) return; // over the scrollbar, or past the last row — leave focus as-is
            int rowIndex = _listView.ItemContainerGenerator.IndexFromContainer(container);
            if (rowIndex < 0) return;
            var textBlock = FindNamedDescendant<TextBlock>(container, "RowText");
            if (textBlock == null) return;
            var pointInText = _listView.TranslatePoint(pointInListView, textBlock);
            int textLength = (_getRowText(_listView.Items[rowIndex]) ?? string.Empty).Length;
            int ch = PlainCharIndexAt(textBlock, pointInText, textLength);
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
                _autoScrollTimer.Stop();
            }
        }

        private void OnAutoScrollTick(object sender, EventArgs e)
        {
            if (_autoScrollDirection == 0 || _scrollViewer == null) { _autoScrollTimer.Stop(); return; }
            _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset + _autoScrollDirection * AutoScrollStep);
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

        /// <summary>Inverse of <see cref="PlainCharIndexAt"/>: finds the on-screen X coordinate (in
        /// overlay space) of the boundary just before character <paramref name="ch"/>. There is no
        /// direct "offset → Rect" API usable here — <c>TextPointer.GetPositionAtOffset</c> counts
        /// "symbols" (which include extra positions at each Run boundary WordDiffHighlighter
        /// introduces), not plain characters, so it doesn't reliably line up with the same
        /// <paramref name="ch"/> this class uses everywhere else. Binary-searching X positions through
        /// the already-verified <see cref="PlainCharIndexAt"/> avoids needing that mapping at all, at
        /// the cost of ~15 cheap iterations — negligible since this only runs for the (at most two)
        /// boundary rows of a selection, never the full range.</summary>
        private double XForCharIndex(TextBlock textBlock, int ch, int textLength)
        {
            if (textLength == 0) return textBlock.TransformToVisual(_overlay).Transform(new Point(0, 0)).X;
            ch = Math.Max(0, Math.Min(ch, textLength));
            double lo = 0, hi = Math.Max(textBlock.ActualWidth, 20000);
            double midY = Math.Max(textBlock.ActualHeight / 2, 1);
            for (int iter = 0; iter < 30 && hi - lo > 0.25; iter++)
            {
                double mid = (lo + hi) / 2;
                int idxAtMid = PlainCharIndexAt(textBlock, new Point(mid, midY), textLength);
                if (idxAtMid < ch) lo = mid; else hi = mid;
            }
            return textBlock.TransformToVisual(_overlay).Transform(new Point(hi, 0)).X;
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
