---
issue: adhoc-multiselect-file-diff
title: Show range diff when a file is selected in multi-commit Files changed
type: bug
component: CommitDetailView / RepositoryViewModel.Detail
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-11-09 09:55
---

# Plan: adhoc-multiselect-file-diff — Show range diff when a file is selected in multi-commit Files changed

## Spec
### Problem Statement
When multiple commits are selected, clicking a file in the aggregated "Files changed" panel shows no diff.

Root cause:
- The flat aggregated `ListView` (`Views/CommitDetailView.xaml:799`) and tree aggregated `ListView` (`:832`) have no `SelectedItem` binding, so a click never reaches the VM.
- `RepositoryViewModel.SelectedFile`'s setter (`RepositoryViewModel.cs:332-334`) only loads a diff in comparison mode, for `_detailCommit`, or for the working dir. Multi-select sets `DetailCommit = null` (`RepositoryViewModel.Detail.cs:113`), so no branch fires.
- `ShowDiffInsteadOfCommits` returns false when `DetailCommit == null`, so the diff pane would not show even if a diff were loaded.

Chosen approach: a range diff from the oldest selected commit's first parent to the newest selected commit. If the selection is non-contiguous, the diff also includes changes from unselected commits in between. The user accepted this.

### Requirements
- FR1: Selecting a file in the multi-select flat list loads that file's range diff into the diff pane.
- FR2: Selecting a file row in the multi-select tree list does the same; folder rows do nothing.
- FR3: Range = first parent of the oldest selected commit (by graph order) → newest selected commit (by graph order). The synthetic "uncommitted changes" node is excluded. If the oldest commit is a root commit (no parent), diff against an empty tree.
- FR4: The middle pane switches to the diff (`ShowDiffInsteadOfCommits` true), and the header shows the file and the commit count.
- FR5: Changing the commit selection clears the aggregated file selection and the stale diff.

### Acceptance Criteria
- AC1 [derived]: Given 2+ commits selected, when I click a file in the flat Files changed list, then its combined diff across the range appears in the diff pane.
- AC2 [derived]: Given tree view mode, when I click a file row, then the same diff appears; clicking a folder row shows no diff.
- AC3 [derived]: Given a range diff is showing, when I change the commit selection (single commit or another multi-select), then the old diff is gone and no file is selected.
- AC4 [derived]: Given a selection that includes the repo's root commit, when I click a file it added, then the diff shows the file as fully added.

## Constraints
- C# 7.3 / .NET 4.7.2.
- All LibGit2Sharp calls go through `GitService.Executor`. The setter fires the load fire-and-forget with a try/catch (same pattern as `LoadComparisonDiffAsync`).
- The existing two-sha `GitService.GetFileDiff(shaA, shaB, path)` returns empty when `shaA` is null, so it cannot handle a root-commit base. It needs a variant that accepts a parent-less base.
- `AggregatedFileChange` is not a `FileChange`. The selection must map to a `FileChange` for `SelectedFile`, so the shared diff-pane plumbing (`DiffPaneHeaderText`, `DiffFileSwitched`, `DiffPaneMode`) keeps working.

