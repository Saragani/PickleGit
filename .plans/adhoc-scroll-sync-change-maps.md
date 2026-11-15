---
issue: adhoc-scroll-sync-change-maps
title: Fix multi-pane scroll sync and add change maps to Ours/Theirs panes
type: bug
component: MergeConflictEditorWindow, DiffView (side-by-side)
phase: SHIP
step: 4
next: Commit, merge to main, push
run_mode: auto
updated: 2026-11-15 20:56
---

# Plan: adhoc-scroll-sync-change-maps — Fix multi-pane scroll sync and add change maps to Ours/Theirs panes

## Spec
### Problem Statement
In the merge-conflict editor the three panes (Ours / Theirs / Result) fall out of vertical sync:
- On open, Ours starts at line 3 while Theirs starts at line 1.
- Clicking a conflict marker on the Result pane's change map scrolls Result only — Ours/Theirs stay put.
- Fast mouse-wheel scrolling on Theirs leaves Ours and Result behind.

Root cause: `MergeConflictEditorWindow.SyncScroll` uses one shared `_syncingScroll` flag held until `DispatcherPriority.ContextIdle`. `ScrollChanged` is delivered after layout, not inside `ScrollToVerticalOffset`, so a single boolean cannot tell "echo of my own push" from "genuine scroll" — while the flag is up every genuine event from any pane is dropped. `DiffView`'s side-by-side `SyncScroll` has the mirror defect: the flag is reset synchronously, so it never blocks the deferred echoes (vertical is harmless since both panes bind the same `SideBySideItems`; a horizontal clamp snap-back is suspected when one side's content is narrower — to be reproduced first).

Additionally the user wants the conflict change-map strip (currently only on Result) on the Ours and Theirs panes too.

### Requirements
- FR1: A reusable scroll-sync helper suppresses only echoes of its own pushes (per-member expected offset, clamp-tolerant), and propagates every genuine scroll of any member.
- FR2: Merge editor: all three panes stay vertically synced under fast wheel / scrollbar drag / Page Up/Down on any pane. Ours↔Theirs horizontally synced; Result horizontally independent.
- FR3: Merge editor: block navigation (initial open, Prev/Next) aligns each pane on the target block independently (no pixel overwrite between panes with different row sets).
- FR4: Merge editor: a Result change-map click on a conflict-block row also brings Ours/Theirs to that block; on a context row, falls back to normal approximate sync.
- FR5: DiffView side-by-side uses the same helper — vertical + horizontal sync with no echo snap-back.
- FR6: Ours and Theirs panes each show a change-map strip: conflict-block rows red while unresolved, green once resolved; click jumps that pane (others follow via sync).

### Acceptance Criteria
- AC1 [derived]: Given a conflicted file open, When the editor first shows, Then Ours and Theirs show the same top line, and all three panes show the first conflict block.
- AC2 [derived]: Given the merge editor, When fast-wheel-scrolling any one pane, Then the other two panes follow and end aligned when scrolling stops.
- AC3 [derived]: Given the merge editor, When clicking a conflict marker on Result's change map, Then Ours and Theirs scroll to the same conflict block.
- AC4 [derived]: Given the merge editor, When pressing Prev/Next, Then all three panes show the target block.
- AC5 [derived]: Given the side-by-side diff with one side's lines much longer, When scrolling horizontally on either pane, Then no snap-back occurs; vertical sync still works.
- AC6 [derived]: Given the merge editor, When viewing Ours/Theirs, Then each has a change-map strip with red markers for unresolved blocks that turn green when resolved, and clicking a marker jumps there.

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- SDK-style csproj with globbing — new `Controls/ScrollSyncGroup.cs` needs no csproj edit.
- Template-part lookup must keep the existing `ApplyTemplate()` + `FindDescendant<ScrollViewer>` pattern (see PickleGit/CLAUDE.md).
- `DiffChangeMapControl` brushes are already frozen statics — reuse, don't re-allocate.
- Non-goal: line-anchored Result↔pane sync during free scrolling (row sets differ; stays approximate by pixel). Follow-up candidate.

