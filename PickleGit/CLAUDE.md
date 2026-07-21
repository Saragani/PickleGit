# PickleGit — Claude Code Guide

## Project Overview

WPF Git client built on .NET Framework 4.7.2. Assembly name: **PickleGit**.

## Build & Run

```
# Build
msbuild PickleGit.csproj /p:Configuration=Debug

# Or open in Visual Studio 2022 and press F5
```

**Target:** x64, .NET 4.7.2, C# 7.3, WinExe output.

## Architecture

**Pattern:** MVVM — no code-behind logic, everything through bindings + commands.

```
App.xaml / App.xaml.cs          — startup, Application.Resources (all converters live here)
MainWindow.xaml                 — shell: toolbar, tab control, status bar
ViewModels/AppViewModel.cs      — tab management, column visibility settings
ViewModels/RepositoryViewModel.cs — per-tab state: commits, branches, diff, staging
ViewModels/BranchNodeViewModel.cs — hierarchical branch tree nodes
Views/SidebarView.xaml          — branches / tags / stashes / remotes panel
Views/CommitListView.xaml       — commit list with graph column
Views/CommitDetailView.xaml     — commit info + file list (right panel)
Views/DiffView.xaml             — diff hunk viewer (bottom panel)
Services/GitService.cs          — all LibGit2Sharp calls
Services/AppSettings.cs         — JSON settings in %APPDATA%\PickleGit\settings.json
Services/CredentialStore.cs     — Windows Credential Manager (advapi32.dll)
Models/                         — plain data classes: CommitInfo, BranchInfo, GraphNode, etc.
Converters/ValueConverters.cs   — all IValueConverters + ShowHideWidthConverter (IMultiValueConverter)
Behaviors/ListViewMultiSelectBehavior.cs — syncs ListView.SelectedItems ↔ VM collection
Controls/CommitGraphControl.cs  — custom DrawingVisual-based graph renderer
Themes/DarkTheme.xaml           — dark palette + base styles (merged into App.xaml)
```

## Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| LibGit2Sharp | 0.27.2 | Git operations (no git.exe required) |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.77 | XAML behavior attachments |
| Newtonsoft.Json | 13.0.3 | Settings + commit cache serialization |

## Important Conventions

### Converters — always in App.xaml
All converters are declared as `Application.Resources` in **App.xaml**, not in any Window or UserControl. `StaticResource` in UserControls resolves at XAML parse time before the control is in the visual tree, so `Window.Resources` is inaccessible. Never move converters back to `MainWindow.xaml`.

### ContextMenu bindings
ContextMenus are not in the visual tree. To conditionally suppress a context menu, use a `DataTrigger` on the **parent Grid** that sets `ContextMenu="{x:Null}"`. Do not bind `Visibility` on the ContextMenu element itself — `RelativeSource Self` there resolves to the ContextMenu, not the DataContext.

### Commands on ContextMenu items
Use `PlacementTarget.Tag` to pass the ViewModel reference through ContextMenu bindings:
```xml
<Grid Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=TreeView}}">
    <Grid.ContextMenu>
        <ContextMenu>
            <MenuItem Command="{Binding PlacementTarget.Tag.SomeCommand,
                                RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext.SomeProperty,
                                RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
        </ContextMenu>
    </Grid.ContextMenu>
</Grid>
```

### Smart branch visibility
Branch membership comes from `CommitInfo.RefMask`, computed during the **single** history walk in `GitService.GetHistory` (bit 0 = reachable from current branch/HEAD; bits 1–63 = other branch tips). The smart filter is `(c.RefMask & CommitHistory.CurrentBranchMask) != 0` — never add a second history walk for branch membership. Masks are always computed so toggling the filter is instant.

### Async pattern & threading
Long git operations go in `Task`-returning methods. Set `IsBusy = true` at start, `false` in a `finally` block. Progress messages go through `StatusMessage`.

**All LibGit2Sharp calls run on `GitService.Executor`** (`Services/Git/GitExecutor.cs`, a single dedicated thread) — never on the UI thread and never via bare `Task.Run` (the `Repository` object is not thread-safe). `RepositoryViewModel.RunAsync` already routes through it; ad-hoc reads use `await _git.Executor.RunAsync(() => _git.Xxx())`. Work items must never synchronously wait on the Dispatcher; `Dispatcher.Invoke` from a work item is tolerable only because the UI thread always awaits (never blocks on) executor tasks.

### Hybrid git backend
`GitService` (LibGit2Sharp) is the single entry point for reads/status/index ops. Operations LibGit2Sharp can't do (rebase, pull --rebase, hunk staging via `git apply`, SSH, GPG) go through `GitService.Cli` (`Services/Git/CliGitService.cs` → `GitCli.cs` process runner). Check `Cli.IsAvailable` and degrade gracefully when git.exe is missing. **After any CLI op that mutates refs/index, call `GitService.Reopen()`** — libgit2 caches ref state.

