# PR Code Review — `merge-conflict-commit-message`

Scope: change how "Continue" behaves for an in-progress merge with resolved
conflicts — instead of shelling out to `git merge --continue` (which strips
the `# Conflicts:` block via git's `--cleanup=strip` default), the new code
composes a commit message (source description + originally-conflicted file
list read from `.git/MERGE_MSG`) and places it into `CommitMessage`, letting
the user finish via the normal Commit button. Cherry-pick/revert/rebase
Continue is unchanged.

---

## Findings

[HIGH] Correctness — Continue can be clicked repeatedly and silently discards user edits
File: PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:287-299
Issue: `ContinueOperationCommand`'s `CanExecute` is `HasConflict && ConflictInfo.ConflictedFiles.Count == 0 && !IsBusy` (RepositoryViewModel.cs:865-866). For a merge, both conditions stay true indefinitely after all conflicts are resolved — `HasConflict` only goes false once the merge actually completes (i.e. after the user clicks Commit), and `ConflictedFiles.Count` stays 0. Nothing in this diff disables/hides the Continue button once it has already been clicked once. Every click of Continue re-runs `GetMergeConflictedFilePaths()` and unconditionally overwrites `CommitMessage` with a freshly regenerated string, silently discarding any edits the user made to the pre-filled message in between clicks (e.g. a second accidental/habitual click, or clicking Continue again after tweaking the message before finding the Commit button). Previously this was a non-issue because Continue immediately completed the merge via `git merge --continue` — there was no intermediate editable message to lose.
Fix: Guard the regeneration — e.g. only set `CommitMessage` from this path when it is currently empty/whitespace, or track a per-conflict "already prefilled" flag (reset when a new merge/`ConflictInfo` starts) and skip regenerating if already prefilled. Alternatively, disable `ContinueOperationCommand` for `ConflictOperation.Merge` once `CommitMessage` has been populated by this path, forcing the user through the normal Commit action next.

[MEDIUM] Error handling — new git-state file read bypasses this file's own established try/catch convention and the ViewModel's error-dialog path
File: PickleGit/Services/GitService.cs:1983-2003; PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:289
Issue: `GetMergeConflictedFilePaths()` calls `File.Exists`/`File.ReadLines` with no try/catch, unlike its neighbors in the same file (`ReadFirstLine`, `ReadIntFile`, both a few lines above at GitService.cs:1959-1969) which wrap file access and degrade gracefully (return `null`/`0`) instead of throwing. Separately, the new merge branch in `ContinueOperationAsync` calls this via a bare `await _git.Executor.RunAsync(...)`, bypassing `RunCliAsync`/`RunAsync`/`RunWorkAsync` (RepositoryViewModel.cs:1994-2021) — the wrapper every sibling branch of this same method (CherryPick/Revert/Rebase) uses, which catches exceptions and reports them via `StatusMessage`/`DialogService.ShowError` instead of letting them propagate. Combined with `ContinueOperationCommand`'s `async () => await ContinueOperationAsync()` lambda (an effectively fire-and-forget "async void" from `RelayCommand`'s perspective), an I/O exception here (e.g. a locked/partially-written `MERGE_MSG`) surfaces as an unhandled-exception dialog rather than the app's normal graceful error path — inconsistent with code-style.md's guidance to wrap genuinely-fallible steps of a fire-and-forget chain in a narrow try/catch with `AppLog`.
Fix: Wrap the body of `GetMergeConflictedFilePaths()` in a try/catch that logs via `AppLog` and returns an empty list on failure (matching `ReadFirstLine`/`ReadIntFile`'s pattern in the same file), so a transient file-read failure degrades to "no file list" instead of throwing.

[LOW] Robustness — conflict-block detection depends on a literal, possibly-localized git string
File: PickleGit/Services/GitService.cs:1995
Issue: The block-start check `line.TrimEnd() == "# Conflicts:"` matches only the literal English text git writes into `MERGE_MSG`. If git.exe is run under a non-English `LC_MESSAGES`/locale that localizes this heading, the check never matches, `inConflictsBlock` never becomes true, and the method silently returns an empty list — degrading gracefully (no crash, just a shorter commit message with no file list) rather than failing loudly.
Fix: Low-impact given the graceful degradation and PickleGit's Windows/git-for-Windows target audience (typically English CLI output), but worth a one-line comment noting the assumption, or normalizing/guarding against locale variance if this is ever seen in practice.

[INFO] Behavior change — merge Continue is now a two-step flow instead of one-click completion
File: PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:287-299; PickleGit/Models/ConflictState.cs:34-38
Issue: Previously, clicking Continue for a merge finished the merge in one action (`git merge --continue`). Now it only pre-fills `CommitMessage`, and the user must separately click Commit to actually complete the merge. This is clearly intentional (the updated `RemainingFilesLabel` text says exactly this — "click Continue, then Commit"), and cherry-pick/revert/rebase Continue is deliberately left as one-click. Flagging only as a documented, verified-intentional UX change, not a defect.

[INFO] Verified pre-existing, non-issues checked per review request
- `ConflictInfo` cannot be null when `ConflictInfo.SourceDescription` is read at RepositoryViewModel.Rebase.cs:290: `op` is derived from `ConflictInfo?.Operation ?? ConflictOperation.None` (line 280) with an early return when `op == None` (line 281), so by the time `op == ConflictOperation.Merge` is reached, `ConflictInfo` is provably non-null. The unguarded `ConflictInfo.ConflictedFiles.Count` access at line 282 already relies on the same invariant and predates this diff — not a new risk.
- Skipping `GitService.Reopen()` in the new merge branch is correct: the branch performs no CLI call and mutates no ref/index state — it only reads `MERGE_MSG` and sets a UI-bound string property. There is nothing for libgit2 to have missed. `Reopen()` remains necessary (and, per the PR's own out-of-scope note, already happens) once the actual commit is made via `GitService.CreateCommit`.
- `StringBuilder`/`TrimEnd('\n')` construction is correct for the empty-`conflictedPaths` case: `sourceDesc` comes from `ReadMergeSourceDescription`, which `.Trim()`s the line before returning it (GitService.cs:1974), so it carries no trailing newline; when `conflictedPaths.Count == 0` no further `Append` calls run, and `TrimEnd('\n')` on a string with no trailing `\n` is a no-op — the result is exactly the bare source description (or the "Merge" fallback), matching the intended behavior.
- `GetMergeConflictedFilePaths()`'s handling of a missing `MERGE_MSG` (`File.Exists` check) and a missing/absent `# Conflicts:` block (loop simply never sets `inConflictsBlock`, falls through to `return result` empty) are both correct — no in-progress-merge and no-conflicts-block cases both cleanly return an empty list.
- `using System.Text;` is already present in RepositoryViewModel.Rebase.cs (line 6), so the new `StringBuilder` usage compiles without an added import.
- `CommitDetailView.xaml`'s Grid.Row for this TextBox (row 5 of the "STAGING / WORKING DIRECTORY VIEW" grid, XAML line 797) is `Height="Auto"`, so bumping the TextBox from `Height="60"` to `Height="100"` grows its container without clipping.

---

## Summary

- CRITICAL: 0
- HIGH: 1
- MEDIUM: 1
- LOW: 1
- INFO: 3

## Verdict: NEEDS CHANGES
