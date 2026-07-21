# UI Performance Improvements — PickleGit

## Context

Code review identified four independent performance problems:

1. **Per-render allocations in graph/ref rendering** — `CommitGraphCell.MakePen()` creates a new `SolidColorBrush` + `Pen` on every `OnRender` call. `DrawCurve()` allocates a `StreamGeometry` per bezier curve. `RefLabel.OnRender()` allocates new brushes and `FormattedText` on every paint. With VirtualizationMode=Recycling, containers are reused and `OnRender` fires frequently during scroll.

2. **Synchronous git operations on the UI thread** — `RepositoryViewModel.SelectedFile` setter calls `LoadDiff()` synchronously (→ `GitService.GetFileDiff()` → `_repo.Diff.Compare<Patch>()`). Also, `SelectCommit()` calls `GetCommitChangedFiles()` synchronously. Both block the UI thread visibly on large repos.

3. **DiffView: no virtualization** — two nested non-virtualized `ItemsControl`s (hunks → lines) render every diff line as a `Border`+`TextBlock` immediately. A 1000-line diff creates 1000+ elements in the visual tree with no recycling.

4. **Sidebar TreeView: no virtualization** — `DarkTreeView` style disables its own scroll (`ScrollViewer.VerticalScrollBarVisibility="Disabled"`) and delegates to the outer `ScrollViewer` in `SidebarView.xaml`. WPF virtualization requires the TreeView to own its scroll. All TreeViewItems exist in the visual tree regardless of visibility.

---

## Task 1 — Cache Pen/Brush in CommitGraphCell and RefLabel (LOW effort, HIGH impact on scroll)

**File:** `Controls/CommitGraphControl.cs`

Replace `MakePen()` with a static `Dictionary<Color, Pen>` cache returning frozen `Pen`+`SolidColorBrush` pairs. Frozen objects skip change-notification overhead and allow WPF to batch draw calls more efficiently.

```csharp
private static readonly Dictionary<Color, Pen> s_penCache = new();
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

- Replace every `MakePen(color)` call (lines 61, 69, 80, 85) with `GetPen(color)`.
- In `OnRender` for the node circle (lines 91–102): cache the `dashedPen` and `fill`/`border` brushes as static frozen fields keyed by color (or as named static fields for the white border pen which never changes).
- In `DrawCurve` (line 135): call `geo.Freeze()` after closing the `StreamGeometry` context — frozen geometries render faster and avoid internal locking.

In `RefLabel`:
- The 4 background colors (HEAD green, tag amber, remote blue, local teal) are constants — define them as `private static readonly SolidColorBrush` fields, frozen at class initialization.
- Cache `FormattedText` in a `(_lastText, _lastFt)` pair — `MeasureOverride` and `OnRender` both call `MakeText()` for the same string in the same layout cycle. Skip recreation when `RefName` hasn't changed.

---

## Task 2 — Async diff and commit-file loading (LOW effort, HIGH impact on file/commit selection)

**File:** `ViewModels/RepositoryViewModel.cs`

### 2a. LoadDiff → async

Replace the synchronous `LoadDiff(string sha, string filePath)` (line 740) with an async version. The `SelectedFile` setter (line 115) fires on the UI thread; offload the git work to a background thread:

```csharp
public FileChange SelectedFile
{
    set
    {
        if (!Set(ref _selectedFile, value)) return;
        if (value != null && _detailCommit != null)
            _ = LoadDiffAsync(_detailCommit.Sha, value.Path);
    }
}

private async Task LoadDiffAsync(string sha, string filePath)
{
    DiffHunks = new ObservableCollection<DiffHunk>(); // clear immediately — remove stale content
    if (!_git.IsOpen) return;
    var hunks = await Task.Run(() => _git.GetFileDiff(sha, filePath));
    DiffHunks = new ObservableCollection<DiffHunk>(hunks);
}
```

### 2b. SelectCommit → async commit file list

`SelectCommit` (line 645) calls `_git.GetCommitChangedFiles(sha)` synchronously. Change the method signature to `async void` (it's a fire-and-forget UI handler) and wrap the git call:

```csharp
public async void SelectCommit(string sha)
{
    var commit = GraphNodes.FirstOrDefault(n => n.Commit.Sha == sha)?.Commit;
    if (commit == null) return;
    DetailCommit = commit;
    CommitFiles = new ObservableCollection<FileChange>(); // clear immediately
    ShowWorkingDir = false;
    DiffHunks = new ObservableCollection<DiffHunk>();
    var files = await Task.Run(() => _git.GetCommitChangedFiles(sha));
    CommitFiles = new ObservableCollection<FileChange>(files);
}
```

---

## Task 3 — Virtualize DiffView with flat item list (MEDIUM effort, HIGH impact on large diffs)

**Files:** `Models/DiffItem.cs` (new), `Views/DiffView.xaml`, `ViewModels/RepositoryViewModel.cs`

The nested `ItemsControl` structure cannot be virtualized — each inner ItemsControl is opaque to the outer VirtualizingStackPanel. Replace with a single flat virtualized `ListView`.

### 3a. Add DiffItem model

```csharp
// Models/DiffItem.cs
public enum DiffItemKind { HunkHeader, Line }

