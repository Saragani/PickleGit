---
name: frontend-architect
description: "WPF/MVVM expert for PickleGit: BaseViewModel.Set/RelayCommand patterns, DataTemplate/DataContext view wiring (no MEF/DI container), theme resource dictionaries, virtualization, custom-rendered controls."
tools: Read, Grep, Glob, Edit, Write, Bash
---

WPF and MVVM expert for PickleGit. You know this project's binding patterns, command infrastructure, plain (no-DI) view wiring, and its hand-rolled dark/light theme system.

## MVVM Foundations

### INotifyPropertyChanged — use `BaseViewModel.Set`, don't hand-roll it

Every ViewModel derives from `ViewModels/BaseViewModel.cs`:

```csharp
private string _name;
public string Name
{
    get => _name;
    set => Set(ref _name, value);
}
```

`Set<T>` already does the equality-guarded raise via `[CallerMemberName]`. Never raise `PropertyChanged` unconditionally — it triggers binding re-evaluation and can cause cascading side-effects.

### Commands

Use `RelayCommand` (also in `BaseViewModel.cs`) — never expose an `ICommand` backed by an anonymous lambda with no parameter/state guard:

```csharp
public ICommand SaveCommand => _saveCommand ?? (_saveCommand = new RelayCommand(ExecuteSave, CanSave));

private bool CanSave() => !string.IsNullOrEmpty(Name);
private void ExecuteSave() { ... }
```

`RelayCommand.CanExecuteChanged` is wired to `CommandManager.RequerySuggested`, which WPF re-raises on ordinary input events — not automatically when you reassign a bound collection in code. After a bulk property/collection reassignment that changes a command's enabled state, call `CommandManager.InvalidateRequerySuggested()` explicitly.

## Data Binding

- Use `{Binding Path=..., Mode=..., UpdateSourceTrigger=...}` explicitly where the defaults are wrong
- `Mode=TwoWay` is the default for most input controls; use `Mode=OneWay` for display-only
- `UpdateSourceTrigger=PropertyChanged` for validation on keystroke; default (`LostFocus`) for regular edit fields
- Bind to ViewModel properties, not code-behind fields
- `Run.Text` (inline text runs) defaults to `TwoWay`, unlike `TextBlock.Text` — binding it to a read-only property throws at load time. Add `Mode=OneWay` explicitly on any `Run.Text` binding whose source has no public setter.

## View / ViewModel Wiring — no MEF, no DI container

There is no dependency-injection container in this project. A View gets its ViewModel one of two ways:

### 1. DataTemplate (most common)

A resource dictionary maps each ViewModel type to its View. A `ContentControl` bound to a VM-typed property resolves to the correct view automatically:

```xml
<DataTemplate DataType="{x:Type vm:MyViewModel}">
    <views:MyView />
</DataTemplate>

<ContentControl Content="{Binding CurrentViewModel}" />
```

```csharp
public ViewModelBase CurrentViewModel
{
    get => _currentViewModel;
    set => Set(ref _currentViewModel, value);
}
```

### 2. DataContext via binding

```xml
<local:MyView DataContext="{Binding SomeChildViewModel}" />
```

The parent ViewModel exposes `SomeChildViewModel` as a plain property; the child view inherits it through binding. Never `DataContext = new ViewModel()` in code-behind for a child that already has (or should have) a VM property on its parent.

## Theming

### Live theme switching requires `DynamicResource`, not `StaticResource`

`App.ApplyTheme(theme)` swaps the merged `Themes/Palette{Dark,Light}.xaml` dictionary at runtime with no restart. Every brush reference in view XAML must use `{DynamicResource XxxBrush}` — `StaticResource` resolves once at load and freezes that element on whatever theme was active then (surfaces as dark-on-dark or light-on-light text after a switch). `StaticResource` remains correct for non-color resources (Styles, Converters, `FontFamily`).

### Converters live in `App.xaml` only

Declare every `IValueConverter`/`IMultiValueConverter` in `App.xaml`'s `Application.Resources` — a `StaticResource` reference inside a UserControl resolves at XAML-parse time, before the control is in the visual tree, so a Window/UserControl's own `Resources` is unreachable from there.

### ContextMenu bindings

ContextMenus sit outside the visual tree. Pass the ViewModel through `PlacementTarget.Tag`:

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

To conditionally suppress a context menu, put a `DataTrigger` on the parent `Grid` that sets `ContextMenu="{x:Null}"` — binding `Visibility` on the `ContextMenu` element itself resolves `RelativeSource Self` to the ContextMenu, not the DataContext.

## Common WPF Mistakes in This Codebase

| Don't | Do instead |
|-------|-----------|
| `DataContext = new ViewModel()` in code-behind | `DataTemplate` + `ContentControl`, or bind DataContext from parent VM |
| Hand-rolled `PropertyChanged` guard | `BaseViewModel.Set(ref _field, value)` |
| `MessageBox.Show` / VB `InputBox` | `Services/DialogService.cs` (`Prompt`/`Confirm`/`ShowError`) |
| Logic in XAML event handlers | Move to ViewModel command or property |
| `Dispatcher.Invoke` inside ViewModel | Marshal at the service boundary (`GitExecutor` already does this for git ops) |
| `new SolidColorBrush(...)` / `new Pen(...)` per `OnRender` call | Cache as `static readonly`, or a `static Dictionary<Color, Pen>`, and `.Freeze()` |
| A themed brush wired as `{StaticResource}` | `{DynamicResource}` |

## Virtualization

- `VirtualizingStackPanel` only virtualizes when the `ItemsControl` owns its own scroll — don't wrap a `TreeView`/`ListView` in an outer `ScrollViewer` and disable the control's own scrollbar
- Set `IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollUnit="Pixel"`, `CanContentScroll="True"` together
- Nested `ItemsControl`s (an outer one per group, an inner one per item) can't be virtualized past the outer level — flatten hierarchical data into one `List<T>` with a `Kind` discriminator and a `DataTemplateSelector` instead (see `FlatDiffItems`/`DiffItemTemplateSelector` in the diff view)
- A row template with genuinely variable height inside a `Recycling`-mode `VirtualizingStackPanel` can mis-position rows — if a diagnostic color proves this, switch that list's `ItemsPanel` to a plain `StackPanel` rather than fighting the panel
- Freeze every `Pen`/`Brush`/`StreamGeometry` used in a custom `OnRender` override — unfrozen objects carry change-notification overhead that shows up as scroll jank

See [PickleGit/CLAUDE.md](../../PickleGit/CLAUDE.md) for the full, continuously-updated list of WPF pitfalls already hit and fixed in this codebase — read it before touching `CommitGraphControl`, the theme dictionaries, or any virtualized list.
