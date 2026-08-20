---
description: PickleGit code style — C# naming, WPF/MVVM patterns without MEF/DI, threading, .NET 4.7.2 constraints. Mandatory for all code.
---

# Code Style — PickleGit Quick Reference

## Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Private field | `_camelCase` | `_selectedFile` |
| Property / Method | `PascalCase` | `SelectedFile`, `GetFileDiff()` |
| Local variable | `camelCase` | `commitCount` |
| Interface | `I` prefix + `PascalCase` | `IHostingProvider` |
| Event | `PascalCase` | `PropertyChanged` |
| Constant / static readonly | `PascalCase` | `MaxHistoryCommits` |
| Enum value | `PascalCase` | `DiffItemKind.HunkHeader` |

## C# / .NET 4.7.2 Rules

- **Target**: .NET Framework 4.7.2, `AnyCPU`, C# 7.3 (tuples, pattern matching, `in`/`ref readonly`)
- Use `nameof()` instead of magic strings in `PropertyChanged` and exceptions
- `null`-conditional `?.` and `??` are available and preferred over manual null checks
- Expression-bodied members (`=>`) for simple getters and one-liners — not for complex logic
- Use `string.IsNullOrEmpty()` / `string.IsNullOrWhiteSpace()` for string guards
- Avoid `var` when the type is not immediately obvious from the right-hand side

## INotifyPropertyChanged — use `BaseViewModel.Set`, don't hand-roll the guard

Every ViewModel derives from `ViewModels/BaseViewModel.cs`, which already provides the equality-guarded setter and `[CallerMemberName]` plumbing:

```csharp
private string _name;
public string Name
{
    get => _name;
    set => Set(ref _name, value);
}
```

`Set<T>` compares with `EqualityComparer<T>.Default` and only raises `PropertyChanged` on a real change. Never raise `PropertyChanged` unconditionally — it re-triggers binding evaluation and can cascade. Use `RaisePropertyChanged(nameof(X))` only for a computed property with no backing field.

## Commands

Use `RelayCommand` (`BaseViewModel.cs`) — never expose an `ICommand` backed by a bare lambda with no `CanExecute` guard when one is needed:

```csharp
public ICommand SaveCommand => _saveCommand ?? (_saveCommand = new RelayCommand(ExecuteSave, CanSave));
private bool CanSave() => !string.IsNullOrEmpty(Name);
private void ExecuteSave() { ... }
```

`RelayCommand.CanExecuteChanged` is wired to `CommandManager.RequerySuggested`, which WPF re-raises on ordinary input events (mouse move, keyboard, focus) — **not** immediately when a bound collection is reassigned in code. After a bulk property/collection reassignment that changes a command's enabled state (e.g. replacing `WorkingDirFiles`), call `CommandManager.InvalidateRequerySuggested()` explicitly so the button's visual state updates without waiting for incidental input.

## WPF / MVVM — no MEF, no DI container

| Don't | Do |
|-------|----|
| `DataContext = new ViewModel()` in code-behind for a child that already has a VM property | `DataTemplate` + `ContentControl`, or bind `DataContext` from the parent VM's property |
| Logic in XAML event handlers | ViewModel command or property |
| `Dispatcher.Invoke` inside a ViewModel | Marshal at the service boundary (`GitExecutor` already does this) |
| `FindName()` / `VisualTreeHelper` in hot paths | Binding + MVVM; reserve tree-walks for one-time template-part lookups |
| Inline style/brush that duplicates a shared resource | `{DynamicResource XxxBrush}` for anything theme-colored (see below) |
| `MessageBox.Show` / VB `InputBox` in new code | `Services/DialogService.cs` (`Prompt` / `Confirm` / `ShowError`) — themed, and VB InputBox's assembly reference was deliberately removed |
| `new SolidColorBrush(...)` / `new Pen(...)` per `OnRender` call | Cache as `static readonly`, or a `static Dictionary<Color, Pen>`, and `.Freeze()` every brush/pen/geometry used in rendering |

