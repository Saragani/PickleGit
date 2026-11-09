# Review: adhoc-multiselect-file-diff

Scope: uncommitted diff vs `main` touching `Services/GitService.cs`, `ViewModels/RepositoryViewModel.Detail.cs`,
`ViewModels/RepositoryViewModel.cs`, `Views/CommitDetailView.xaml`.

Feature: with 2+ commits multi-selected, clicking a file in the aggregated "Files changed" panel (flat or tree)
shows a range diff `oldest^ (or empty tree) -> newest`, ordered by `GraphNodes` graph order.

Checklist results that came back clean: every git call goes through `GitService.Executor` (`ContextLines` and
`GraphNodes` are read on the UI thread before the hop). There are no CLI mutations, so no `Reopen` is needed, and
there is no git.exe dependency. The XAML only adds `SelectedItem` bindings, with no new brushes or converters.
There is no code-behind logic and no `MessageBox` (`DialogService.ShowError`). The async load has a
version-guarded `try/catch`. Security: `filePath` comes from git's own change list and goes only to a
LibGit2Sharp pathspec, with no process launch and no file-system write. No security findings.

---

## Findings

[HIGH] Correctness — stale working-dir SelectedFile now leaks into the multi-select diff pane
File: PickleGit/ViewModels/RepositoryViewModel.cs:352 (with RepositoryViewModel.Detail.cs:76-81, 116-121)
Issue: The new `if (IsMultiSelection) return true;` branch in `ShowDiffInsteadOfCommits` assumes any non-null
`SelectedFile` in multi-select mode came from the aggregated panel. That is not guaranteed.
`ClearAggregatedFileSelection` only nulls `SelectedFile` when it is `_aggregatedSelectedFileChange`. Before this
change, a stale `SelectedFile` was harmless because the fallback was `DetailCommit != null`, which is false in
multi-select mode. The doc comment on `ShowDiffInsteadOfCommits` relies on exactly that guard.
Repro: select the "Uncommitted changes" node and click an unstaged file, so the working-dir diff shows. Then
Ctrl/Shift-click a commit, so 2 nodes are selected. `ShowWorkingDir` flips to false, but `WorkingDirFiles` is not
cleared. Nothing pushes null into `SelectedFile`, so it still holds the working-dir `FileChange`.
`ShowDiffInsteadOfCommits` now returns true. The diff pane keeps showing the working-tree diff with
`_currentDiffFile` still set. So `IsWorkingDirDiff` and `CanPartialStage` stay true, and Stage/Discard hunk
buttons stay live on a pane the user believes is a commit-range view. The header switches to
"Diff (2 commits): <path>" the next time it is re-evaluated; `DiffPaneHeaderText` is not raised on the
`IsMultiSelection` change.
The single-commit-to-multi path is probably saved incidentally: `CommitFiles = new ...` empties the flat
ListView, which pushes null through its TwoWay `SelectedItem="{Binding SelectedFile}"`. That is a binding side
effect, not a design guarantee, and it does not happen for the working-dir lists.
Fix: Gate the branch on provenance, i.e. `if (IsMultiSelection) return _aggregatedSelectedFileChange != null &&
ReferenceEquals(SelectedFile, _aggregatedSelectedFileChange);`. Better still, null any non-aggregated
`SelectedFile` in `OnSelectedNodesChanged` when entering multi-select (`if (_selectedNodes.Count > 1) SelectedFile
= null;`) and clear `_currentDiffFile` and the diff items. Apply the same provenance check to the
`DiffPaneHeaderText` multi-select branch.

