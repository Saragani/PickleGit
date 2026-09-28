---
issue: adhoc-conflict-manual-edit-find-bar
title: Replace AvalonEdit's SearchPanel with a themed ResultFind bar in the merge-conflict manual-edit pane
type: bug
component: MergeConflictEditor
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-28 15:56
---

# Plan: adhoc-conflict-manual-edit-find-bar — Replace AvalonEdit's SearchPanel with a themed ResultFind bar in the merge-conflict manual-edit pane

## Spec
### Problem Statement
The merge conflict editor's manual-edit Result pane (the AvalonEdit `TextEditor` shown once "Edit Manually" is clicked) gets its Ctrl+F find UI from `SearchPanel.Install(ConflictResultEditBox)` — AvalonEdit's own stock-styled search popup. This looks visually inconsistent with the Left/Right/Result-in-diff-mode panes, which each use PickleGit's own themed per-pane find bar (`DarkTextBox`/`ToolbarButton`-styled `Border`, driven by a `PaneFindState` instance — `LeftFind`/`RightFind`/`ResultFind`). The `ResultFindBox` bar already exists in XAML and is already positioned (`Panel.ZIndex="20"`) to overlay the AvalonEdit editor too, but nothing currently routes Ctrl+F to it while the editor has focus, and its match computation (`FindMatchesInResult`) reads the read-only `ResultItems` rows, which go stale once `IsManuallyEdited` is true (live content is in `ResultText` / the AvalonEdit document instead).

### Requirements
- FR1: Pressing Ctrl+F while the manual-edit AvalonEdit pane has focus opens the same themed `ResultFindBox` bar used by the other three panes — not AvalonEdit's own SearchPanel.
- FR2: Typing a search term while manually editing finds matches in the live editor text (not stale `ResultItems`), including text the user just typed.
- FR3: Next/Prev navigate matches and scroll the current match into view inside the AvalonEdit editor.
- FR4: The current match is highlighted inside the editor using the same search-match color the other panes use (`DiffSearchMatchBrush`).
- FR5: Esc/✕ close the bar the same way they do for the other three panes.

