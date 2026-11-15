using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PickleGit.Controls
{
    /// <summary>Keeps several ScrollViewers scrolled together (vertically always, horizontally only
    /// between members added with <c>syncHorizontal: true</c>).
    ///
    /// ScrollChanged is not raised inside ScrollToVerticalOffset — it arrives on a later layout
    /// pass — so a single "syncing" boolean can't tell the echo of our own push apart from a genuine
    /// user scroll: held too long it drops genuine scrolls (panes left behind on a fast wheel),
    /// released too early it lets echoes push stale offsets back onto the source. Instead, each push
    /// records the offset the target is expected to land on (clamped to its own scrollable range);
    /// the target's next ScrollChanged is an echo only if it lands there, and anything else is
    /// genuine and propagates.
    ///
    /// Members sharing an <c>alignmentKey</c> show the same rows, so they follow each other's
    /// absolute vertical offset. Members with different row sets (e.g. the merge editor's Ours/Theirs
    /// panes vs its Result pane) follow each other's scroll <em>delta</em> instead, which preserves
    /// whatever relative alignment <see cref="RunIndependent"/> set up (e.g. all panes on the same
    /// conflict block); when the source reaches its top or bottom edge, they snap to the same edge so
    /// the panes re-converge there.</summary>
    public sealed class ScrollSyncGroup
    {
        private const double Tolerance = 0.5;

        private sealed class Member
        {
            public ScrollViewer Viewer;
            public bool SyncHorizontal;
            public object AlignmentKey;
            public double? ExpectedVertical;
            public double? ExpectedHorizontal;
        }

        private readonly List<Member> _members = new List<Member>();
        // Captured at construction (always on the UI thread — the owning view's field initializer),
        // never Dispatcher.CurrentDispatcher at call time: a call from another thread would otherwise
        // queue EndIndependent on a dispatcher that never pumps, disabling sync for good.
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private int _independentDepth;

        /// <param name="alignmentKey">Members with equal non-null keys share the same row set and
        /// sync absolute vertical offsets; all other pairs sync by scroll delta.</param>
        public void Add(ScrollViewer viewer, bool syncHorizontal, object alignmentKey = null)
        {
            if (viewer == null) throw new ArgumentNullException(nameof(viewer));
            if (Find(viewer) != null) return;
            var member = new Member { Viewer = viewer, SyncHorizontal = syncHorizontal, AlignmentKey = alignmentKey };
            _members.Add(member);
            viewer.ScrollChanged += (s, e) => OnScrollChanged(member, e);
        }

        /// <summary>Runs <paramref name="action"/> (typically several ScrollIntoView calls, one per
        /// pane, each positioning its own pane) without letting the resulting scrolls propagate —
        /// used when panes with different row sets must each be aligned on their own content rather
        /// than on a shared pixel offset. ListView.ScrollIntoView on an unrealized item defers its
        /// work to a DispatcherPriority.Loaded callback, which then triggers a layout pass, so
        /// suppression is released two Loaded hops later to cover that whole chain.</summary>
        public void RunIndependent(Action action)
        {
            if (action == null) return;
            _dispatcher.VerifyAccess();
            _independentDepth++;
            try
            {
                action();
            }
            finally
            {
                _dispatcher.BeginInvoke(new Action(() =>
                    _dispatcher.BeginInvoke(new Action(EndIndependent), DispatcherPriority.Loaded)),
                    DispatcherPriority.Loaded);
            }
        }

        private void EndIndependent()
        {
            if (_independentDepth > 0) _independentDepth--;
            if (_independentDepth > 0) return;
            foreach (var m in _members)
            {
                m.ExpectedVertical = null;
                m.ExpectedHorizontal = null;
            }
        }

        /// <summary>Pushes <paramref name="source"/>'s current absolute offsets onto every other
        /// member (regardless of alignment key) — used to bring all panes to one common position.</summary>
        public void SyncFrom(ScrollViewer source)
        {
            var member = Find(source);
            if (member == null || _independentDepth > 0) return;
            foreach (var target in _members)
            {
                if (ReferenceEquals(target, member)) continue;
                PushVertical(target, member.Viewer.VerticalOffset);
                if (member.SyncHorizontal && target.SyncHorizontal)
                    PushHorizontal(target, member.Viewer.HorizontalOffset);
            }
        }

        private void OnScrollChanged(Member member, ScrollChangedEventArgs e)
        {
            // ScrollChanged bubbles — ignore any ScrollViewer nested inside this pane's rows.
            if (!ReferenceEquals(e.OriginalSource, member.Viewer)) return;

            bool verticalMoved = Math.Abs(e.VerticalChange) > double.Epsilon;
            bool horizontalMoved = Math.Abs(e.HorizontalChange) > double.Epsilon;
            if (!verticalMoved && !horizontalMoved) return; // extent/viewport-only change

            if (_independentDepth > 0)
            {
                member.ExpectedVertical = null;
                member.ExpectedHorizontal = null;
                return;
            }

            var viewer = member.Viewer;
            bool verticalGenuine = verticalMoved &&
                !ConsumeEcho(ref member.ExpectedVertical, viewer.VerticalOffset, viewer.ScrollableHeight);
            bool horizontalGenuine = horizontalMoved &&
                !ConsumeEcho(ref member.ExpectedHorizontal, viewer.HorizontalOffset, viewer.ScrollableWidth);

            if (verticalGenuine) BroadcastVertical(member, e.VerticalChange);
            if (horizontalGenuine && member.SyncHorizontal)
            {
                foreach (var target in _members)
                    if (!ReferenceEquals(target, member) && target.SyncHorizontal)
                        PushHorizontal(target, viewer.HorizontalOffset);
            }
        }

        /// <summary>True when an expectation was pending and the actual offset matches it — the
        /// event is our own push landing. Either way the expectation is consumed: a mismatch means
        /// the pane moved somewhere else since (a genuine scroll), which must propagate.</summary>
        private static bool ConsumeEcho(ref double? expected, double actual, double scrollable)
        {
            if (!expected.HasValue) return false;
            double target = Clamp(expected.Value, scrollable);
            expected = null;
            return Math.Abs(actual - target) <= Tolerance;
        }

        private void BroadcastVertical(Member source, double delta)
        {
            var sv = source.Viewer;
            bool atTop = sv.VerticalOffset <= Tolerance;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - Tolerance;

            foreach (var target in _members)
            {
                if (ReferenceEquals(target, source)) continue;
                var tv = target.Viewer;
                double value;
                if (source.AlignmentKey != null && Equals(source.AlignmentKey, target.AlignmentKey))
                    value = sv.VerticalOffset;
                else if (atTop)
                    value = 0;
                else if (atBottom)
                    value = tv.ScrollableHeight;
                else
                    // A push still awaiting its layout pass hasn't reached tv.VerticalOffset yet —
                    // build on it, or rapid wheel ticks would each overwrite the previous one.
                    value = (target.ExpectedVertical ?? tv.VerticalOffset) + delta;
                PushVertical(target, value);
            }
        }

        private static void PushVertical(Member target, double value)
        {
            var tv = target.Viewer;
            double landing = Clamp(value, tv.ScrollableHeight);
            // Already there → no ScrollChanged will come, so recording an expectation would leave
            // it stale and swallow a later genuine scroll to this offset.
            if (Math.Abs(tv.VerticalOffset - landing) <= Tolerance && !target.ExpectedVertical.HasValue) return;
            target.ExpectedVertical = landing;
            tv.ScrollToVerticalOffset(landing);
        }

        private static void PushHorizontal(Member target, double value)
        {
            var tv = target.Viewer;
            double landing = Clamp(value, tv.ScrollableWidth);
            if (Math.Abs(tv.HorizontalOffset - landing) <= Tolerance && !target.ExpectedHorizontal.HasValue) return;
            target.ExpectedHorizontal = landing;
            tv.ScrollToHorizontalOffset(landing);
        }

        private static double Clamp(double value, double scrollable)
        {
            if (value < 0) return 0;
            if (scrollable < 0) scrollable = 0;
            return value > scrollable ? scrollable : value;
        }

        private Member Find(ScrollViewer viewer)
        {
            foreach (var m in _members)
                if (ReferenceEquals(m.Viewer, viewer)) return m;
            return null;
        }
    }
}
