# PR Review — `main` (merged from `conflict-manual-edit-find-bar`)

Base: `b327b6a` → `HEAD` (3 commits: `9e4826e`, `8656296`, `4be0abc`)

Scope reviewed: `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`, `PickleGit/Views/MergeConflictEditorWindow.xaml.cs` (`.xaml` unchanged in this diff). Read in full alongside `PaneFindState.cs`, `Converters/ValueConverters.cs` (`ThemeBrushes`, `CurrentFindMatchConverter`), `MergeConflictEditorWindow.xaml`, and `Models/RepositoryAccount.cs` (`DiffHighlightSpan`) for context.

## Findings

```
[LOW] Commit format
File: git commit 9e4826e (subject line)
Issue: Subject "Replace AvalonEdit's search panel with the themed find bar in the manual-edit pane" is 82 characters — exceeds commit-format.md's hard cap of 72 (target ≤50).
Fix: Shorten, e.g. "Replace AvalonEdit's search panel with the themed find bar" (61 chars), moving the "in the manual-edit pane" detail into the body (which already has a `## Why`/`## What` covering it).
```

```
[LOW] Code comment accuracy
File: PickleGit/ViewModels/MergeConflictEditorViewModel.cs:1159-1163
Issue: The comment claims `StringReader.ReadLine()` splits "exactly like AvalonEdit's own TextDocument line numbering." That's true for line numbers 1..N-1, but AvalonEdit's `TextDocument` treats a trailing newline as producing one further (empty) final line, while `StringReader.ReadLine()` does not emit that trailing empty line for text ending in a line terminator (a very common case for real files). This causes no observable bug today — the "missing" line is always empty and can never contain a match, and `doc.GetLineByNumber(matchLine)` in the view is always called with a line number that's valid in the (equal-or-larger) AvalonEdit numbering — but the "exactly" claim overstates the equivalence.
Fix: Soften the comment (e.g. "matches AvalonEdit's line numbering for every line that can actually contain a match"), or if exactness matters later, special-case a trailing terminator to emit one more empty line number.
```

```
[LOW] Minor state hygiene
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:304-324 (RewireFileVm), 244-260 (OnResultFindPropertyChanged)
Issue: `_lastHighlightedResultLine` is a window-level field that isn't reset when `RewireFileVm` switches to a different `MergeConflictFileViewModel` (e.g. navigating to the next conflicted file while ResultFind is open). The next find-state change then calls `doc.GetLineByNumber(_lastHighlightedResultLine)` against the *new* file's AvalonEdit document. This is bounds-checked (`_lastHighlightedResultLine <= doc.LineCount`) so it cannot throw, but it can issue one harmless spurious `Redraw` of an unrelated line in the newly-loaded document.
Fix: Set `_lastHighlightedResultLine = -1;` inside `RewireFileVm` when the file instance actually changes.
```

```
[INFO] Theme-brush cache staleness (pre-existing, not introduced by this diff)
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:76-77
Observation: `ResultFindHighlightColorizer.HighlightBrush` resolves `"DiffSearchMatchBrush"` once via `Converters.ThemeBrushes.Get`, which caches the resolved `Brush` by key for the process's lifetime (`ThemeBrushes.Cache`). A live Dark↔Light theme switch (`App.ApplyTheme`) while the merge-conflict editor is open would leave this highlight on the old theme's color. This is not a new gap — `Behaviors/WordDiffHighlighter.cs:144` and `Behaviors/BlameSearchHighlighter.cs:60` already resolve the exact same key through the same cached helper and have the identical limitation, in a codebase where CLAUDE.md documents `DynamicResource`-vs-`StaticResource` staleness as a known bug class. No action needed for this diff specifically; if ever fixed, the fix belongs in `ThemeBrushes` itself (e.g. clear its cache from `App.ApplyTheme`), not at this call site.
```

```
[INFO] MVVM boundary — code-behind placement confirmed appropriate
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:74-99, 214-260
Observation: `ResultFindHighlightColorizer` and its state-sync (`OnResultFindPropertyChanged`) live in code-behind as a nested private class, exactly mirroring the existing `LigatureSuppressingColorizer` in the same file — both exist because AvalonEdit's `DocumentColorizingTransformer` rendering pipeline has no ViewModel-reachable seam (no binding/converter path). This is the established, correct pattern for this codebase, not a violation.
```

```
[INFO] Boxed-int `PaneFindState.Item` convention verified safe
File: PickleGit/ViewModels/MergeConflictEditorViewModel.cs:1149-1180; PickleGit/Converters/ValueConverters.cs:436-446 (CurrentFindMatchConverter)
Observation: `CurrentFindMatchConverter.Convert` decides highlighting via `ReferenceEquals(row, currentMatch)`, comparing each row's own `ConflictResultItem` instance against `PaneFindState.CurrentMatch`. A boxed `int` (the manual-edit line-number convention introduced here) can never be `ReferenceEquals` to a `ConflictResultItem`, so it cannot cause a false-positive highlight anywhere that binds to `ResultFind.CurrentMatch`. Additionally, `ConflictResultListView` (the only XAML consumer bound to that converter for the Result pane, at MergeConflictEditorWindow.xaml:804-826) is `Collapsed` (via `InvertBoolToVis` on `IsManuallyEdited`) for the entire duration the boxed-int convention is active, so its `ListViewItem` containers aren't even realized — no interaction at all, not just a non-match.
```

```
[INFO] `ResultFindHighlightColorizer.ColorizeLine` offset clamping verified correct
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:82-98
Observation: The clamp `if (start < line.Offset || end > line.EndOffset || start >= end) return;` correctly bounds `ChangeLinePart`'s arguments to `[line.Offset, line.EndOffset]`, which is the documented safe range for a `DocumentColorizingTransformer.ColorizeLine` override on a non-folded line (this project does not use AvalonEdit folding). A stale `CurrentMatchRange` from just before a same-line edit can at worst paint the wrong span for one frame — never throw `ArgumentOutOfRangeException`. `Start`/`Length` on `DiffHighlightSpan` (Models/RepositoryAccount.cs:166-171) are always non-negative with `Length > 0` (matches come only from `AddOccurrences`' `IndexOf`, and `PaneFindState.Recompute` never runs `computeMatches` for an empty search term), so `start >= end` can't be hit by valid data either — the clamp is defense-in-depth, not covering a live bug.
```

```
[INFO] Threading / hybrid git backend — confirmed out of scope
File: PickleGit/ViewModels/MergeConflictEditorViewModel.cs, PickleGit/Views/MergeConflictEditorWindow.xaml.cs
Observation: Nothing in this diff calls `GitService`, `_git`, `CliGitService`, or `GitExecutor` — it is pure UI/AvalonEdit rendering and WPF event wiring on the UI thread, consistent with `architecture.md`'s threading model. No `GitService.Reopen()` or `GitCli.IsGitAvailable` concerns apply.
```

```
[INFO] `using ICSharpCode.AvalonEdit.Search;` removal verified clean
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:14 (removed)
Observation: A codebase-wide search for `SearchPanel`/`AvalonEdit.Search` after this change finds only a doc-comment mentioning "SearchPanel" by name (line 105) — no other file references the removed `using` or the `SearchPanel` type. The removal is clean; no dangling consumer.
```

## Other checklist areas — no findings

- **Security** (path traversal, process execution, credentials, logging, JSON deserialization, patch application, external processes, `BinaryFormatter`, reflection): none apply — this diff touches no file I/O paths, no `Process.Start`/`GitCli`, no credentials, no serialization.
- **Code style**: naming conventions (`_lastHighlightedResultLine`, `ResultFindHighlightColorizer`, etc.) all correct; no hand-rolled `INotifyPropertyChanged` guards added; no new bare-lambda `ICommand`; `var` usage is all on types obvious from the right-hand side or the immediately-adjacent cast/pattern.
- **WPF/MVVM**: no `MessageBox.Show`/`InputBox` introduced; no new converter declared outside `App.xaml`; no new brush reference added to view XAML (the one new brush reference is in code-behind, reusing the existing `ThemeBrushes` helper — see INFO above); no `ObservableCollection<T>` mutated off the UI thread (none mutated at all in this diff).
- **Memory leaks**: `_sessionVm.ResultFind.PropertyChanged += OnResultFindPropertyChanged` (added) is paired with `oldVm.ResultFind.PropertyChanged -= OnResultFindPropertyChanged` in the same `OnDataContextChanged` method (diff hunk at line ~223) — correctly unsubscribed on `DataContext` change, no leak.
- **Rendering/virtualization**: `HighlightBrush` is a `static readonly` field, not reallocated per `ColorizeLine` call; no new virtualized list was introduced; no `RelayCommand`-backed button needed `CommandManager.InvalidateRequerySuggested()` here (no bulk collection reassignment in this diff).

## Verdict

**READY TO MERGE**

CRITICAL: 0  HIGH: 0  MEDIUM: 0  LOW: 3  INFO: 6
