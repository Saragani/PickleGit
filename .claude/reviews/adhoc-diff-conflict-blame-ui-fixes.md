# PR Review — adhoc-diff-conflict-blame-ui-fixes

Branch: `adhoc-diff-conflict-blame-ui-fixes`
Merge-base: `d7a1e39995287c654ca1378b2499502ef6e1c452`

## Commit format

`git log d7a1e39995287c654ca1378b2499502ef6e1c452..HEAD --oneline` printed **nothing** — all seven fixes are uncommitted working-tree changes (staged/unstaged files only, no commits on this branch yet). Commit-format checklist (subject line, no WIP/fixup commits) is not yet applicable; it will need to be re-checked once `pkl:commit` actually creates the commit(s).

## Findings

[INFO] Commit format
File: (n/a — no commits yet)
Issue: All 7 fixes are currently uncommitted working-tree changes; `commit-format.md`'s checklist can't be evaluated against real commit messages yet.
Fix: Apply `commit-format.md` at SHIP time. Given this is a batch of 7 independent bug fixes touching multiple areas (tooltip/XAML, AvalonEdit rendering, two Ctrl+G/Ctrl+F wiring efforts, hunk-scroll, and two independent refresh/reselect guards), this is a strong candidate for either several atomic "Standard" tier commits (one per bug) or a single "Complex multi-area" commit with a `## Why` + one named section per area — not a single "Standard" 2–6 line commit, since more than two distinct areas change behavior.

[LOW] MVVM / BaseViewModel.Set convention
File: PickleGit/ViewModels/RepositoryViewModel.cs:1663-1671 (`ReselectSameCommit`)
Issue: `ReselectSameCommit` assigns `_selectedNode = restored;` directly and then unconditionally calls `RaisePropertyChanged(nameof(SelectedNode))`, bypassing `BaseViewModel.Set<T>`. `code-style.md` asks for `Set<T>` rather than hand-rolling the field/notify pair. In this specific case the intent (always notify, since `restored` is always a fresh `GraphNode` reference after a rebuild, and the whole point is to skip `OnSelectedNodesChanged()`/`LoadCommitDetail`, not the property-changed notification itself) is legitimate and well-documented in the method's XML doc, so this isn't a functional bug — but it's still a hand-rolled notify that duplicates what `Set(ref _selectedNode, restored)` would do for the field+notify half, while keeping the custom `_selectedNodes` sync separately.
Fix: Purely cosmetic — could read `if (Set(ref _selectedNode, restored)) RaisePropertyChanged(...)`-style via `Set` for the scalar field, then keep the custom `_selectedNodes` clear/add block as-is. Not blocking; the existing real `SelectedNode` setter (line ~162) already does the same manual `_selectedNode`/`_selectedNodes` dance without going through `Set` either (it calls `Set(ref _selectedNode, value)` first, then does the manual `_selectedNodes` sync) — `ReselectSameCommit` could mirror that exactly by calling `Set(ref _selectedNode, restored)` instead of direct field assignment + manual raise, for consistency with its sibling code path.

[INFO] Plan/Touches drift (informational only, not a code defect)
File: PickleGit/ViewModels/RepositoryViewModel.cs
Issue: Step 8's plan `Touches` listed `RepositoryViewModel.cs, RepositoryViewModel.Detail.cs`, but the actual diff only touches `RepositoryViewModel.cs` (plus `RepositoryViewModel.Staging.cs`, already flagged as a Step-7 deviation in the plan's own Deviation Register). `RepositoryViewModel.Detail.cs` (`LoadCommitDetail`) was correctly left untouched since the fix works by avoiding the call into it, not by changing it.
Fix: None needed — noting only so this doesn't get flagged again at a later review pass as an unexplained Touches mismatch. Already implicitly covered by the Step 7/8 deviation entries in the plan file.

[INFO] AvalonEdit SearchPanel lifecycle
File: PickleGit/Views/MergeConflictEditorWindow.xaml.cs:69 (`SearchPanel.Install(ConflictResultEditBox)`)
Issue: `SearchPanel.Install` is called once in the constructor and never explicitly uninstalled/disposed on window close. AvalonEdit's `SearchPanel` attaches command bindings to the `TextArea`; since `MergeConflictEditorWindow` instances are short-lived, per-merge-session windows (not long-lived singletons), this is very unlikely to leak in practice.
Fix: No action required; flagged only as a defensive note in case this window type is ever changed to be reused/cached across merge sessions.

## Verified correct (called out per the task's "don't flag intentional design" guidance)

