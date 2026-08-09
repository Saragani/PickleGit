using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PickleGit.Controls;
using PickleGit.Models;
using PickleGit.ViewModels;

namespace PickleGit.Views
{
    public partial class MergeConflictEditorWindow : Window
    {
        private MergeConflictSessionViewModel _sessionVm;
        private MergeConflictFileViewModel _currentFileVm;

        // Scroll sync between the two Ours/Theirs pane ListViews — copies DiffView.xaml.cs's
        // exact mechanism (ApplyTemplate() before FindDescendant<ScrollViewer>, since neither
        // Loaded firing nor a same-priority deferred callback reliably guarantees the ListView's
        // ControlTemplate has been applied yet — see CLAUDE.md).
        private ScrollViewer _leftScroll, _rightScroll;
        private bool _syncingScroll;

        // Cross-line text selection (drag / Ctrl+click-extend / Ctrl+A / Ctrl+C) for copying —
        // purely a copy/paste convenience, with NO effect on which lines are Included. Picking a
        // line only ever happens via its own glyph click or the per-hunk/whole-file checkboxes.
        // See Controls/DiffTextSelectionController.cs (already used by the normal diff view).
        private DiffTextSelectionController _leftTextSelection, _rightTextSelection, _resultTextSelection;

        public MergeConflictEditorWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            // None of these rows' model text carries a leading diff-marker character (unlike
            // DiffView's git-diff Content strings) — every character is selectable from offset 0.
            _leftTextSelection = new DiffTextSelectionController(ConflictLeftListView, ConflictLeftTextSelectionOverlay, LeftPaneRowText, _ => 0);
            _rightTextSelection = new DiffTextSelectionController(ConflictRightListView, ConflictRightTextSelectionOverlay, RightPaneRowText, _ => 0);
            _resultTextSelection = new DiffTextSelectionController(ConflictResultListView, ConflictResultTextSelectionOverlay, ResultRowText, _ => 0);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MergeConflictSessionViewModel oldVm)
            {
                oldVm.RequestClose -= OnRequestClose;
                oldVm.PropertyChanged -= OnSessionVmPropertyChanged;
            }
            _sessionVm = e.NewValue as MergeConflictSessionViewModel;
            if (_sessionVm != null)
            {
                _sessionVm.RequestClose += OnRequestClose;
                _sessionVm.PropertyChanged += OnSessionVmPropertyChanged;
            }
            RewireFileVm(_sessionVm?.CurrentFile);
        }

        private void OnSessionVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MergeConflictSessionViewModel.CurrentFile))
                RewireFileVm(_sessionVm?.CurrentFile);
        }

        private void RewireFileVm(MergeConflictFileViewModel vm)
        {
            if (_currentFileVm != null) _currentFileVm.ScrollToBlockRequested -= OnScrollToBlockRequested;
            _currentFileVm = vm;
            if (_currentFileVm != null) _currentFileVm.ScrollToBlockRequested += OnScrollToBlockRequested;

            // Row indices are only meaningful within one file's PaneItems/ResultItems — switching
            // files must drop any selection from the previous file entirely.
            _leftTextSelection?.ClearSelection();
            _rightTextSelection?.ClearSelection();
            _resultTextSelection?.ClearSelection();
        }

        private void OnScrollToBlockRequested(MergeConflictBlock block)
        {
            var vm = _currentFileVm;
            if (vm == null) return;

            var paneItem = vm.PaneItems.FirstOrDefault(i =>
                i.Kind == ConflictPaneRowKind.BlockToolbar && i.BlockVm?.Block == block);
            if (paneItem != null)
            {
                ConflictLeftListView.ScrollIntoView(paneItem);
                ConflictRightListView.ScrollIntoView(paneItem);
            }

            var resultItem = vm.ResultItems.FirstOrDefault(i => i.BlockVm?.Block == block);
            if (resultItem != null) ConflictResultListView.ScrollIntoView(resultItem);
        }

        private void OnRequestClose(bool saved) => DialogResult = saved;

        private ScrollViewer _resultScroll;

        // All three panes scroll together — scrolling any one of Ours/Theirs/Result moves the
        // other two to match (vertically only; each pane's horizontal scroll stays independent,
        // since their content widths/gutters differ).
        private void ConflictLeftListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_leftScroll != null) return;
                ConflictLeftListView.ApplyTemplate();
                _leftScroll = FindDescendant<ScrollViewer>(ConflictLeftListView);
                if (_leftScroll != null)
                    _leftScroll.ScrollChanged += (s, ev) => { SyncScroll(_leftScroll, _rightScroll, _resultScroll); _leftTextSelection.Recompute(); };
                ConflictLeftListView.SizeChanged += (s, ev) => _leftTextSelection.Recompute();
            }), DispatcherPriority.Loaded);
        }

        private void ConflictRightListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_rightScroll != null) return;
                ConflictRightListView.ApplyTemplate();
                _rightScroll = FindDescendant<ScrollViewer>(ConflictRightListView);
                if (_rightScroll != null)
                    _rightScroll.ScrollChanged += (s, ev) => { SyncScroll(_rightScroll, _leftScroll, _resultScroll); _rightTextSelection.Recompute(); };
                ConflictRightListView.SizeChanged += (s, ev) => _rightTextSelection.Recompute();
            }), DispatcherPriority.Loaded);
        }

        private void ConflictResultListView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (_resultScroll != null) return;
                ConflictResultListView.ApplyTemplate();
                _resultScroll = FindDescendant<ScrollViewer>(ConflictResultListView);
                if (_resultScroll != null)
                    _resultScroll.ScrollChanged += (s, ev) => { SyncScroll(_resultScroll, _leftScroll, _rightScroll); _resultTextSelection.Recompute(); };
                ConflictResultListView.SizeChanged += (s, ev) => _resultTextSelection.Recompute();
            }), DispatcherPriority.Loaded);
        }

        // ScrollViewer.ScrollChanged does not fire synchronously inside ScrollToVerticalOffset —
        // it's delivered on a later dispatcher pass. Resetting _syncingScroll immediately after
        // issuing the target1/target2 offset changes therefore does NOT guard against them: by the
        // time their own ScrollChanged events actually arrive, the guard has long since gone back
        // to false, so each one re-enters SyncScroll for real and pushes an offset back onto the
        // other two panes. With three panes ping-ponging like that every auto-scroll tick, the
        // offsets race and visibly jump/oscillate instead of advancing smoothly. Fix: keep the
        // guard up until after a dispatcher pass has had a chance to deliver those deferred events.
        private void SyncScroll(ScrollViewer source, ScrollViewer target1, ScrollViewer target2)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            target1?.ScrollToVerticalOffset(source.VerticalOffset);
            target1?.ScrollToHorizontalOffset(source.HorizontalOffset);
            target2?.ScrollToVerticalOffset(source.VerticalOffset);
            target2?.ScrollToHorizontalOffset(source.HorizontalOffset);
            Dispatcher.BeginInvoke(new System.Action(() => _syncingScroll = false), DispatcherPriority.ContextIdle);
        }

        // ── Cross-line text selection (copy only) ───────────────────────────────────────────────
        // Row text sources per pane. BlockToolbar rows have no RowText element at all (see the
        // templates), so they simply never take part in selection — nothing special needed here.
        private static string LeftPaneRowText(object item)
        {
            var row = item as ConflictPaneItem;
            if (row == null) return string.Empty;
            switch (row.Kind)
            {
                case ConflictPaneRowKind.Context: return row.ContextText ?? string.Empty;
                case ConflictPaneRowKind.BlockBaseLine: return row.BaseLine?.Text ?? string.Empty;
                case ConflictPaneRowKind.BlockLine: return row.LeftLine?.Text ?? string.Empty;
                default: return string.Empty;
            }
        }

        private static string RightPaneRowText(object item)
        {
            var row = item as ConflictPaneItem;
            if (row == null) return string.Empty;
            switch (row.Kind)
            {
                case ConflictPaneRowKind.Context: return row.ContextText ?? string.Empty;
                case ConflictPaneRowKind.BlockBaseLine: return row.BaseLine?.Text ?? string.Empty;
                case ConflictPaneRowKind.BlockLine: return row.RightLine?.Text ?? string.Empty;
                default: return string.Empty;
            }
        }

        private static string ResultRowText(object item)
        {
            var row = item as ConflictResultItem;
            if (row == null) return string.Empty;
            switch (row.Kind)
            {
                case ConflictResultRowKind.Context:
                case ConflictResultRowKind.ResolvedBaseLine:
                case ConflictResultRowKind.DefaultBaseLine:
                    return row.LineText ?? string.Empty;
                case ConflictResultRowKind.ResolvedLine: return row.SourceLine?.Text ?? string.Empty;
                default: return string.Empty;
            }
        }

        private DiffTextSelectionController ResolvePane(ListView lv)
        {
            if (ReferenceEquals(lv, ConflictLeftListView)) return _leftTextSelection;
            if (ReferenceEquals(lv, ConflictRightListView)) return _rightTextSelection;
            return _resultTextSelection;
        }

        /// <summary>True when the hit-test result is the glyph circle (or something inside it) —
        /// its own MouseBinding handles the click; the row's text-selection logic below must not
        /// also treat that click as the start of a selection.</summary>
        private static bool IsWithinNamedElement(DependencyObject d, string name)
        {
            while (d != null)
            {
                if (d is FrameworkElement fe && fe.Name == name) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>True when the click landed on (or inside) a Button/CheckBox/any ToggleButton —
        /// e.g. a per-hunk "Select All Mine" checkbox, or the Use Base/Reset buttons, which all
        /// live inside a BlockToolbar row that has no "RowText" element of its own. Those clicks
        /// must always reach the control untouched; the content-click routing below only runs for
        /// rows with actual selectable text (see CLAUDE.md's identical lesson for the normal diff
        /// view's hunk-header buttons — a Preview handler that blocks selection can just as easily
        /// swallow a click before the control's own routed-event handling ever sees it).</summary>
        private static bool IsWithinButtonBase(DependencyObject d)
        {
            while (d != null)
            {
                if (d is ButtonBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private static ListViewItem ListViewItemFromPoint(ListView lv, Point p)
        {
            var element = lv.InputHitTest(p) as DependencyObject;
            while (element != null && !(element is ListViewItem))
            {
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }
            return element as ListViewItem;
        }

        private static bool TryGetRowTextForContentClick(ListView lv, ListViewItem container, Point positionInListView, out TextBlock rowText)
        {
            rowText = DiffTextSelectionController.FindNamedDescendant<TextBlock>(container, "RowText");
            if (rowText == null) return false;
            var rowTextLeft = rowText.TransformToAncestor(lv).Transform(new Point(0, 0)).X;
            return positionInListView.X >= rowTextLeft;
        }

        private void ConflictPaneListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var lv = (ListView)sender;
            var position = e.GetPosition(lv);
            var hit = lv.InputHitTest(position) as DependencyObject;
            if (IsWithinButtonBase(hit)) return; // Select All Mine/Theirs checkbox, Use Base, Reset — let it through untouched
            if (IsWithinNamedElement(hit, "GlyphCircle")) return; // its own MouseBinding handles this click

            var container = ListViewItemFromPoint(lv, position);
            if (container == null) return; // scrollbar, empty space — leave it alone

            if (!TryGetRowTextForContentClick(lv, container, position, out var rowText))
            {
                e.Handled = true; // clicked the gutter/glyph column but missed the glyph itself
                return;
            }

            var controller = ResolvePane(lv);
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var pointInText = lv.TranslatePoint(position, rowText);
                controller.ExtendTo(container, rowText, pointInText);
                e.Handled = true;
            }
            else
            {
                controller.BeginSelection(e, container, rowText); // sets e.Handled itself
            }
        }

        private void ConflictPaneListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            var controller = ResolvePane((ListView)sender);
            if (!controller.IsSelecting) return;
            controller.UpdateDrag(e);
        }

        private void ConflictPaneListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var controller = ResolvePane((ListView)sender);
            if (controller.IsSelecting) controller.EndSelection();
        }

        private void ConflictTextSelection_KeyDown(object sender, KeyEventArgs e)
        {
            var controller = ResolvePane((ListView)sender);
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (controller.TryCopySelection()) e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                controller.SelectAll();
                e.Handled = true;
            }
            else if ((e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                     && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                // Only Handled when a caret actually exists — otherwise leave arrow keys alone
                // rather than silently eating them for no visible effect.
                if (controller.MoveCaret(e.Key, extendSelection: Keyboard.Modifiers == ModifierKeys.Shift))
                    e.Handled = true;
            }
        }

        private void ConflictLeftCopySelection_Click(object sender, RoutedEventArgs e) => _leftTextSelection.TryCopySelection();

        private void ConflictResultListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset == 0)
            {

            }
        }

        private void ConflictRightCopySelection_Click(object sender, RoutedEventArgs e) => _rightTextSelection.TryCopySelection();
        private void ConflictResultCopySelection_Click(object sender, RoutedEventArgs e) => _resultTextSelection.TryCopySelection();

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
