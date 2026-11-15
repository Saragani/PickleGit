using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PickleGit.Controls
{
    /// <summary>Jump-to-row scrolling that leaves the target near the TOP of the viewport.
    ///
    /// ListView.ScrollIntoView scrolls the minimum distance: jumping downward lands the target row
    /// on the viewport's bottom edge, so whatever follows it (a conflict block's lines, a run of
    /// changed lines) stays off-screen — only jumping upward happens to show it. Used by the
    /// change-map click-to-jump and conflict navigation, where the point is to see the change.</summary>
    public static class ListViewScrollHelper
    {
        /// <summary>Scrolls <paramref name="item"/> to a few rows below the top of
        /// <paramref name="scrollViewer"/> (the ListView's own ScrollViewer).
        ///
        /// Done synchronously, before the next frame renders: the row is realized via the items
        /// host's BringIndexIntoViewPublic (ScrollIntoView would defer that to a Loaded-priority
        /// callback, after a render), laid out with UpdateLayout (no render in between), measured,
        /// and the final offset set — so only the end position is ever painted. Doing it in two
        /// dispatcher hops instead visibly flickers through the intermediate bottom-edge position.</summary>
        public static void ScrollToTop(ListView listView, ScrollViewer scrollViewer, object item)
        {
            if (listView == null || item == null) return;
            int index = listView.Items.IndexOf(item);
            if (index < 0) return;

            var host = FindItemsHost(listView);
            if (host == null || scrollViewer == null)
            {
                listView.ScrollIntoView(item);
                return;
            }

            host.BringIndexIntoViewPublic(index);
            listView.UpdateLayout();

            if (!(listView.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
                || !container.IsVisible)
                return;

            double y = container.TranslatePoint(new Point(0, 0), scrollViewer).Y;
            double context = Math.Min(scrollViewer.ViewportHeight * 0.25, 3 * container.ActualHeight);
            double target = scrollViewer.VerticalOffset + y - context;
            if (target < 0) target = 0;
            scrollViewer.ScrollToVerticalOffset(target);
        }

        private static VirtualizingPanel FindItemsHost(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is VirtualizingPanel panel && panel.IsItemsHost) return panel;
                var found = FindItemsHost(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
