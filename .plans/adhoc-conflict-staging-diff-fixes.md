---
issue: adhoc-conflict-staging-diff-fixes
title: Merge-editor, staging tree, BOM diff, and conflict-resolve fixes
type: bug
component: Views/MergeConflictEditorWindow, Views/CommitDetailView, RepositoryViewModel, Services/GitService, Services/FileTreeBuilder
phase: BUILD
step: 14
next: BUILD complete — SHIP when ready
run_mode: auto
updated: 2026-11-02 13:25
---

# Plan: adhoc-conflict-staging-diff-fixes — Merge-editor, staging tree, BOM diff, and conflict-resolve fixes

## Spec
### Problem Statement
Seven defects reported by the user:

1. Merge-conflict manual-edit pane: deleting text misplaces the caret.
2. Merge-conflict manual-edit pane: clicking a conflict marker on the scrollbar change-map doesn't scroll the manual-edit view.
3. Staged/Unstaged files in Tree view should auto-expand by default.
4. Staged/Unstaged files in Tree view: selecting a file doesn't show it in the diff view.
5. Textual Staged/Unstaged files are shown as "binary" when the committed blob or the working file has a UTF-16 or UTF-8 BOM.
6. Double-clicking "Open Merge Editor" (or clicking again before the window appears) opens 2+ windows.
7. Unstaged conflicts: with several files selected, right-click "Resolve using ours/theirs" affects only the first selected file.

User scope decisions (2026-11-02):
- Auto-expand also applies to the commit "Files changed" tree.
- The commit "Files changed" tree's selection-to-diff gap is fixed too.
- Staged/Unstaged trees get real multi-select (Ctrl/Shift); a selected folder row stands for every file beneath it.
- BOM/UTF-16 text must render as text at any file size — no line-count cap that falls back to the "binary" notice.
- "Mark resolved" gets the same multi-file fix as resolve ours/theirs.

Root-cause findings:
- B1: `ConflictResultEditBox_TextChanged` (MergeConflictEditorWindow.xaml.cs:421) writes `ResultText` on every keystroke → `OnCurrentFilePropertyChanged` (MergeConflictEditorViewModel.cs:1261) → `ResultFind.Invalidate()` → `PaneFindState.Recompute()` (PaneFindState.cs:74-84) calls `Navigate(+1)` → `ScrollToMatchRequested` → `OnResultFindScrollRequested` (xaml.cs:289) sets `Caret.Offset` to the first match. Runs even with the find bar closed if a search term lingers. **Hypothesis** — why only deletion shows it is unproven; Step 1 reproduces live before fixing.
- B2: `ConflictResultChangeMap_JumpRequested` (xaml.cs:854-862) only calls `ConflictResultListView.ScrollIntoView`, and that ListView is collapsed in manual-edit mode.
- B3: `FileTreeBuilder.Flatten`/`FlattenAggregated` (FileTreeBuilder.cs:56/129) expand only folders in an "expanded" set that starts empty (RepositoryViewModel.cs:255, 563-566).
- B4: the tree ListViews (CommitDetailView.xaml:1040-1052, ~1254, Files-changed tree ~630-636) have no `SelectedItem` binding — a selected `FileTreeRow` never reaches `SelectedFile`.
- B5: `ParsePatchEntries` (GitService.cs:1158-1172) trusts libgit2's `IsBinaryComparison` (NUL-byte heuristic → UTF-16 always binary). Same path serves unstaged, staged, history, and the ignore-whitespace git.exe path (`"Binary files "` check, GitService.cs:1149). No encoding-aware decode exists in the diff path.
- B6: `OpenMergeEditor` (RepositoryViewModel.Rebase.cs:220) is `async void` and awaits twice (:227, :239) before `ShowDialog` (:271); CanExecute (RepositoryViewModel.cs:899) has no reentrancy guard.
- B7: resolve commands (RepositoryViewModel.cs:886-889) take the single right-clicked `FileChange`; `ResolveConflictSideAsync` / `MarkResolvedAsync` (Rebase.cs:97-106) handle one path.