### External-change refresh
`Services/RepositoryWatcher.cs` (FileSystemWatcher on workdir + .git, 400 ms debounce, `Refs` vs `WorkingDir` classification) triggers refreshes; app-initiated ops are wrapped in `watcher.Suppress()` scopes (done inside `RunAsync`). A 5-minute `DispatcherTimer` remains only as a failsafe for network drives. `RefreshAsync` computes a state signature and **skips the graph/UI/cache rebuild when nothing changed** — don't remove the signature check; rebuilding GraphNodes resets scroll/selection.

### Dialogs
Use `Services/DialogService.cs` (`Prompt` / `Confirm` / `ShowError` → themed windows in `Views/Dialogs/`). Never use `MessageBox.Show` or VB `InputBox` in new code (the Microsoft.VisualBasic reference was removed).

### Multi-select commits
`SelectedNodes` is an `ObservableCollection<CommitNode>` synced via `ListViewMultiSelectBehavior`. When `IsMultiSelection` is true, show aggregated file list (`AggregatedFiles`) instead of single-commit files.

## Namespaces

```
PickleGit                — App, MainWindow
PickleGit.ViewModels     — *ViewModel, RelayCommand
PickleGit.Models         — CommitInfo, BranchInfo, GraphNode, FileChange, DiffHunk, …
PickleGit.Services       — GitService, AppSettings, CredentialStore, ShellFolderPicker
PickleGit.Views          — *View, *Dialog UserControls
PickleGit.Converters     — all IValueConverter implementations
PickleGit.Behaviors      — ListViewMultiSelectBehavior
PickleGit.Controls       — CommitGraphControl
```

## Settings & Cache

- Settings file: `%APPDATA%\PickleGit\settings.json`
- Commit cache: `%APPDATA%\PickleGit\cache\<hash>.json` (keyed by repo path)
- Credentials: Windows Credential Manager under target prefix `PickleGit:`

## CommitListView Layout

### Last visible column fill
CommitListView does not use a true WPF star column for the commit list. The header row and list items can have different available widths, so a star column can land at different x-positions and break alignment.

Instead, `CommitListView.xaml.cs` tracks the ListView ScrollViewer viewport width and stores it on `AppViewModel.CommitListViewportWidth`. The `EffectiveColWidth*` properties return fixed `GridLength` values for both the header grid and item grid: hidden columns are `0`, normal visible columns use their saved user width, and the last visible column is set to `max(columnMinWidth, viewportWidth - otherVisibleColumnWidths)`. If the earlier visible columns leave less than the last column's minimum width, horizontal scrolling is expected.

### Header scroll sync
The header row (Grid row 0 in CommitListView.xaml) is outside the ListView's ScrollViewer. Syncing is done in code-behind:
- The header's column cells are wrapped in `<ScrollViewer x:Name="HeaderScroller" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Disabled">`. The gear button sits in a fixed 22px column outside that ScrollViewer.
- `OnLoaded` finds the ListView's internal ScrollViewer via `FindDescendant<ScrollViewer>(CommitList)` and subscribes to `ScrollChanged`.
- `OnListScrollChanged` calls `HeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset)` whenever `e.HorizontalChange != 0`.

### Date display
The date/time column binds to `Commit.AuthorDate` through `Converters.DateFormatConverter` (`{StaticResource DateFmt}`), not a literal `StringFormat` — this lets the Settings → UI date-format preference apply without rebinding. `DateFormatConverter.CurrentFormat` is a static field set once at startup and whenever the setting changes (avoids a settings-file read per row render); default is `yyyy-MM-dd HH:mm`. The `AuthorDateRelative` property exists on `CommitInfo` but is not used in CommitListView.

## Dark Theme — WPF Pitfalls

### Theme switching is live — brush bindings must use DynamicResource, not StaticResource
`App.ApplyTheme(string theme)` (`App.xaml.cs`) swaps the merged `Themes/Palette{theme}.xaml` dictionary at runtime (removes the old one by matching its `Source` path, inserts the new one at index 0) so Settings → UI → Theme applies immediately with no restart. This only works because every brush reference throughout the view XAML (`MainWindow.xaml`, every `Views/*.xaml`) uses `{DynamicResource XxxBrush}`. `StaticResource` resolves once at load time and never reacts to a later dictionary swap — a single `StaticResource` brush reference anywhere is enough to leave that one element frozen on the old theme (seen as dark-on-dark or light-on-light low-contrast text after a switch). **Any new brush reference added to view XAML must be `DynamicResource`.** `StaticResource` remains correct for non-theme resources (Styles, Converters, StaticResource-only things like `FontFamily`).

`App.ApplyTheme` also re-applies the DWM dark-title-bar attribute (`Services/TitleBarTheme.cs`) to every currently open `Application.Current.Windows`, since that's a native window attribute, not a resource — it doesn't react to the dictionary swap at all.