### Acceptance Criteria
- AC1: Given a conflicted file switched to manual-edit mode, when the user presses Ctrl+F with the editor focused, then the themed find bar (matching the Left/Right panes' look) appears — not AvalonEdit's stock search popup.
- AC2: Given the themed bar is open and the manually-edited text contains a term on multiple lines, when the user types that term, then the status shows the correct total match count computed from the live text, including edits made after the bar was opened. [derived]
- AC3: Given multiple matches exist, when the user presses Next/Prev, then the editor scrolls so the current match's line is visible and the matched span is highlighted in the shared search-match color. [derived]
- AC4: Given the bar is open, when the user presses Esc or clicks ✕, then the bar closes the same way it does for the other panes. [derived]

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- No MEF/DI container — this is plain code-behind + ViewModel wiring already established in `MergeConflictEditorWindow`/`MergeConflictEditorViewModel`.
- AvalonEdit is the one sanctioned third-party control here (`docs/adr/0003-no-third-party-controls.md`), scoped to this exact editable pane — removing its own `SearchPanel` add-on doesn't violate that scoping, it narrows AvalonEdit's footprint to just the editable text pane itself.
- `PaneFindState`'s match model is per-occurrence, not per-row (`AddOccurrences`) — the AvalonEdit-side match source must follow the same one-entry-per-occurrence contract, using a boxed line number as the match `Item` since there's no row object for editor text.
- Threading: none of this touches git operations — no `GitService.Executor` involvement.

## Tests
### Manual / Black-Box
- [ ] Switch a conflicted file to manual-edit mode, press Ctrl+F with the editor focused → themed bar appears (not AvalonEdit's own popup) — AC1
- [ ] Type a term appearing on several lines of the manually-edited text → status count matches live text occurrences; edit the text further while the bar stays open → count updates — AC2
- [ ] Press Next/Prev with matches scrolled off-screen → editor scrolls to bring the match into view and highlights it in the shared search-match color — AC3
- [ ] Press Esc, then reopen and click ✕ → bar closes both ways, same as the other panes — AC4

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Retire AvalonEdit's SearchPanel and route Ctrl+F/Esc to the themed ResultFind bar ✓
**Why**: Doing this first makes the themed bar openable from the editor immediately, so each later step's effect (match counting, then highlighting) is independently visible/testable in the running app rather than all landing invisibly behind a still-intercepted Ctrl+F.
**What**: Remove `SearchPanel.Install(ConflictResultEditBox);` from the constructor and the now-unused `using ICSharpCode.AvalonEdit.Search;` directive. Extend `ConflictResultEditBox_PreviewKeyDown` (currently only handles Ctrl+G) to also handle Ctrl+F — set `_sessionVm.ResultFind.IsOpen = true` and focus/select-all `ResultFindBox` (mirroring `OpenFind`'s behavior for the ListView panes) — and Esc, closing `ResultFind` the same way `HandleFindBoxKeyDown` does for the other panes' boxes.
**Touches**: `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ctrl+F with the AvalonEdit editor focused opens the themed bar, not AvalonEdit's stock popup (manual)
- [x] Ctrl+G (Go to Line) in the editor still works unchanged after the PreviewKeyDown handler extension (manual)
- [x] Esc while the bar is open closes it from editor focus too (manual)
**Risk**: None — `SearchPanel.Install` has no other consumer in this file, and removing it only drops AvalonEdit's own find/replace UI, which this change intentionally replaces.
**Deviations**: None

### Step 2: Compute Find matches against the live manually-edited text ✓
**Why**: `FindMatchesInResult` currently only reads `ResultItems`, which stops being updated once `IsManuallyEdited` is true — matches must instead be found in `ResultText`, split per line to preserve `PaneFindState`'s per-occurrence contract, using AvalonEdit-compatible line numbering (`StringReader.ReadLine()` splits on `\r\n`/`\r`/`\n` the same way AvalonEdit's own `TextDocument` does, keeping line numbers consistent for Step 3's highlight targeting).
**What**: In `MergeConflictEditorViewModel.cs`, branch `FindMatchesInResult(term)`: when `file.IsManuallyEdited`, iterate `file.ResultText` line-by-line (1-based line numbers) and call `AddOccurrences` per line using the boxed line number as `Item`; otherwise keep the existing `ResultItems` path unchanged. Add `nameof(MergeConflictFileViewModel.ResultText)` and `nameof(MergeConflictFileViewModel.IsManuallyEdited)` cases to `OnCurrentFilePropertyChanged` that call `ResultFind.Invalidate()`, so an open search stays correct as the user types or toggles modes.
**Touches**: `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] With the bar open (Step 1) and a term appearing on several lines of manually-edited text, status count reflects the live text's actual occurrence count (manual)
- [x] Typing further text into the editor while the bar stays open updates the match count without closing/reopening the bar (manual)
- [x] Next/Prev cycle through the correct number of matches and wrap around at the ends (manual)
**Risk**: Off-by-one or mismatched line-splitting versus AvalonEdit's own line numbering would silently misplace Step 3's highlight even though the match count itself looks correct.
**Mitigation**: Use `StringReader.ReadLine()` (same delimiter handling as AvalonEdit's `TextDocument`) rather than a manual `Split`, and verify Step 3's highlight lands on the visually correct line before marking this step's own stubs GREEN is not required — but flag if Step 3 reveals a mismatch, since the fix would live here.
**Deviations**: None

### Step 3: Highlight the current match and scroll it into view in the editor ✓
**Why**: Matches by themselves aren't visible without a highlight and a way to bring an off-screen match on-screen — AvalonEdit has no built-in per-span "current match" highlight to reuse, so this needs its own small colorizer, following the same pattern as the existing `LigatureSuppressingColorizer`.
**What**: Add a `DocumentColorizingTransformer` that reads `_sessionVm.ResultFind.CurrentMatch` (line number) / `CurrentMatchRange` (start, length) and paints that span's background with `ThemeBrushes.Get("DiffSearchMatchBrush", ...)`; redraw the affected line (not the whole document) when those properties change while `IsManuallyEdited`. Extend `OnResultFindScrollRequested` with an `IsManuallyEdited` branch that moves the AvalonEdit caret to the match offset and brings it into view (`Caret.BringCaretToView()` after positioning, or `ScrollToLine` + caret offset) instead of `ConflictResultListView.ScrollIntoView`.
**Touches**: `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] With matches scrolled off-screen, pressing Next scrolls the editor so the match's line becomes visible (manual)
- [x] The current match's exact substring is highlighted in the same amber/gold color the other panes use for search matches (manual)
- [x] Pressing Next/Prev moves the highlight to the correct next/previous occurrence, including across lines (manual)
**Risk**: Redrawing the whole document on every navigation step could be noticeably slow on a very large manually-edited file.
**Mitigation**: Redraw only the previously- and newly-highlighted lines (via the `DocumentLine`-scoped `TextView.Redraw` overload) rather than the whole `TextView`.
**Deviations**: None

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