### Requirements
- FR1: Editing in the manual-edit pane (typing, Backspace, Delete, selection-delete) never moves the caret away from the edit point. Text edits may refresh find match counts but must not navigate/scroll to a match.
- FR2: Clicking a change-map marker while in manual-edit mode scrolls the AvalonEdit pane to the corresponding line.
- FR3: Folder rows in the Staged, Unstaged, and commit "Files changed" trees (plain and aggregated) are expanded by default; user collapse/expand toggles still persist across refreshes within the session as today.
- FR4: Selecting a single file row in the Staged, Unstaged, or "Files changed" tree shows that file's diff, exactly as flat mode does.
- FR5: Staged and Unstaged trees support Ctrl/Shift multi-select. The effective selection is the union of selected file rows plus every file beneath each selected folder row (deduplicated). Multi-file commands (stage, unstage, discard, resolve ours/theirs, mark resolved, and any other command using `ResolveTargets`) act on that effective set. The diff view shows a diff only when the effective set is exactly one file. Selection survives a tree rebuild for rows still visible.
- FR6: Diffs where either side is UTF-16 (LE/BE, with or without BOM) or UTF-8 with BOM render as text, at any file size, in staged, unstaged, history, and ignore-whitespace diffs. BOM characters are not shown. Genuinely binary content still shows the binary notice.
- FR7: Hunk/line stage, unstage, and discard work on decoded-text diffs (FR6) when both sides share the same encoding + BOM. `git apply` operates on raw bytes and can't apply a patch built from decoded text, so these diffs use an in-app path: decode the target side, apply the selected hunks/lines in text, re-encode with the original encoding + BOM + line endings, and write the result (index: new blob + index entry via LibGit2Sharp; discard: working file). Mixed-encoding diffs (e.g. UTF-16 → UTF-8-BOM) allow whole-file operations only — partial staging there would produce a mixed-encoding file.
- FR8: "Open Merge Editor" opens at most one window per invocation, regardless of repeated clicks while it is loading.
- FR9: "Resolve using ours", "Resolve using theirs", and "Mark resolved" apply to every selected conflicted file (flat and tree mode), not only the right-clicked one.
- FR10 (added 2026-11-02 after Step 10): Merge conflicts in UTF-16 (and other NUL-containing but decodable) text files — which git refuses to text-merge ("Cannot merge binary files"), leaving our version in the working file with no conflict markers — open in the merge editor as real conflicts: base/ours/theirs are read from the index stages as bytes, decoded, and three-way merged in-app; Theirs pane shows the real theirs content; non-conflicting changes from both sides merge automatically.
- FR11: Line picking (Select Mine/Theirs per block), manual edit, and Save & Stage work on those files and save in the file's original encoding, BOM and line endings.
- FR12: A conflicted file with no markers whose stages can't be decoded (genuinely binary), or whose working copy was already changed since the conflict, is never silently "resolved" to ours by Save & Stage: the editor shows a warning and blocks Save & Stage for it (Take Ours/Theirs still available from the file list).

### Acceptance Criteria
- AC1: Given manual-edit mode on a conflicted file (with and without a prior find term), when the user deletes characters via Backspace/Delete/selection-delete, then the caret stays at the deletion point. (FR1) [derived]
- AC2: Given manual-edit mode, when the user clicks a change-map marker, then the edit pane scrolls to that region. (FR2) [derived]
- AC3: Given Tree view, when Staged/Unstaged/Files-changed trees load, then all folders are expanded. (FR3) [derived]
- AC4: Given Tree view, when the user selects one file row, then its diff appears in the diff view. (FR4) [derived]
- AC5: Given Tree view, when the user Ctrl/Shift-selects multiple file rows and/or folders and invokes stage/unstage/resolve, then every file in the effective set is affected. (FR5, FR9) [derived]
- AC6: Given a file committed as UTF-16 and modified in the working dir (as UTF-16, and as UTF-8-BOM), and a UTF-8-BOM file, when selected in Unstaged/Staged/history, then a text diff appears with no BOM glyph; a >2000-line case also renders as text. (FR6) [derived]
- AC7: Given a same-encoding UTF-16 decoded-text diff, when the user stages/unstages/discards a hunk or selected lines, then exactly those changes move and the file stays UTF-16 (BOM + line endings preserved); given a mixed-encoding diff, then hunk/line UI is disabled and whole-file stage works. (FR7) [derived]
- AC8: Given conflicts exist, when the user double-clicks "Open Merge Editor", then exactly one window opens. (FR8) [derived]
- AC9: Given several conflicted files selected in flat mode, when the user picks resolve ours/theirs or mark resolved, then all of them are resolved. (FR9) [derived]
- AC10: Given a UTF-16 merge conflict (ours and theirs both changed line 5; ours-only line 10; theirs-only line 15), when the merge editor opens, then one conflict block (line 5) is shown, lines 10 and 15 are merged automatically, and the Theirs pane shows THEIRS content. (FR10) [derived]
- AC11: Given that file, when the user picks Theirs for the block and clicks Save & Stage, then the index/working file is UTF-16 with BOM and CRLF and contains line 5 THEIRS, line 10 OURS only, line 15 THEIRS; the same via manual edit. (FR11) [derived]
- AC12: Given a binary (non-text) conflict, when the merge editor opens, then a warning is shown and Save & Stage is disabled for that file. (FR12) [derived]