[HIGH] Correctness — after "Close diff" the same aggregated file can't be reopened
File: PickleGit/ViewModels/RepositoryViewModel.Detail.cs:184-207 (with RepositoryViewModel.cs:807)
Issue: `CloseDiffPaneCommand` (and `ExitComparisonCommand`) set `SelectedFile = null`, but
`_selectedAggregatedFile` and `_selectedAggregatedFilesTreeRow` are left unchanged and no PropertyChanged is
raised for them. The flat and tree ListViews keep the row highlighted, so clicking that same row again produces
no `SelectionChanged` and the binding never fires. Even if it did, the setters would return early on
`ReferenceEquals(value, _selectedAggregatedFile)`. The user has to click a different file and then come back.
The single-commit "Files changed" list doesn't have this problem because its `SelectedItem` is bound directly to
`SelectedFile`, so nulling it deselects the row.
Fix: Keep the aggregated selection in sync with `SelectedFile`. In the `SelectedFile` setter (or a hook called
from it), when `value` is not `_aggregatedSelectedFileChange`, clear `_selectedAggregatedFile`,
`_selectedAggregatedFilesTreeRow` and `_aggregatedSelectedFileChange` with PropertyChanged raised. Then the view
deselects the row, and a re-click is a real selection change. Raising a null from the VM is safe because the
setter's ignore-null guard only applies to view-to-VM pushes.

[MEDIUM] Correctness — LoadRangeDiffAsync doesn't reset IsConflictedFileDiff
File: PickleGit/ViewModels/RepositoryViewModel.Detail.cs:253-266
Issue: The reset block was copied from `LoadComparisonDiffAsync` rather than `LoadDiffAsync`, so it omits
`IsConflictedFileDiff = false`. `LoadDiffAsync` (RepositoryViewModel.Diff.cs:30-34) carries an explicit comment
explaining that without this reset, the "unresolved merge conflicts" banner leaks onto an unrelated diff after a
conflicted working-dir file was viewed. Repro: during a merge, view a conflicted file, multi-select two commits,
then click an aggregated file. The stale conflict banner shows over the range diff, and `CanPartialStage`
evaluation is skewed.
Fix: Add `IsConflictedFileDiff = false;` to the reset block. Also consider moving the shared reset into one
`ResetDiffPaneState()` helper used by all four loaders, so they stop drifting apart.

[MEDIUM] Correctness — background refresh silently narrows the range while IsMultiSelection stays true
File: PickleGit/ViewModels/RepositoryViewModel.Detail.cs:248-251 (with RepositoryViewModel.cs:1470-1477, 1687-1692)
Issue: `RefreshOnceAsync` and `ApplyFilter` rebuild `GraphNodes` and call `ReselectSameCommit(restored)`. That
collapses `_selectedNodes` to the single last-selected node under `_syncingFromSelectedNode`, so
`OnSelectedNodesChanged` does not run. `IsMultiSelection`, `SelectedCount`, the aggregated file list and the
aggregated selection all stay at their multi-select values. A file click after any refresh (an external change,
or the new background auto-fetch landing new refs) then runs `LoadRangeDiffAsync` against a one-node
`_selectedNodes`. It renders a single-commit diff under a "Diff (N commits)" header with no indication anything
changed. The root cause predates this change, but this change is what makes it produce a wrong diff rather than
only a stale file list.
Fix: In the refresh path, when `_selectedNodes.Count > 1` before the rebuild, restore every selected node by sha
into `_selectedNodes`. If that isn't feasible, fall back to a full `OnSelectedNodesChanged()` so multi-select
state is cleared consistently. At minimum, have `LoadRangeDiffAsync` bail out with a cleared pane when fewer than
2 selected nodes resolve in `GraphNodes`.

[LOW] Correctness — image files fall through to the binary notice
File: PickleGit/ViewModels/RepositoryViewModel.Detail.cs:246
Issue: `LoadDiffAsync` has a `GitService.IsImagePath` branch that renders old/new images. The range path has no
such branch, so an image shows "binary". This matches the documented v1 limitation of comparison mode.
Fix: Add a two-sha `GetImageDiff` overload, which comparison mode would also use, or accept the limitation and
document it in the method summary like `LoadComparisonDiffAsync` does.

