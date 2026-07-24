using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Converters
{
    /// <summary>Formats a DateTimeOffset using a runtime-configurable format string, so the
    /// Settings UI tab's date-format preference can apply without a per-binding StringFormat.
    /// CurrentFormat is set once at startup and whenever the setting changes — no disk I/O per render.</summary>
    public class DateFormatConverter : IValueConverter
    {
        public static string CurrentFormat = "yyyy-MM-dd HH:mm";
        /// <summary>When true the commit list shows "2h ago"-style dates. Pass ConverterParameter
        /// "abs" to force the absolute format regardless (used by tooltips).</summary>
        public static bool UseRelative;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool forceAbsolute = (p as string) == "abs";
            if (value is DateTimeOffset dto)
                return UseRelative && !forceAbsolute ? ToRelative(dto) : dto.ToString(CurrentFormat, c);
            if (value is DateTime dt)
                return UseRelative && !forceAbsolute ? ToRelative(dt) : dt.ToString(CurrentFormat, c);
            return value?.ToString() ?? string.Empty;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;

        private static string ToRelative(DateTimeOffset date)
        {
            var span = DateTimeOffset.Now - date;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool bv && bv;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>Visible when the bound enum value's name matches ConverterParameter (case-insensitive),
    /// e.g. <c>Visibility="{Binding StagedFileViewMode, Converter={StaticResource EnumEqualsVis},
    /// ConverterParameter=Tree}"</c> — used to switch between the Flat and Tree file-list ListViews.</summary>
    public class EnumEqualsVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value == null || p == null) return Visibility.Collapsed;
            return string.Equals(value.ToString(), p.ToString(), StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Shows/hides a conflict block's "unresolved" (inline accept buttons) vs "resolved"
    /// (collapsed strip) sub-panel based on MergeConflictBlock.Resolution. A fresh ConflictViewItem
    /// is rebuilt for every block on every resolve, so this never needs to react to the same
    /// instance changing — the value is fixed for that item's lifetime.</summary>
    public class ResolutionToVisibilityConverter : IValueConverter
    {
        /// <summary>false (default) = visible while Unresolved; true = visible once resolved.</summary>
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool isUnresolved = value is ConflictResolution r && r == ConflictResolution.Unresolved;
            bool show = Invert ? !isUnresolved : isUnresolved;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Inverts a bool — for IsEnabled bindings that should be false while some other
    /// bool (e.g. IsBusy) is true.</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            !(value is bool b && b);
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            !(value is bool b && b);
    }

    /// <summary>Detects RepositoryViewModel.CurrentBranch's "detached @ &lt;sha&gt;" / "(detached)"
    /// text convention to drive a dedicated detached-HEAD badge, distinct from the normal branch label.</summary>
    public class DetachedHeadConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool isDetached = value is string s && s.StartsWith("detached", StringComparison.Ordinal);
            if (Invert) isDetached = !isDetached;
            return isDetached ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool isNull = value == null;
            if (Invert) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class FileChangeKindToColorConverter : IValueConverter
    {
        // Cached + frozen once instead of a fresh SolidColorBrush per call — this converter fires
        // once per row in the (virtualized, but still large) staged/unstaged/commit file lists,
        // the same avoidable per-render allocation the OnRender freezing rule elsewhere targets.
        private static readonly Brush Added = Freeze(Color.FromRgb(0x67, 0xAD, 0x50));
        private static readonly Brush Deleted = Freeze(Color.FromRgb(0xBE, 0x4B, 0x48));
        private static readonly Brush Modified = Freeze(Color.FromRgb(0x5C, 0x9B, 0xD6));
        private static readonly Brush Renamed = Freeze(Color.FromRgb(0xD7, 0x9B, 0x57));
        private static readonly Brush Conflicted = Freeze(Color.FromRgb(0xBC, 0x6B, 0xAC));

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (!(value is FileChangeKind kind)) return Brushes.Gray;
            switch (kind)
            {
                case FileChangeKind.Added: return Added;
                case FileChangeKind.Deleted: return Deleted;
                case FileChangeKind.Modified: return Modified;
                case FileChangeKind.Renamed: return Renamed;
                case FileChangeKind.Conflicted: return Conflicted;
                default: return Brushes.Gray;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>Bisect good/bad/skip/current badge color for the commit-graph row — mirrors
    /// FileChangeKindToColorConverter's hardcoded-per-value convention.</summary>
    public class BisectMarkToColorConverter : IValueConverter
    {
        // Cached + frozen once — see FileChangeKindToColorConverter above; this fires once per
        // commit-graph row while a bisect is active.
        private static readonly Brush Good = Freeze(Color.FromRgb(0x67, 0xAD, 0x50));
        private static readonly Brush Bad = Freeze(Color.FromRgb(0xBE, 0x4B, 0x48));
        private static readonly Brush Skip = Freeze(Color.FromRgb(0x8A, 0x95, 0xA3));
        private static readonly Brush Current = Freeze(Color.FromRgb(0x5C, 0x9B, 0xD6));

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (!(value is BisectMark mark)) return Brushes.Transparent;
            switch (mark)
            {
                case BisectMark.Good: return Good;
                case BisectMark.Bad: return Bad;
                case BisectMark.Skip: return Skip;
                case BisectMark.Current: return Current;
                default: return Brushes.Transparent;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    public class BisectMarkToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (!(value is BisectMark mark)) return string.Empty;
            switch (mark)
            {
                case BisectMark.Good: return "G";
                case BisectMark.Bad: return "B";
                case BisectMark.Skip: return "S";
                case BisectMark.Current: return "?";
                default: return string.Empty;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    public class GraphWidthConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            const double lw = Controls.CommitGraphCell.LaneWidth;
            if (value is int lanes) return Math.Max(lanes, 1) * lw + lw;
            if (value is GraphNode node) return Math.Max(node.TotalLanes, 1) * lw + lw;
            return lw * 2;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class ColorToSolidBrushConverter : IValueConverter
    {
        // The Color varies per call (author/ref color, etc.), so a fixed set of fields won't do —
        // cache per distinct Color instead of allocating (and never freezing) a new brush every call.
        private static readonly Dictionary<Color, Brush> Cache = new Dictionary<Color, Brush>();

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (!(value is Color col)) return Brushes.Gray;
            if (Cache.TryGetValue(col, out var cached)) return cached;
            var brush = new SolidColorBrush(col);
            brush.Freeze();
            Cache[col] = brush;
            return brush;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>All refs joined for a tooltip; null (no tooltip) when the commit has no refs.</summary>
    public class RefsTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var refs = value as List<string>;
            return refs != null && refs.Count > 0 ? string.Join("\n", refs) : null;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>Prepends ConverterParameter to the bound value — used instead of Binding.StringFormat
    /// where the target is a non-String DP (e.g. CommandParameter, typed object): WPF only applies
    /// StringFormat when the target property's type is exactly String, so a "{0}"-style format on a
    /// CommandParameter binding is silently ignored and the raw unformatted value passes through.</summary>
    public class PrependConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => $"{p}{value}";
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class HeadBoldConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is BranchInfo bi && bi.IsHead) return FontWeights.Bold;
            return FontWeights.Normal;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class IntAboveZeroConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool above = value is int i && i > 0;
            if (p is string s && s == "Invert") above = !above;
            return above ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>Subtracts a fixed budget (ConverterParameter, default 40) from a bound column
    /// width, floored at 20 — used to give the primary ref label in the commit list's BRANCH/TAG
    /// column a MaxWidth that leaves room for the "+N" overflow pill, so the two sit packed
    /// together (via a plain StackPanel) instead of the label sitting in its own Star-sized Grid
    /// cell with a gap before the pill's Auto column.</summary>
    public class WidthMinusBudgetConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            double budget = p is string s && double.TryParse(s, out var parsed) ? parsed : 40;
            double width = value is double d ? d : value is GridLength gl ? gl.Value : 0;
            return Math.Max(20, width - budget);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Truncates a full commit sha to its short 7-char form — for BisectState's raw
    /// sha strings, which (unlike CommitInfo) have no ShortSha property of their own.</summary>
    public class ShaShortConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var sha = value as string;
            return string.IsNullOrEmpty(sha) ? string.Empty : sha.Substring(0, Math.Min(7, sha.Length));
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Bisect progress line: " — N revisions left (~M steps)", or " — resuming…" when
    /// RevisionsLeft is -1 (unknown after an app restart — git only reports this on the stdout of
    /// the step that produced it, so it can't be reconstructed from .git/BISECT_* files).</summary>
    public class BisectProgressTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var revisionsLeft = value is int i ? i : -1;
            if (revisionsLeft < 0) return " — resuming…";
            return $" — {revisionsLeft} revision{(revisionsLeft == 1 ? "" : "s")} left";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    public class AuthorInitialsConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var name = value as string;
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[parts.Length - 1][0]}".ToUpper();
            return name[0].ToString().ToUpper();
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class AuthorAvatarColorConverter : IValueConverter
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x56, 0xB6, 0xC2),
            Color.FromRgb(0x6F, 0xBE, 0x72),
            Color.FromRgb(0xD1, 0x9A, 0x66),
            Color.FromRgb(0x61, 0xAF, 0xEF),
            Color.FromRgb(0xC6, 0x78, 0xDD),
            Color.FromRgb(0xE0, 0x6C, 0x75),
            Color.FromRgb(0xE5, 0xC0, 0x7B),
        };

        // One frozen brush per palette entry, built once — the hash only ever selects among these
        // fixed 7 colors, so there's no need to allocate a fresh SolidColorBrush per row per render.
        private static readonly Brush[] Brushes_ = Palette.Select(color =>
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return (Brush)brush;
        }).ToArray();

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var name = value as string ?? "";
            int hash = 0;
            foreach (char ch in name) hash = hash * 31 + ch;
            return Brushes_[Math.Abs(hash) % Brushes_.Length];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>
    /// bool → GridLength. true=ConverterParameter width (or * if parameter="*"), false=0.
    /// </summary>
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type t, object parameter, CultureInfo c)
        {
            bool show = value is bool b && b;
            if (!show) return new System.Windows.GridLength(0);
            if (parameter is string s)
            {
                if (s == "*") return new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return new System.Windows.GridLength(d);
            }
            return new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>
    /// MultiBinding: (bool showGraph, int totalLanes) → GridLength for the graph column.
    /// Returns 0 when hidden, otherwise LaneWidth * lanes + LaneWidth.
    /// </summary>
    public class ConditionalGraphWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            bool show = values.Length > 0 && values[0] is bool b && b;
            if (!show) return new System.Windows.GridLength(0);
            int lanes = values.Length > 1 && values[1] is int i ? i : 1;
            double lw = Controls.CommitGraphCell.LaneWidth;
            return new System.Windows.GridLength(Math.Max(lanes, 1) * lw + lw);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo c) => null;
    }

    /// <summary>
    /// MultiBinding: (bool show, double width) → GridLength. Returns 0 when hidden.
    /// </summary>
    public class ShowHideWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            bool isLastColumn = false;
            bool show = values.Length > 0 && values[0] is bool b && b;
            if (values.Length > 2 && values[2] is bool isLast)
                isLastColumn = isLast;

            if (!show) return new GridLength(0);

            if (isLastColumn)
                return new GridLength(1, GridUnitType.Star);

            if (values.Length > 1 && values[1] is double d)
                return new GridLength(Math.Max(d, 0));

            return new GridLength(1, GridUnitType.Star);
        }
        public object[] ConvertBack(object values, Type[] t, object p, CultureInfo c) => null;
    }

    /// <summary>Looks up theme brushes from application resources (falls back to a hardcoded
    /// dark-theme color if the key is missing), cached per key — the theme is fixed for the
    /// lifetime of the process (changing it requires a restart).</summary>
    internal static class ThemeBrushes
    {
        private static readonly System.Collections.Generic.Dictionary<string, Brush> Cache =
            new System.Collections.Generic.Dictionary<string, Brush>();

        public static Brush Get(string key, Color fallback)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var brush = System.Windows.Application.Current?.TryFindResource(key) as Brush;
            if (brush == null)
            {
                brush = new SolidColorBrush(fallback);
                brush.Freeze();
            }
            Cache[key] = brush;
            return brush;
        }
    }

    /// <summary>Switches a hunk header button's label between the whole-hunk action ("Stage Hunk")
    /// and the selection-scoped action ("Stage Lines") based on DiffHunk.HasLineSelection.
    /// ConverterParameter is the verb: "Stage" / "Discard" / "Unstage".</summary>
    public class HunkActionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object parameter, CultureInfo c)
        {
            var verb = parameter as string ?? "Stage";
            var hasSelection = value is bool b && b;
            return hasSelection ? $"{verb} Lines" : $"{verb} Hunk";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class DiffLineBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (!(value is DiffLineKind kind)) return Brushes.Transparent;
            switch (kind)
            {
                case DiffLineKind.Added: return ThemeBrushes.Get("DiffAddedBgBrush", Color.FromRgb(0x1B, 0x3A, 0x1B));
                case DiffLineKind.Deleted: return ThemeBrushes.Get("DiffDeletedBgBrush", Color.FromRgb(0x3A, 0x1B, 0x1B));
                case DiffLineKind.Header: return ThemeBrushes.Get("DiffHeaderBgBrush", Color.FromRgb(0x1B, 0x2A, 0x3A));
                default: return Brushes.Transparent;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    /// <summary>Diff text reads like a normal code editor — colored by syntax (see
    /// WordDiffHighlighter's per-run SyntaxSpans), not by add/delete status. Added/Deleted
    /// deliberately fall through to the same neutral default as Context; only the row background
    /// (DiffLineBackgroundConverter) still indicates add/delete. Header (a rare "\ No newline"
    /// marker) keeps its own distinct tint — it isn't part of that complaint.</summary>
    public class DiffLineForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is DiffLineKind kind && kind == DiffLineKind.Header)
                return ThemeBrushes.Get("DiffHeaderFgBrush", Color.FromRgb(0x5C, 0x9B, 0xD6));
            return ThemeBrushes.Get("DiffDefaultFgBrush", Color.FromRgb(0xD5, 0xD8, 0xDE));
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => null;
    }

    public class DiffItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate HunkTemplate { get; set; }
        public DataTemplate LineTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            // Shared selector for both the unified (DiffItem) and side-by-side (SideBySideItem)
            // projections — two unrelated types that each carry their own Kind property, so both
            // need an explicit check (a single "is DiffItem" check silently always falls through to
            // LineTemplate for SideBySideItem, rendering hunk headers as blank line rows).
            if (item is DiffItem di) return di.Kind == DiffItemKind.HunkHeader ? HunkTemplate : LineTemplate;
            if (item is SideBySideItem sbs) return sbs.Kind == DiffItemKind.HunkHeader ? HunkTemplate : LineTemplate;
            return LineTemplate;
        }
    }

    /// <summary>Picks the context-text vs conflict-block row template for the merge editor's
    /// flattened, interleaved document view (MergeConflictFileViewModel.FlatItems).</summary>
    public class ConflictViewItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ContextTemplate { get; set; }
        public DataTemplate BlockTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ConflictViewItem ci)
                return ci.Kind == ConflictDocItemKind.Block ? BlockTemplate : ContextTemplate;
            return ContextTemplate;
        }
    }

    /// <summary>Picks the DataTemplate for one flattened Staged/Unstaged Tree-view row by
    /// FileTreeRowKind — Folder (group header) vs File (leaf, wraps the real FileChange).</summary>
    public class FileTreeRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate FolderTemplate { get; set; }
        public DataTemplate FileTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is FileTreeRow row)
                return row.Kind == FileTreeRowKind.Folder ? FolderTemplate : FileTemplate;
            return null;
        }
    }

    /// <summary>Turns a SidebarRow.IndentLevel into a left Margin, applied to a row's inner
    /// content only — never the ListViewItem container — so selection/hover highlighting always
    /// spans the full row width regardless of nesting depth.</summary>
    public class IndentLevelToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int level = value is int i ? i : 0;
            return new Thickness(16 * level, 0, 0, 0);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Picks the DataTemplate for one flattened sidebar row by SidebarRowKind.</summary>
    public class SidebarRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate LocalBranchesHeaderTemplate { get; set; }
        public DataTemplate RemoteBranchesHeaderTemplate { get; set; }
        public DataTemplate TagsHeaderTemplate { get; set; }
        public DataTemplate StashesHeaderTemplate { get; set; }
        public DataTemplate RemotesHeaderTemplate { get; set; }
        public DataTemplate PullRequestsHeaderTemplate { get; set; }
        public DataTemplate SubmodulesHeaderTemplate { get; set; }
        public DataTemplate WorktreesHeaderTemplate { get; set; }
        public DataTemplate LocalBranchGroupTemplate { get; set; }
        public DataTemplate LocalBranchLeafTemplate { get; set; }
        public DataTemplate RemoteBranchGroupTemplate { get; set; }
        public DataTemplate RemoteBranchLeafTemplate { get; set; }
        public DataTemplate TagTemplate { get; set; }
        public DataTemplate StashTemplate { get; set; }
        public DataTemplate RemoteTemplate { get; set; }
        public DataTemplate PullRequestTemplate { get; set; }
        public DataTemplate SubmoduleTemplate { get; set; }
        public DataTemplate WorktreeTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (!(item is SidebarRow row)) return null;
            switch (row.Kind)
            {
                case SidebarRowKind.LocalBranchesHeader: return LocalBranchesHeaderTemplate;
                case SidebarRowKind.RemoteBranchesHeader: return RemoteBranchesHeaderTemplate;
                case SidebarRowKind.TagsHeader: return TagsHeaderTemplate;
                case SidebarRowKind.StashesHeader: return StashesHeaderTemplate;
                case SidebarRowKind.RemotesHeader: return RemotesHeaderTemplate;
                case SidebarRowKind.PullRequestsHeader: return PullRequestsHeaderTemplate;
                case SidebarRowKind.SubmodulesHeader: return SubmodulesHeaderTemplate;
                case SidebarRowKind.WorktreesHeader: return WorktreesHeaderTemplate;
                case SidebarRowKind.LocalBranchGroup: return LocalBranchGroupTemplate;
                case SidebarRowKind.LocalBranchLeaf: return LocalBranchLeafTemplate;
                case SidebarRowKind.RemoteBranchGroup: return RemoteBranchGroupTemplate;
                case SidebarRowKind.RemoteBranchLeaf: return RemoteBranchLeafTemplate;
                case SidebarRowKind.Tag: return TagTemplate;
                case SidebarRowKind.Stash: return StashTemplate;
                case SidebarRowKind.Remote: return RemoteTemplate;
                case SidebarRowKind.PullRequest: return PullRequestTemplate;
                case SidebarRowKind.Submodule: return SubmoduleTemplate;
                case SidebarRowKind.Worktree: return WorktreeTemplate;
                default: return null;
            }
        }
    }

    /// <summary>Button label for Stage All/Unstage All: "{Verb} All" normally, "{Verb} N Files" once
    /// 2+ files are multi-selected (ConverterParameter = verb, e.g. "Stage" or "Unstage").</summary>
    public class BulkActionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int count = value is int i ? i : 0;
            string verb = p as string ?? "Stage";
            return count >= 2 ? $"{verb} {count} Files" : $"{verb} All";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Tooltip for the bulk discard button: "Discard all files" normally, "Discard N files"
    /// once 2+ files are multi-selected.</summary>
    public class DiscardTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int count = value is int i ? i : 0;
            return count >= 2 ? $"Discard {count} files" : "Discard all files";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Badge text on the bulk discard button: empty below 2 selected (badge hidden via
    /// IntAtLeastTwoToVisibilityConverter), the selection count at/above 2.</summary>
    public class DiscardBadgeTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int count = value is int i ? i : 0;
            return count >= 2 ? count.ToString(c) : string.Empty;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>Visibility for the discard badge: visible only once 2+ files are selected.</summary>
    public class IntAtLeastTwoToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is int i && i >= 2) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
