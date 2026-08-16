using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PickleGit.Converters;
using PickleGit.Models;

namespace PickleGit.Behaviors
{
    /// <summary>
    /// Attached property that renders a DiffLine's Content into a TextBlock's Inlines, combining
    /// two independent span sets: word-level diff highlighting (see GitService.ComputeWordDiff,
    /// rendered as a background tint) and syntax-highlighting (see SyntaxHighlighter, rendered as
    /// a foreground color). Where a span isn't covered by either, the Run is left with no explicit
    /// Foreground/Background so it inherits the TextBlock's own diff-kind coloring.
    /// </summary>
    public static class WordDiffHighlighter
    {
        public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
            "Line", typeof(DiffLine), typeof(WordDiffHighlighter),
            new PropertyMetadata(null, OnAnyChanged));

        public static void SetLine(TextBlock element, DiffLine value) => element.SetValue(LineProperty, value);
        public static DiffLine GetLine(TextBlock element) => (DiffLine)element.GetValue(LineProperty);

        /// <summary>The exact (start, length) span to highlight in this line's plain-text content —
        /// an already-resolved character range, not a term to re-search for. Callers resolve this to
        /// a non-null value only for the one row that is the current find match AND only the one
        /// specific occurrence within it (see PickleGit.ViewModels.PaneFindState /
        /// Converters.CurrentFindMatchConverter): a row with the search term appearing twice must
        /// highlight only whichever single occurrence is currently selected, not both — highlighting
        /// by re-searching the term within the row can't distinguish between them, only an exact
        /// position can.</summary>
        public static readonly DependencyProperty HighlightRangeProperty = DependencyProperty.RegisterAttached(
            "HighlightRange", typeof(DiffHighlightSpan?), typeof(WordDiffHighlighter),
            new PropertyMetadata(null, OnAnyChanged));

        public static void SetHighlightRange(TextBlock element, DiffHighlightSpan? value) => element.SetValue(HighlightRangeProperty, value);
        public static DiffHighlightSpan? GetHighlightRange(TextBlock element) => (DiffHighlightSpan?)element.GetValue(HighlightRangeProperty);

        private static readonly SolidColorBrush AddedHighlightBrush = MakeFrozen(Color.FromArgb(110, 46, 160, 67));
        private static readonly SolidColorBrush DeletedHighlightBrush = MakeFrozen(Color.FromArgb(110, 220, 60, 60));

        // Word-diff highlight spans keep the line's own green/red hue for their background, so the
        // foreground can no longer just inherit the TextBlock's green/red diff-kind color there —
        // that reads as green-on-green / red-on-red. Force a light, neutral foreground for exactly
        // the highlighted span instead (verified by pixel-sampling, not a screenshot glance).
        private static readonly SolidColorBrush HighlightedSpanForeground = MakeFrozen(Color.FromRgb(0xF2, 0xF2, 0xF0));

        private static readonly Dictionary<TokenKind, SolidColorBrush> SyntaxBrushes = new Dictionary<TokenKind, SolidColorBrush>
        {
            [TokenKind.Keyword] = MakeFrozen(Color.FromRgb(0x56, 0x9C, 0xD6)),
            [TokenKind.String] = MakeFrozen(Color.FromRgb(0xCE, 0x91, 0x78)),
            [TokenKind.Comment] = MakeFrozen(Color.FromRgb(0x6A, 0x99, 0x55)),
            [TokenKind.Number] = MakeFrozen(Color.FromRgb(0xB5, 0xCE, 0xA8)),
            [TokenKind.Type] = MakeFrozen(Color.FromRgb(0x4E, 0xC9, 0xC0)),
            [TokenKind.Preprocessor] = MakeFrozen(Color.FromRgb(0xC5, 0x86, 0xC0)),
        };

        private static SolidColorBrush MakeFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock tb)) return;
            Render(tb);
        }

        private static void Render(TextBlock tb)
        {
            tb.Inlines.Clear();
            if (!(GetLine(tb) is DiffLine line)) return;

            var content = line.Content ?? string.Empty;
            if (content.Length == 0) return;

            var hasHighlights = line.HighlightSpans != null && line.HighlightSpans.Count > 0;
            var hasSyntax = line.SyntaxSpans != null && line.SyntaxSpans.Count > 0;
            var highlightRange = GetHighlightRange(tb);
            var hasSearch = highlightRange.HasValue;
            var searchSpans = hasSearch ? new List<DiffHighlightSpan> { highlightRange.Value } : null;

            if (!hasHighlights && !hasSyntax && !hasSearch)
            {
                tb.Inlines.Add(new Run(content));
                return;
            }

            var boundaries = new SortedSet<int> { 0, content.Length };
            if (hasHighlights)
                foreach (var s in line.HighlightSpans)
                {
                    boundaries.Add(Clamp(s.Start, content.Length));
                    boundaries.Add(Clamp(s.Start + s.Length, content.Length));
                }
            if (hasSyntax)
                foreach (var s in line.SyntaxSpans)
                {
                    boundaries.Add(Clamp(s.Start, content.Length));
                    boundaries.Add(Clamp(s.Start + s.Length, content.Length));
                }
            if (hasSearch)
                foreach (var s in searchSpans)
                {
                    boundaries.Add(Clamp(s.Start, content.Length));
                    boundaries.Add(Clamp(s.Start + s.Length, content.Length));
                }

            var highlightBrush = line.Kind == DiffLineKind.Added ? AddedHighlightBrush : DeletedHighlightBrush;
            var points = boundaries.ToList();
            for (int i = 0; i < points.Count - 1; i++)
            {
                int start = points[i], end = points[i + 1];
                if (end <= start) continue;
                var run = new Run(content.Substring(start, end - start));
                bool isWordDiffSpan = hasHighlights && line.HighlightSpans.Any(s => start >= s.Start && start < s.Start + s.Length);
                if (isWordDiffSpan)
                {
                    run.Background = highlightBrush;
                    run.Foreground = HighlightedSpanForeground;
                }
                if (hasSyntax)
                {
                    foreach (var s in line.SyntaxSpans)
                    {
                        if (start >= s.Start && start < s.Start + s.Length)
                        {
                            if (SyntaxBrushes.TryGetValue(s.Kind, out var brush)) run.Foreground = brush;
                            break;
                        }
                    }
                }
                // Search match wins visually over word-diff/syntax coloring — it's what the user is
                // actively looking for right now.
                if (hasSearch && searchSpans.Any(s => start >= s.Start && start < s.Start + s.Length))
                {
                    run.Background = ThemeBrushes.Get("DiffSearchMatchBrush", Color.FromArgb(0x66, 0xE0, 0xB0, 0x00));
                    run.Foreground = HighlightedSpanForeground;
                }
                tb.Inlines.Add(run);
            }
        }

        private static int Clamp(int value, int max) => Math.Max(0, Math.Min(value, max));
    }
}
