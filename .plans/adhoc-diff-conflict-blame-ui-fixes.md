---
issue: adhoc-diff-conflict-blame-ui-fixes
title: Diff/Conflict/Blame UI fixes — tooltips, font, Ctrl+G/Ctrl+F, hunk scroll, refresh clobbering selection
type: bug
component: Views/CommitDetailView, Views/MergeConflictEditorWindow, Views/DiffView, RepositoryViewModel
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-28 11:07
---

# Plan: adhoc-diff-conflict-blame-ui-fixes — Diff/Conflict/Blame UI fixes — tooltips, font, Ctrl+G/Ctrl+F, hunk scroll, refresh clobbering selection

## Spec
### Problem Statement
A batch of seven UI/UX defects reported against the diff, merge-conflict, and blame views:

1. The "Files changed" list is missing a tooltip showing the full file path when a row is truncated.
2. The merge-conflict manual-edit pane (AvalonEdit) renders with a visibly different font treatment than every other code/diff pane in the app.
3. The diff view has no Ctrl+G shortcut.
4. Next/previous-hunk navigation in the diff view scrolls the target hunk to the bottom of the viewport instead of the top.
5. An explicitly opened diff/commit view gets silently replaced by the Staged/Unstaged view (or by the bare commit list) when an unrelated repository refresh fires — e.g. selecting a commit found via hash search, then an external tool (Visual Studio compiling) touches the working directory, and the selected commit's diff disappears in favor of the Staged/Unstaged view. The same happens in reverse (a selected working-dir file's diff gets hidden). A conflicting file being edited during an active merge also triggers this.
6. The merge-conflict view is missing Ctrl+G everywhere, and the manual-edit pane specifically is also missing Ctrl+F (the three read-only conflict panes already have Ctrl+F).
7. The blame view is missing Ctrl+G.

### Requirements
- FR1: The "Files changed" list's flat-mode rows (both the plain-commit `CommitFiles` list and the `AggregatedFiles` list) show a tooltip with the full file path, matching the tree-mode template's existing behavior.
- FR2: The merge-conflict manual-edit pane's text rendering visually matches the other code/diff panes in the window (ligatures/contextual alternates consistently suppressed).
- FR3: Ctrl+G opens a "Go to Line" prompt and scrolls/highlights the requested line, available identically in the diff view, the blame view, and all four surfaces of the merge-conflict window (the three read-only panes plus the manual-edit pane). `[derived]` — no existing Ctrl+G convention exists anywhere in the codebase to copy (confirmed: zero `Key.G` bindings), so "go to line number" was chosen as the standard cross-editor meaning of Ctrl+G.
- FR4: Ctrl+F (find) works in the merge-conflict manual-edit pane, matching the find capability the other three panes in that window already have.
- FR5: Next/previous-hunk navigation in the diff view scrolls so the target hunk's header lands at (or near) the top of the viewport.
- FR6: An explicitly selected diff/commit view is not reset by an unrelated repository refresh unless the selected object itself becomes genuinely invalid (the specific commit no longer exists, or the specific conflicted file was resolved). This covers two independent triggers found in `RepositoryViewModel.cs`:
  - FR6a: A merge-conflict refresh tick unconditionally forces `ShowWorkingDir = true` (clearing `DetailCommit`) on every tick while any conflict exists, not only when the conflict state actually starts.
  - FR6b: After any `GraphNodes` rebuild, the previously selected node is unconditionally re-applied through `SelectedNode`, which unconditionally re-triggers `LoadCommitDetail` (clearing `SelectedFile`/`CommitFiles`) even when the reselected commit's sha is identical to what was already selected.

