using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.ViewModels;

namespace PickleGit
{
    public partial class MainWindow : Window
    {
        private readonly AppViewModel _vm;
        private RepositoryViewModel _trackedTab;

        private void ApplyDarkTitleBar()
        {
            Services.TitleBarTheme.Apply(this, !App.IsLightTheme);
        }

        /// <summary>Rebuilds Window.InputBindings from Services/ShortcutManager.cs. Each KeyBinding's
        /// Command (and CommandParameter, where the action needs one) is a live data binding against
        /// the AppViewModel, so it keeps working across ActiveTab changes exactly like the static
        /// XAML bindings this replaced. Called at startup and whenever a shortcut is rebound in Settings.</summary>
        public void RebuildInputBindings()
        {
            InputBindings.Clear();
            foreach (var action in ShortcutManager.Actions)
            {
                var gestureText = ShortcutManager.GetGesture(action.Id);
                if (string.IsNullOrWhiteSpace(gestureText)) continue;
                KeyGesture gesture;
                try { gesture = (KeyGesture)new KeyGestureConverter().ConvertFromString(gestureText); }
                catch { continue; }

                var kb = new KeyBinding { Gesture = gesture };
                System.Windows.Data.BindingOperations.SetBinding(kb, InputBinding.CommandProperty,
                    new System.Windows.Data.Binding(action.CommandPath) { Source = _vm });
                if (action.CommandParameterPath != null)
                    System.Windows.Data.BindingOperations.SetBinding(kb, InputBinding.CommandParameterProperty,
                        new System.Windows.Data.Binding(action.CommandParameterPath) { Source = _vm });
                InputBindings.Add(kb);
            }
        }

        public MainWindow()
            : this(new AppViewModel())
        {
        }

