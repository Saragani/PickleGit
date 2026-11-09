# Code Review — adhoc-merge-commit-no-changes

Scope: uncommitted diff to `PickleGit/ViewModels/RepositoryViewModel.cs` (ConflictInfo setter adds
`CommandManager.InvalidateRequerySuggested()`; new `IsResolvedMergePending`; CommitCommand and
CommitAndPushCommand CanExecute accept it).

Read for context: `Models/ConflictState.cs`, `GitService.GetConflictState` / `CreateCommit`,
`RepositoryViewModel.Staging.cs` (`CommitAsync`, `RefreshConflictStateAsync`, `ApplyWorkingDirStatus`),
`RepositoryViewModel.Rebase.cs` (`ContinueOperationAsync`), `RepositoryViewModel.cs` RefreshOnceAsync
(~L1401) and the watcher callback (~L1847), `Services/UndoService.cs`, `Services/AppCommandRegistry.cs`,
`Views/CommitDetailView.xaml`.

## Questions checked (no finding)

- **Is ConflictedFiles populated in every merge state?** Yes. `GetConflictState` fills `ConflictedFiles`
  from `_repo.Index.Conflicts` whenever `Operation != None`, so a Merge state always has a real list,
  never a default empty one. `Operation == Merge` comes only from `MERGE_HEAD`, and the rebase dirs are
  checked first, so a rebase that stops on a merge commit is not treated as a Merge.
  `git merge --squash` writes no MERGE_HEAD, so it is unaffected. The only way to get an empty list while
  unmerged entries exist is a stale libgit2 index (see the LOW finding).
- **Executor / reference equality.** Both writers (`RefreshConflictStateAsync`, Staging.cs:89, and
  RefreshOnceAsync, RepositoryViewModel.cs:1409) call `GetConflictState` on `_git.Executor` and set
  `ConflictInfo` on the UI thread with a new instance. `ConflictState` has no Equals override, so
  `Set` always returns true and the requery always fires. That is correct, and the cost is covered in
  the INFO finding.
- **No git in CanExecute.** `IsResolvedMergePending` only reads fields. OK.
- **Other entry points.** The Ctrl+Enter KeyBinding (CommitDetailView.xaml ~L1348), both buttons
  (~L1410/1413) and the command palette (`AppCommandRegistry.cs:63`, Commit & Push) all bind to the same
  two `RelayCommand`s. There is no other copy of the `StagedFiles.Count > 0 || IsAmend` guard. Nothing
  was missed.
- **LibGit2Sharp path.** In a non-amend `_repo.Commit`, the parents are HEAD plus MERGE_HEADs, and the
  call ends with `git_repository_state_cleanup`. It does not throw EmptyCommitException by default.
  This matches the live verification.
- **Signed CLI path (`git commit -S -F - --author=...`).** git skips its "nothing to commit" check while
  MERGE_HEAD exists, so it records the 2-parent merge even when the tree equals HEAD. If unmerged entries
  somehow remain, git refuses cleanly ("Committing is not possible because you have unmerged files"),
  so nothing is corrupted. Not run live, but the reasoning holds.
- **Commit & Push.** `PushAsync` runs only when `CommitAsync` returns true, and a merge commit pushes
  like any other commit. If the push fails, the local merge commit is kept and the error is reported,
  which is the same as a normal commit. Not run live.
- **Undo.** `UndoKind.Commit` runs a soft reset to the pre-merge HEAD. MERGE_HEAD is already gone and the
  index equals the old HEAD, so undo simply removes the merge. That is acceptable and matches undoing a
  merge commit that had changes.
- **Cherry-pick/revert.** Excluded as intended, since their Continue path commits through the CLI.

## Findings