## Constraints
- .NET Framework 4.7.2 / C# 7.3.
- All LibGit2Sharp / git.exe calls through `GitService.Executor`; after CLI ops that mutate the index (`checkout --ours/--theirs`, `add`) call `GitService.Reopen()`.
- CLI arguments as discrete/quoted args via the shared `GitCli` runner (`CliGitService.Quote`) — never concatenated unquoted paths.
- No third-party controls; the tree remains a flattened `ListView` (no switch to a WPF `TreeView`).
- `RefreshAsync`'s state-signature skip must be preserved; tree rebuilds must not reset selection unnecessarily.
- AvalonEdit APIs only for the manual-edit pane (ADR 0003).
- Decoded-text diff (FR6) is computed in-app (new line-diff helper, Myers O(ND)), not via temp files + `git diff --no-index` — avoids temp-file writes outside `%APPDATA%\PickleGit\` and a git.exe dependency for a read-only view.

## Tests
### Manual / Black-Box
- [ ] Manual-edit pane: type a find term, close find, then Backspace/Delete/selection-delete in several places → caret stays put <!-- AC1 -->
- [ ] Manual-edit pane: click each change-map marker → pane scrolls to it <!-- AC2 -->
- [ ] Tree view: open Staged, Unstaged, and a commit's Files changed → all folders expanded; collapse one, refresh → stays collapsed <!-- AC3 -->
- [ ] Tree view: click a file in Staged, Unstaged, Files changed → diff shown <!-- AC4 -->
- [ ] Tree view: Ctrl-select two files + Shift-range, and select a folder → stage/unstage acts on all of them; diff view empty for multi-selection <!-- AC5 -->
- [ ] UTF-16 LE/BE, UTF-8-BOM, mixed UTF-16→UTF-8-BOM, and a 5000-line UTF-16 file → text diffs in Unstaged, Staged, history, and with ignore-whitespace <!-- AC6 -->
- [ ] UTF-16 file with 3 hunks → stage hunk 2, unstage it, stage 2 lines, discard 1 hunk → correct content each time, index/workdir bytes still UTF-16 with BOM; mixed-encoding file → hunk/line UI disabled <!-- AC7 -->
- [ ] Double/triple-click "Open Merge Editor" → one window <!-- AC8 -->
- [ ] Select 3 conflicted files → resolve theirs / mark resolved → all 3 resolved <!-- AC9 -->
- [ ] UTF-16 conflict → merge editor shows 1 block, auto-merged non-overlapping changes, correct Theirs pane <!-- AC10 -->
- [ ] Pick Theirs → Save & Stage → UTF-16+BOM+CRLF result with expected lines; repeat with manual edit <!-- AC11 -->
- [ ] Binary conflict → warning, Save & Stage disabled <!-- AC12 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh adhoc-conflict-staging-diff-fixes N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Stop text edits from moving the manual-edit caret ✓
**Why**: Root cause is a hypothesis; reproduce live first. If the repro shows a different cause (e.g. `LigatureSuppressingColorizer`), stop and raise a deviation.
**What**: Reproduce in the running app. Then split `PaneFindState.Recompute` into a navigating variant (search-text change / explicit find) and a non-navigating refresh used by `Invalidate()` — refresh recomputes matches and status, keeps the current match position clamped, and never raises `ScrollToMatchRequested`. Also make `OnResultFindScrollRequested` ignore requests while the find bar is closed.
**Touches**: `PickleGit/ViewModels/PaneFindState.cs`, `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Bug reproduced before the fix (caret jump observed, cause confirmed)  (manual)
- [x] After fix: Backspace/Delete/selection-delete in manual edit, with a lingering find term → caret stays at edit point  (manual)
- [x] Find bar open in the three read-only panes and manual edit: typing a term still jumps to the first match; Enter/F3 navigation still works; file switch keeps the search open with refreshed matches  (manual)
**Risk**: Other panes rely on `Invalidate()` navigating after a row-list rebuild.
**Mitigation**: Keep navigation on rebuild for read-only panes if needed by passing a flag only from the `ResultText` path.
**Deviations**: Also touched ViewModels/MergeConflictEditorViewModel.cs: manual-edit ResultText changes call ResultFind.RefreshWithoutNavigating() instead of Invalidate() (plan's own mitigation; file not in Touches). Approved. Finding: bug fired on any edit (insert too) while the find bar was open with a term, not only deletion.

### Step 2: Scroll the manual-edit pane on change-map marker click ✓
**What**: In `ConflictResultChangeMap_JumpRequested`, when `IsManuallyEdited`, map the click fraction to a document line (clamped 1..LineCount), call `ConflictResultEditBox.ScrollToLine`, and place the caret at that line start.
**Touches**: `PickleGit/Views/MergeConflictEditorWindow.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Manual edit: clicking top/middle/bottom markers scrolls the edit pane to matching regions  (manual)
- [x] Non-manual mode: marker click behavior unchanged  (manual)
**Risk**: Markers come from `ResultItems`, which stop updating after manual edits start, so positions can drift from edited text.
**Mitigation**: Accept fraction mapping (stated limitation); remapping markers is out of scope.
**Deviations**: None

### Step 3: Guard "Open Merge Editor" against reentrancy ✓
**What**: Add `_openingMergeEditor` flag: early-return if set, set before the first await, clear in `finally`; add `&& !_openingMergeEditor` to the command's CanExecute; call `CommandManager.InvalidateRequerySuggested()` on set/clear.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Rapid double/triple-click → exactly one window  (manual)
- [x] After closing the window, the button works again  (manual)
**Risk**: An exception before `ShowDialog` leaves the flag set.
**Mitigation**: `try/finally` covers the whole body.
**Deviations**: None

### Step 4: Resolve ours/theirs and mark-resolved for all selected conflicts ✓
**What**: Commands use `StagingService.ResolveTargets(_selectedWorkingFiles, p)` filtered to conflicted files. `ResolveConflictSideAsync` / `MarkResolvedAsync` take a list: one `checkout --ours|--theirs -- <quoted paths>` and one `add -- <quoted paths>` (chunked if command line gets long), then `Reopen()` and a single `LoadWorkingDirAsync`.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.cs`, `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Flat mode, 3 conflicted files selected → resolve theirs → all 3 resolved with theirs content  (manual)
- [x] Same for ours and mark resolved  (manual)
- [x] Right-click an unselected conflicted file while others are selected → only that file resolved (existing `ResolveTargets` semantics)  (manual)
- [x] Selection mixing conflicted and non-conflicted files → only conflicted ones touched  (manual)
**Risk**: A delete/modify conflict where one side lacks the file makes `checkout --theirs` fail for the whole batch.
**Mitigation**: On batch failure, fall back to per-file execution and report the failing paths via `DialogService.ShowError`.
**Deviations**: Instead of batch-then-per-file fallback, files whose chosen side is missing (delete/modify conflict) are partitioned out up front and reported in one ShowError; the rest run as one checkout + add. Approved.

### Step 5: Expand tree folders by default ✓
**What**: Invert the expansion state: track collapsed folders (`_collapsed*TreeFolders`) for Staged, Unstaged, CommitFiles, and Aggregated trees; `FileTreeBuilder` treats "not in collapsed set" as expanded; update `ToggleTreeFolder` and doc comments (incl. `FileTreeRow.IsExpanded`).
**Touches**: `PickleGit/Services/FileTreeBuilder.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`, `PickleGit/Models/RepositoryAccount.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Staged, Unstaged, Files changed (single commit and multi-commit aggregated) trees open fully expanded  (manual)
- [x] Collapse a folder, trigger a refresh → stays collapsed; expand again works  (manual)
**Risk**: None
**Deviations**: None

### Step 6: Show the diff for a selected tree file row ✓
**What**: Add `SelectedStagedTreeRow`, `SelectedWorkingTreeRow`, and a Files-changed tree row selection property (plain + aggregated, if aggregated rows differ in type); setters map a `File` row to the same path as flat selection (`SelectedFile` / the Files-changed equivalent); bind `SelectedItem` on the tree ListViews. Keep flat and tree selections consistent when switching modes.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.cs`, `PickleGit/ViewModels/RepositoryViewModel.Staging.cs`, `PickleGit/Views/CommitDetailView.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Tree mode: clicking a file in Staged, Unstaged, Files changed (single commit) shows its diff  (manual)
- [x] Clicking a folder row doesn't clear or change the diff wrongly, and still toggles expand as before  (manual)
- [x] Switch tree ↔ flat → selected file stays selected  (manual)
**Risk**: Step 7 replaces single `SelectedItem` binding for Staged/Unstaged with multi-select.
**Mitigation**: Step 7 builds on these properties (effective set of size 1 → `SelectedFile`).
**Deviations**: Aggregated (multi-commit) Files-changed tree not wired to diff-on-select: its flat list has no diff-on-select either (no single diff for a cross-commit file); kept consistent. Approved.

### Step 7: Multi-select in Staged/Unstaged trees with folder semantics ✓
**What**: Set the Staged/Unstaged tree ListViews to `SelectionMode="Extended"` with `ListViewMultiSelectBehavior` bound to `ObservableCollection<FileTreeRow>` VM collections. Folder rows carry their descendant `FileChange`s (populated by `FileTreeBuilder`, regardless of collapse state). On selection change, compute the effective set (file rows ∪ folder descendants, dedup) into `_selectedStagedFiles` / `_selectedWorkingFiles`; if exactly one file → `SelectedFile`, else clear the diff. Context-menu `CommandParameter` on a folder row passes the folder row; `ResolveTargets` overload expands it. Restore selection by path after a tree rebuild. Folder row single-click keeps its expand/collapse toggle; Ctrl/Shift-click selects without toggling.
**Touches**: `PickleGit/Views/CommitDetailView.xaml`, `PickleGit/Views/CommitDetailView.xaml.cs` (only if a click-vs-toggle handler exists there), `PickleGit/ViewModels/RepositoryViewModel.cs`, `PickleGit/ViewModels/RepositoryViewModel.Staging.cs`, `PickleGit/Services/Git/StagingService.cs`, `PickleGit/Services/FileTreeBuilder.cs`, `PickleGit/Models/RepositoryAccount.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Ctrl-click and Shift-range across file rows → stage/unstage/discard affect all selected  (manual)
- [x] Select a folder (expanded or collapsed) → stage affects every file under it, including nested folders  (manual)
- [x] Folder + a file inside it selected → file processed once  (manual)
- [x] Unstaged tree: 3 conflicted files selected → resolve theirs / mark resolved → all 3 resolved  (manual)
- [x] Single file selected → diff shown; 2+ files or folder → diff view cleared  (manual)
- [x] Refresh with an unchanged tree → selection preserved  (manual)
**Risk**: Folder-row click currently toggles expand; extended selection may conflict with toggle gestures (e.g. Shift-click toggling).
**Mitigation**: Toggle only on plain click on the chevron/folder row; modifier clicks only select. Raise a deviation if the existing click wiring needs a different gesture.
**Deviations**: With 2+ files or a folder selected, the diff shows the first selected file instead of clearing (matches flat-list behavior; hidden flat list drives SelectedFile). Added folder-row context menus (Staged: Unstage/Discard; Unstaged: Take Ours/Theirs/Mark Resolved when HasConflicts, Stage/Discard) and FileTreeRow.HasConflicts. Step 6 single-row props for Staged/Unstaged trees replaced by multi-select collections. Approved.

### Step 8: Add encoding-aware text decode and in-app line diff ✓
**What**: New `Services/TextDecoding.cs`: `TryDecode(byte[] bytes, out string text, out Encoding enc)` — BOM sniff (`EF BB BF`, `FF FE`, `FE FF`), BOM-less UTF-16 heuristic (even length, NULs predominantly at odd or even offsets), otherwise UTF-8 if no NUL; returns false for genuinely binary content; strips BOM. New `Services/LineDiff.cs`: Myers O(ND) linear-space line diff producing `DiffHunk`/`DiffLine` (same header format and old/new line numbering as `ParsePatch`) with configurable context lines and an optional whitespace-insensitive comparer. Reproduce B5 first (confirm the UTF-8-BOM case's other side).
**Touches**: `PickleGit/Services/TextDecoding.cs` (new), `PickleGit/Services/LineDiff.cs` (new), `PickleGit/PickleGit.csproj`
**verification stubs** *(verify each before marking step ✓)*:
- [x] B5 reproduced; actual byte content of the failing UTF-8-BOM case recorded in Deviations notes  (manual)
- [x] Throwaway harness (scratchpad, not committed) diffs sample pairs: identical, insert/delete at start/end/middle, empty side, 10k-line files → hunks match `git diff` output for the UTF-8 equivalents  (manual)
- [x] PNG/EXE bytes → `TryDecode` returns false  (manual)
**Risk**: UTF-16 heuristic misclassifies a binary file as text.
**Mitigation**: Require the BOM-less UTF-16 heuristic to also pass a printable-character ratio check; fall back to binary.
**Deviations**: None. Notes: B5 repro — UTF-16 LE/BE/no-BOM and big UTF-16 show as binary; a UTF-8-BOM file is binary only when its other side is UTF-16 (mixed.txt); UTF-8 BOM on both sides already diffs as text. csproj untouched (SDK-style auto-includes new .cs). LineDiff adds git-xdiff-style cost limit + 3 s safety budget so huge heavily-changed files stay fast (20k lines every-other changed: 183 ms, still minimal).

### Step 9: Use the decoded diff when libgit2 reports binary ✓
**What**: In `GetUnstagedFileDiff`, `GetStagedFileDiff`, `GetFileDiff` (history), and `GetFileDiffIgnoreWhitespace`: when the result is binary with no hunks, load both sides' bytes (index/HEAD/parent blob/workdir via existing `GetIndexBlobBytes` / `GetBlobBytesAt` / file read), decode both with `TextDecoding`; if both decode, build hunks with `LineDiff` (respecting context lines and ignore-whitespace), run `ComputeWordDiffsForHunk` + `SyntaxHighlighter.Apply`, set `IsBinary = false`, a new `FileDiffResult.IsDecodedText = true`, and record each side's encoding/BOM/line-ending style on the result. Until Step 10 lands, `CanPartialStage` also requires `!IsDecodedText`.
**Touches**: `PickleGit/Services/GitService.cs`, `PickleGit/Models/RepositoryAccount.cs`, `PickleGit/ViewModels/RepositoryViewModel.Diff.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] UTF-16 LE, UTF-16 BE, UTF-8-BOM, UTF-16→UTF-8-BOM change → text diff in Unstaged and Staged; no BOM glyph  (manual)
- [x] Same files in a commit's history diff → text diff  (manual)
- [x] Ignore-whitespace on → text diff, whitespace-only changes hidden  (manual)
- [x] 5000-line UTF-16 file → text diff, UI responsive  (manual)
- [x] Decoded diff → hunk/line stage UI disabled; whole-file stage/unstage works and file bytes stay UTF-16 in the index  (manual)
- [x] Real binary (PNG non-image ext / .exe) → still "binary" notice  (manual)
- [x] Ordinary UTF-8 file diff unchanged, hunk staging still works  (manual)
**Risk**: Large files make the in-app diff slow on the executor thread.
**Mitigation**: Myers is O((N+M)·D); measure on the 5000-line case and a 50k-line case; raise a deviation if > ~500 ms.
**Deviations**: Also touched Services/LineDiff.cs: GroupDeletesFirst reorders each contiguous change block to deletes-then-inserts (git convention); harness still all-pass. Branch comparison view not wired (not in plan scope). Approved.