## Tests
### Manual / Black-Box
- [ ] Open a conflicted file in the merge editor → Ours/Theirs top line identical; first block visible in all three panes  <!-- AC1 -->
- [ ] Fast wheel on Theirs, then Ours, then Result → other panes track and end aligned  <!-- AC2 -->
- [ ] Click a red marker on Result's change map → Ours/Theirs show the same block  <!-- AC3 -->
- [ ] Prev/Next across a multi-conflict file → all three panes on the target block  <!-- AC4 -->
- [ ] Side-by-side diff with mismatched line widths → horizontal scroll on either pane, no snap-back; vertical sync intact  <!-- AC5 -->
- [ ] Ours/Theirs change maps show red markers; resolve a block → markers turn green; click marker → pane jumps  <!-- AC6 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh adhoc-scroll-sync-change-maps N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Add ScrollSyncGroup helper ✓
**Why**: One echo-aware mechanism replaces two divergent boolean guards (one too sticky, one a no-op). Echo detection by expected offset is timing-independent, unlike any dispatcher-priority reset.
**What**: New `Controls/ScrollSyncGroup.cs`. `Add(ScrollViewer sv, bool syncHorizontal)` subscribes to `ScrollChanged`. On a member's event: if `VerticalChange == 0 && HorizontalChange == 0` ignore; if the member has a pending expected offset and its actual offset matches `min(expected, Scrollable{Height,Width})` within 0.5px → consume as echo; otherwise genuine → for every other member record expected offset and call `ScrollToVerticalOffset` (and `ScrollToHorizontalOffset` only when both source and target are horizontal-sync members). `RunIndependent(Action)` suppresses broadcasting while the action's scrolls settle (released at `DispatcherPriority.Loaded` after layout), then clears pending expectations. `Remove`/`Clear` for teardown. Not wired anywhere yet.
**Touches**: `PickleGit/Controls/ScrollSyncGroup.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Solution builds with no new warnings  (manual)
- [x] Code review: echo path only consumes when actual offset matches clamped expectation; genuine path never drops an event  (manual)
**Risk**: A target that never fires ScrollChanged (offset already equal) leaves a stale expectation that later swallows a genuine scroll landing on that same value.
**Mitigation**: Skip pushing (and recording) when target offset already equals the clamped value.
**Deviations**: None

### Step 2: Adopt ScrollSyncGroup in the merge editor ✓
**What**: Replace `_syncingScroll`/`SyncScroll` in `MergeConflictEditorWindow.xaml.cs` with a `ScrollSyncGroup`: Ours + Theirs added with `syncHorizontal: true`, Result with `false`. Keep the text-selection `Recompute()` hooks on each pane's `ScrollChanged`. Fix the horizontal-sync comment (~:354) to match. `OnScrollToBlockRequested` wraps its three `ScrollIntoView` calls in `group.RunIndependent(...)`. `ConflictResultChangeMap_JumpRequested` (list mode): resolve the target Result row; if it has a `BlockVm`, `RunIndependent` scroll Result to the row and Ours/Theirs to that block's `BlockToolbar` pane row; else plain `ScrollIntoView` on Result (group syncs). Ensure the initial `ScrollToBlockRequested` (if fired before all three ScrollViewers are wired) still lands aligned — re-run block alignment once all three panes are wired if a block is current.
**Touches**: `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] On open, Ours/Theirs show the same top line; first conflict visible in all three  (manual)
- [x] Fast wheel on each of Ours, Theirs, Result — other panes follow and end aligned  (manual)
- [x] Result change-map click on a conflict marker brings Ours/Theirs to that block  (manual)
- [x] Prev/Next aligns all three panes on the target block  (manual)
- [x] Horizontal scroll on Ours moves Theirs, not Result  (manual)
**Risk**: Initial-open ordering — `ScrollToBlockRequested` may fire before the `_Loaded` deferred wiring runs.
**Mitigation**: Trigger block alignment from the last pane's wiring callback when a current block exists.
**Deviations**: None