[MEDIUM] Correctness (pre-existing, not introduced by this diff)
File: PickleGit/Services/GitService.cs:1044 (reached from RepositoryViewModel.Staging.cs:600-628)
Issue: Amend during an in-progress merge silently drops the merge. `CommitCommand` was already enabled
with `IsAmend` during a merge, so this diff does not introduce the bug. It is more likely to be hit now,
because the flow it encourages (Continue prefills a message, then the user clicks Commit) sits right
next to the Amend checkbox. With amend, LibGit2Sharp uses HEAD~'s parents instead of HEAD plus
MERGE_HEAD, and then `git_repository_state_cleanup` deletes MERGE_HEAD. The result is that the
previous commit is rewritten with the merged tree, the second parent is lost, and no error is shown.
The signed CLI path fails cleanly instead ("You are in the middle of a merge -- cannot amend."), so the
two backends behave differently.
Fix: In `CommitAsync`, if `amend && ConflictInfo?.Operation == ConflictOperation.Merge`, show
`DialogService.ShowError("Amend", "Cannot amend while a merge is in progress — commit the merge first.")`
and return false. Alternatively, add `!(IsAmend && HasConflict)` to the CanExecute lambdas, or disable the
Amend checkbox while `HasConflict` is true.

[LOW] Robustness: a stale libgit2 index can make `ConflictedFiles` empty while unmerged entries exist
File: PickleGit/ViewModels/RepositoryViewModel.cs:475
Issue: `ConflictedFiles` reads the in-process cached `_repo.Index`, but `Operation` reads MERGE_HEAD
fresh from disk. Suppose a merge is started outside the app (terminal or another client) and its index
write falls inside a Suppress or grace-period window before the watcher's `Reopen()` runs. The state can
then read as `Merge` with 0 conflicted files, which makes `IsResolvedMergePending` true. The LibGit2Sharp
commit would then write the tree from the stale index, producing a merge commit with HEAD's tree, and
clean up MERGE_HEAD. The window is narrow, and the same staleness already affects
`ContinueOperationAsync` and the staged-files path (CLI status shows auto-merged files as staged). This
diff extends it to the nothing-staged case.
Fix (optional hardening): when `CommitAsync` is about to commit a merge, re-check on the executor first:
`await _git.Executor.RunAsync(() => { _git.Reopen(); return _git.GetConflictState(); })`. If
`ConflictedFiles.Count > 0`, refresh and abort with an error.

[INFO] Performance / noise
File: PickleGit/ViewModels/RepositoryViewModel.cs:467
Issue: `ConflictInfo` gets a fresh instance on every refresh, including watcher ticks and auto-fetch,
so `InvalidateRequerySuggested()` now fires on every refresh even when no repo is in a merge. This is
harmless: one background-priority requery, and `ApplyWorkingDirStatus` already does the same thing.
Fix: Optional. Call it only when the derived value changes, for example by caching the previous
`IsResolvedMergePending` and invoking only when it flips.

[INFO] Style
File: PickleGit/ViewModels/RepositoryViewModel.cs:475-476
Issue: `_conflictInfo?.Operation == ... && _conflictInfo.ConflictedFiles.Count == 0` relies on `?.`
short-circuiting so the second operand never sees null. That is correct, but `_conflictInfo` is never
null in practice (it is initialized and always assigned a non-null value), so `?.` is defensive only.
It is consistent with `HasConflict`. No change needed.

[INFO] Test gap
Issue: The signed (`commit.gpgsign` / app setting) path and the push half of Commit & Push were not run
live. The reasoning above says both should work, but a quick manual run of the signed path with
`commit.gpgsign=true` in a scratch repo would close the gap.

## Rules compliance
- Code style: `Set<T>` guard kept, early-return form is fine, and `InvalidateRequerySuggested` matches the
  code-style.md guidance. The XML doc comment is appropriate.
- Threading: no git calls on the UI thread or in CanExecute.
- Security: no new process, path, credential or logging surface.

## Counts
CRITICAL 0 · HIGH 0 · MEDIUM 1 (pre-existing) · LOW 1 · INFO 3

**Verdict: READY TO MERGE**. Fixing the MEDIUM amend-during-merge guard in this branch or as a
follow-up is recommended.

## Resolution (2026-11-09)
- MEDIUM (amend during merge silently drops the merge) — fixed: CommitAsync refuses amend while ConflictInfo.Operation == Merge, with a themed error. User-approved scope addition (touches RepositoryViewModel.Staging.cs). Verified live: amend ticked → dialog, HEAD and MERGE_HEAD untouched; unticked → 2-parent merge commit, MERGE_HEAD removed.
- LOW (stale index could enable Commit) — not addressed.