## Tests
### Manual / Black-Box
- [ ] Ctrl-click 2+ commits → click a file in the flat Files changed list → the range diff shows in the middle pane  <!-- AC1 -->
- [ ] Switch to tree mode → click a file row → diff shows; click a folder row → nothing changes  <!-- AC2 -->
- [ ] With a range diff showing → click a single commit → the commit list or the single-commit panel shows, no stale diff  <!-- AC3 -->
- [ ] Multi-select including the root commit → click a file added there → the diff shows all lines added  <!-- AC4 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh adhoc-multiselect-file-diff N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Add a range file-diff method to GitService ✓
**Why**: The existing two-sha `GetFileDiff` needs both endpoints to exist as commits. A range starting at a root commit has no base commit, so its base must be an empty tree.
**What**: Add `GetRangeFileDiff(string oldestSha, string newestSha, string filePath, int contextLines = DefaultContextLines)`. It looks up both commits, uses `oldest.Parents.FirstOrDefault()?.Tree` as the base (null = empty tree, same as the single-commit overload), and diffs against `newest.Tree`. It applies the same `DecodedTextFallback` (`stageable: false`) and `SyntaxHighlighter.Apply` as the two-sha overload, with a null-safe `SourceAt` for the base side.
**Touches**: `PickleGit/Services/GitService.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Builds clean; the method mirrors the two-sha overload except for the parent-based, null-tolerant base  (manual)
**Risk**: `DecodedTextFallback`'s source lambda may not tolerate a null commit.
**Mitigation**: Return null or empty text from the base-side lambda when there is no parent; check how the single-commit overload handles the root case and match it.
**Deviations**: None

### Step 2: Load the range diff when a file is selected during multi-select ✓
**What**: In `RepositoryViewModel.Detail.cs`, add `LoadRangeDiffAsync(path)`. It computes oldest and newest selected SHAs by `GraphNodes` index (uncommitted excluded), clears the diff state as `LoadComparisonDiffAsync` does, calls `GetRangeFileDiff` on the executor, then `ApplyDiffResult`, with try/catch + `DialogService.ShowError`. In `RepositoryViewModel.cs`: the `SelectedFile` setter gets `else if (IsMultiSelection) _ = LoadRangeDiffAsync(value.Path);`. `ShowDiffInsteadOfCommits` returns true when `IsMultiSelection`. `DiffPaneHeaderText` shows `Diff ({SelectedCount} commits): {path}` in multi-select.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Detail.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Logic review: range endpoints come from graph order and not dates; uncommitted node excluded; no git call on the UI thread  (manual)
**Risk**: A stale range diff could arrive after the selection changes.
**Mitigation**: Guard with the `_aggregationVersion` counter (or a dedicated version counter) and drop results that arrive after a selection change.
**Deviations**: None

### Step 3: Wire the aggregated lists' selection to SelectedFile ✓
**What**: Add `SelectedAggregatedFile` (`AggregatedFileChange`) and `SelectedAggregatedFilesTreeRow` (`AggregatedFileTreeRow`) properties via `Set`. Their setters map a file to `SelectedFile = new FileChange { Path, Kind }`; tree folder rows are ignored. Bind `SelectedItem` on both ListViews in `CommitDetailView.xaml` (flat :799, tree :832), TwoWay. In `OnSelectedNodesChanged`, clear both properties and `SelectedFile` whenever the selection changes.
**Touches**: `PickleGit/Views/CommitDetailView.xaml`, `PickleGit/ViewModels/RepositoryViewModel.Detail.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Multi-select 2+ commits, click a file in the flat list → range diff shows in the middle pane  (manual)
- [x] Tree mode: file row → diff shows; folder row → no change  (manual)
- [x] With a diff showing, click a single commit → no stale diff, no file selected  (manual)
- [x] Selection including the root commit → added file shows as fully added  (manual)
**Risk**: Rebuilding `AggregatedFilesTreeRows` (sort or collapse) drops the tree's selected row, and the TwoWay binding could null `SelectedFile`.
**Mitigation**: In the tree-row setter, ignore null while a rebuild is in progress (same idea as `_syncingTreeSelection`), or re-select the row matching `SelectedFile.Path` after a rebuild.
**Deviations**: None

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-11-09 10:05 -->

**Deviations:** No step deviations. The post-build review found 2 HIGH + 2 MEDIUM issues (stale WIP diff leaking into multi-select, an aggregated row that couldn't be reopened, an unreset conflict banner, a background refresh narrowing the range); all were fixed and re-verified live before commit.
**Steps planned vs. actual:** 3 planned / 3 actual, plus one unplanned review-fix pass in the same 2 VM files.
**Process improvement:** When a plan adds a new SelectedFile source, add stubs for entering it from every other selection context (WIP file, single commit, comparison) and for close/reopen, so cross-context leaks are caught in BUILD, not review.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