### Step 10: In-app hunk/line staging for decoded-text diffs ✓
**Why**: `git apply` splits on byte `0A`, which breaks UTF-16 (`0A 00`), and a patch of decoded text never matches the raw blob bytes. Applying the selection in text and re-encoding avoids both.
**What**: New `Services/Git/DecodedStagingService.cs`: given the decoded diff, the selected hunks/lines, and the op (Stage / Unstage / Discard), compute the target text. Stage = index text + selected changes. Unstage = index text minus selected changes (against HEAD). Discard = working text minus selected changes (against index). Reuse the same selection semantics `PatchBuilder` uses for line selection, incl. its direction rules. Re-encode with the side's original encoding, BOM, and line endings. Write: Stage/Unstage → `ObjectDatabase.CreateBlob` + `Index.Add(blob, path, mode)` + `Index.Write()` on the executor (keep the existing file mode); Discard → write the working file. Route the existing hunk/line stage/unstage/discard commands here when `IsDecodedText` and both sides share an encoding; enable `CanPartialStage` for that case only.
**Touches**: `PickleGit/Services/Git/DecodedStagingService.cs` (new), `PickleGit/PickleGit.csproj`, `PickleGit/Services/GitService.cs`, `PickleGit/ViewModels/RepositoryViewModel.Staging.cs`, `PickleGit/ViewModels/RepositoryViewModel.Diff.cs`, `PickleGit/Models/RepositoryAccount.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] UTF-16 LE file, 3 hunks: stage hunk 2 → `git diff --cached` (via `git diff --cached --text`) shows only hunk 2; index blob starts with `FF FE`  (manual)
- [x] Unstage that hunk → index equals HEAD bytes again  (manual)
- [x] Stage 2 selected lines from a mixed add/remove hunk → exactly those lines staged  (manual)
- [x] Discard 1 hunk → working file reverts that hunk only, still UTF-16 + BOM, CRLF preserved  (manual)
- [x] UTF-8-BOM-only file goes through the normal `git apply` path, unchanged behavior  (manual)
- [x] Mixed-encoding diff → hunk/line UI disabled, whole-file stage works  (manual)
- [x] Executable-bit file keeps its mode after a partial stage  (manual)
**Risk**: Writing the index directly bypasses git's clean filters (`text=auto` CRLF normalization, LFS).
**Mitigation**: Before writing, check `.gitattributes` for the path via LibGit2Sharp; if a clean filter / eol conversion applies, keep hunk/line UI disabled for that file and raise a deviation.
**Deviations**: None. Notes: in-app staging code lives in RepositoryViewModel.Diff.cs (HunkOrLinesPatchAsync branch) rather than .Staging.cs; csproj untouched (SDK auto-include). Attribute risk handled up front: git check-attr (filter, working-tree-encoding, text, eol, crlf) at diff load sets FileDiffResult.CanStagePartially, so hunk/line UI is hidden for such files (verified with attr16.txt 'text').

### Step 11: Read conflict stages as bytes and three-way merge decoded text in-app ✓
**Why**: git leaves no conflict markers for files it treats as binary (any NUL byte, so every UTF-16 file), so the marker-driven merge editor sees "no conflicts" and the working file holds only ours. The editor's existing machinery (MergeConflictParser blocks, per-block picking, manual edit) can be reused unchanged by feeding it marker text synthesized from a real in-app 3-way merge.
**What**: `GitService.GetConflictStageBytes(path)` returns base/ours/theirs blob bytes from `Index.Conflicts` (on the executor). New `Services/ThreeWayMerge.cs`: decode the three sides with `TextDecoding`, match base→ours and base→theirs with LineDiff's Myers matcher (exposed as an internal/public helper), run diff3 chunking (stable vs unstable regions; ours-only / theirs-only / identical changes merge cleanly, others become conflicts), and emit marker text (`<<<<<<< ours-label`, `||||||| base`, `=======`, `>>>>>>> theirs-label`) using ours' dominant line terminator and final-newline state; returns the text plus the target encoding/BOM.
**Touches**: `PickleGit/Services/GitService.cs`, `PickleGit/Services/LineDiff.cs`, `PickleGit/Services/ThreeWayMerge.cs` (new)
**verification stubs** *(verify each before marking step ✓)*:
- [x] Throwaway harness: synthesized marker text for sample triples matches `git merge-file --diff3 -p` output on UTF-8 copies of the same triples (clean merges identical; conflict blocks same ranges)  (manual)
- [x] UTF-16 test conflict: stage bytes read correctly (BOMs ff fe on all three)  (manual)
**Risk**: diff3 chunking differs from git's in edge cases (adjacent changes), giving different block boundaries.
**Mitigation**: Harness comparison against `git merge-file`; accept boundary differences only if the merge is still correct (no lost lines).
**Deviations**: None. Harness: 9 named + 40 random triples byte-identical to git merge-file --diff3 -p; UTF-16 stage bytes read with ff fe on all three.

### Step 12: Merge editor uses the synthesized merge, saves in original encoding, guards binary conflicts ✓
**What**: `OpenMergeEditorCoreAsync` collects stage bytes for content-conflicted files and passes them to the session. `MergeConflictFileViewModel`: when any stage contains a NUL byte (git treated it as binary) and the working file bytes still equal the ours stage, parse the ThreeWayMerge marker text instead of the working file, and remember the encoding + preamble; Save writes preamble + encoded text (instead of `File.WriteAllText` with the StreamReader encoding) for these files. If the stages don't decode, or the working copy was changed since the conflict, show a warning banner and disable Save & Stage for that file.
**Touches**: `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`, `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`, `PickleGit/Views/MergeConflictEditorWindow.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] UTF-16 conflict: editor shows 1 conflict block, Theirs pane shows THEIRS lines, lines 10/15 auto-merged in Result  (manual)
- [x] Pick Theirs for the block → Save & Stage → file and index are UTF-16 LE + BOM + CRLF with the expected lines; conflict resolved  (manual)
- [x] Manual edit on the same conflict (fresh repo): edit, Save & Stage → expected text, encoding preserved  (manual)
- [x] Ordinary UTF-8 conflict (marker-based) behaves exactly as before  (manual)
- [x] Binary conflict (bin.dat) → warning shown, Save & Stage disabled  (manual)
**Risk**: Working file edited by the user after the conflict (but not staged) must not be overwritten by a re-synthesized conflict.
**Mitigation**: Only synthesize when working bytes == ours stage bytes; otherwise warn and block Save & Stage.
**Deviations**: Rebuilt conflicts use git's default merge-style markers (no base section) and pass the decoded base through ApplyGitAncestorText, instead of diff3 markers — a diff3 base made untouched blocks count as resolved-to-base, unlike UTF-8 conflicts. Added includeBase flag (default true) to Services/ThreeWayMerge.cs. Approved.

