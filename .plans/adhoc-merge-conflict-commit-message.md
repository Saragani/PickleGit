---
issue: adhoc-merge-conflict-commit-message
title: Compose an editable merge-conflict commit message
type: bug
component: RepositoryViewModel / GitService (merge conflict resolution)
phase: BUILD
step: 3
next: BUILD complete — SHIP when ready
run_mode: auto
updated: 2026-08-24 19:15
---

# Plan: adhoc-merge-conflict-commit-message — Compose an editable merge-conflict commit message

## Spec
### Problem Statement
When PickleGit concludes a merge that had conflicts, the resulting commit message is just
`Merge branch 'X' into Y` — it loses which files were actually conflicted, unlike tools like
SourceTree, which show a message that also lists the conflicted files.

Root cause (verified empirically in a scratch repo): `RepositoryViewModel.Rebase.cs`'s
`ContinueOperationAsync`, for `ConflictOperation.Merge`, shells out to `git merge --continue`.
`GitCli.cs` sets `GIT_EDITOR=true` for every CLI invocation, and `git merge --continue` behaves
like an interactive `git commit` (no `-m`/`--no-edit`) — git treats that as "editing" the message
even though the no-op editor changes nothing, so it applies its default `--cleanup=strip`, which
strips the `# Conflicts:` comment block it originally wrote into `.git/MERGE_MSG` when the merge
first conflicted. Confirmed: `git commit --no-edit` (skips invoking any editor) preserves the
`# Conflicts:` section verbatim; `git merge --continue` (even with `GIT_EDITOR=true`) strips it.
`git merge --continue` doesn't accept extra flags (`fatal: --continue expects no arguments`), so
there's no way to keep using it and also pass `--no-edit`.

### Requirements
- FR1: When a user resolves a merge conflict and all conflicted files are staged, clicking
  **Continue** composes a commit message (source branch → target branch, plus the list of files
  that were originally conflicted) and places it directly into the existing commit-message text
  box (`CommitMessage`), instead of running `git merge --continue`.
- FR2: The composed message is plain, editable text in the normal commit box — the user can change
  it before committing, exactly like any other commit message. No auto-commit happens as part of
  Continue for a merge.
- FR3: The user finishes the merge with the app's ordinary **Commit** button. Verified empirically
  (via a direct LibGit2Sharp call against a repo with `MERGE_HEAD` present) that `Repository.Commit`
  — the same call `GitService.CreateCommit` already uses — automatically detects the in-progress
  merge and adds `MERGE_HEAD`'s commit as a second parent, using whatever message string is passed
  verbatim (no cleanup/stripping). No change to the Commit path itself is needed.
- FR4: The conflicted-file list must be captured before it's needed for the message, since
  `ConflictInfo.ConflictedFiles` is re-read live from the index and reaches 0 once every file is
  resolved and staged (i.e., exactly when Continue is clicked). Verified `.git/MERGE_MSG`'s
  `# Conflicts:` block is untouched by resolving/staging files — it stays intact, listing every
  originally conflicted file, until the merge actually concludes (a real commit clears
  `MERGE_HEAD`/`MERGE_MSG`). Read that block at Continue-time instead of adding new snapshot state.
- FR5: Only the `ConflictOperation.Merge` case changes. Cherry-pick, revert, and rebase continue
  keep calling their existing CLI `--continue` commands unchanged — out of scope per the original
  ask.
- FR6: The "all conflicts resolved" guidance text shown while a merge is in progress currently
  reads "All conflicts resolved — click Continue to complete the operation"
  (`ConflictState.RemainingFilesLabel`) — under the new flow, Continue no longer completes the
  merge by itself, so this text needs to say something accurate for the Merge case specifically
  (e.g., "...click Continue, then Commit, to complete the merge"). Cherry-pick/revert/rebase keep
  the existing wording since their Continue still completes the operation directly.

### Acceptance Criteria
- AC1: Given a merge with conflicts in `f.txt` and `g.txt`, when both are resolved, staged, and
  Continue is clicked, then the commit-message box contains a message naming the source and target
  branches and listing `f.txt` and `g.txt` — and the merge is NOT yet committed (`MERGE_HEAD` still
  present, working tree/staged files unchanged by the click itself).
- AC2: Given the state in AC1, when the user edits the message and clicks Commit, then a single
  merge commit is created with two parents (the pre-merge HEAD and the merged-in branch tip) and
  the exact message the user left in the box — no `# Conflicts:` stripping, no extra editor pass.
- AC3: Given the state in AC1, when the user clicks Abort instead, then the merge aborts exactly as
  it does today (unaffected by this change).
- AC4: Cherry-pick/revert/rebase Continue behavior is unchanged — still commits immediately via the
  existing CLI `--continue` commands. [derived]

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- All git reads/writes must route through `GitService.Executor` — no direct LibGit2Sharp/CLI calls
  from the UI thread (see `architecture.md`).
- `GitService` stays the read/domain layer (raw git-state parsing); message *composition* (the
  user-facing string) belongs in `RepositoryViewModel.Rebase.cs`, matching this codebase's existing
  separation (e.g. `ReadMergeSourceDescription` in `GitService.cs` vs. banner text built in the
  ViewModel/View).
- Process execution rules (`code-style.md`): no shell-interpreted string concatenation of branch
  names/paths — not directly relevant here since this change removes a CLI call rather than adding
  one, but any new CLI usage must still follow that rule.