- **`ReselectSameCommit` bypass of the normal `SelectedNode` setter** (`RepositoryViewModel.cs:1663`) is correct and intentional: confirmed both call sites (`RefreshOnceAsync`'s `Dispatcher.Invoke` block, and `ApplyFilter`'s `Application.Current.Dispatcher.Invoke` block at line 1624) run on the UI thread, and the helper correctly mirrors the real `SelectedNode` setter's `_syncingFromSelectedNode` guard around the `_selectedNodes` Clear/Add so the `CollectionChanged` handler doesn't re-fire `OnSelectedNodesChanged()`.
- **Both conflict-transition-guard fixes** (`RepositoryViewModel.cs:1374` in `RefreshOnceAsync`, and `RepositoryViewModel.Staging.cs:94` in `RefreshConflictStateAsync`) are implemented identically: `bool conflictJustStarted = conflict.HasConflicts && !HasConflict;` computed **before** `ConflictInfo = conflict;` reassigns the backing field that `HasConflict` (a computed property, `RepositoryViewModel.cs:443`) reads from. Ordering is correct in both places — if `ConflictInfo` were assigned first, `HasConflict` would already reflect the new state and the transition check would always read false-on-false, defeating the guard. Both are correct.
- **Both reselect-guard fixes** (`RefreshOnceAsync` and `ApplyFilter`, both in `RepositoryViewModel.cs`) call the same shared `ReselectSameCommit(restored)` helper — not duplicated/divergent implementations.
- **`NoLigaturesTypographyProperties`** (`MergeConflictEditorWindow.xaml.cs:109-153`) overrides exactly `StandardLigatures` and `ContextualAlternates` to `false`; every other member returns WPF's real `Typography.*` default (`ContextualLigatures`/`Kerning` = `true`; all other bools = `false`; all enums = `Normal`; all ints = `0`). This exactly mirrors `RowText`'s `Typography.StandardLigatures="False" Typography.ContextualAlternates="False"` (confirmed present at 9 call sites in `MergeConflictEditorWindow.xaml`, no other `Typography.*` attributes set anywhere in that file) — no default-value regression.
- **`ScrollDiffItemToTop`'s hardcoded `RowHeight = 24`** (`DiffView.xaml.cs:611`) matches the actual `Height="24"` on every hunk-header and line `Border` in `DiffView.xaml` (unified and both side-by-side templates, confirmed via grep) — the magic number is accurate, not stale.
- **`GoToLine(ListView lv)`'s implicit "else → BlameListView" branch** (`DiffView.xaml.cs`) is safe: `DiffTextSelection_KeyDown` (which calls `GoToLine(lv)` for the non-Blame case) is wired only to the Unified/Left/Right ListViews (confirmed by its own `SideBySideLeftListView`/`SideBySideRightListView` `ReferenceEquals` checks for Ctrl+A handling in the same method), and `BlameListView_PreviewKeyDown` is a fully separate handler — the two call paths never cross, so the type-narrowing by elimination is safe, if a little implicit.
- **Tooltip bindings** (`CommitDetailView.xaml:329,338`) — both `FileChange.Path` and `AggregatedFileChange.Path` are confirmed direct string properties (`Models/RepositoryAccount.cs`), so `{Binding Path}` resolves correctly in both the plain-commit and aggregated flat-mode templates.
- **No security-checklist items apply** — this PR touches no file-path handling, no `Process.Start`/`GitCli` invocation, no credentials, no logging, no JSON (de)serialization, and no patch construction. Purely UI/XAML/view-code-behind and two `RepositoryViewModel` guard conditions.
- **No `GitService`/LibGit2Sharp calls added**, no new CLI-backed mutation, so `GitService.Reopen()`/`GitCli.IsGitAvailable` checks don't apply to this PR.
- **No new brush/pen/geometry allocations in any `OnRender`** — no `OnRender` override touched by this PR. `NoLigaturesTypographyProperties`'s singleton instance (`private static readonly NoLigatures`, line 94) is correctly cached once, not reallocated per line/render.
- **No `MessageBox.Show`/VB `InputBox` introduced** — new "Go to Line" prompt correctly routes through `DialogService.Prompt` via the new `DialogService.PromptForLineNumber` helper.
- **No new converters** added outside `App.xaml` (none added at all in this PR).
- **`ObservableCollection` mutations** (`_selectedNodes.Clear()`/`.Add()` in `ReselectSameCommit`) all happen inside UI-thread `Dispatcher.Invoke` blocks — consistent with the rest of the codebase.

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 0 |
| MEDIUM | 0 |
| LOW | 1 |
| INFO | 3 |

**Verdict: READY TO MERGE**
