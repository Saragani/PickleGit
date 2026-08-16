using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
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
            // isRowSelectable excludes rows with no real text of their own (the "Select All
            // Mine/Theirs" toolbar row, and the Result pane's unnumbered label/placeholder rows) —
            // without it, a selection spanning past one of these included it as a blank line in both
            // the highlight and any copied text, same class of issue DiffView's hunk headers had.
            _leftTextSelection = new DiffTextSelectionController(ConflictLeftListView, ConflictLeftTextSelectionOverlay, LeftPaneRowText, _ => 0,
                item => (item as ConflictPaneItem)?.Kind != ConflictPaneRowKind.BlockToolbar);
            _rightTextSelection = new DiffTextSelectionController(ConflictRightListView, ConflictRightTextSelectionOverlay, RightPaneRowText, _ => 0,
                item => (item as ConflictPaneItem)?.Kind != ConflictPaneRowKind.BlockToolbar);
            _resultTextSelection = new DiffTextSelectionController(ConflictResultListView, ConflictResultTextSelectionOverlay, ResultRowText, _ => 0,
                IsResultRowSelectable);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MergeConflictSessionViewModel oldVm)
            {
                oldVm.RequestClose -= OnRequestClose;
                oldVm.PropertyChanged -= OnSessionVmPropertyChanged;
                oldVm.ScrollToFindMatchRequested -= OnScrollToFindMatchRequested;
            }
            _sessionVm = e.NewValue as MergeConflictSessionViewModel;
            if (_sessionVm != null)
            {
                _sessionVm.RequestClose += OnRequestClose;
                _sessionVm.PropertyChanged += OnSessionVmPropertyChanged;
                _sessionVm.ScrollToFindMatchRequested += OnScrollToFindMatchRequested;
            }
            RewireFileVm(_sessionVm?.CurrentFile);
        }

        private void OnScrollToFindMatchRequested(object item, MergeConflictSessionViewModel.FindPane pane)
        {
            if (item == null) return;
            ListView lv;
            switch (pane)
            {
                case MergeConflictSessionViewModel.FindPane.Left: lv = ConflictLeftListView; break;
                case MergeConflictSessionViewModel.FindPane.Right: lv = ConflictRightListView; break;
                default: lv = ConflictResultListView; break;
            }
            lv.ScrollIntoView(item);
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

            // If the newly-current file is ALSO already in manual-edit mode, the edit box's
            // Visibility binding never flips (stays Visible across the switch), so
            // ConflictResultEditBox_IsVisibleChanged never fires to reseed it — do that here instead.
            if (vm != null && vm.IsManuallyEdited && ConflictResultEditBox.Visibility == Visibility.Visible)
            {
                ConflictResultEditBox.SyntaxHighlighting = ResolveHighlighting(vm.RelativePath);
                ConflictResultEditBox.Text = vm.ResultText ?? string.Empty;
            }

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

        /// <summary>Seeds the manual-edit AvalonEdit box from the current ResultText, picks a
        /// syntax-highlighting definition off the file's own extension, wires up the one-way
        /// text-changed sync back into the ViewModel (AvalonEdit's Text isn't a DependencyProperty,
        /// so there's no XAML binding to lean on the way the old plain TextBox had), and focuses it
        /// — all as soon as it's swapped in (see IsManuallyEdited in MergeConflictEditorViewModel) so
        /// the user can start typing immediately after clicking "Edit Manually" instead of having to
        /// click into it first. Unhooks TextChanged when hidden so RebuildResultText's own later
        /// writes to ResultText (once back in automatic mode) don't bounce off a stale handler.</summary>
        private void ConflictResultEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ConflictResultEditBox.TextChanged -= ConflictResultEditBox_TextChanged;
            if (e.NewValue is bool visible && visible)
            {
                var vm = _currentFileVm;
                if (vm != null)
                {
                    ConflictResultEditBox.SyntaxHighlighting = ResolveHighlighting(vm.RelativePath);
                    ConflictResultEditBox.Text = vm.ResultText ?? string.Empty;
                }
                ConflictResultEditBox.TextChanged += ConflictResultEditBox_TextChanged;
                Dispatcher.BeginInvoke(new System.Action(() => ConflictResultEditBox.Focus()), DispatcherPriority.Input);
            }
        }

        private void ConflictResultEditBox_TextChanged(object sender, EventArgs e)
        {
            if (_currentFileVm != null) _currentFileVm.ResultText = ConflictResultEditBox.Text;
        }

        // AvalonEdit's built-in highlighting definitions ship colors tuned for a light background —
        // readable enough on dark but visually inconsistent with the rest of the app's own hand-rolled
        // lexer (Services/Highlighting/SyntaxHighlighter.cs) and its colors (WordDiffHighlighter.
        // SyntaxBrushes). Re-tint each definition's named colors to match those exact values the
        // first time it's used; HighlightingManager.Instance caches definitions process-wide, so this
        // only needs to run once per definition, not once per keystroke or per file switch.
        private static readonly HashSet<string> _tunedHighlightingNames = new HashSet<string>();

        private static IHighlightingDefinition ResolveHighlighting(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            var ext = Path.GetExtension(relativePath);
            if (string.IsNullOrEmpty(ext)) return null;
            var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
            if (def != null && _tunedHighlightingNames.Add(def.Name))
                TuneForDarkTheme(def);
            return def;
        }

        private static void TuneForDarkTheme(IHighlightingDefinition def)
        {
            foreach (var color in def.NamedHighlightingColors)
            {
                var tint = ClassifyHighlightColorName(color.Name);
                if (tint.HasValue)
                {
                    color.Foreground = new SimpleHighlightingBrush(tint.Value);
                    continue;
                }

                // Verified by dumping every built-in definition this app can pick (.cs/.py/.xml/
                // .json/.css/.ps1/.md/.js): ClassifyHighlightColorName's patterns cover most common
                // categories, but each language also ships a handful of one-off names that don't
                // match anything recognizable and are dark enough to be nearly invisible against
                // this app's dark background — e.g. C#'s "MethodCall" (navy #191970, colors every
                // method call), XML's "XmlTag" (#8B008B, colors every <tag>), JSON/CSS's
                // "Punctuation"/"CurlyBraces"/"Colon" (literally black), JS's "Digits". Rather than
                // hand-naming every one of them (and whatever a language not checked here has),
                // lighten any leftover default color whose luminance is too low for dark-background
                // contrast — a general safety net instead of a name whitelist.
                var hex = color.Foreground?.ToString();
                if (string.IsNullOrEmpty(hex) || hex[0] != '#') continue;
                Color existing;
                try { existing = (Color)ColorConverter.ConvertFromString(hex); }
                catch (FormatException) { continue; }
                if (RelativeLuminance(existing) < 0.35)
                    color.Foreground = new SimpleHighlightingBrush(Lighten(existing));
            }
        }

        private static double RelativeLuminance(Color c) =>
            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        /// <summary>Blends toward white until the color clears the dark-background contrast
        /// threshold, preserving hue instead of collapsing every dark leftover color to one flat
        /// gray. Terminates in a handful of iterations for any input — each step strictly raises
        /// luminance toward white's 1.0, well above the 0.55 target.</summary>
        private static Color Lighten(Color c)
        {
            double r = c.R, g = c.G, b = c.B;
            while ((0.299 * r + 0.587 * g + 0.114 * b) / 255.0 < 0.55)
            {
                r += (255 - r) * 0.3;
                g += (255 - g) * 0.3;
                b += (255 - b) * 0.3;
            }
            // Color.FromRgb forces alpha to 255 — preserve the original alpha instead, so a
            // (currently hypothetical, no built-in highlighting color actually does this today)
            // semi-transparent foreground isn't silently made fully opaque by this correction.
            return Color.FromArgb(c.A, (byte)r, (byte)g, (byte)b);
        }

        // Mirrors WordDiffHighlighter.SyntaxBrushes' colors exactly, matched by AvalonEdit's own
        // highlighting-definition color names (e.g. the built-in C# XSHD's "Comment", "String",
        // "ReferenceTypeKeywords", "NumberLiteral", "Preprocessor" …) rather than TokenKind, since
        // AvalonEdit definitions don't share that enum. Names not matched here (Punctuation, etc.)
        // are left at AvalonEdit's own default.
        private static Color? ClassifyHighlightColorName(string name)
        {
            if (name.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0x6A, 0x99, 0x55);
            if (name.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Char", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0xCE, 0x91, 0x78);
            if (name.IndexOf("Number", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0xB5, 0xCE, 0xA8);
            if (name.IndexOf("Preprocessor", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0xC5, 0x86, 0xC0);
            if (name.IndexOf("Keyword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0x56, 0x9C, 0xD6);
            if (name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0)
                return Color.FromRgb(0x4E, 0xC9, 0xC0);
            return null;
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

        // ResolvedBaseLabel ("Resolved — used base") and Unresolved ("Unresolved conflict") have no
        // RowText of their own — same reasoning as excluding BlockToolbar above.
        private static bool IsResultRowSelectable(object item)
        {
            var kind = (item as ConflictResultItem)?.Kind;
            return kind != ConflictResultRowKind.ResolvedBaseLabel && kind != ConflictResultRowKind.Unresolved;
        }

        private DiffTextSelectionController ResolvePane(ListView lv)
        {
            if (ReferenceEquals(lv, ConflictLeftListView)) return _leftTextSelection;
            if (ReferenceEquals(lv, ConflictRightListView)) return _rightTextSelection;
            return _resultTextSelection;
        }

        /// <summary>The pane's own ScrollViewer, keyed the same way as <see cref="ResolvePane"/> —
        /// used so Page Up/Down scrolls exactly the pane that has focus. Scrolling it is enough to
        /// keep all three panes in sync: each one's ScrollChanged handler (wired in the *_Loaded
        /// methods above) already calls SyncScroll to move the other two to match, the same
        /// mechanism a mouse-driven scrollbar drag already goes through.</summary>
        private ScrollViewer ResolvePaneScrollViewer(ListView lv)
        {
            if (ReferenceEquals(lv, ConflictLeftListView)) return _leftScroll;
            if (ReferenceEquals(lv, ConflictRightListView)) return _rightScroll;
            return _resultScroll;
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
            else if (e.ClickCount == 2)
            {
                foreach (var other in new[] { _leftTextSelection, _rightTextSelection, _resultTextSelection })
                    if (!ReferenceEquals(other, controller)) other.ClearSelection();
                var pointInText = lv.TranslatePoint(position, rowText);
                controller.SelectWordAt(container, rowText, pointInText);
                e.Handled = true;
            }
            else
            {
                // Left/Right/Result are three independent DiffTextSelectionController instances, so
                // nothing stops all three from holding a selection at once by default — a single
                // logical selection that's on exactly one of them (never more) is what a normal text
                // editor's behavior would lead you to expect, and matches the same fix already
                // applied to DiffView's side-by-side panes. Starting a new one here drops whatever
                // was selected on the other two.
                foreach (var other in new[] { _leftTextSelection, _rightTextSelection, _resultTextSelection })
                    if (!ReferenceEquals(other, controller)) other.ClearSelection();
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
            else if ((e.Key == Key.PageUp || e.Key == Key.PageDown) && Keyboard.Modifiers == ModifierKeys.None)
            {
                var scrollViewer = ResolvePaneScrollViewer((ListView)sender);
                if (scrollViewer != null)
                {
                    if (e.Key == Key.PageUp) scrollViewer.PageUp(); else scrollViewer.PageDown();
                    e.Handled = true;
                }
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

        private void MergeConflictEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _sessionVm != null)
            {
                OpenFind();
                e.Handled = true;
            }
        }

        private void OpenFind()
        {
            if (_sessionVm == null) return;
            _sessionVm.IsFindOpen = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FindBox.Focus();
                FindBox.SelectAll();
            }), DispatcherPriority.Render);
        }

        private void FindToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionVm == null) return;
            if (_sessionVm.IsFindOpen) { _sessionVm.IsFindOpen = false; return; }
            OpenFind();
        }

        private void FindClose_Click(object sender, RoutedEventArgs e)
        {
            if (_sessionVm != null) _sessionVm.IsFindOpen = false;
        }

        private void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (_sessionVm == null) return;
            if (e.Key == Key.Escape)
            {
                _sessionVm.IsFindOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // FindBox's Text binding has Delay=150 (debounces recomputing matches on every
                // keystroke) — without forcing it to commit here, pressing Enter right after typing
                // (well within 150ms, easy when typing fast) would navigate against whatever FindText
                // still held from before this keystroke instead of what's actually in the box.
                FindBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (Keyboard.Modifiers == ModifierKeys.Shift) _sessionVm.PrevFindMatchCommand.Execute(null);
                else _sessionVm.NextFindMatchCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void ConflictResultChangeMap_JumpRequested(object sender, double fraction)
        {
            var lv = ConflictResultListView;
            if (lv.Items.Count == 0) return;
            int idx = (int)(fraction * lv.Items.Count);
            if (idx < 0) idx = 0;
            if (idx >= lv.Items.Count) idx = lv.Items.Count - 1;
            lv.ScrollIntoView(lv.Items[idx]);
        }

        private void ConflictLeftCopySelection_Click(object sender, RoutedEventArgs e) => _leftTextSelection.TryCopySelection();

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
