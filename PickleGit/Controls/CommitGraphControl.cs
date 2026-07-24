using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PickleGit.Models;

namespace PickleGit.Controls
{
    /// <summary>
    /// Renders one row of the commit graph: passthrough lane lines, edges, and the node circle.
    /// </summary>
    public class CommitGraphCell : FrameworkElement
    {
        public const double LaneWidth = 20.0;
        public const double RowHeight = 32.0;
        private const double NodeRadius = 9.0;
        private const double HalfRow = RowHeight / 2.0;

        private static readonly Typeface InitialsTf = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private double _dpi = 96;
        private string _cachedInitials;
        private FormattedText _cachedInitialsFt;

        public static readonly DependencyProperty NodeProperty =
            DependencyProperty.Register(nameof(Node), typeof(GraphNode), typeof(CommitGraphCell),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public GraphNode Node
        {
            get => (GraphNode)GetValue(NodeProperty);
            set => SetValue(NodeProperty, value);
        }

        public CommitGraphCell()
        {
            Loaded += (s, e) => PickleGit.Services.AvatarService.AvatarReady += OnAvatarReady;
            Unloaded += (s, e) => PickleGit.Services.AvatarService.AvatarReady -= OnAvatarReady;
        }

        private void OnAvatarReady(string email)
        {
            var mine = Node?.Commit?.AuthorEmail;
            if (!string.IsNullOrWhiteSpace(mine) && string.Equals(mine.Trim(), email, StringComparison.OrdinalIgnoreCase))
                InvalidateVisual();
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var lanes = Node?.TotalLanes ?? 1;
            return new Size(lanes * LaneWidth + LaneWidth, RowHeight);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Node == null) return;
            var node = Node;

            double myX = CenterX(node.Lane);
            double midY = HalfRow;

            // 1. Passthrough lane lines
            foreach (var (lane, color) in node.PassthroughLanes)
            {
                double lx = CenterX(lane);
                dc.DrawLine(GetPen(color), new Point(lx, 0), new Point(lx, RowHeight));
            }

            // 2. Outgoing edges (lower half)
            foreach (var edge in node.Edges)
            {
                double fromX = CenterX(edge.FromLane);
                double toX = CenterX(edge.ToLane);
                var pen = GetPen(edge.Color);

                if (edge.FromLane == edge.ToLane)
                    dc.DrawLine(pen, new Point(fromX, midY), new Point(toX, RowHeight));
                else
                    DrawCurve(dc, pen, new Point(fromX, midY), new Point(toX, RowHeight));
            }

            // 3. Incoming lines (top half → node)
            // Straight line for my own lane; only drawn when a parent row was tracking this commit
            if (node.HasIncomingLine)
                dc.DrawLine(GetPen(node.LaneColor), new Point(myX, 0), new Point(myX, midY));
            // Curved convergence lines for other lanes that were also tracking this commit
            foreach (var (lane, color) in node.IncomingLanes)
            {
                double lx = CenterX(lane);
                DrawCurve(dc, GetPen(color), new Point(lx, 0), new Point(myX, midY));
            }

            // 4 & 5. Node circle — dashed hollow for uncommitted, filled avatar for real commits
            if (node.Commit?.IsUncommitted == true)
            {
                var dashedPen = GetDashedPen(node.LaneColor);
                dc.DrawEllipse(null, dashedPen, new Point(myX, midY), NodeRadius, NodeRadius);
            }
            else
            {
                var avatar = PickleGit.Services.AvatarService.TryGet(node.Commit?.AuthorEmail);
                if (avatar != null)
                {
                    var clip = new EllipseGeometry(new Point(myX, midY), NodeRadius, NodeRadius);
                    dc.PushClip(clip);
                    dc.DrawImage(avatar, new Rect(myX - NodeRadius, midY - NodeRadius, NodeRadius * 2, NodeRadius * 2));
                    dc.Pop();
                    dc.DrawEllipse(null, s_nodeBorderPen, new Point(myX, midY), NodeRadius, NodeRadius);
                }
                else
                {
                    var fill = GetFillBrush(node.LaneColor);
                    dc.DrawEllipse(fill, s_nodeBorderPen, new Point(myX, midY), NodeRadius, NodeRadius);

                    var initials = GetInitials(node.Commit?.AuthorEmail);
                    if (!string.IsNullOrEmpty(initials))
                    {
                        if (_cachedInitials != initials || _cachedInitialsFt == null)
                        {
                            _cachedInitials = initials;
                            _cachedInitialsFt = new FormattedText(initials,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight, InitialsTf, 9, Brushes.White, _dpi);
                        }
                        dc.DrawText(_cachedInitialsFt,
                            new Point(myX - _cachedInitialsFt.Width / 2, midY - _cachedInitialsFt.Height / 2));
                    }
                }
            }
        }

