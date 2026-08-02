using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PickleGit.Converters;

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

        /// <summary>The current "Find" search term (RepositoryViewModel.DiffSearchText).</summary>
        public static readonly DependencyProperty SearchTermProperty = DependencyProperty.RegisterAttached(
            "SearchTerm", typeof(string), typeof(BlameSearchHighlighter),
            new PropertyMetadata(null, OnAnyChanged));
        public static void SetSearchTerm(TextBlock element, string value) => element.SetValue(SearchTermProperty, value);
        public static string GetSearchTerm(TextBlock element) => (string)element.GetValue(SearchTermProperty);

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

            var term = GetSearchTerm(tb);
            if (string.IsNullOrEmpty(term))
            {
                tb.Inlines.Add(new Run(content));
                return;
            }

            var matchBrush = ThemeBrushes.Get("DiffSearchMatchBrush", Color.FromArgb(0x66, 0xE0, 0xB0, 0x00));
            int idx = 0;
            while (idx < content.Length)
            {
                int found = content.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    tb.Inlines.Add(new Run(content.Substring(idx)));
                    break;
                }
                if (found > idx) tb.Inlines.Add(new Run(content.Substring(idx, found - idx)));
                tb.Inlines.Add(new Run(content.Substring(found, term.Length))
                {
                    Background = matchBrush,
                    Foreground = HighlightedForeground
                });
                idx = found + term.Length;
            }
        }
    }
}
