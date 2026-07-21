using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PickleGit.Models;

namespace PickleGit.Controls
{
    /// <summary>Vertical strip showing where a diff's Added/Deleted lines fall,
    /// proportionally over the whole file, with click-to-jump. Placed in its own narrow column next
    /// to a diff ListView's native scrollbar (not replacing it — reusing the scrollbar's own
    /// drag/thumb physics avoids re-implementing scroll input handling). See DiffView.xaml
    /// (Unified/Side-by-side) and DiffView.xaml.cs's ChangeMap_JumpRequested.</summary>
    public class DiffChangeMapControl : FrameworkElement
    {
        public static readonly DependencyProperty RowKindsProperty =
            DependencyProperty.Register(nameof(RowKinds), typeof(IReadOnlyList<DiffLineKind>), typeof(DiffChangeMapControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<DiffLineKind> RowKinds
        {
            get => (IReadOnlyList<DiffLineKind>)GetValue(RowKindsProperty);
            set => SetValue(RowKindsProperty, value);
        }

        /// <summary>Fired on click with the clicked position as a 0..1 fraction of the strip's
        /// height — the handler converts this to a row index and scrolls the paired ListView.</summary>
        public event EventHandler<double> JumpRequested;

        private static readonly SolidColorBrush s_addedBrush = MakeBrush(0x2E, 0xA0, 0x43);
        private static readonly SolidColorBrush s_deletedBrush = MakeBrush(0xDC, 0x3C, 0x3C);
        private static readonly SolidColorBrush s_trackBrush = MakeBrush(0x80, 0x80, 0x80, 0x18);

        private static SolidColorBrush MakeBrush(byte r, byte g, byte b, byte a = 0xFF)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public DiffChangeMapControl()
        {
            Cursor = Cursors.Hand;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRectangle(s_trackBrush, null, new Rect(0, 0, w, h));

            var kinds = RowKinds;
            int n = kinds?.Count ?? 0;
            if (n == 0) return;

            int i = 0;
            while (i < n)
            {
                var k = kinds[i];
                if (k != DiffLineKind.Added && k != DiffLineKind.Deleted) { i++; continue; }
                int start = i;
                while (i < n && kinds[i] == k) i++;
                double y0 = (double)start / n * h;
                double y1 = (double)i / n * h;
                double markH = Math.Max(2.0, y1 - y0);
                dc.DrawRectangle(k == DiffLineKind.Added ? s_addedBrush : s_deletedBrush, null,
                    new Rect(1, y0, Math.Max(0, w - 2), markH));
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (ActualHeight <= 0) return;
            double frac = e.GetPosition(this).Y / ActualHeight;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            JumpRequested?.Invoke(this, frac);
            e.Handled = true;
        }
    }
}