### Acceptance Criteria
- AC1: Given the "Files changed" list is in flat mode with a truncated path, when the user hovers a row, then a tooltip shows the full path. (FR1)
- AC2: Given the merge-conflict manual-edit pane is open, when its rendering is compared against another pane showing the same ligature-bearing text (e.g. `->`, `!=`), then ligatures/contextual alternates are suppressed the same way as the other panes. (FR2) `[derived]`
- AC3: Given the diff view, blame view, or any pane of the merge-conflict window (including manual-edit) is focused, when the user presses Ctrl+G and enters a valid line number, then the view scrolls to and highlights that line. (FR3)
- AC4: Given the merge-conflict manual-edit pane is focused, when the user presses Ctrl+F, then a find UI appears and can locate text in that pane. (FR4)
- AC5: Given the diff view has multiple hunks, when the user navigates to the next/previous hunk, then the hunk's header is at the top of the viewport, not the bottom. (FR5)
- AC6: Given a commit found via hash search is selected and its diff is shown, when an external process makes a working-dir-only change (no ref change) that triggers a repository refresh, then the same commit's diff remains displayed — no fallback to the Staged/Unstaged view. (FR6b)
- AC7: Given a merge conflict is in progress and the user is viewing something other than the Staged/Unstaged view, when a conflicting file is edited again (another working-dir tick during the same conflict), then the view is not forced back to Staged/Unstaged on that subsequent tick. (FR6a)

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- All LibGit2Sharp/git.exe calls must stay routed through `GitService.Executor`; none of these fixes touch the git backend directly, but `RefreshAsync`'s surrounding flow must not be restructured in a way that breaks that routing.
- `RepositoryWatcher.Suppress()` scopes and the existing `_lastRefreshSignature` skip-check in `RefreshAsync` must be preserved — FR6 fixes narrow the unconditional resets, they don't replace the signature-based skip mechanism (`architecture.md`'s "Change Detection" section explicitly calls that mechanism required, not removable).
- No third-party WPF control library — the Go-to-Line prompt must be a stock/hand-rolled WPF dialog (or `DialogService.Prompt`), not a new external dependency.
- AvalonEdit is scoped narrowly to the merge-conflict editable RESULT pane (ADR 0003) — FR2/FR3/FR4 fixes for that pane must use AvalonEdit's own APIs (`TextEditorOptions`, `Search.SearchPanel`, `TextArea`) rather than reaching into WPF `TextBlock`-only mechanisms that don't apply to it.
- `ShortcutManager`'s `Actions` list + `MainWindow` `InputBindings` is the app's one existing convention for registering global shortcuts (`AppCommandRegistry` mirrors it for the command palette) — Ctrl+G should follow this pattern where it's a window-global shortcut, but the three separate merge-conflict surfaces and the diff/blame ListViews currently wire Ctrl+F via local `PreviewKeyDown` handlers instead, not `ShortcutManager` — match whichever pattern each surface already uses for Ctrl+F so the new Ctrl+G handler sits next to its sibling instead of introducing a second registration mechanism on the same control.

