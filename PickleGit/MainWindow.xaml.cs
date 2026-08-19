using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        /// <summary>Seeds SidebarView/CommitDetailView's own Width (not their ColumnDefinition's —
        /// both columns are Auto, see MainWindow.xaml's own comment) from the persisted
        /// <see cref="AppViewModel.SidebarPaneWidth"/>/<see cref="AppViewModel.DetailPanelPaneWidth"/>
        /// once per fresh Grid instance (TabControl only realizes the active tab's DataTemplate, so
        /// this runs again every time a different tab becomes active). Nothing else ever needs to
        /// touch these two controls' Width in reaction to HasDetailPanel changing — an Auto column
        /// collapses to zero on its own the moment its (single) child's Visibility goes Collapsed,
        /// so there's no separate "width" state that can drift out of sync with "visibility" the
        /// way there was when columns 3/4 had their own bound Pixel widths.
        ///
        /// This and the four Thumb handlers below are this window's one deliberate exception to
        /// CLAUDE.md's "no code-behind logic" rule: SidebarView/CommitDetailView's Width has to be
        /// something a Thumb.DragDelta handler can set directly (see the handlers' own doc
        /// comments for why a {Binding} here specifically doesn't mix safely with an interactive
        /// drag), and a DragDelta handler is inherently code-behind — there's no bindable command
        /// for "the mouse moved by this many pixels while a button is held."</summary>
        private void TabContentColumnsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Grid grid)) return;
            var sidebar = grid.Children.OfType<Views.SidebarView>().FirstOrDefault();
            if (sidebar != null) sidebar.Width = _vm.SidebarPaneWidth;
            else AppLog.Warn("TabContentColumnsGrid_Loaded: SidebarView not found — sidebar width not seeded");
            var detail = grid.Children.OfType<Views.CommitDetailView>().FirstOrDefault();
            if (detail != null) detail.Width = _vm.DetailPanelPaneWidth;
            else AppLog.Warn("TabContentColumnsGrid_Loaded: CommitDetailView not found — detail panel width not seeded");
        }

        /// <summary>Live-resizes <typeparamref name="T"/> (SidebarView or CommitDetailView) while
        /// dragging its neighboring Thumb — a plain Thumb, not GridSplitter: see MainWindow.xaml's
        /// own comment on why GridSplitter's built-in column-resize behavior doesn't mix safely
        /// with this shell's layout. A Thumb has no resize logic of its own at all, so this (and
        /// <see cref="PersistPaneWidth{T}"/>) are the only things that ever set either control's
        /// Width. <paramref name="sign"/> is +1 for a pane to the Thumb's left (dragging right
        /// grows it) or -1 for one to its right (dragging right shrinks it).</summary>
        private static void ResizePaneOnDrag<T>(object sender, double horizontalChange, double sign, double min, double max)
            where T : FrameworkElement
        {
            if (!(sender is FrameworkElement el) || !(el.Parent is Grid grid)) return;
            var pane = grid.Children.OfType<T>().FirstOrDefault();
            if (pane == null) return;
            var current = double.IsNaN(pane.Width) ? pane.ActualWidth : pane.Width;
            pane.Width = Math.Max(min, Math.Min(max, current + sign * horizontalChange));
        }

        /// <summary>Persists <typeparamref name="T"/>'s current Width once its drag actually
        /// finishes, via <paramref name="save"/> — <see cref="AppViewModel.SidebarPaneWidth"/> or
        /// <see cref="AppViewModel.DetailPanelPaneWidth"/>, each of which clamps and writes to
        /// settings.json on its own.</summary>
        private static void PersistPaneWidth<T>(object sender, Action<double> save) where T : FrameworkElement
        {
            if (!(sender is FrameworkElement el) || !(el.Parent is Grid grid)) return;
            var pane = grid.Children.OfType<T>().FirstOrDefault();
            if (pane != null) save(pane.Width);
        }

        private void SidebarThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) =>
            ResizePaneOnDrag<Views.SidebarView>(sender, e.HorizontalChange, +1,
                AppViewModel.MinSidebarPaneWidth, AppViewModel.MaxSidebarPaneWidth);

        private void SidebarThumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            PersistPaneWidth<Views.SidebarView>(sender, w => _vm.SidebarPaneWidth = w);

        private void DetailPanelThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) =>
            ResizePaneOnDrag<Views.CommitDetailView>(sender, e.HorizontalChange, -1,
                AppViewModel.MinDetailPanelPaneWidth, AppViewModel.MaxDetailPanelPaneWidth);

        private void DetailPanelThumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            PersistPaneWidth<Views.CommitDetailView>(sender, w => _vm.DetailPanelPaneWidth = w);

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
            Loaded += (s, e) => SetupTabScrollArrows();

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

                    // The TabControl doesn't keep an inactive tab's visual tree alive — switching
                    // back rebuilds a brand-new CommitListView bound to the same (persisted)
                    // RepositoryViewModel. SelectedNode/SelectedNodes survive that (they're plain VM
                    // state), and the multi-select behavior re-syncs the highlight on attach, but the
                    // freshly-realized ListView still starts scrolled to the top — bring the already-
                    // selected commit back into view, same as picking a branch/tag does.
                    var restoreNode = _trackedTab.SelectedNode;
                    if (restoreNode != null)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                        {
                            MainTabControl.UpdateLayout();
                            OnScrollToNodeRequested(this, restoreNode);
                        }));
                    }
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

        // ── Tab strip scroll arrows: only shown when the tabs actually overflow ─

        private ScrollViewer _tabScroller;
        private Button _tabScrollLeftButton;
        private Button _tabScrollRightButton;

        private void SetupTabScrollArrows()
        {
            // ApplyTemplate() forces the DarkTabControl style's ControlTemplate to be built
            // immediately, so Template.FindName can locate its named parts right away.
            MainTabControl.ApplyTemplate();
            _tabScroller = MainTabControl.Template.FindName("PART_TabScroller", MainTabControl) as ScrollViewer;
            _tabScrollLeftButton = MainTabControl.Template.FindName("TabScrollLeftButton", MainTabControl) as Button;
            _tabScrollRightButton = MainTabControl.Template.FindName("TabScrollRightButton", MainTabControl) as Button;
            if (_tabScroller == null) return;

            // ScrollViewer.ScrollableWidth isn't a DependencyProperty, so whether the tab strip
            // overflows can't be expressed as a XAML binding — ScrollChanged fires both on user
            // scroll and whenever Extent/Viewport change (tab added/removed, window resized).
            _tabScroller.ScrollChanged += (s, e) => UpdateTabScrollArrows();
            UpdateTabScrollArrows();
        }

        private void UpdateTabScrollArrows()
        {
            if (_tabScroller == null) return;
            var visibility = _tabScroller.ScrollableWidth > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            if (_tabScrollLeftButton != null) _tabScrollLeftButton.Visibility = visibility;
            if (_tabScrollRightButton != null) _tabScrollRightButton.Visibility = visibility;
        }

        // Fires for the TabControl itself AND bubbles up from any Selector inside a tab's own
        // content (ComboBoxes, ListViews, ...) — OriginalSource narrows it down to the former,
        // so picking a sort-mode dropdown doesn't also re-scroll the tab strip.
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != MainTabControl) return;
            ScrollActiveTabIntoView();
        }

        private void ScrollActiveTabIntoView()
        {
            // A newly-added tab's container may not exist yet when the selection change fires;
            // deferring to Loaded priority and forcing UpdateLayout gives the generator a chance
            // to realize it first (see the ApplyTemplate/Loaded timing note in CLAUDE.md).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var activeTab = _vm.ActiveTab;
                if (activeTab == null) return;
                MainTabControl.UpdateLayout();
                if (MainTabControl.ItemContainerGenerator.ContainerFromItem(activeTab) is TabItem item)
                    item.BringIntoView();
            }));
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