        private static string GetInitials(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "?";
            var atIdx = email.IndexOf('@');
            var local = atIdx > 0 ? email.Substring(0, atIdx) : email;

            // Strip numeric GitHub noreply prefix: "34218077+username" → "username"
            var plusIdx = local.IndexOf('+');
            if (plusIdx > 0)
            {
                var prefix = local.Substring(0, plusIdx);
                if (prefix.Length > 0 && prefix.All(char.IsDigit))
                    local = local.Substring(plusIdx + 1);
            }

            // Split on . and - to extract initials from name parts
            var tokens = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var t in tokens)
            {
                if (sb.Length >= 2) break;
                // Skip all-numeric tokens (e.g. version numbers)
                if (t.Length > 0 && !char.IsDigit(t[0])) sb.Append(char.ToUpper(t[0]));
            }
            // Fallback: first letter of the local part
            if (sb.Length == 0 && local.Length > 0 && char.IsLetter(local[0]))
                sb.Append(char.ToUpper(local[0]));
            return sb.Length > 0 ? sb.ToString() : "?";
        }

        private static readonly Dictionary<Color, Pen> s_penCache = new Dictionary<Color, Pen>();
        private static readonly Dictionary<Color, Pen> s_dashedPenCache = new Dictionary<Color, Pen>();
        private static readonly Dictionary<Color, SolidColorBrush> s_brushCache = new Dictionary<Color, SolidColorBrush>();
        private static readonly Pen s_nodeBorderPen;

        static CommitGraphCell()
        {
            var borderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            borderBrush.Freeze();
            s_nodeBorderPen = new Pen(borderBrush, 1.2);
            s_nodeBorderPen.Freeze();
        }

        private static Pen GetPen(Color color)
        {
            if (!s_penCache.TryGetValue(color, out var pen))
            {
                var br = new SolidColorBrush(color);
                br.Freeze();
                pen = new Pen(br, 1.5) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                s_penCache[color] = pen;
            }
            return pen;
        }

        private static Pen GetDashedPen(Color color)
        {
            if (!s_dashedPenCache.TryGetValue(color, out var pen))
            {
                var br = new SolidColorBrush(color);
                br.Freeze();
                var dash = new DashStyle(new double[] { 3, 2 }, 0);
                dash.Freeze();
                pen = new Pen(br, 1.8) { DashStyle = dash };
                pen.Freeze();
                s_dashedPenCache[color] = pen;
            }
            return pen;
        }

        private static SolidColorBrush GetFillBrush(Color color)
        {
            if (!s_brushCache.TryGetValue(color, out var br))
            {
                br = new SolidColorBrush(color);
                br.Freeze();
                s_brushCache[color] = br;
            }
            return br;
        }

        private static void DrawCurve(DrawingContext dc, Pen pen, Point from, Point to)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(from, false, false);
                ctx.BezierTo(
                    new Point(from.X, from.Y + (to.Y - from.Y) * 0.6),
                    new Point(to.X, from.Y + (to.Y - from.Y) * 0.4),
                    to, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static double CenterX(int lane) => lane * LaneWidth + LaneWidth / 2.0;
    }

