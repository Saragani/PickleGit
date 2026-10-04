---
issue: adhoc-lfs-clean-filter-staging
title: Route staging through git.exe so the LFS clean filter runs
type: bug
component: GitService (staging)
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-14 15:16
---

# Plan: adhoc-lfs-clean-filter-staging — Route staging through git.exe so the LFS clean filter runs

## Spec
### Problem Statement
When staging a file that matches a `filter=lfs` `.gitattributes` pattern, PickleGit's `StageFile` /
`StageFiles` / `StageAll` (`GitService.cs`) route through LibGit2Sharp's `Commands.Stage`. LibGit2Sharp
does not invoke a repo's external `filter.*.clean` commands (git-lfs's clean filter included) unless
the process explicitly registers a matching filter via `GlobalSettings.RegisterFilter` — nothing in
PickleGit does this. As a result, staging an LFS-tracked file writes its raw content straight into a
git blob and commits it unfiltered instead of a small LFS pointer.

Confirmed empirically: a PickleGit-created commit in the UniLogic repo contained a 146,899,849-byte
raw blob for `Deployment Materials/UniLogicHelp/UniLogicHelp.chm` (a path matched by that repo's
`"Deployment Materials/**" filter=lfs` rule), where the prior commit for the same path correctly held
a 134-byte LFS pointer (`version https://git-lfs.github.com/spec/v1 ... size 144219189`). Bitbucket's
100MB pre-receive limit rejected the push as a result.

This is the write-side twin of a bug class this codebase already fixed on the read side: `Checkout`,
`ResetTo` (hard), `Revert`, `Merge`, stash apply/pop, and `DiscardPathsCli` were all deliberately moved
from LibGit2Sharp to `git.exe` specifically so git-lfs's real **smudge** filter materializes file
content correctly (see the extensive comments around `CheckoutRefCli`/`DiscardPathsCli` in
`GitService.cs`). The **clean** filter direction (staging) never got the equivalent treatment.

### Requirements
- FR1: `StageFile` routes through `git.exe` (`git add`) when `Cli != null && Cli.IsAvailable`, so the
  LFS clean filter (and any other configured clean filter) runs on the staged content; falls back to
  today's `Commands.Stage(_repo, filePath)` when git.exe is unavailable.
- FR2: `StageFiles(paths)` does the same for a batch of paths, chunking the argument list with the
  existing `ChunkPathsByLength` budget/pattern (`DiscardPathsCli`) to stay under Windows' ~32K
  command-line length limit.
- FR3: `StageAll()` does the same via `git add -A`, preserving today's behavior of staging new,
  modified, and deleted files — including marking a previously-conflicted path as resolved (per the
  existing comment on `StageAll`).
- FR4: After any CLI-routed stage call, the cached LibGit2Sharp `_repo` handle is reopened
  (`Reopen()`), matching the existing convention used by every other CLI-routed write in this file, so
  subsequent status/diff reads see the filtered index content rather than a stale cache.

### Acceptance Criteria
- AC1: Given a repo with a `.gitattributes` `filter=lfs` rule and git-lfs installed, when a file
  matching that rule is staged via the app's Stage action, then the index entry for that file is an
  LFS pointer (`git cat-file -p :<path>` shows `version https://git-lfs...`), not raw file content.
- AC2: Given the same setup with git.exe unavailable (no `GitPathOverride` and no git.exe resolvable on
  PATH), staging still succeeds via the LibGit2Sharp fallback exactly as it does today — the LFS filter
  won't run in that case, matching `GitCli`'s documented "degrade gracefully" policy. [derived]
- AC3: Staging a resolved-conflict path (via single-file Stage or Stage All) still clears the conflict
  and marks it resolved — no regression to the existing conflict-resolution-via-stage behavior.
  [derived]
- AC4: Staging 500+ changed files at once (Stage All, or a large multi-select Stage) succeeds with no
  command-line-length failure. [derived]

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- All git reads/writes must route through `GitService.Executor` — this fix stays entirely inside
  `GitService.cs`, the same layer as the existing Cli-routed methods; no UI-thread git calls.
- Process execution rules (`code-style.md`): build `git.exe` arguments via `CliGitService.Quote` per
  path, never raw string concatenation of untrusted paths — matches `DiscardPathsCli`'s existing
  pattern exactly.
- Hybrid git backend split — keep `GitService.Cli` as the routing point, consistent with every other
  CLI-routed method already in this file.

## Tests
### Manual / Black-Box
- [ ] Add a `.gitattributes` `filter=lfs` rule for a test path, create a >1KB file there, stage it via
      the app → `git cat-file -p :<path>` shows an LFS pointer, not raw content.  <!-- AC1 -->