## Tests
### Manual / Black-Box
- [ ] Open a commit with a long/truncated file path in flat file-list mode → hover a row → tooltip shows the full path <!-- AC1 -->
- [ ] Open the merge-conflict manual-edit pane on a file containing `->`/`!=` → compare glyph rendering against another pane showing the same text → ligatures render the same way (suppressed) in both <!-- AC2 -->
- [ ] Press Ctrl+G in the diff view, blame view, and each of the four merge-conflict panes (left/right/result/manual-edit) → prompt appears in each, entering a line number scrolls to/highlights it <!-- AC3 -->
- [ ] Press Ctrl+F in the merge-conflict manual-edit pane → find UI appears and locates text <!-- AC4 -->
- [ ] Open a diff with 3+ hunks, use next/previous-hunk navigation → target hunk header lands at the top of the viewport <!-- AC5 -->
- [ ] Search for a commit by hash, select it (diff shown) → externally touch a tracked working-dir file (e.g. edit and save it outside PickleGit) to trigger a WorkingDir-only refresh → selected commit's diff is still shown <!-- AC6 -->
- [ ] Start a merge with a conflict, view something other than Staged/Unstaged (e.g. a past commit's diff) while the conflict is outstanding, then edit the conflicting file again → the view is not forced back to Staged/Unstaged on that second tick <!-- AC7 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step. **At checkpoint**: (1) if any deviation occurred — state it, get approval, wait; (2) verify all stubs via the `run` skill (all `(manual)` — no test project exists yet); (3) run `checkpoint.sh adhoc-diff-conflict-blame-ui-fixes N --deviations "<text>"`; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Add tooltip to flat-mode "Files changed" rows ✓
**What**: Add `ToolTip="{Binding Path}"` to the row `Grid` in the `CommitFiles` flat-mode `DataTemplate` (`Views/CommitDetailView.xaml`, `DataType="models:FileChange"`, row `Grid` around line 579) and the `AggregatedFiles` flat-mode `DataTemplate` (row `Grid` around line 746), matching the tooltip already present on the tree-mode templates (`CommitFilesTreeSelector.FileTemplate` line ~277, `AggregatedFileTreeRowTemplateSelector.FileTemplate` line ~360, both bound to `File.Path`).
**Touches**: `Views/CommitDetailView.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Flat-mode file list row shows a tooltip with the full path on hover (manual)
- [x] Tree-mode file list tooltip behavior is unchanged (manual)
**Risk**: None.
**Deviations**: None

### Step 2: Fix merge-conflict manual-edit pane font mismatch ✓
**Why**: The AvalonEdit `ConflictResultEditBox` already uses the same `FontFamily`/`FontSize` as the other panes (`Views/MergeConflictEditorWindow.xaml:858-869` vs. e.g. line 302/343/367/407) — the visible mismatch is that every ListView-based `RowText` TextBlock sets `Typography.StandardLigatures="False"` / `Typography.ContextualAlternates="False"` while the AvalonEdit control has no equivalent applied, so ligature glyphs (e.g. Cascadia Code's `->`) render only there.
**What**: Suppress ligatures/contextual alternates on `ConflictResultEditBox` so it matches the other panes. Try applying `Typography.StandardLigatures="False"`/`Typography.ContextualAlternates="False"` directly on the `TextEditor` element first; if AvalonEdit's internal `TextView` doesn't honor WPF `Typography` attached properties on the host control (this is uncertain — flagged in investigation), fall back to setting the equivalent via AvalonEdit's font-rendering options or a `TextArea`-level style/resource that reaches the internal glyph-run construction.
**Touches**: `Views/MergeConflictEditorWindow.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Open a conflict file containing ligature-prone character sequences (`->`, `!=`, `==`) in manual-edit mode → glyphs render unligated, matching the read-only panes' rendering of the same text (manual)
**Risk**: AvalonEdit may not honor WPF `Typography.*` attached properties the way plain `TextBlock`s do, since it does its own text layout — the straightforward XAML-only fix may not work.
**Mitigation**: If the attached-property approach has no visible effect, investigate AvalonEdit's `TextView`/`VisualLineElementGenerator` font-feature options as a fallback within this same step before marking it done; do not mark ✓ on a fix that doesn't visibly change the rendering.
**Deviations**: Original plan's first approach (WPF Typography.* attached properties on the TextEditor element) had no effect — AvalonEdit's rendering pipeline never reads WPF Typography attached properties. Root-caused via reflection: AvalonEdit's own DefaultTextRunTypographyProperties also turned out to be a fixed, non-settable class whose StandardLigatures/ContextualAlternates both default to true (matching WPF's own Typography default), so constructing one changed nothing either. Fix implemented via a custom TextRunTypographyProperties subclass (NoLigaturesTypographyProperties) applied through a DocumentColorizingTransformer registered on the TextEditor's TextView.LineTransformers — explicitly overrides only StandardLigatures/ContextualAlternates to false, mirroring RowText's Typography settings exactly, all other features left at WPF default. This was anticipated in the plan's Step 2 Risk/Mitigation and required investigating within the same step, not a scope change.