### Step 13: Fix static-analysis findings (1 MEDIUM, 8 LOW) ✓
**What**: (1) decoded hunk/line ops verify the target (index or working file) still holds the bytes the diff was computed from, else abort with a reload message; (2) clear the remembered decoded diff wherever `_currentDiffFile` changes; (3) UTF-16 save catches encoder errors and reports them; (4) `git check-attr` and commit lookups only when needed; (5) conflict stages loaded only for blobs libgit2 flags binary, with a size cap; (6) batch `checkout`/`add` path lists into chunks under the Windows command-line limit; (7) decode + 3-way merge for rebuilt conflicts runs off the UI thread; (8) workdir write guard normalises the root with a trailing separator; (9) workdir read uses the same inside-repo guard.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Diff.cs`, `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`, `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`, `PickleGit/Services/GitService.cs`, `PickleGit/Services/TextDecoding.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Decoded Stage Hunk after the index changed underneath → error, nothing overwritten; normal decoded stage still works  (manual)
- [x] UTF-16 conflict editor still opens/merges/saves correctly; binary conflict still blocked  (manual)
- [x] UTF-16 diffs (unstaged/staged/history) still render; multi-file resolve still works  (manual)
**Risk**: None
**Deviations**: Also touched RepositoryViewModel.Blame.cs and .Compare.cs (finding 2 reset sites) and added ConflictStages to Services/ThreeWayMerge.cs (finding 5). Found and fixed: LibGit2Sharp's cached index made WriteIndexContent's check and Index.Write() undo external staging — WriteIndexContent now Reopen()s first. Approved.