### A Style-derived Foreground override can silently fail to reach Button/ComboBoxItem content
`AccentButton`/`DangerButton` (`BasedOn="{StaticResource ToolbarButton}"`) override `Foreground` to `White` so light-on-dark buttons (Close, OK, Create, Commit, Stage All, …) stay readable against their purple/red/green background in both themes. The base `ToolbarButton` template originally rendered plain-string `Content` via a bare `<ContentPresenter/>`, relying on ordinary property-value inheritance to carry the overridden `Foreground` down into the auto-generated content. **It didn't work** — verified by setting the override to an unmistakable diagnostic color (`Lime`) and sampling actual rendered pixel values (not just eyeballing a screenshot, which is easy to misjudge — a genuinely dark near-black color can look "close enough" to white at a glance): the diagnostic color never appeared, even after a full (non-incremental) rebuild, ruling out a stale-build issue. Root cause not fully identified (`TextElement.Foreground` set explicitly on the `ContentPresenter` — same DP that `Control.Foreground`/`TextBlock.Foreground` add-own — *also* didn't fix it, ruling out simple inheritance).

First fix attempt — replacing the bare `ContentPresenter` with `<TextBlock Text="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}"/>` — worked for plain-string `Content` but **broke any button whose `Content` is a hand-built element** (e.g. the bulk-discard button's icon+badge `Grid`): `TextBlock.Text` bound to a non-string `Content` via `TemplateBinding` just calls `.ToString()` on it, rendering nothing useful, and the button collapses to a sliver since there's no real text. The actual fix: keep a plain `ContentPresenter` (so it can host *any* `Content`), and give it a nested implicit `DataTemplate` keyed `DataType="{x:Type sys:String}"` (needs `xmlns:sys="clr-namespace:System;assembly=mscorlib"`) that renders the string via a `TextBlock` bound to the Foreground through `RelativeSource AncestorType=Button` (NOT `TemplateBinding` — a nested `DataTemplate` is a separate templating context, so `TemplateBinding` doesn't reach through it). This DataTemplate only engages when `Content` is literally a `System.String`; anything else (a `Grid`, etc.) renders through the `ContentPresenter` untouched, coloring itself explicitly. Applied to both `ToolbarButton`'s and `ComboBoxItem`'s templates (the latter via `RelativeSource AncestorType=ComboBoxItem`).

**When verifying a text-color fix, sample actual pixel RGB values (`Bitmap.GetPixel` after `CopyFromScreen`), not just a screenshot glance** — this bug was reported fixed twice on visual inspection before pixel sampling proved the color hadn't changed at all.

### TemplateBinding cannot target an attached property inside a Setter.Value
An attached DP (e.g. a custom `ButtonChrome.HoverBackground`, used to let `ToolbarButton`-based styles each specify their own hover tint) can be set fine as a plain `Setter Property="ctrl:ButtonChrome.HoverBackground"` — but referencing it as `Value="{TemplateBinding ctrl:ButtonChrome.HoverBackground}"` inside a `ControlTemplate.Triggers` `Setter` throws at load time: `XamlParseException: 'Set property 'System.Windows.Setter.Value' threw an exception.' ... Expression type is not a valid Style value.` `TemplateBinding` only reliably supports plain (non-attached) dependency properties as its source. Fix: use the standard workaround for binding to an attached property from inside a template — `{Binding Path=(ctrl:ButtonChrome.HoverBackground), RelativeSource={RelativeSource TemplatedParent}}` — which behaves identically at runtime but doesn't hit the `TemplateBinding` restriction.

This is a nasty one to debug live: the crash happens during `App.OnStartup`, before any window is shown, and if the app's own `DispatcherUnhandledException` handler tries to show the error via a themed `ErrorDialog` that itself merges the same broken theme dictionary, constructing *that* dialog throws a second, unhandled exception that kills the process with no error UI at all — the original exception's message is only visible by launching the exe directly from a console instead of via the app's own dialog-based error path.

### RelayCommand's CanExecute can visually lag after a bulk property change
`RelayCommand.CanExecuteChanged` is wired to `CommandManager.RequerySuggested` (`BaseViewModel.cs`), which WPF re-raises on common input events (mouse move, keyboard, focus change) — not immediately when an arbitrary bound property changes in code. Reassigning `WorkingDirFiles`/`StagedFiles` (e.g. `ApplyWorkingDirStatus`) can leave a colored button (`SuccessButton`/`DangerButton`, driven by `IsEnabled=False → Opacity 0.35`) looking disabled for a moment even though `CanExecute` would already return `true` — confirmed by clicking it anyway: the action fired correctly despite the dim appearance, since WPF re-checks `CanExecute` synchronously right before `Execute`. Fix: call `CommandManager.InvalidateRequerySuggested()` right after reassigning the lists so the visual state updates immediately rather than waiting for incidental input.

### ScrollBar custom template
The dark ScrollBar style handles both orientations via `Style.Triggers`. The two orientations differ in:
- Vertical: `Width="8"`, `IsDirectionReversed="True"`, `PageUpCommand`/`PageDownCommand`
- Horizontal trigger: `Width="Auto"`, `Height="8"`, `IsDirectionReversed="False"`, `PageLeftCommand`/`PageRightCommand`

Setting `Width="8"` on a ScrollBar constrains its **length** (not thickness) when horizontal, making it invisible/dysfunctional. The trigger overrides Width/Height for the horizontal case.

The Track's `DecreaseRepeatButton` and `IncreaseRepeatButton` **must** have explicit `Command` attributes (`ScrollBar.PageUpCommand` etc.) — the Track element itself does not subscribe to mouse clicks on those areas; only the RepeatButton command bindings fire.

### ListView dark corner rectangle
WPF's default ListView fills the bottom-right corner (between the two scrollbars) with `SystemColors.ControlBrush` (white). The `DarkListView` style includes a full `ControlTemplate` with a custom `ScrollViewer.Template` that uses a `Rectangle Fill="{StaticResource BackgroundBrush}"` in that corner. Any ListView that needs horizontal scrolling uses this style (and sets `ScrollViewer.HorizontalScrollBarVisibility="Auto"`).

### Separator in ContextMenu
WPF's `MenuBase.PrepareContainerForItemOverride` resolves `MenuItem.SeparatorStyleKey` from resources and applies that style to every `<Separator/>` inside a menu — **overriding** any implicit `<Style TargetType="Separator">`. The WPF Aero theme default for that key has `Margin="30,2,2,2"`, which indents the separator. To get a full-width separator in context menus, define both:
```xml
<!-- for standalone Separators -->
<Style TargetType="Separator"> ... </Style>
<!-- for Separators inside ContextMenu / Menu -->
<Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
    <Setter Property="Margin" Value="0,2"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Separator">
                <Rectangle Height="1" Fill="{StaticResource BorderBrush}"/>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

### Implicit TreeViewItem style applies to ALL TreeViewItems
`<Style TargetType="TreeViewItem" BasedOn="{StaticResource DarkTreeViewItem}"/>` (no `x:Key`) is applied to **every** TreeViewItem in the app — including static section-header items (LOCAL BRANCHES, REMOTE BRANCHES, etc.) whose `DataContext` is `RepositoryViewModel`, not `BranchNodeViewModel`. Any binding in that style (e.g. `IsExpanded`, `IsHead`) must resolve on every possible DataContext it might encounter.

**Fix pattern:** Remove DataContext-specific bindings from the global implicit style. Put them in a named style (`BranchTreeViewItemStyle`) that is applied explicitly only where appropriate. For bindings that must stay in the global style (e.g. a `DataTrigger` on `IsHead`), add a stub property with `FallbackValue=False` to `RepositoryViewModel` so the binding resolves without a warning:
```csharp
// In RepositoryViewModel — prevents binding warning when implicit DarkTreeViewItem
// style is applied to section-header TreeViewItems
public bool IsHead => false;
```

### HierarchicalDataTemplate.ItemContainerStyle scope
`HierarchicalDataTemplate.ItemContainerStyle` applies to the **children of items that use the template** (grandchildren of the section header), NOT to the direct children of the section header. To cover level-1 nodes (direct children of the section TreeViewItem), set `ItemContainerStyle` on the section TreeViewItem itself:
```xml
<!-- ItemContainerStyle here covers level-1 branch nodes -->
<TreeViewItem Header="LOCAL BRANCHES"
              ItemContainerStyle="{StaticResource BranchTreeViewItemStyle}">
    <TreeViewItem.ItemTemplate>
        <!-- ItemContainerStyle here covers level-2+ (grandchildren) -->
        <HierarchicalDataTemplate ItemsSource="{Binding Children}"
                                  ItemContainerStyle="{StaticResource BranchTreeViewItemStyle}">
            ...
        </HierarchicalDataTemplate>
    </TreeViewItem.ItemTemplate>
</TreeViewItem>
```
Omitting the `ItemContainerStyle` on the section TreeViewItem means level-1 nodes fall back to the implicit style, losing any extra bindings (like `IsExpanded TwoWay`) that the named style adds.

## Tab Drag-and-Drop Live Reordering

### Overview
Tabs reorder live via mouse drag. A ghost `Border` floats on a `Canvas` overlay (`Panel.ZIndex=100`) at the cursor position. The real tab slot is hidden (`Opacity=0`) to leave an empty gap. On mouse-up the ghost collapses and `Opacity` is restored.

### Ghost element
Defined in `MainWindow.xaml` inside a `Canvas` spanning all grid rows:
```xml
<Canvas x:Name="TabDragCanvas" Panel.ZIndex="100" IsHitTestVisible="False" Grid.RowSpan="3">
    <Border x:Name="TabDragGhost" Visibility="Collapsed" ...>
        <StackPanel Orientation="Horizontal">
            <TextBlock x:Name="TabDragGhostLabel"/>  <!-- repo name set in code-behind -->
            <TextBlock Text="✕" .../>                <!-- static ✕ always present -->
        </StackPanel>
    </Border>
</Canvas>
```

### Logical positions vs TranslatePoint
**Never use `TranslatePoint` to determine tab positions during drag.** The displaced-tab slide animation applies a `TranslateTransform` (via `RenderTransform`) that `TranslatePoint` includes in the returned coordinates. During the 150 ms animation the displaced tab reports a visual position that may re-cross the swap threshold, causing a re-swap (flicker loop).

Use `GetTabLogicalLeft(idx)` instead — it sums `ActualWidth` of all preceding tabs. `ActualWidth` is unaffected by `RenderTransform`:
```csharp
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
```

### Swap triggers
Compare the **implied left edge** of the dragged tab (`cursorX - _dragCursorTabOffset`) against the logical left edge of the neighbor:
- Swap left when `impliedLeft < leftNeighborLogicalLeft`
- Swap right when `impliedLeft > rightNeighborLogicalLeft`

After `_vm.Tabs.Move()`, use `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` to wait one render pass before calling `AnimateTabSlide`, so the displaced tab's container is at its new index before the animation starts.

### Do not set ActiveTab during swaps
Calling `_vm.ActiveTab = _draggingTab` during a swap triggers `EnsureLoadedAsync` → `IsBusy=true`, which hides the ✕ button. Only the final drop (or explicit user click) should change the active tab.

## Performance

### Ahead/behind is an uncached divergence walk — cache it per branch by tip SHA
`GitService.GetBranches()` reads `Branch.TrackingDetails.AheadBy`/`BehindBy` for every local tracking branch. LibGit2Sharp does **not** cache this — each access is a fresh `git_graph_ahead_behind` walk from the branch tip to its upstream tip back to their merge-base, and `GetBranches()` runs on every refresh, including the ones `RepositoryWatcher`'s debounce/failsafe timer triggers automatically with no user action. On a long-diverged branch in a large repo this walk alone can take seconds, and it reruns even when nothing about that branch changed. Fixed by caching `(AheadBy, BehindBy)` per branch keyed on `(localTipSha, upstreamTipSha)` (`GitService._aheadBehindCache`) — skip the LibGit2Sharp call entirely when both SHAs match the last computed value; only a real branch/upstream move triggers a recompute. Cache is cleared in `TryOpen` (new repo path).

### Freeze brushes, pens, and geometries used in OnRender
WPF's `DrawingContext` methods are fastest when passed frozen objects — frozen objects bypass change-notification infrastructure and allow the render thread to use them without locking. In `OnRender` overrides (e.g. `CommitGraphCell`, `RefLabel`), **never** allocate `new SolidColorBrush(...)` or `new Pen(...)` per call. Instead:

- Cache as `private static readonly` fields, or
- Use a `static Dictionary<Color, Pen>` for dynamic colors, frozen at creation time:

```csharp
private static readonly Dictionary<Color, Pen> s_penCache = new Dictionary<Color, Pen>();
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
```

Always call `geo.Freeze()` on a `StreamGeometry` after closing its context — frozen geometries render faster.

Also: if `MeasureOverride` and `OnRender` both call the same expensive computation (e.g. `new FormattedText(...)`), cache the result in an instance field keyed on the input so the two layout passes share the same object.

### TreeView virtualization requires the control to own its scroll
WPF's `VirtualizingStackPanel` only virtualizes items when the `ItemsControl` owns its own internal `ScrollViewer`. If you wrap a `TreeView` in an outer `ScrollViewer` and set `ScrollViewer.VerticalScrollBarVisibility="Disabled"` on the TreeView, the VSP cannot virtualize — all items are always in the visual tree.

Fix: let the TreeView scroll itself and add:
```xml
<Setter Property="ScrollViewer.VerticalScrollBarVisibility"   Value="Auto"/>
<Setter Property="VirtualizingStackPanel.IsVirtualizing"      Value="True"/>
<Setter Property="VirtualizingStackPanel.VirtualizationMode"  Value="Recycling"/>
<Setter Property="VirtualizingPanel.ScrollUnit"               Value="Pixel"/>
<Setter Property="ScrollViewer.CanContentScroll"              Value="True"/>
```

`VirtualizingPanel.ScrollUnit="Pixel"` (available since .NET 4.5) preserves smooth pixel scrolling; without it, `CanContentScroll="True"` produces coarse item-by-item scrolling.

Remove any `PreviewMouseWheel` workaround that was forwarding events to an outer `ScrollViewer` — it will call `e.Handled = true` and break the TreeView's native scroll.

(Historical note: this project no longer has a `TreeView` anywhere — see the sidebar entries below for why.)

### VirtualizingStackPanel + Recycling can strand an Auto-column at a stale width after a resize
A row template with `Grid.ColumnDefinitions` `Auto | * | Auto` (e.g. a right-aligned badge in the last column) can, inside a `Recycling`-mode `VirtualizingStackPanel`, have that last `Auto` column render **fully blank** — not just clipped — for already-realized rows after a width-only resize (e.g. dragging a `GridSplitter`), even when the other columns leave plenty of spare width. Confirmed by disabling `VirtualizingStackPanel.IsVirtualizing` entirely (bug disappears) — it's a VSP recycling defect, not a Grid Auto/Star math issue, and it is **not** specific to `TreeView`; a flat `ListView`/`ListBox` with the same panel settings reproduces it identically.

Do not "fix" this with a runtime workaround — toggling `IsVirtualizing` off/on (or forcing `UpdateLayout()`) after a resize forces every row to realize at once and can hang the UI on a large list. The real fix: give that column a **fixed pixel width** instead of `Auto`. A fixed width needs no Grid measurement of the column's content at all, so there's nothing for the VSP to cache stale. See the ahead/behind badge column in `SidebarView.xaml` (`LocalBranchLeafTemplate`, `Width="90"` instead of `Auto`).

### Nested TreeViewItem indentation breaks full-width selection/hover highlighting
A recursive `TreeViewItem` `ControlTemplate` that indents child levels via `ItemsPresenter Margin="16,0,0,0"` on the *parent's* template means a nested row's own selection `Border` only spans its own (already-indented) width — it never reaches the left gutter where an ancestor's expand arrow lives. Visually the highlight looks "indented" instead of flush to the panel's left edge.

Fix used for the sidebar: don't nest `TreeViewItem`s at all. Flatten the hierarchy (see below) into rows for a single, flat `ListView`, and apply indentation as a `Margin` on the row's *inner content* only, inside a wrapper the `ListViewItem`'s own template doesn't indent. `DarkListViewItem`'s `Border`-based template naturally spans the full row width regardless of what's inside it, so the highlight is always flush left — see `IndentLevelToMarginConverter` and `SidebarRowTemplateSelector` in `Converters/ValueConverters.cs`.

### Nested ItemsControls cannot be virtualized — flatten to a single list
If diff hunks are rendered as an outer `ItemsControl` (one item per hunk) with an inner `ItemsControl` per hunk (one item per line), the outer VSP can only virtualize at the hunk level — all lines within visible hunks are always rendered. For large diffs this is thousands of live elements.

Fix: flatten the hierarchical data into a single `List<DiffItem>` where each element is either a hunk header or a line (discriminated by a `Kind` enum), then bind a single virtualized `ListView` to that list with a `DataTemplateSelector`. A 5000-line diff then renders only ~30 visible rows regardless of scroll position.

```csharp
public enum DiffItemKind { HunkHeader, Line }
public class DiffItem { public DiffItemKind Kind; public string Header; public DiffLine Line; }
```

```xml
<ListView ItemsSource="{Binding FlatDiffItems}"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          VirtualizingPanel.ScrollUnit="Pixel"
          ScrollViewer.CanContentScroll="True">
    <ListView.ItemTemplateSelector>
        <conv:DiffItemTemplateSelector .../>
    </ListView.ItemTemplateSelector>
</ListView>
```

### Don't call git on the UI thread from property setters
Property setters fire synchronously on the UI thread. If a setter calls `GitService` directly (e.g. `SelectedFile` setter calling `GetFileDiff`), the UI freezes for the duration of the git operation.

Fix: fire-and-forget an `async Task` from the setter, clear the display immediately so stale content is gone, then populate from a `Task.Run` result:
```csharp
set {
    if (!Set(ref _selectedFile, value)) return;
    if (value != null && _detailCommit != null)
        _ = LoadDiffAsync(_detailCommit.Sha, value.Path);
}

private async Task LoadDiffAsync(string sha, string path)
{
    FlatDiffItems = Array.Empty<DiffItem>(); // clear immediately
    var hunks = await Task.Run(() => _git.GetFileDiff(sha, path));
    // build FlatDiffItems from hunks ...
}
```

### An exception anywhere in the diff-loading pipeline silently blanks the pane, no banner, no error
`SelectedFile`'s setter fires `LoadDiffAsync`/`LoadWorkingDiffAsync` as fire-and-forget (`_ = LoadDiffAsync(...)`, per the pattern above) — nobody awaits or observes the returned `Task`. If ANY step in that pipeline throws (git call, `ParsePatchEntries`, `Highlighting.SyntaxHighlighter.Apply`, word-diff computation, …), the exception propagates out of the async method and becomes an unobserved task exception: no error dialog, no crash, nothing in the console. Since `FlatDiffItems`/`SideBySideItems` and every banner flag (`IsBinaryDiff`, `IsLfsPointerDiff`, `IsLargeDiffPending`) were already reset to empty/false at the top of the method (to clear stale content immediately), the diff pane just renders **completely blank** — indistinguishable from "this file genuinely has an empty diff."

Confirmed root cause of exactly this symptom: `SyntaxHighlighter.TokenizeGeneric`'s identifier-token branch matched `char.IsLetter(c) || c == '_' || c == '$'` to start scanning a word, but only *continued* the scan on `char.IsLetterOrDigit(code[pos]) || code[pos] == '_'` — which a bare `$` never satisfies. A C# interpolated string (`$"..."`) anywhere in a changed hunk (extremely common) hit this: the scan loop never advanced past the `$`, producing an **empty** `word`, and `word[0]` on an empty string throws `IndexOutOfRangeException`. That exception unwound through `GetStagedFileDiff`/`GetUnstagedFileDiff` → the fire-and-forget `LoadWorkingDiffAsync` → silently vanished, leaving the diff pane blank for every file whose diff happened to touch a `$"..."` line, with zero diagnostic trace.

**When a diff mysteriously renders blank with no banner, don't assume the git layer returned nothing — verify it did.** Temporarily instrument the exact call chain (`Console.Error.WriteLine` + `Console.Error.Flush()` after each step, since redirected stdout is buffered and won't show up until either a flush or a *graceful* process exit — `taskkill /F` does not flush it) to see whether hunks were actually parsed before the pane went blank. In this case they were (8 hunks logged successfully) right up until the syntax highlighter threw.

**Fix:** any per-line lexer branch that admits a "start" character into a token must guarantee the scan position advances past it before computing `word`/`token` — verify entry conditions and continuation conditions accept exactly the same character sets, or explicitly consume the entry character first (as done here: `if (c == '$') pos++;` before the generic letter/digit/underscore scan loop).

### `Run.Text` binds TwoWay by default — binding it to a read-only property throws at load time
`TextBlock.Text` defaults to a `OneWay` binding mode, but `System.Windows.Documents.Run.Text` (used for inline runs inside a `TextBlock`, e.g. `<Run Text="{Binding Foo}"/>`) defaults to `TwoWay`. Binding a `Run.Text` to a property with only a private setter (e.g. `public string Foo { get; private set; }`) throws `System.InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only property '...'` as soon as the element loads — this surfaced as the app's own error dialog on startup/navigation, not a XAML-parse-time failure, so it only appears once the bound `Run` actually enters the visual tree. Every other `Run.Text` binding in this codebase happens to target a plain `{ get; set; }` property (e.g. `CommitInfo.AuthorName`), which is why this hadn't come up before. **Fix:** add `Mode=OneWay` explicitly on any `Run.Text` binding whose source property is read-only: `<Run Text="{Binding ComparisonTargetName, Mode=OneWay}"/>`.

### Parsing git.exe stdout: verify the exact text against real output, don't guess from memory
Implementing `git bisect` support (`RepositoryViewModel.Bisect.cs`), the regex for detecting bisect completion was written from memory as `<sha> is the first bad commit` — plausible-looking, and wrong: git's actual output quotes the word, `<sha> is the first 'bad' commit`. The mismatched regex silently never matched, so completion was never detected — no exception, no error, the bisect banner just stayed in "in progress" state forever after the final judgment. This is the same *general* failure class as the `$"..."` syntax-highlighter bug above (a silent parse miss masquerading as "nothing happened"), but here rooted in a wrong assumption about **external** command output rather than a local lexer bug. **Verified the fix by piping the actual command** (`git bisect bad <sha> | cat -A`) rather than re-guessing — this is the reliable way to pin down any git.exe output format before writing a parser for it.

A related, narrower miss in the same feature: the parser for the commit summary that follows the completion line skipped lines with `StartsWith("commit ", ...)` to filter out `git show`'s own `commit <sha>` header — but a test repo whose commit messages were literally "commit 1", "commit 2", etc. has a real message that *also* starts with "commit ", so the filter ate it too, leaving the diffstat line as the "summary" instead. Fixed by matching the header line's exact shape (`^commit [0-9a-f]{40}$`) rather than a generic prefix. Lesson: a prefix/substring check meant to filter out one specific known line is safer as an exact-shape match than a loose `StartsWith`, especially when the content being filtered (commit messages) is arbitrary user text.

### `VirtualizingStackPanel` (the ListView default) mis-positions variable-height rows in a custom-templated ListView
The diff view's hunk-header row (taller — it has Stage/Discard/Unstage buttons) was, for the entire lifetime of this feature, invisible: the *next* row was positioned as if every row shared one uniform height, overlapping and hiding the hunk header's lower portion (buttons included) behind the following row's opaque background. Confirmed empirically, not guessed: a diagnostic `Border Height="40" Background="Lime"` wrapping the hunk template rendered only a ~2px sliver before being painted over. Disabling `VirtualizingStackPanel.IsVirtualizing` did **not** fix it — the mis-measurement is in the panel's row-positioning logic itself, not virtualization recycling. **Fix:** replace the ListView's items host with a plain `<StackPanel/>` via `ListView.ItemsPanel` (sacrificing UI virtualization — acceptable here since diffs are already size-capped, see `LargeDiffLineThreshold`). If a ListView/ListBox has rows of genuinely different heights and a custom `ControlTemplate`, don't assume `VirtualizingStackPanel` handles that correctly — verify with an unmissable diagnostic color, the same way this was caught.

### A `PreviewMouseLeftButtonDown` handler that blocks selection also silently swallows clicks on buttons in the same row
Adding click-based line selection to the diff view (`e.Handled = true` on the ListView's `PreviewMouseLeftButtonDown` to stop non-line rows like the hunk header from becoming "selected") also prevented the hunk header's own Stage/Discard/Unstage buttons from ever receiving their click — a `Handled = true` set during the tunneling phase on an ancestor stops the event before it reaches a descendant `Button`'s own routed-event handling, so the click just does nothing (no exception, no dialog — `Button.Command` never fires). Confirmed by adding logging directly inside the command handler and seeing it never called, before finding the real cause. **Fix:** before deciding to mark a mouse event `Handled`, walk up from the actual hit-test result checking for `ButtonBase` (or any interactive control) in the ancestor chain, and bail out untouched if found — a click that lands on a button should always reach that button, regardless of what row-level selection logic would otherwise do with it.

### `DiffItemTemplateSelector.SelectTemplate`'s type check silently no-ops for a second unrelated model type
The shared hunk/line template selector checked `if (item is DiffItem di && di.Kind == HunkHeader) return HunkTemplate;` — correct for the unified view's `DiffItem`, but when the same selector class was reused for the side-by-side view's *different*, unrelated `SideBySideItem` type, the `is DiffItem` check always failed, silently falling through to `LineTemplate` for every row including hunk headers. A `SideBySideItem` hunk header rendered via the line template (whose `Left`/`Right` are null for a header row) is just an empty, near-invisible row — no exception, no visual sign anything was wrong, so this went unnoticed through several rounds of otherwise-successful testing until a direct pixel scan of the hunk-header row's expected screen position turned up nothing. **Fix:** a template selector shared across multiple model types must explicitly check each type it's expected to handle, not just the first/original one — `if (item is DiffItem di) return ...; if (item is SideBySideItem sbs) return ...;`.

### Line-level patch construction for staging is direction-dependent, not just "selected vs unselected"
`PatchBuilder.BuildLinePatch` (used by both Stage and Unstage/Discard) originally always dropped unselected added lines and kept unselected deleted lines as context — correct **only** for Stage (forward `git apply --cached`, where the index doesn't yet have any of this hunk's changes, so an unselected addition genuinely isn't there yet). For Unstage/Discard (`--reverse`, matching against a target — the index, or the working tree — that **already** reflects the *full* hunk), the correct handling is the opposite: unselected added lines must stay as context (they're already present) and unselected deleted lines must be dropped (already absent). Getting this backwards produces a hunk whose declared post-image doesn't match the real target content, and `git apply` rejects it with "patch does not apply" — reproduced directly by staging one line of a multi-change hunk, then trying to unstage just that same line back out. **Fix:** thread a `reverseTarget` bool (`op != StagingPatchOp.Stage`) into the per-line hunk-body builder and swap which line-kind gets dropped vs kept-as-context based on it.

### `ApplyTemplate()` may still be needed after `Loaded`, even deferred to `DispatcherPriority.Loaded`
Looking up a control's internal template parts (e.g. finding a `ListView`'s own `ScrollViewer` via `VisualTreeHelper` to wire up scroll-sync) failed with zero visual children — even inside a `Loaded` handler, and even inside `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`, which is supposed to run after layout. This specific case involved a `ListView` whose ancestor `Grid` starts `Visibility="Collapsed"` (Side-by-side isn't the default diff mode) — a `Collapsed` ancestor skips layout for descendants entirely, so even once made `Visible`, neither `Loaded` firing nor a same-priority deferred callback reliably guaranteed `ApplyTemplate` had run for the newly-realized descendant. Confirmed via `VisualTreeHelper.GetChildrenCount` returning 0 at both points. **Fix:** call `.ApplyTemplate()` explicitly and immediately before the `VisualTreeHelper` search — it forces template application synchronously and made the descendant search reliably succeed. If a "find a template part" search comes up empty despite seemingly-correct event timing, don't add another layer of deferral — call `ApplyTemplate()` directly first.

### A "not selectable" hit-test result must distinguish "no row here" from "wrong kind of row"
The line-selection `PreviewMouseLeftButtonDown` handler on the diff `ListView`s walked up from the hit-test point to the nearest `ListViewItem` and, if the resulting item wasn't a selectable line (context row, hunk header, *or no `ListViewItem` found at all* — e.g. a click on the `ListView`'s own scrollbar, which is part of its control template, not a row), marked the event `Handled` to block native selection. That conflated two genuinely different cases: "this row exists but shouldn't be selectable" (correct to block) and "this point isn't over a row at all" (should be left completely alone). The result: a real mouse drag on the horizontal scrollbar thumb did nothing — confirmed by direct incremental mouse-drag testing (not just `ScrollPattern.SetScrollPercent`, which bypasses real input and had already been used to "verify" scroll-sync worked, missing this entirely). **Fix:** split the point-to-content lookup into two steps — find the `ListViewItem` container first, and if `container == null` (not over any row), return immediately without touching `Handled`; only inspect `container.Content` and possibly block selection once a real row was found. Lesson: **automation-driven verification (`ScrollPattern`, `InvokePattern`) can pass while the actual mouse-driven interaction is broken** — for anything reachable by a literal click-drag, test with real mouse-event sequences, not just the accessibility API shortcut.

### `NullToVisibilityConverter`'s `Invert` flag: double-check which one hides on null vs shows on null
`InvertNullToVis` (used elsewhere for a "no file selected" placeholder, which should show when the bound value **is** null) was reused by mistake for a toolbar section that should show only when a file **is** selected — the opposite condition. Both converters produce a `Visibility`, both look superficially interchangeable at the call site, and the mistake produces no error, just permanently-invisible UI. **Fix:** `NullToVis` = hidden when null, visible otherwise (the common case — hide something until there's data); `InvertNullToVis` = the reverse (show a placeholder only in the absence of data). When wiring a new "show when X is set" binding, prefer copying an existing binding with the same intent over reasoning about `Invert` from the name alone.