### Step 3: Implement shared "Go to Line" prompt and wire Ctrl+G in the diff view ✓
**Why**: No Ctrl+G convention exists anywhere in the codebase (confirmed zero `Key.G` bindings) — this is a net-new feature, not a rewiring, so it's built once here and reused by Steps 4 and 5 rather than reimplemented per surface.
**What**: Add a small "Go to Line" prompt (via `DialogService.Prompt` or a lightweight inline overlay consistent with existing find-bar UX in this codebase) that accepts a line number and scrolls/highlights that line in the target pane. Wire Ctrl+G to invoke it in the diff view's line-based ListView(s), following whatever local key-handling pattern that view already uses for its own shortcuts (e.g. its existing Ctrl+F handler) rather than introducing a second registration mechanism.
**Touches**: `Views/DiffView.xaml.cs` (or wherever the diff view's existing Ctrl+F is handled), a new shared Go-to-Line prompt (reused, not duplicated, by Steps 4–5)
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ctrl+G in the diff view opens the prompt; entering a valid line number scrolls to and highlights that line (manual)
- [x] Entering an out-of-range line number is handled without a crash (manual)
**Risk**: None.
**Deviations**: None

### Step 4: Wire Ctrl+G (Go to Line) in the blame view ✓
**What**: Reuse the Step 3 Go-to-Line prompt, wiring Ctrl+G in `BlameListView_PreviewKeyDown` (`Views/DiffView.xaml.cs:633-640`, which currently handles only Ctrl+F for `BlameFind`) the same way Ctrl+F is already wired there.
**Touches**: `Views/DiffView.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ctrl+G in the blame view opens the prompt and jumps to the requested line (manual)
- [x] Existing Ctrl+F blame-find behavior is unchanged (manual)
**Risk**: None.
**Deviations**: The blame-view Ctrl+G wiring (BlameListView_PreviewKeyDown -> GoToLine(BlameListView)) was actually written and code-complete during Step 3 (same DiffView.xaml.cs edit, since both handlers share the GoToLine method) -- this step just verified it and made one small robustness tweak (reordering Focus()/SelectedItem/ScrollIntoView so an explicit ScrollIntoView always runs last). That reordering turned out not to be fixing a real bug -- initial verification attempts appeared to fail only because of a testing-harness mistake (cropping the wrong screen region for this window's actual layout), confirmed once a correctly-sized crop showed line 35 properly scrolled-to and highlighted with both the pre- and post-reorder code. Kept the reorder since it is a harmless, arguably more-correct precaution, but it should not be read as a bug fix.

### Step 5: Wire Ctrl+G and Ctrl+F into the merge-conflict manual-edit pane ✓
**Why**: The manual-edit `ConflictResultEditBox` currently has no key handling wired at all (`Views/MergeConflictEditorWindow.xaml:858-870` sets only `IsVisibleChanged`) — the three read-only panes already have Ctrl+F via `ConflictTextSelection_KeyDown` (`Views/MergeConflictEditorWindow.xaml.cs:512-546`), but that handler targets `ListView` selections and doesn't apply to AvalonEdit.
**What**: Install AvalonEdit's built-in `ICSharpCode.AvalonEdit.Search.SearchPanel` on `ConflictResultEditBox` (gives Ctrl+F find for free via AvalonEdit's own key handling) and wire Ctrl+G to the Step 3 Go-to-Line prompt on the same control. Also wire Ctrl+G on the three read-only panes' existing `ConflictTextSelection_KeyDown` handler so all four conflict-window surfaces have Ctrl+G (per FR3).
**Touches**: `Views/MergeConflictEditorWindow.xaml`, `Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ctrl+F in the manual-edit pane opens AvalonEdit's search panel and finds text (manual)
- [x] Ctrl+G in the manual-edit pane and in each of the three read-only panes opens the Go-to-Line prompt and jumps to the requested line (manual)
- [x] Existing Ctrl+F behavior on the three read-only panes is unchanged (manual)
**Risk**: None.
**Deviations**: None

### Step 6: Fix hunk navigation to scroll the target hunk to the top of the viewport ✓
**Why**: `RepositoryViewModel.Diff.cs:704-726 NavigateHunk` fires `ScrollToDiffItemRequested`, handled in `MainWindow.xaml.cs:452-464 OnScrollToDiffItemRequested` via plain `lv.ScrollIntoView(item)`. WPF's `ScrollIntoView` only scrolls the minimum distance needed, which lands the item at whichever edge it was approached from — moving forward through hunks, that's the bottom.
**What**: Replace the `ScrollIntoView` call with a direct `ScrollViewer.ScrollToVerticalOffset` computed from the target item's index × fixed row height (per `PickleGit/CLAUDE.md`'s virtualization notes on this view's fixed-height rows), positioning the hunk header at/near the top of the viewport.
**Touches**: `MainWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Navigating to the next hunk in a multi-hunk diff scrolls the hunk header to the top of the viewport (manual)
- [x] Navigating to the previous hunk does the same (manual)
- [x] Navigating to the last hunk near the end of the file doesn't scroll past the end of the list / leave a blank gap (manual)
**Risk**: A hand-computed offset can overshoot at the start/end of the list where there isn't enough content above/below to reach the target row all the way to the top.
**Mitigation**: Clamp the computed offset to `[0, ScrollableHeight]`.
**Deviations**: Refactored slightly beyond the plan's literal Touches (MainWindow.xaml.cs only): moved the actual scroll-offset computation into a new internal method DiffView.ScrollDiffItemToTop(object) in Views/DiffView.xaml.cs, and had MainWindow.OnScrollToDiffItemRequested delegate to it, instead of re-implementing a fresh VisualTreeHelper ScrollViewer search in MainWindow.xaml.cs. Reason: DiffView already caches reliable ScrollViewer references (_unifiedScroll/_leftScroll/_rightScroll) established via the documented ApplyTemplate()-before-search pattern (see PickleGit/CLAUDE.md); a first attempt using a fresh VisualTreeHelper search from MainWindow silently found a ScrollViewer whose ExtentHeight/ScrollableHeight were not yet reflecting the true virtualized content size, so scrolling silently no-op'd. Confirmed via temporary diagnostic logging, removed after diagnosis.

### Step 7: Only force the Staged/Unstaged view on a conflict *transition*, not every refresh tick ✓
**Why**: `RepositoryViewModel.cs` (~line 1383) runs `if (conflict.HasConflicts) ShowWorkingDir = true;` unconditionally on every `RefreshAsync` tick, including ordinary `WorkingDir`-classified ticks during an already-known conflict — so re-editing a conflicting file re-forces the Staged/Unstaged view even if the user had deliberately navigated elsewhere while resolving the conflict.
**What**: Track whether the previous refresh already had `HasConflicts == true`; only force `ShowWorkingDir = true` when conflict state transitions from absent to present, not on every subsequent tick while it remains present.
**Touches**: `RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Starting a conflicted merge still forces the Staged/Unstaged view on the first tick (manual)
- [x] Navigating away from Staged/Unstaged while the conflict is still outstanding, then editing a conflicting file again, does not force the view back (manual)
- [x] Resolving the conflict still behaves as before (manual)
**Risk**: If the "previous conflict state" tracking isn't reset correctly across repo/tab switches, a stale flag could suppress the initial forced switch for a newly opened conflicted repo.
**Mitigation**: Reset the tracked state whenever the repository/tab is (re)loaded, not only inside `RefreshAsync`.
**Deviations**: The plan's Touches for this step listed only RepositoryViewModel.cs, but the same unconditional 'if (conflict.HasConflicts) ShowWorkingDir = true;' pattern also existed as a second, independent occurrence in RepositoryViewModel.Staging.cs's RefreshConflictStateAsync (the lighter WorkingDir-only refresh path — this is actually the one that runs for the user's literal reported scenario, editing a conflicting file again). Discovered via live testing: the RepositoryViewModel.cs-only fix did not actually resolve the bug when reproducing it end-to-end (view still got forced back to Staged/Unstaged), tracing back to this second call site. Applied the identical conflict-transition guard there too.

### Step 8: Don't reload commit detail when the reselected commit's sha is unchanged ✓
**Why**: After any `GraphNodes` rebuild, `RefreshAsync` (~lines 1417-1469) re-finds the previously selected node by sha and reassigns it to `SelectedNode` unconditionally — even when the found node has the exact same sha as what's already selected. `SelectedNode`'s setter (`RepositoryViewModel.cs:157-169`) always calls `OnSelectedNodesChanged()`, which always calls `LoadCommitDetail(sha)` (`RepositoryViewModel.Detail.cs:44-71`), which unconditionally clears `SelectedFile` and resets `CommitFiles` — hiding whatever diff was open, even though nothing about the selection actually changed. This is the mechanism behind the hash-search-then-VS-compiles scenario.
**What**: Before reassigning `SelectedNode` in the post-rebuild reselect path, compare the found node's sha against the currently selected sha; if unchanged, skip the reassignment (and thus skip the redundant `LoadCommitDetail` call) entirely.
**Touches**: `RepositoryViewModel.cs`, `RepositoryViewModel.Detail.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Select a commit via hash search, then trigger a WorkingDir-only refresh (external file edit) → the commit's diff remains displayed, not reset to Staged/Unstaged (manual)
- [x] Select a working-dir file's diff, then trigger the same kind of refresh → the diff remains displayed (manual)
- [x] Genuinely selecting a *different* commit still loads that commit's detail correctly (manual)
- [x] A refresh where the previously selected sha is no longer present in the rebuilt graph still falls back gracefully (unchanged existing behavior — not a regression target of this step) (manual)
**Risk**: If the sha-equality check is placed incorrectly, a genuine commit switch could be mistaken for a no-op and fail to reload.
**Mitigation**: Compare shas immediately before the existing `SelectedNode = restored` assignment, not earlier in the method, so no other code path is short-circuited.
**Deviations**: Found and fixed a SECOND, independent occurrence of the identical bug in ApplyFilter() (RepositoryViewModel.cs) -- not mentioned in the plan's Touches or investigation, since the original investigation only examined RefreshOnceAsync. ApplyFilter is a separate GraphNodes-rebuild path triggered whenever _hasUncommittedChanges flips (e.g. a new untracked file appearing, or the search text changing) and it had the exact same unconditional 'SelectedNode = restored' reselect bug. Discovered via live end-to-end testing: the RefreshOnceAsync-only fix did not actually resolve the user's reported hash-search scenario (diff still got hidden after an external file change), traced to ApplyFilter firing instead of RefreshOnceAsync for that trigger. Extracted a shared ReselectSameCommit(GraphNode) helper and applied it at both call sites to avoid duplicating the fix.

## After Implementation
- Run `pkl:static-analyze` — Steps 6-8 touch scroll/refresh logic in compiled `.cs` files with real regression risk (selection state, virtualized scrolling); Steps 1-5 are XAML/isolated key-handler changes with lower risk but are included in the same pass since it's a single build.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-09-28 12:00 -->

**Deviations:** Step 2's Typography-attached-property approach had no effect on AvalonEdit (root-caused to a hand-written `TextRunTypographyProperties` subclass + colorizer instead — anticipated in the step's own Risk/Mitigation, not a scope change). Step 6 moved the scroll-offset computation from `MainWindow.xaml.cs` into a new `DiffView.ScrollDiffItemToTop` method after a fresh `VisualTreeHelper` search returned a stale `ScrollViewer`. Steps 7 and 8 each turned out to have a **second, independent occurrence of the identical bug** (`RepositoryViewModel.Staging.cs`'s `RefreshConflictStateAsync` for Step 7; `ApplyFilter()` for Step 8) that the original investigation missed — both found only by reproducing the reported scenario end-to-end after the first fix looked complete on code review alone.
**Steps planned vs. actual:** 8 planned / 8 actual — matched, no steps added/dropped/merged.
**Process improvement:** When a step's fix targets "an unconditional bad pattern in method X," grep the whole codebase for other occurrences of that same pattern before writing the step's Touches list — two of eight steps here had a second independent occurrence that only surfaced through live end-to-end reproduction after the single-occurrence fix already looked done.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