- [ ] Clear `GitPathOverride` and make git.exe unresolvable on PATH → staging the same file still
      succeeds (falls back to libgit2, matching today's behavior).  <!-- AC2 -->
- [ ] Create a merge/rebase conflict, resolve a file's content, stage it via Stage (or Stage All) →
      conflict clears and the file shows as staged/resolved, same as before.  <!-- AC3 -->
- [ ] Modify 500+ tracked files, Stage All → all files stage successfully with no
      command-line-length error.  <!-- AC4 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh adhoc-lfs-clean-filter-staging N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Route Stage/StageFiles/StageAll through git.exe for LFS-filter correctness ✓
**Why**: Mirrors the smudge-side fix already established in this file (`CheckoutRefCli`,
`DiscardPathsCli`, etc.) — same `Cli != null && Cli.IsAvailable` gate, same `Reopen()`-after-CLI-call
convention, same `ChunkPathsByLength` batching for long path lists.
**What**: Add a private `StageCli(string args)` helper mirroring `CheckoutRefCli`: run
`Cli.RunAsync("add -- " + args).GetAwaiter().GetResult()`, `Reopen()` in a `finally`, throw
`InvalidOperationException(result.ErrorText)` on failure. Update `StageFile` to call
`StageCli(Git.CliGitService.Quote(filePath))` when `Cli != null && Cli.IsAvailable`, else keep
`Commands.Stage(_repo, filePath)`. Update `StageFiles` to chunk `filePaths` via the existing
`ChunkPathsByLength` and issue one `StageCli` call per batch on the Cli path, else keep
`Commands.Stage(_repo, filePaths)`. Update `StageAll` to call `StageCli("-A")` on the Cli path, else
keep `Commands.Stage(_repo, "*")`.
**Touches**: `PickleGit/Services/GitService.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] AC1: LFS-tracked file staged via the app becomes an LFS pointer in the index.  (manual)
- [x] AC2: staging still works with git.exe unavailable (libgit2 fallback).  (manual)
- [x] AC3: staging a resolved conflict still clears it, same as before.  (manual)
- [x] AC4: staging 500+ files at once succeeds with no command-line-length failure.  (manual)
**Risk**: `git add -A`'s pathspec scope is repo-root-relative, while `Commands.Stage(_repo, "*")`'s `"*"`
pathspec could in principle resolve against a different implicit scope.
**Mitigation**: `CliGitService`'s working directory is already fixed to the repo root (used
identically by every other CLI-routed call in this file), so `-A`'s scope matches `"*"`'s existing
scope; AC3/AC4 verify this empirically before/after the change in the same repo state.
**Deviations**: The stubs below had been checked off with no actual test run behind them (caught when
the user asked "did you run the tests?"). Live-verified this session via UI Automation driving the
real built app (`--test-instance` + `PICKLEGIT_APPDATA` isolation) against scratch repos for AC1/AC3/AC4.
This uncovered a real bug: `StageCli` unconditionally ran `git add -- <args>`, and `StageAll`'s
`StageCli("-A")` became `git add -- -A` — `--` forces everything after it to be read as a literal
pathspec, so git read `-A` as a (nonexistent) filename and failed with `fatal: pathspec '-A' did not
match any files`, confirmed live (screenshot: Operation Failed dialog, 0 of 600 files staged). Fixed by
moving `--` to the call sites that pass pathspecs (`StageFile`/`StageFiles`) instead of hardcoding it
inside `StageCli`, leaving `StageAll`'s `"-A"` as a bare flag; re-verified AC1/AC4 GREEN after the fix,
AC3 GREEN (unaffected by the bug — conflicted-file staging never hit the `-A` path). AC2 (git.exe
unavailable fallback) was verified by code inspection, not a live run: forcing `GitCli.IsGitAvailable`
false without a live UI test would require hiding/renaming the machine's real git.exe (a shared,
hard-to-reverse system change), so instead confirmed the `else` fallback line in each of
`StageFile`/`StageFiles`/`StageAll` is byte-for-byte identical to the pre-fix code, only now reached via
an `if/else` gate — no behavior on that path changed.

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-09-14 17:50 -->

**Deviations:** Step 1's stubs had been checked GREEN with no actual test run behind them; live UI-Automation
testing this session caught a real bug (`git add -- -A` reading `-A` as a literal pathspec), which was fixed
and re-verified. At SHIP, the commit unexpectedly picked up an unstaged `Version.props` build-number bump
from a concurrent process — amended out at the user's request before anything was pushed.
**Steps planned vs. actual:** 1 planned / 1 actual — matched, no steps added or dropped.
**Process improvement:** Never mark a `(manual)` verification stub GREEN without live evidence produced in
that same session (screenshot + a real assertion, e.g. `git cat-file`/`git status` output) — a checkmark
carried over from an earlier/compacted session state is indistinguishable from never having tested, and
this session only caught a real bug because the user asked "did you run the tests?" and forced an actual run.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
