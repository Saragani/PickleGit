# /pkl:static-analyze — Static Analysis

Run manually with `/pkl:static-analyze`. Not auto-invoked.
Fix all CRITICAL/HIGH findings before proceeding to commit.

---

## Step 1: Get changed files

```bash
eval "$(.claude/scripts/detect-base-branch.sh)"
git diff "$MERGE_BASE" --name-only
```

Count the results:
- **≤ 5 files** → run all steps inline in the current context
- **> 5 files** → spawn an `Explore` agent with the full checklist (Steps 2–5) and the list of changed files; report findings back here

Read every changed file and check against all rule sections below.

If `.claude/rules/code-style.md` is not already loaded, read it now.
If `.claude/rules/security.md` is not already loaded, read it now.

---

## Step 2: code-style.md compliance

Apply the full checklist from the loaded `.claude/rules/code-style.md`.

---

## Step 3: security.md compliance

Apply the full checklist from the loaded `.claude/rules/security.md`.

---

## Step 4: C# pattern checks (always)

- [ ] `INotifyPropertyChanged` goes through `BaseViewModel.Set<T>` — no manually re-implemented equality guard
- [ ] `nameof()` used in `PropertyChanged` / exception messages — no magic strings
- [ ] No `DataContext = new ViewModel()` in code-behind for a child that already has a VM property — use `DataTemplate` or a bound `DataContext`
- [ ] No direct `GitService`/LibGit2Sharp call from the UI thread or a bare `Task.Run` — routed through `GitService.Executor`
- [ ] Any CLI-backed mutation (`GitService.Cli`) calls `GitService.Reopen()` afterward
- [ ] `IDisposable` implementations have a complete `Dispose()` — no resource leaks (file handles, watcher handles, event subscriptions)
- [ ] Event subscriptions (`+=`) in long-lived objects are unsubscribed (`-=`) on dispose/unload to prevent memory leaks
- [ ] `ObservableCollection<T>` mutations happen on the UI thread — background threads rebuild-and-reassign rather than mutate in place
- [ ] `null`-conditional `?.` used before dereferencing an optional service/VM reference
- [ ] `RelayCommand`-backed buttons whose enabled state depends on a bulk collection reassignment call `CommandManager.InvalidateRequerySuggested()`
- [ ] No `MessageBox.Show` / VB `InputBox` introduced — use `Services/DialogService.cs`
- [ ] Credentials/tokens never logged via `AppLog` or embedded in an exception message

---

## Step 5: WPF-specific checks (only if changed files include XAML or WPF code-behind)

Scan changed files for `.xaml` or WPF base classes — skip this step entirely if none found.

- [ ] No business logic in XAML code-behind — all logic belongs in ViewModel
- [ ] `{Binding}` expressions have `Mode` and `UpdateSourceTrigger` set explicitly where defaults are wrong
- [ ] New theme-colored brush references use `{DynamicResource}`, not `{StaticResource}`
- [ ] New/changed `IValueConverter` instances are declared in `App.xaml`, not a Window/UserControl's own `Resources`
- [ ] New virtualized `ListView`/`ListBox` uses `IsVirtualizing="True"` + `VirtualizationMode="Recycling"` + `ScrollUnit="Pixel"` unless rows have genuinely variable height (see `PickleGit/CLAUDE.md`)
- [ ] New `OnRender` overrides cache/freeze brushes, pens, and geometries rather than allocating per call
- [ ] New `DependencyProperty` has the correct `PropertyMetadata` default value and callback if needed
- [ ] No `Dispatcher.Invoke` inside a ViewModel — marshal at the service boundary instead

---

**Report each finding with file:line and severity (CRITICAL / HIGH / MEDIUM / LOW).**
**Fix all CRITICAL and HIGH findings before continuing.**
