using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PickleGit.Converters;
using PickleGit.Models;

namespace PickleGit.Behaviors
{
    /// <summary>Attached property highlighting Find-in-blame search matches inside a Blame row's
    /// Content TextBlock. The Blame ListView renders BlameLine.Content, not DiffLine, so it can't
    /// reuse WordDiffHighlighter (word-diff/syntax spans are diff-specific) — this is a plain
    /// substring highlight, analogous to that behavior's own search-match span.</summary>
    public static class BlameSearchHighlighter
    {
        public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
            "Content", typeof(string), typeof(BlameSearchHighlighter),
            new PropertyMetadata(null, OnAnyChanged));
        public static void SetContent(TextBlock element, string value) => element.SetValue(ContentProperty, value);
        public static string GetContent(TextBlock element) => (string)element.GetValue(ContentProperty);

        /// <summary>The exact (start, length) span to highlight — an already-resolved character
        /// range, not a term to re-search for. Resolved non-null only for the one row that is the
        /// current find match AND only the one specific occurrence within it (RepositoryViewModel.
        /// BlameFind) — a row with the search term appearing twice must highlight only whichever
        /// single occurrence is currently selected, not both.</summary>
        public static readonly DependencyProperty HighlightRangeProperty = DependencyProperty.RegisterAttached(
            "HighlightRange", typeof(DiffHighlightSpan?), typeof(BlameSearchHighlighter),
            new PropertyMetadata(null, OnAnyChanged));
        public static void SetHighlightRange(TextBlock element, DiffHighlightSpan? value) => element.SetValue(HighlightRangeProperty, value);
        public static DiffHighlightSpan? GetHighlightRange(TextBlock element) => (DiffHighlightSpan?)element.GetValue(HighlightRangeProperty);

        private static readonly SolidColorBrush HighlightedForeground = MakeFrozen(Color.FromRgb(0xF2, 0xF2, 0xF0));

        private static SolidColorBrush MakeFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb) Render(tb);
        }

        private static void Render(TextBlock tb)
        {
            tb.Inlines.Clear();
            var content = GetContent(tb) ?? string.Empty;
            if (content.Length == 0) return;

            var range = GetHighlightRange(tb);
            if (!range.HasValue)
            {
                tb.Inlines.Add(new Run(content));
                return;
            }

            var matchBrush = ThemeBrushes.Get("DiffSearchMatchBrush", Color.FromArgb(0x66, 0xE0, 0xB0, 0x00));
            int start = Clamp(range.Value.Start, content.Length);
            int end = Clamp(range.Value.Start + range.Value.Length, content.Length);
            if (start > 0) tb.Inlines.Add(new Run(content.Substring(0, start)));
            if (end > start)
                tb.Inlines.Add(new Run(content.Substring(start, end - start))
                {
                    Background = matchBrush,
                    Foreground = HighlightedForeground
                });
            if (end < content.Length) tb.Inlines.Add(new Run(content.Substring(end)));
        }

        private static int Clamp(int value, int max) => value < 0 ? 0 : value > max ? max : value;
    }
}