public class DiffItem
{
    public DiffItemKind Kind { get; init; }
    public string Header { get; init; }   // non-null when Kind == HunkHeader
    public DiffLine Line { get; init; }   // non-null when Kind == Line
}
```

### 3b. Expose FlatDiffItems from RepositoryViewModel

Replace `ObservableCollection<DiffHunk> DiffHunks` exposure in the VM with a `List<DiffItem> FlatDiffItems` property. Build it in `LoadDiffAsync`:

```csharp
private async Task LoadDiffAsync(string sha, string filePath)
{
    FlatDiffItems = Array.Empty<DiffItem>();
    if (!_git.IsOpen) return;
    var hunks = await Task.Run(() => _git.GetFileDiff(sha, filePath));
    var flat = new List<DiffItem>();
    foreach (var hunk in hunks)
    {
        flat.Add(new DiffItem { Kind = DiffItemKind.HunkHeader, Header = hunk.Header });
        foreach (var line in hunk.Lines)
            flat.Add(new DiffItem { Kind = DiffItemKind.Line, Line = line });
    }
    FlatDiffItems = flat;
}
```

`DiffHunks` can stay for any other consumer (e.g. working-dir staging), or be retired if unused.

### 3c. Replace DiffView.xaml ItemsControls with flat ListView

```xml
<ListView ItemsSource="{Binding FlatDiffItems}"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          ScrollViewer.CanContentScroll="True"
          VirtualizingPanel.ScrollUnit="Pixel"
          BorderThickness="0">
    <ListView.ItemContainerStyle>
        <Style TargetType="ListViewItem">
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Margin" Value="0"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template">...<!-- no selection highlight needed --></Setter>
        </Style>
    </ListView.ItemContainerStyle>
    <ListView.ItemTemplateSelector>
        <local:DiffItemTemplateSelector
            HunkTemplate="{StaticResource DiffHunkHeaderTemplate}"
            LineTemplate="{StaticResource DiffLineTemplate}"/>
    </ListView.ItemTemplateSelector>
</ListView>
```

Add `DiffItemTemplateSelector` as a small class in `Converters/ValueConverters.cs` (or a new file) and declare the two `DataTemplate`s as resources in `DiffView.xaml`.

---

## Task 4 — Virtualize the sidebar TreeView (LOW effort, HIGH impact on large branch lists)

**Files:** `Views/SidebarView.xaml`, `Themes/DarkTheme.xaml`

WPF TreeView virtualization requires the control to own its scroll. Current setup: `DarkTreeView` style has `ScrollViewer.VerticalScrollBarVisibility="Disabled"` and an outer `ScrollViewer` in `SidebarView.xaml` handles scrolling. Fix:

### 4a. Update DarkTheme.xaml — DarkTreeView style

Remove the `VerticalScrollBarVisibility=Disabled` setter. Add virtualization settings:

```xml
<Style x:Key="DarkTreeView" TargetType="TreeView">
    ...
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
    <Setter Property="ScrollViewer.VerticalScrollBarVisibility"   Value="Auto"/>
    <Setter Property="VirtualizingStackPanel.IsVirtualizing"      Value="True"/>
    <Setter Property="VirtualizingStackPanel.VirtualizationMode"  Value="Recycling"/>
    <Setter Property="VirtualizingPanel.ScrollUnit"               Value="Pixel"/>
    <Setter Property="ScrollViewer.CanContentScroll"              Value="True"/>
</Style>
```

`VirtualizingPanel.ScrollUnit="Pixel"` (available since .NET 4.5 / .NET Framework 4.5) keeps smooth pixel-level scrolling while enabling virtualization.

### 4b. Update SidebarView.xaml

Remove the outer `<ScrollViewer>` wrapper around the `<TreeView>`. The TreeView now manages its own scroll. The `<Border>` that currently wraps the `ScrollViewer` stays; the `TreeView` fills it directly.

---

## Verification

Build with `msbuild PickleGit.csproj /p:Configuration=Debug` — must have zero new errors (pre-existing CS0067 unused-event warnings are OK).

Manual test checklist:
1. Open a repo with 100+ commits — scroll the commit list rapidly; check for jank
2. Select different files in a commit — diff should appear without perceptible UI freeze
3. Open a repo with 100+ remote branches — expand REMOTE BRANCHES and scroll; should be smooth
4. Select a commit with a 500+ line diff — diff should render near-instantly and scroll smoothly
5. Ensure `IsExpanded` state still saves/restores for branch sub-folders after TreeView virtualization change

---

## Files to Modify

| File | Change |
|------|--------|
| `Controls/CommitGraphControl.cs` | Cache `Pen`/`Brush`; freeze `StreamGeometry`; cache `FormattedText` in RefLabel |
| `ViewModels/RepositoryViewModel.cs` | `LoadDiffAsync`, async `SelectCommit`, expose `FlatDiffItems` |
| `Models/DiffItem.cs` | New file — `DiffItem` + `DiffItemKind` |
| `Views/DiffView.xaml` | Replace nested ItemsControls with flat virtualized ListView |
| `Converters/ValueConverters.cs` | Add `DiffItemTemplateSelector` |
| `Themes/DarkTheme.xaml` | Enable virtualization on `DarkTreeView` style |
| `Views/SidebarView.xaml` | Remove outer `ScrollViewer` |