## Tests
### Manual / Black-Box
- [ ] Create a merge with 2+ conflicting files, resolve and stage all of them, click Continue →
      commit-message box shows source/target branch names and lists every originally-conflicted
      file; `MERGE_HEAD` still exists; no new commit yet.  <!-- AC1 -->
- [ ] From that state, edit the message and click Commit → resulting commit has two parents and the
      exact edited message, verified via `git log -1 --pretty=full`.  <!-- AC2 -->
- [ ] From that state, click Abort instead → merge aborts and working tree returns to pre-merge
      state, same as before this change.  <!-- AC3 -->
- [ ] Trigger a cherry-pick conflict, resolve it, click Continue → commits immediately exactly as
      today (no commit-message-box pre-fill, no behavior change).  <!-- AC4 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Read the original conflicted-file list from MERGE_MSG ✓
**Why**: `ConflictInfo.ConflictedFiles` is a live re-read of the index and is empty by the time
Continue is clickable (FR4). `.git/MERGE_MSG`'s `# Conflicts:` block is the one place that still
has the full original list at that point, and it's already read once by `ReadMergeSourceDescription`
for the first line — this adds a sibling read for the file-list block, not a whole new mechanism.
**What**: Add a method to `GitService.cs` (near `ReadMergeSourceDescription`) that reads
`.git/MERGE_MSG` and returns the list of paths from its `# Conflicts:` block (lines after that
marker, stripping the leading `#\t`/`# ` prefix), returning an empty list if the file or the
section is missing.
**Touches**: `PickleGit/Services/GitService.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Create a merge conflict in 2 files, resolve+stage them, call the new method directly (or via
      a temporary breakpoint/log) and confirm it returns both file paths even though the index no
      longer reports any conflicts.  (manual)
**Risk**: MERGE_MSG format could differ across git versions (e.g. tab vs. spaces after `#`).
**Mitigation**: Trim whitespace generously when stripping the `#` prefix rather than matching an
exact literal tab character.
**Deviations**: None

### Step 2: Compose the merge commit message and route it to the commit box instead of `merge --continue` ✓
**Why**: Keeps `GitService` as a pure read layer (Step 1) while the user-facing message format
lives where the UI decisions already are (this file already builds `SourceDescription`-based UI
text).
**What**: In `RepositoryViewModel.Rebase.cs`'s `ContinueOperationAsync`, split the `switch` so
`ConflictOperation.Merge` no longer falls into the shared `RunCliAsync(..., "merge --continue", ...)`
path. Instead: read the conflicted-file list (Step 1's method) and `ConflictInfo.SourceDescription`,
read the current branch name, compose a message such as:
```
Merge '<SourceDescription>' into <currentBranch>

Resolved conflicts in:
  <file1>
  <file2>
```
and set `CommitMessage` to it. Do not call any CLI command and do not call `CaptureUndo` (no HEAD
move has happened yet — that now happens later, through the ordinary Commit flow, which already
handles its own undo capture). Leave the CherryPick/Revert/Rebase branches of the switch unchanged.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] AC1 scenario end-to-end via the `run` skill: resolve a 2-file conflict, click Continue,
      confirm the commit box shows both file names and the branch names, and that no commit was
      created yet.  (manual)
- [x] AC2: edit the pre-filled message, click the existing Commit button, confirm (via
      `git log -1 --pretty=full` in the repo) a 2-parent commit with the exact edited message —
      no `# Conflicts:` stripping, no unexpected editor pass.  (manual)
- [x] AC3: Abort still works unchanged from the pre-fill state.  (manual)
- [x] AC4: cherry-pick conflict Continue still commits immediately with no commit-box pre-fill.  (manual)
**Risk**: If `GitService.CreateCommit`'s LibGit2Sharp call ever stops auto-detecting `MERGE_HEAD`
(e.g. a future LibGit2Sharp upgrade), merges would silently stop getting a second parent.
**Mitigation**: AC2's manual verification stub explicitly checks parent count each time this path
is touched; no code-level guard is added since this is inherent, already-relied-upon LibGit2Sharp
behavior (confirmed empirically for the version currently pinned, 0.27.2).
**Deviations**: Composed the merge commit message using ConflictInfo.SourceDescription verbatim as the first line, instead of the plan's literal 'Merge <SourceDescription> into <currentBranch>' template. SourceDescription for a merge is already MERGE_MSG's own first line (e.g. "Merge branch 'feature'", which git itself appends ' into <target>' to when relevant) — wrapping it in another 'Merge ... into ...' layer would have produced a garbled double-nested sentence. Using it verbatim satisfies FR1's actual requirement (source/target branch info + conflict file list) without the bug. No Touches/scope change.

### Step 3: Fix the "all conflicts resolved" guidance text for merges ✓
**Why**: FR6 — `ConflictState.RemainingFilesLabel`'s current wording ("click Continue to complete
the operation") becomes wrong for merges specifically once Continue no longer completes anything by
itself.
**What**: In `PickleGit/Models/ConflictState.cs`, make the fully-resolved branch of
`RemainingFilesLabel` operation-aware: for `Operation == ConflictOperation.Merge`, say something
like "All conflicts resolved — click Continue, then Commit, to complete the merge"; keep the
existing "click Continue to complete the operation" wording for CherryPick/Revert/Rebase.
**Touches**: `PickleGit/Models/ConflictState.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Resolve all files in a merge conflict (before clicking Continue) → banner shows the new
      merge-specific wording.  (manual)
- [x] Resolve all files in a cherry-pick conflict → banner still shows the original wording.  (manual)
**Risk**: None.
**Deviations**: None

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
