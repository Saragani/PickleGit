---
issue: adhoc-merge-commit-no-changes
title: Allow committing a resolved merge with no net changes
type: bug
component: RepositoryViewModel (commit commands)
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-11-09 10:17
---

# Plan: adhoc-merge-commit-no-changes — Allow committing a resolved merge with no net changes

## Spec
### Problem Statement
During a merge where every changed file conflicts, resolving all of them with "ours" leaves the index identical to HEAD. Nothing shows as staged or unstaged. Git still has `MERGE_HEAD`, and `git commit` creates the merge commit, but PickleGit's Commit and Commit & Push buttons stay disabled. The only way to finish the merge is the command line.

Root cause: both commands' `CanExecute` is `HasRepo && !string.IsNullOrWhiteSpace(CommitMessage) && (StagedFiles.Count > 0 || IsAmend)` (`ViewModels/RepositoryViewModel.cs:861`, `:865`). A merge with no net changes has `StagedFiles.Count == 0`, so both stay disabled.

### Requirements
- FR1: Commit and Commit & Push are enabled when a merge is in progress (`ConflictInfo.Operation == Merge`), no conflicted files remain, and the commit message is non-empty, even with nothing staged.
- FR2: Committing in that state creates a real merge commit (2 parents: HEAD and MERGE_HEAD), ends the merge state (banner gone), and returns the working dir to clean.
- FR3: Button state updates as soon as the conflict state changes, without waiting for incidental mouse or keyboard input.
- FR4: Nothing else changes: with no merge in progress and nothing staged, Commit stays disabled. While conflicts remain unresolved, Commit stays disabled.

### Acceptance Criteria
- AC1 [derived]: Given a merge whose only changes are conflicts, all resolved with "ours", when Continue fills the merge message, then Commit and Commit & Push are enabled.
- AC2 [derived]: Given AC1, when I click Commit, then a merge commit with 2 parents is created, the conflict banner disappears, and the graph shows the merge.
- AC3 [derived]: Given a merge with at least one unresolved conflict, then Commit stays disabled.
- AC4 [derived]: Given no merge in progress and nothing staged, then Commit stays disabled (regression).

## Constraints
- C# 7.3 / .NET 4.7.2.
- `CanExecute` must not call git. It reads only the already-loaded `ConflictInfo` (populated through `GitService.Executor`).
- `RelayCommand` re-queries on `CommandManager.RequerySuggested`, so call `CommandManager.InvalidateRequerySuggested()` when `ConflictInfo` changes (code-style.md).
- LibGit2Sharp `Repository.Commit` throws `EmptyCommitException` for a commit with no changes. To be verified: I believe that check covers only single-parent commits, which would let a 2-parent merge through. The signed path (`git commit -S`) accepts a merge with no net changes because `MERGE_HEAD` exists.
- Verification uses an isolated `--test-instance` + `PICKLEGIT_APPDATA` scratch repo (retro-log: verify against a sandbox, not the user's live repos). Prefer UIA patterns (`SelectionItem`/`Invoke`) over synthetic mouse input on the live desktop.

## Tests
### Manual / Black-Box
- [ ] Scratch repo: branch `feature` and `main` both edit the same line of `a.txt` → merge `feature` into `main` → resolve with "ours" → Continue → Commit enabled  <!-- AC1 -->
- [ ] Click Commit → `git log -1 --format=%P` shows 2 parents, `MERGE_HEAD` is gone, banner is hidden  <!-- AC2 -->
- [ ] Repeat the merge, leave the conflict unresolved → Commit disabled  <!-- AC3 -->
- [ ] Clean repo with no merge and a typed message → Commit disabled  <!-- AC4 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh adhoc-merge-commit-no-changes N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Enable commit for a resolved merge with nothing staged ✓
**What**: In `RepositoryViewModel.cs`, add `private bool IsResolvedMergePending => _conflictInfo?.Operation == ConflictOperation.Merge && _conflictInfo.ConflictedFiles.Count == 0;`. Extend both `CommitCommand` and `CommitAndPushCommand` `CanExecute` to `(StagedFiles.Count > 0 || IsAmend || IsResolvedMergePending)`. In the `ConflictInfo` setter, call `CommandManager.InvalidateRequerySuggested()` on change.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Merge resolved with "ours" with no net change → after Continue, Commit and Commit & Push are enabled  (manual)
- [x] Clicking Commit creates a 2-parent merge commit; `MERGE_HEAD` is removed; the conflict banner hides  (manual)
- [x] A merge with an unresolved conflict → Commit is disabled  (manual)
- [x] No merge, nothing staged, message typed → Commit is disabled  (manual)
**Risk**: LibGit2Sharp's `Repository.Commit` may throw `EmptyCommitException` for a merge commit with no changes.
**Mitigation**: Pass `AllowEmptyCommit = true` in `GitService.CreateCommit`'s `CommitOptions` only when `MERGE_HEAD` exists. This touches `Services/GitService.cs`, which makes it a deviation that needs approval.
**Deviations**: None

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-11-09 10:30 -->

**Deviations:** Step 1 held as written. One user-approved addition after review: an amend guard in `RepositoryViewModel.Staging.cs` (CommitAsync refuses amend mid-merge), fixing an existing bug where the LibGit2Sharp amend silently dropped the merge. Verified live.
**Steps planned vs. actual:** 1 planned / 1 actual, plus one post-review addition (amend guard).
**Process improvement:** When a plan enables an action in a new state, list every option that changes how that action runs (amend, sign-off, signed CLI path) and add a stub for each in that state, so modifier interactions are caught in BUILD, not review.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