There is no dependency-injection container. A View gets its ViewModel one of two ways:
1. **DataTemplate resolution** — a `DataTemplate DataType="{x:Type vm:MyViewModel}"` maps the VM type to its View; a `ContentControl` bound to a VM-typed property resolves automatically.
2. **DataContext binding** — `<local:MyView DataContext="{Binding SomeChildViewModel}" />`, where the parent VM exposes the child as a property.

### Theme-aware brushes: `DynamicResource`, not `StaticResource`

`App.ApplyTheme` swaps the merged `Themes/Palette{Dark,Light}.xaml` dictionary at runtime with no restart. This only works if every themed brush reference in view XAML uses `{DynamicResource XxxBrush}` — `StaticResource` resolves once at load and freezes that one element on the old theme. `StaticResource` remains correct for non-color resources (Styles, Converters, `FontFamily`, etc.).

### Converters live in `App.xaml` only

All `IValueConverter`/`IMultiValueConverter` instances are declared in `App.xaml`'s `Application.Resources` — never in a Window or UserControl's own `Resources`. A `StaticResource` reference inside a UserControl resolves at XAML-parse time, before the control is in the visual tree, so `Window.Resources` at that point is unreachable.

### ContextMenu bindings

ContextMenus are not part of the visual tree, so `RelativeSource Self`/`AncestorType` bindings inside one don't reach the DataContext the normal way. Pass the ViewModel through `PlacementTarget.Tag`:

```xml
<Grid Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=TreeView}}">
    <Grid.ContextMenu>
        <ContextMenu>
            <MenuItem Command="{Binding PlacementTarget.Tag.SomeCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext.SomeProperty, RelativeSource={RelativeSource AncestorType=ContextMenu}}"/>
        </ContextMenu>
    </Grid.ContextMenu>
</Grid>
```

To conditionally suppress a context menu, use a `DataTrigger` on the parent `Grid` that sets `ContextMenu="{x:Null}"` — never bind `Visibility` on the `ContextMenu` element itself.

## Threading

- **Never call `GitService`/LibGit2Sharp directly from the UI thread or a bare `Task.Run`.** Route through `GitService.Executor` — see `architecture.md`.
- A property setter must not call git synchronously: clear visible state immediately, then populate asynchronously (fire-and-forget from the setter, `await` inside the async method).
- `ObservableCollection<T>` mutations must happen on the UI thread; if a background executor callback needs to update one, marshal via the Dispatcher at that boundary — not deeper in the call chain.
- Never let an `async void`/fire-and-forget method's exception go unobserved and silent — if a pipeline can throw partway through (parsing, highlighting, diffing), a bug there manifests as a silently blank pane, not a crash. Prefer narrow `try/catch` with `AppLog` around genuinely-fallible steps in a fire-and-forget chain.

## Collections

- Use `ObservableCollection<T>` for collections bound to WPF UI; `List<T>` internally when not bound
- For batch mutations on an `ObservableCollection`, build a new collection and reassign the property once rather than mutating in place item-by-item (avoids per-item `CollectionChanged` re-render and matches this codebase's existing pattern, e.g. `LoadDiffAsync`)
- Avoid non-generic `ArrayList`, `Hashtable` — use generic equivalents

## Error Handling & Logging

- Do not swallow exceptions with empty `catch` blocks — log via `Services/AppLog.cs` at minimum
- Validate method arguments at public API boundaries (service entry points); trust internal callers
- Use `ArgumentNullException`, `ArgumentException`, `InvalidOperationException` — not custom exception types unless truly needed
- Never log credentials, PATs, or other secrets — `CredentialStore`/`Services/Hosting` tokens are logged only as "present/absent", never their value

## Process / CLI Invocation

- Build arguments to `GitCli.RunAsync` as a discrete argument list/array — never by concatenating branch names, paths, or commit messages into a single shell-interpreted string
- Always pass `GIT_TERMINAL_PROMPT=0` and `--no-optional-locks` where `GitCli` already sets them by convention — don't bypass the shared runner with an ad-hoc `Process.Start`