    /// <summary>
    /// Renders a ref label (branch, tag, HEAD → …) as a colored pill badge.
    /// </summary>
    public class RefLabel : FrameworkElement
    {
        private const double HPad = 6.0;
        private const double VPad = 2.5;
        private const double BadgeHeight = 17.0;
        private const double FontSz = 10.5;
        private const double IconGap = 3.0;
        private static readonly Typeface Tf = new Typeface(
            new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface IconTf = new Typeface(
            new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly SolidColorBrush s_brushHead;
        private static readonly SolidColorBrush s_brushTag;
        private static readonly SolidColorBrush s_brushRemote;
        private static readonly SolidColorBrush s_brushLocal;
        private static readonly Pen s_badgeBorderPen;
        private static readonly Pen s_branchIconPen;
        private const string TagGlyph = "";
        private const double BranchIconSize = 10.0;

        static RefLabel()
        {
            s_brushHead   = new SolidColorBrush(Color.FromRgb(0x6F, 0xBE, 0x72)); s_brushHead.Freeze();
            s_brushTag    = new SolidColorBrush(Color.FromRgb(0xC9, 0x82, 0x47)); s_brushTag.Freeze();
            s_brushRemote = new SolidColorBrush(Color.FromRgb(0x3A, 0x72, 0xA8)); s_brushRemote.Freeze();
            s_brushLocal  = new SolidColorBrush(Color.FromRgb(0x36, 0x7A, 0x8A)); s_brushLocal.Freeze();

            var borderBr = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            borderBr.Freeze();
            s_badgeBorderPen = new Pen(borderBr, 0.8);
            s_badgeBorderPen.Freeze();

            s_branchIconPen = new Pen(Brushes.White, 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            s_branchIconPen.Freeze();
        }

        public static readonly DependencyProperty RefNameProperty =
            DependencyProperty.Register(nameof(RefName), typeof(string), typeof(RefLabel),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsMeasure));

        public string RefName
        {
            get => (string)GetValue(RefNameProperty);
            set => SetValue(RefNameProperty, value);
        }

        private double _dpi = 96;
        private string _cachedRef;
        private FormattedText _cachedFt;
        private string _cachedIconChar;
        private FormattedText _cachedIconFt;

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }
        /// <summary>Branches (HEAD/local/remote) get a hand-drawn branch icon (see DrawBranchIcon) -
        /// a plain single character was tried first (reusing the status bar's current-branch glyph)
        /// but didn't read well at this badge's size/weight, so it's drawn as vector shapes instead,
        /// the same reliable approach already used for the sidebar's branch icon. Tags get the Segoe
        /// Fluent Icons Tag glyph (U+E8EC, verified against Microsoft's official icon list, and
        /// already confirmed rendering correctly in this exact badge).</summary>
        private (string text, SolidColorBrush bg, bool isHead, bool isBranch) Classify()
        {
            if (RefName == null) return (string.Empty, s_brushLocal, false, false);
            if (RefName.StartsWith("HEAD -> "))
                return (RefName.Substring(8), s_brushHead, true, true);
            if (RefName.StartsWith("tag: "))
                return (RefName.Substring(5), s_brushTag, false, false);
            if (RefName.Contains("/"))
                return (RefName, s_brushRemote, false, true);
            return (RefName, s_brushLocal, false, true);
        }

        /// <summary>Small git-branch glyph (trunk + fork to a branch tip), matching the sidebar's
        /// Local Branches header icon shape, scaled down to fit inline in a ref badge.</summary>
        private static void DrawBranchIcon(DrawingContext dc, double x, double yCenter, double size)
        {
            double r = size * 0.16;
            double top = yCenter - size * 0.4;
            double bottom = yCenter + size * 0.4;
            double leftX = x + size * 0.3;
            double rightX = x + size * 0.85;

            dc.DrawLine(s_branchIconPen, new Point(leftX, bottom - r), new Point(leftX, top + r));

            var curve = new StreamGeometry();
            using (var ctx = curve.Open())
            {
                ctx.BeginFigure(new Point(leftX, bottom - r * 1.6), false, false);
                ctx.BezierTo(new Point(leftX, yCenter), new Point(rightX, yCenter),
                    new Point(rightX, top + r * 1.6), true, false);
            }
            curve.Freeze();
            dc.DrawGeometry(null, s_branchIconPen, curve);

            dc.DrawEllipse(Brushes.White, null, new Point(leftX, bottom), r, r);
            dc.DrawEllipse(Brushes.White, null, new Point(leftX, top), r, r);
            dc.DrawEllipse(Brushes.White, null, new Point(rightX, top), r, r);
        }

        private FormattedText GetText(string t)
        {
            if (_cachedRef == t && _cachedFt != null)
                return _cachedFt;
            _cachedRef = t;
            _cachedFt = new FormattedText(t, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Tf, FontSz, Brushes.White, _dpi);
            return _cachedFt;
        }

        private FormattedText GetIconText(string icon, bool fluent)
        {
            if (_cachedIconChar == icon && _cachedIconFt != null)
                return _cachedIconFt;
            _cachedIconChar = icon;
            _cachedIconFt = new FormattedText(icon, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, fluent ? IconTf : Tf, FontSz, Brushes.White, _dpi);
            return _cachedIconFt;
        }

        protected override Size MeasureOverride(Size avail)
        {
            var c = Classify();
            if (string.IsNullOrEmpty(c.text)) return new Size(0, 0);
            var ft = GetText(c.text);
            // OnRender mutates this same cached FormattedText's MaxTextWidth down to whatever fit
            // last time (e.g. when the column was narrow) and never resets it — reading ft.Width
            // without resetting first would report that stale truncated width forever after, even
            // once the column is widened back out. 0 is FormattedText's own "unconstrained" value.
            ft.MaxTextWidth = 0;
            double iconW = (c.isBranch ? BranchIconSize : GetIconText(TagGlyph, true).Width) + IconGap;
            double desired = ft.Width + HPad * 2 + 2 + iconW;
            // Respect a real (finite) width constraint from the parent — e.g. the commit list's
            // BRANCH/TAG column, which now shrinks this label under width pressure instead of
            // measuring/rendering it at full natural size regardless of available space.
            if (!double.IsInfinity(avail.Width))
                desired = Math.Min(desired, Math.Max(avail.Width, iconW + HPad * 2 + 8));
            return new Size(desired, BadgeHeight + 2);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var c = Classify();
            if (string.IsNullOrEmpty(c.text)) return;

            FormattedText tagIconFt = c.isBranch ? null : GetIconText(TagGlyph, true);
            double iconW = (c.isBranch ? BranchIconSize : tagIconFt.Width) + IconGap;

            var ft = GetText(c.text);
            ft.MaxTextWidth = 0; // reset before reading .Width below — see MeasureOverride's comment

            // Use the actually-arranged width (which can be less than what MeasureOverride asked
            // for, once the parent Grid's overflow-count column claims its own space) so the name
            // shrinks with an ellipsis instead of just being measured/clipped at full size.
            double w = !double.IsNaN(ActualWidth) && ActualWidth > 0
                ? ActualWidth : ft.Width + HPad * 2 + iconW;
            ft.MaxTextWidth = Math.Max(10, w - HPad * 2 - iconW);
            // Without this, FormattedText wraps the name across multiple lines to stay within
            // MaxTextWidth instead of truncating a single line — Trimming only takes effect once
            // the text is also capped to one line.
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;

            var rect = new Rect(1, 1, w, BadgeHeight);
            dc.DrawRoundedRectangle(c.bg, s_badgeBorderPen, rect, 4, 4);
            double x = 1 + HPad;
            if (c.isBranch)
            {
                DrawBranchIcon(dc, x, 1 + BadgeHeight / 2, BranchIconSize);
                x += BranchIconSize + IconGap;
            }
            else
            {
                dc.DrawText(tagIconFt, new Point(x, 1 + (BadgeHeight - tagIconFt.Height) / 2));
                x += tagIconFt.Width + IconGap;
            }
            dc.DrawText(ft, new Point(x, 1 + (BadgeHeight - ft.Height) / 2));
        }
    }
}