### Step 3: Adopt ScrollSyncGroup in DiffView side-by-side ✓
**What**: First reproduce the suspected horizontal snap-back in side-by-side mode (file with much longer lines on one side) via the `run` skill and record the result. Then replace `_syncingScroll`/`SyncScroll` in `DiffView.xaml.cs` with a `ScrollSyncGroup` (both panes `syncHorizontal: true`), keeping the text-selection `Recompute()` hooks. `SideBySideChangeMap_JumpRequested` stays as-is (drives left; group syncs right).
**Touches**: `PickleGit/Views/DiffView.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Vertical wheel/drag on either side-by-side pane keeps both aligned  (manual)
- [x] Horizontal scroll past the narrower side's max on the wider side → no snap-back  (manual)
- [x] Side-by-side change-map click still moves both panes  (manual)
**Risk**: Snap-back doesn't reproduce — change is then a consistency refactor only.
**Mitigation**: Still adopt (removes dead guard); note the repro outcome in Deviations.
**Deviations**: Suspected horizontal snap-back did not reproduce on the old build (user-verified); adopted ScrollSyncGroup anyway per plan mitigation — removes the no-op guard, behavior unchanged.

### Step 4: Add change maps to Ours/Theirs panes ✓
**What**: In `MergeConflictFileViewModel` add `PaneRowKinds` (`IReadOnlyList<DiffLineKind>`), built alongside `ResultRowKinds` from the `pane` list: rows with a `BlockVm` → `Added` if `IsResolvedEffective` else `Deleted`; others → `Context`. In `MergeConflictEditorWindow.xaml`, give the Ours and Theirs pane `Grid`s a `*` + `10` column layout mirroring Result's, hosting a `DiffChangeMapControl` bound to `PaneRowKinds` with `JumpRequested` handlers. Handlers in code-behind map fraction → pane item index and `ScrollIntoView` on that pane (group syncs the rest).
**Touches**: `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`, `PickleGit/Views/MergeConflictEditorWindow.xaml`, `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ours and Theirs show change-map strips with red markers at conflict blocks  (manual)
- [x] Resolving a block turns its markers green on Ours/Theirs (and Result, unchanged behavior)  (manual)
- [x] Clicking a marker on Ours or Theirs jumps that pane; other panes follow  (manual)
- [x] Find bars / copy-selection overlays in Ours/Theirs still positioned correctly with the extra column  (manual)
**Risk**: Extra column shifts the floating find bar or selection overlay geometry.
**Mitigation**: Keep the ListView in column 0 and the find-bar overlay spanning only column 0.
**Deviations**: None

## After Implementation
- Manual smoke pass via the `run` skill over the full Tests list (broad UI surface across two views). ✓ — user-driven on a `--test-instance` (live desktop, so no scripted input); all GREEN.

### Post-BUILD changes (user-approved, from review + retest)
- Review (`.claude/reviews/adhoc-scroll-sync-change-maps.md`, READY TO MERGE, 1 MEDIUM / 5 LOW) — fixed the MEDIUM and the worthwhile LOWs:
  - Block alignment was lost on the next scroll (absolute-offset broadcast). `ScrollSyncGroup` now takes an `alignmentKey`: members with the same row set (Ours/Theirs, side-by-side L/R) sync absolute offsets; Result follows by scroll delta and snaps to the same edge at top/bottom.
  - Dispatcher captured at construction (not `Dispatcher.CurrentDispatcher` at call time); `OriginalSource` filter for nested ScrollViewers; Ours/Theirs change-map clicks block-align all three panes; re-align on file switch; one fraction→row path.
- User retest: change-map jumps left the target change below the fold when jumping downward (`ScrollIntoView` scrolls minimally — row lands on the bottom edge), in both the merge editor and the diff view. Added `Controls/ListViewScrollHelper.ScrollToTop` (target near the top with ~3 rows of context) and `DiffChangeMapControl.ResolveTargetRow` (snap a click to the start of the nearest changed run within 4px). First version positioned in a second dispatcher hop and flickered; reworked to run synchronously (`BringIndexIntoViewPublic` + `UpdateLayout` + final offset before render). User retest: all GREEN.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-11-15 20:57 -->

**Deviations:** Step 3's suspected horizontal snap-back didn't reproduce (adopted anyway as a consistency fix). Post-BUILD: review MEDIUM (block alignment lost on next scroll → delta sync across row sets) and user retest (jumps left the change below the fold → top-positioning helper; first version flickered → made synchronous).
**Steps planned vs. actual:** 4 planned / 4 actual + 2 unplanned post-BUILD rounds (review fixes, jump positioning) — the plan's stubs only checked "pane jumps there", not "the target change is visible".
**Process improvement:** For any "jump/scroll to X" stub, phrase it as "X is visible in the viewport after jumping from above AND from below" — `ScrollIntoView` scrolls minimally, so a downward jump leaves the target on the bottom edge and a one-direction check passes falsely.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