### Step 14: Fix PR-review findings (5 MEDIUM, 5 LOW, 1 INFO) ✓
**What**: M1 tree multi-select applies selection in bulk (suspendable collection + behavior batching) instead of per item; M2 Ctrl-deselecting a file under a selected folder drops the folder and keeps its other files selected; M3 confirm Take Ours/Theirs and Mark Resolved when acting on more than one file, and warn when Mark Resolved targets still contain conflict markers; M4 conflict stages sniffed (first 8 KB) before loading — non-text blobs never fully read; M5 decoded-text diff fallback size-capped and prefix-sniffed before full reads; L1 rebuilt-conflict save uses a throwing encoder (no silent '?'); L2 `git check-attr` result cached per path, keyed on the relevant .gitattributes timestamps; L3 missing working file for a content conflict disables Save & Stage; L4 path chunking reuses GitService's existing helper; L5 remove unused IsDecodedTextDiff; INFO branch Compare view gets the decoded-text fallback. (Test-coverage LOW needs a test project — out of scope, reported.)
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Staging.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`, `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`, `PickleGit/ViewModels/RepositoryViewModel.Diff.cs`, `PickleGit/ViewModels/MergeConflictEditorViewModel.cs`, `PickleGit/ViewModels/BulkObservableCollection.cs` (new), `PickleGit/Behaviors/ListViewMultiSelectBehavior.cs`, `PickleGit/Services/GitService.cs`, `PickleGit/Services/TextDecoding.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Select a folder of 2,000 untracked files in the Unstaged tree → responsive; Stage acts on all  (manual)
- [x] Folder selected, Ctrl-deselect one file under it → that file no longer staged by Stage; others are  (manual)
- [x] Take Theirs on 3 selected conflicts → confirmation listing them; Cancel does nothing; OK resolves all. Mark Resolved on a file with markers → warning  (manual)
- [x] UTF-16/BOM diffs, decoded staging, UTF-16 conflict merge, binary conflict guard, multi-resolve all still work  (manual)
- [x] Compare view shows a UTF-16 file as text  (manual)
**Risk**: Behavior change in ListViewMultiSelectBehavior affects every multi-select list.
**Mitigation**: Batching only engages when the bound collection supports suspension; flat lists re-tested.
**Deviations**: Added NotText flag to ConflictStages (Services/ThreeWayMerge.cs). ListViewMultiSelectBehavior re-syncs the ListView after the VM adjusts the bound collection. BulkObservableCollection.ChangesApplied fixes the reentrancy crash ('Cannot change ObservableCollection during a CollectionChanged event'). Test-coverage LOW not done (no test project). Approved.

## After Implementation
- Run `pkl:static-analyze` — 10+ compiled files changed across ViewModels, Services, and Views.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