[LOW] UI — aggregated tree highlight not re-synced after tree rebuilds
File: PickleGit/ViewModels/RepositoryViewModel.Staging.cs:453-457 (related to Detail.cs:216-224)
Issue: `RebuildAggregatedFileTreeRows` runs on folder collapse/expand and on a sort-mode change, and replaces
every row instance. The ListView drops its highlight, the null push is ignored, and
`_selectedAggregatedFilesTreeRow` keeps pointing at an orphaned row. `RebuildCommitFileTreeRows` calls
`SyncTreeSelectionToSelectedFile()` for exactly this reason. A sort change also re-adds `_aggregatedFiles` items
after `Clear()`, which drops the flat highlight too.
Fix: After rebuilding, re-resolve `_selectedAggregatedFilesTreeRow` from `_selectedAggregatedFile` (same
`FirstOrDefault` as `SelectAggregatedFile`) and raise PropertyChanged for both properties.

[LOW] UI — header "N commits" counts selected nodes, not the diffed range
File: PickleGit/ViewModels/RepositoryViewModel.cs:361-363
Issue: `SelectedCount` includes the synthetic uncommitted node, which `LoadRangeDiffAsync` filters out. It also
ignores the unselected commits a non-contiguous selection deliberately includes. Selecting WIP + 1 commit reads
"Diff (2 commits)" for what is a single-commit diff.
Fix: Phrase the header as a range (e.g. `Diff <oldestShort>^..<newestShort>: path`) or count only real
selected commits. The `!ShowWorkingDir` term is redundant, because multi-select always sets
`ShowWorkingDir = false`.

[LOW] Maintainability — GetRangeFileDiff duplicates GetFileDiff(shaA, shaB)
File: PickleGit/Services/GitService.cs:1122-1135
Issue: Its body is the two-sha `GetFileDiff` with the first tree replaced by `oldest.Parents.FirstOrDefault()?.Tree`.
Fix: Extract a private `GetTreeFileDiff(Commit a, Commit b, path, ctx)` that tolerates a null `a`, and have both
public methods call it.

[INFO] Diff toggles — ShowEntireFile / IgnoreWhitespace don't reload the range diff
File: PickleGit/ViewModels/RepositoryViewModel.Diff.cs:117-128
Issue: `ReloadCurrentDiffOrCommitDiffAsync` has no multi-select branch, so toggling "Show entire file" or
"Ignore whitespace" leaves the range diff as it was. The range diff also never honours `IgnoreWhitespace`, while
`LoadDiffAsync` does via `GetFileDiffIgnoreWhitespace`. Comparison mode has the same gap. This is known and out
of scope.
Fix (follow-up): Add `else if (IsMultiSelection && SelectedFile != null) await LoadRangeDiffAsync(SelectedFile.Path);`
to the reload method, alongside a comparison-mode branch.

[INFO] Renames — pathspec-only diff shows a renamed file as a full add
File: PickleGit/Services/GitService.cs:1129
Issue: Filtering `Diff.Compare` to the new path excludes the old path, so a file renamed within the range renders
as an all-added file. `AggregatedFileChange` carries no `OldPath`. The single-commit `GetFileDiff` has the same
limitation.
Fix (follow-up): Carry `OldPath` through the aggregation and pass both paths to the pathspec with rename detection.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 2 |
| MEDIUM | 2 |
| LOW | 4 |
| INFO | 2 |

**Verdict: NEEDS CHANGES**

## Resolution (2026-11-09)
- HIGH 1 (stale working-dir diff under multi-select) — fixed: SelectedFile nulled on entering multi-select; ShowDiffInsteadOfCommits / range load gated on IsAggregatedFileSelected. Verified live (WIP file → Ctrl-select commit → no stale diff).
- HIGH 2 (can't reopen same aggregated file) — fixed: SelectedFile moving off the aggregated pick calls DropAggregatedRowSelection. Verified live (close diff → re-click same file reloads).
- MEDIUM 1 (IsConflictedFileDiff not reset) — fixed in LoadRangeDiffAsync.
- MEDIUM 2 (refresh narrows range) — fixed: range uses the _aggregatedShas snapshot; header count uses it too (also drops the WIP node from the count). Verified live (external tag → refresh collapsed list highlight; range diff still spans both commits).
- LOW / INFO — not addressed.