        public MainWindow(AppViewModel viewModel)
        {
            _vm = viewModel;
            InitializeComponent();
            DataContext = _vm;
            RebuildInputBindings();
            _vm.CommitSearchRequested += OnCommitSearchRequested;
            _vm.CommitListFocusRequested += OnCommitListFocusRequested;
            RestoreWindowGeometry();
            Closing += (s, e) => SaveWindowGeometry();
            Loaded += (s, e) => ApplyDarkTitleBar();

            // Catch mouse-up anywhere on the window so a drag is always cleared
            PreviewMouseLeftButtonUp += (s, e) => EndTabDrag();

            // Track active tab changes to wire/unwire scroll events
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(AppViewModel.ActiveTab)) return;
                if (_trackedTab != null)
                {
                    _trackedTab.ScrollToNodeRequested -= OnScrollToNodeRequested;
                    _trackedTab.ScrollToDiffItemRequested -= OnScrollToDiffItemRequested;
                }
                _trackedTab = _vm.ActiveTab;
                if (_trackedTab != null)
                {
                    _trackedTab.ScrollToNodeRequested += OnScrollToNodeRequested;
                    _trackedTab.ScrollToDiffItemRequested += OnScrollToDiffItemRequested;
                }
            };

        }

        // ── Window geometry persistence ───────────────────────────────────────

        private void RestoreWindowGeometry()
        {
            var (left, top, width, height, maximized) = AppSettings.LoadWindowGeometry();
            // First run (or corrupt values): keep the XAML default, maximized
            if (width < 200 || height < 200 || double.IsNaN(left) || double.IsNaN(top))
            {
                WindowState = WindowState.Maximized;
                return;
            }
            // Ignore a saved position that's entirely off the current virtual screen
            // (e.g. a disconnected second monitor)
            var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (left + width < SystemParameters.VirtualScreenLeft + 40 || left > virtualRight - 40 ||
                top < SystemParameters.VirtualScreenTop - 10 || top > virtualBottom - 40)
            {
                WindowState = maximized ? WindowState.Maximized : WindowState.Normal;
                return;
            }
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left; Top = top; Width = width; Height = height;
            if (maximized) WindowState = WindowState.Maximized;
        }

        private void SaveWindowGeometry()
        {
            // When maximized, persist the restore bounds so un-maximizing after a restart
            // returns to the last normal size.
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            if (bounds.Width < 200 || bounds.Height < 200) return;
            AppSettings.SaveWindowGeometry(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized);
        }

        // ── Toolbar dropdown buttons (▼) open their ContextMenu on left-click ─

        private void DropdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                // ContextMenu is not in the visual tree — give it the window's DataContext
                btn.ContextMenu.DataContext = DataContext;
                btn.ContextMenu.IsOpen = true;
            }
        }

        // ── Find commits (default Ctrl+F, rebindable) → focus the commit filter ──

        private void OnCommitSearchRequested(object sender, EventArgs e)
        {
            var listView = FindVisualChildren<Views.CommitListView>(MainTabControl).FirstOrDefault();
            listView?.OpenSearch();
        }

        private void OnCommitListFocusRequested(object sender, EventArgs e)
        {
            var listView = FindVisualChildren<Views.CommitListView>(MainTabControl).FirstOrDefault();
            listView?.FocusList();
        }

        // ── Tab drag-and-drop live reordering ─────────────────────────────────

        private RepositoryViewModel _draggingTab;
        private bool _isDragging;
        private Point _dragStartPoint;
        private double _dragCursorTabOffset;
        private double _dragGhostY;

        public void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            if (sender is TabItem tab && tab.DataContext is RepositoryViewModel repo)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggingTab = repo;
                _isDragging = false;
                var tabOrigin = tab.TranslatePoint(new Point(0, 0), MainTabControl);
                _dragCursorTabOffset = e.GetPosition(MainTabControl).X - tabOrigin.X;
                // Y position of the tab in window coordinates (for ghost placement)
                _dragGhostY = tab.TranslatePoint(new Point(0, 0), (UIElement)Content).Y;
            }
        }

        public void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingTab == null || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(null);
            var diff = pos - _dragStartPoint;

            var srcIdx = _vm.Tabs.IndexOf(_draggingTab);
            if (srcIdx < 0) return;

            if (!_isDragging)
            {
                if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;
                _isDragging = true;

                // Hide real tab slot (empty gap), show ghost above it
                if (MainTabControl.ItemContainerGenerator.ContainerFromItem(_draggingTab) is TabItem draggedItem)
                    draggedItem.Opacity = 0;
                TabDragGhostLabel.Text = _draggingTab.RepoName;
                Canvas.SetTop(TabDragGhost, _dragGhostY);
                Canvas.SetLeft(TabDragGhost, e.GetPosition((UIElement)Content).X - _dragCursorTabOffset);
                TabDragGhost.Visibility = Visibility.Visible;
            }

            // Keep ghost following the cursor
            Canvas.SetLeft(TabDragGhost, e.GetPosition((UIElement)Content).X - _dragCursorTabOffset);

            // Use logical positions (cumulative ActualWidth) instead of TranslatePoint so that
            // displacement animations cannot interfere with the swap threshold.
            double impliedLeft = e.GetPosition(MainTabControl).X - _dragCursorTabOffset;

            // Swap left when implied left edge of dragged tab crosses left neighbor's left edge
            if (srcIdx > 0)
            {
                var leftItem = MainTabControl.ItemContainerGenerator.ContainerFromIndex(srcIdx - 1) as TabItem;
                if (leftItem != null)
                {
                    double leftNeighborLeft = GetTabLogicalLeft(srcIdx - 1);
                    if (impliedLeft < leftNeighborLeft)
                    {
                        double displacedWidth = leftItem.ActualWidth;
                        _vm.Tabs.Move(srcIdx, srcIdx - 1);
                        int displacedNewIdx = srcIdx;
                        Dispatcher.BeginInvoke(DispatcherPriority.Render,
                            new Action(() => AnimateTabSlide(displacedNewIdx, -displacedWidth)));
                        return;
                    }
                }
            }

            // Swap right when implied left edge of dragged tab crosses right neighbor's left edge
            if (srcIdx < _vm.Tabs.Count - 1)
            {
                var rightItem = MainTabControl.ItemContainerGenerator.ContainerFromIndex(srcIdx + 1) as TabItem;
                if (rightItem != null)
                {
                    double rightNeighborLeft = GetTabLogicalLeft(srcIdx + 1);
                    if (impliedLeft > rightNeighborLeft)
                    {
                        double displacedWidth = rightItem.ActualWidth;
                        _vm.Tabs.Move(srcIdx, srcIdx + 1);
                        int displacedNewIdx = srcIdx;
                        Dispatcher.BeginInvoke(DispatcherPriority.Render,
                            new Action(() => AnimateTabSlide(displacedNewIdx, displacedWidth)));
                        return;
                    }
                }
            }
        }

        private void AnimateTabSlide(int tabIdx, double fromX)
        {
            var tabItem = MainTabControl.ItemContainerGenerator.ContainerFromIndex(tabIdx) as TabItem;
            if (tabItem == null) return;
            var transform = new TranslateTransform(fromX, 0);
            tabItem.RenderTransform = transform;
            var anim = new DoubleAnimation(fromX, 0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, ev) => tabItem.RenderTransform = null;
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private double GetTabLogicalLeft(int idx)
        {
            double x = 0;
            for (int i = 0; i < idx; i++)
            {
                var item = MainTabControl.ItemContainerGenerator.ContainerFromIndex(i) as TabItem;
                x += item?.ActualWidth ?? 0;
            }
            return x;
        }

        public void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => EndTabDrag();

        private void EndTabDrag()
        {
            if (_isDragging)
            {
                if (_draggingTab != null &&
                    MainTabControl.ItemContainerGenerator.ContainerFromItem(_draggingTab) is TabItem draggedItem)
                    draggedItem.Opacity = 1;
                _vm.SaveSettings();
                TabDragGhost.Visibility = Visibility.Collapsed;
            }
            _draggingTab = null;
            _isDragging = false;
        }

        // ── Branch selection → commit scroll ─────────────────────────────────

        private void OnScrollToNodeRequested(object sender, GraphNode node)
        {
            // Find the commit ListView (items are GraphNode, not FileChange)
            foreach (var lv in FindVisualChildren<ListView>(MainTabControl))
            {
                if (lv.Items.Count > 0 && lv.Items[0] is GraphNode)
                {
                    lv.ScrollIntoView(node);
                    return;
                }
            }
        }

        private void OnScrollToDiffItemRequested(object sender, object item)
        {
            if (item == null) return;
            // The unified list and the side-by-side pair hold different item types; scroll every
            // ListView whose items match the requested item's type — for side-by-side that's both
            // the left and right panes (same underlying SideBySideItems collection), keeping them
            // aligned even before the scroll-sync ScrollChanged handlers would otherwise catch up.
            foreach (var lv in FindVisualChildren<ListView>(MainTabControl))
            {
                if (lv.Items.Count > 0 && lv.Items[0]?.GetType() == item.GetType())
                    lv.ScrollIntoView(item);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var grandchild in FindVisualChildren<T>(child))
                    yield return grandchild;
            }
        }
    }
}
