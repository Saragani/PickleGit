# PickleGit Claude Workflow — Team Guide

## Overview

| Layer | What it does | Lives in |
|---|---|---|
| **pkl: skills** | Quality gates — fetch issue, plan, static analysis, GitHub issues, commit, handoff, and manual tools | `.claude/commands/pkl/` |
| **sc: skills** | Thinking tools — brainstorm, design, troubleshoot, improve, cleanup | `.claude/commands/sc/` |
| **plans guides** | Per-type instructions — which sc: skills to run, then invoke `pkl:plan` | `.claude/rules/plans/` |
| **CLAUDE.md** | Coding rules, restrictions, doc loading | `CLAUDE.md` |

**No per-developer setup required.** Everything is committed to the repo and loads automatically. `pkl:fetch-issue`, `pkl:create-issue`, `pkl:issue-comment`, and `pkl:issue-close` talk to GitHub through `.claude/scripts/github.sh`, which picks its transport automatically: if the `gh` CLI is installed and `gh auth status` succeeds, it uses `gh api` directly — no extra setup, it reuses `gh`'s own stored credential. Otherwise it falls back to the raw REST API via `curl`, which needs a `$GITHUB_TOKEN` env var (a PAT with `repo` scope) set in your environment.

---

## Quick Start

### Start a new issue
Mention the issue number:
> "gh-42"

Claude will:
1. Run `pkl:fetch-issue` — fetch and summarize the issue from GitHub
2. Identify the plan type (`bug`, `feature`, `refactor`, `investigation`, `script`)
3. Load the matching type guide and run SC analysis (or skip if you say "write directly")
4. Run `pkl:plan` — asks for any spec additions, then writes `.plans/gh-42.md` with Spec, Constraints, Tests, Steps, and After Implementation sections
5. Show the gate summary and wait — **say "proceed"**: `pkl:plan` creates the branch automatically, then BUILD begins

### Resume an existing session
Any of these resumes where you left off:
- `"status"` / `"continue"` / `"where were we"`
- Mentioning any `gh-XXXX` issue number

Claude reads `.plans/gh-<N>.md` frontmatter (`phase`, `step`, `next`, `run_mode`) and jumps straight to the right point.

### Run pkl:plan standalone
`pkl:plan` works without a GitHub issue — pass an issue number, paste requirements, or describe the task in free-form. Useful for ad-hoc tasks or when you don't want to create a tracked issue for something small.

---

## Phase Map

| Phase | What happens | Gate |
|---|---|---|
| **PLAN** | `pkl:fetch-issue` → type guide → SC analysis → `pkl:plan` → `pkl:create-branch` (auto on approval) | BLOCKING — say "proceed" |
| **BUILD** | implement plan steps | BLOCKING — per-step checkpoint |
| **SHIP** | `pkl:commit` → each step is an offer: review-pr → commit → QA list → issue comment → close → retro → cleanup | — |

**Exception — `investigation` type**: no branch, no SHIP phase. Findings go in the plan file itself.

---

## PLAN Phase — Thinking by Type

Claude reads `.claude/rules/plans/<type>.md` and runs the appropriate SC analysis:

| Type | SC skill invoked |
|---|---|
| `bug` | `sc:troubleshoot --type bug --think` (or `--ultrathink` for multi-component) |
| `feature` (unclear approach) | `sc:brainstorm` → offers design or plan |
| `feature` (architecture needed) | `sc:design --type architecture --think-hard` |
| `feature` (1–2 files, clear scope) | skips SC, goes straight to `pkl:plan` |
| `refactor` | `sc:improve` → `sc:cleanup` |
| `investigation` | Explore agent + `sc:troubleshoot` |
| `script` | skips SC, goes straight to `pkl:plan` |

Every type guide ends with `pkl:plan`, which writes the enriched plan file and shows the gate.

### What the plan file contains

`.plans/gh-<N>.md` has four main sections:

| Section | Contents |
|---|---|
| `## Spec` | Problem Statement, Requirements (FR1, FR2…), Acceptance Criteria |
| `## Constraints` | Non-obvious constraints affecting implementation (.NET/C# version, no-DI-container patterns, threading via `GitExecutor`, past solution hints) |
| `## Tests` | Manual/Black-Box scenarios |
| `## Steps` | Implementation steps tracked with ✓ / ← current; each step has the fields below |
| `## After Implementation` | Any post-build verification items specific to this plan (empty if none apply) |
| `## Deviation Register` | Approved deviations that affect upcoming steps — added at checkpoints with a `→ Step M:` impact clause; entries marked ✓ when the target step completes; only unresolved entries surfaced at session resume |

`investigation` and `script` types omit `## Tests` and `## After Implementation`.

Acceptance Criteria not present in the GitHub issue are derived from requirements and marked `[derived]` — review them before approving.

### Step format

Each step has these fields:

| Field | Required | What it's for |
|---|---|---|
| **Why** | Optional | Only present when the approach is non-obvious or a design choice was made over alternatives. Omitted for routine steps. |
| **What** | Always | The atomic action to perform |
| **Touches** | Always | Files that will be modified — defines the scope of the step |
| **Verification stubs** | Always | Plain-English tests, each tagged with the verification type (see below) |
| **Risk** | Always | One line describing what could go wrong, or "None" |
| **Mitigation** | If Risk ≠ None | What to do if the risk fires — omitted when Risk is "None" |
| **Deviations** | Filled at checkpoint | Written by Claude when the step is done — "None" or a description of what changed from the plan and why. If the deviation affects an upcoming step, it also goes into `## Deviation Register`. |

#### Verification stub type tags

Every stub ends with one of:

| Tag | Who runs it | How |
|---|---|---|
| `(unit)` | Automated, dev machine | An automated test project covering the change. **PickleGit currently has no test project** — treat this tag as aspirational until one exists. |
| `(manual)` | Developer, in the app | Use the `run` skill to launch PickleGit, drive it to the relevant state, and observe behavior/logs — or verify by hand if the skill can't reach the relevant control (native dialog, drag-and-drop, credential prompt). |

There's no hardware/device tier — everything PickleGit does is exercisable on the dev machine.

---

## BUILD Phase

1. First, Claude offers a **run mode** choice: `manual` (default — stop at each step's checkpoint for your approval) or `auto` (run steps consecutively, stopping only for deviations, hard blockers, `(manual)` stubs the `run` skill can't complete, or a genuine blocking question). The choice is stored as `run_mode` in the plan frontmatter.
2. Claude implements plan steps one at a time. **Before starting each step**, Claude reads `## Deviation Register` and applies any unresolved entries (not marked ✓) targeting it — each entry's `→ Step M:` clause says exactly what to do differently. After every step:
   - Verifies each stub: `(unit)` → run the relevant test project (once one exists); `(manual)` → drive the app via the `run` skill
   - If any deviation occurred — **BLOCKING in both run modes**: Claude states what deviated and why, asks for your approval, and waits before continuing
   - Once approved, Claude runs `.claude/scripts/checkpoint.sh gh-<N> N --deviations "<text>"` — this single script atomically writes the **Deviations** field, ticks stubs, marks Step N `✓` / Step N+1 `← current`, resolves any `## Deviation Register` entries targeting Step N, and syncs the frontmatter (`step=N+1`, `next=<title>`, `updated=<YYYY-MM-DD HH:MM>`). Claude never hand-edits the plan file directly.
   - Outputs a checkpoint:
   ```
   Step N done: <title>
   Plan updated: phase=BUILD, step=N+1, next=<title>
   Tests: (unit) GREEN | (manual) GREEN
   Deviations: <None or description + reason>
   Register: <entry added or "None">
   Proceed to Step N+1? (approved: step N / rejected: step N / parked)
   ```
   All stubs must be GREEN before the checkpoint is output. If any stub is RED, Claude fixes and re-runs before showing the checkpoint. A fix that touched files outside **Touches** is a deviation.
   - In `manual` mode this checkpoint is **BLOCKING** — Claude waits for `approved: step N` (or an unambiguous bare `ok`/`next`/`go`/`1`) before continuing.
   - In `auto` mode, Claude records a one-line summary and continues straight to the next step unless a deviation, hard blocker, or `(manual)` stub gates it.
   - `parked` (or `pause`) → run `pkl:handoff` then stop
3. After all steps, Claude works through `## After Implementation` items in the plan — each item is offered in order, skipped if the section is empty
4. Then stops — you initiate the commit when ready

### pkl:static-analyze — What it checks (run manually with `/pkl:static-analyze`)

| Check | Condition |
|---|---|
| Code style — naming, threading/no-DI-container patterns, `.claude/rules/code-style.md` checklist | Always |
| Security — path traversal, credential storage, process-execution injection, `.claude/rules/security.md` checklist | Always |
| C# patterns — `BaseViewModel.Set` usage, `IDisposable`, `GitService.Executor`/`GitService.Reopen()` discipline | Always |
| WPF-specific — XAML/code-behind separation, bindings, virtualization, `DynamicResource` theming | Only if changed files include XAML or WPF code-behind |

≤ 5 changed files → runs inline. > 5 files → spawns an `Explore` agent to protect context.

---

## SHIP Phase

When you're ready, say "commit" — `pkl:commit` handles everything in order:

1. "Review-PR hasn't been run yet — want me to run it now?" → `pkl:review-pr`
2. Reads every hunk of the staged diff, then suggests a tiered message per `.claude/rules/commit-format.md` (Trivial / Standard / Complex with `## Why` + `## What` required, `## Tested` optional; plain imperative subject line, no invented issue-number prefix). Commits and **reminds you to push manually.**
3. "Want me to generate a test list?" → `pkl:qa-list`
4. If a linked GitHub issue exists: "Want me to post a summary comment to the issue?" → `pkl:issue-comment`
5. If a linked GitHub issue exists: "Close the issue?" → `pkl:issue-close`
6. "Append a retro to the plan?" → `pkl:retro` — appends a compact `## Retro` (deviations summary, planned-vs-actual step count, one process improvement) and feeds the improvement line forward into `.claude/retro-log.md`.
7. Marks the plan `phase: SHIP, step: done` silently (if a plan file exists)
8. "Delete local plan and review files?" → cleanup

Every step is an offer — nothing is forced.

### pkl:review-pr — How it works

`pkl:review-pr` writes a structured review to `.claude/reviews/<branch>.md` and returns a one-line verdict. It runs as part of `pkl:commit` (step 1) or manually at any time. The base branch is detected dynamically via `.claude/scripts/detect-base-branch.sh` (currently always `main` — never hardcoded elsewhere).

**Checklist sections:** commit format, code style (`code-style.md`), security (`security.md`), WPF/MVVM, threading/git-backend discipline (`GitService.Executor`, `GitService.Reopen()`), rendering/virtualization.

**Severity rubric:**

| Severity | Meaning |
|---|---|
| `CRITICAL` | Security vulnerability, data loss, or crash risk |
| `HIGH` | Build break, wrong runtime behavior, or architecture violation |
| `MEDIUM` | Style/naming/MVVM pattern violation that could cause subtle bugs |
| `LOW` | Nitpick, readability, or minor convention miss |
| `INFO` | Observation, no action required |

**Verdict scale:**

| Verdict | Condition |
|---------|-----------|
| `BLOCKED` | Any CRITICAL finding |
| `NEEDS CHANGES` | Any HIGH finding, no CRITICAL |
| `READY TO MERGE` | Only MEDIUM/LOW/INFO findings |

**What Claude reports after the review:**
```
Review written to .claude/reviews/<branch-name>.md
Verdict: NEEDS CHANGES
CRITICAL: 0  HIGH: 2  MEDIUM: 1  LOW: 3  INFO: 0
```

### pkl:qa-list — How it works

If a `.plans/gh-<N>.md` exists, `pkl:qa-list` reads the `### Manual / Black-Box` section from `## Tests` as its starting point, then enriches it with anything visible from the changed files. If no plan exists, it generates the list from changed files alone. Either way, the output is a plain Markdown list with no internal technical details, ready to paste into a GitHub issue comment or keep for yourself.

---

## Session Handoff

### Auto-resume without handoff

After every Step Checkpoint, the plan frontmatter is already updated (`step=N+1`, `next=<title>`). If you clear the conversation at a clean step boundary, just mention the issue in a new session — Claude reads the frontmatter and resumes from the right step automatically. No handoff needed.

When resuming a BUILD-phase plan, Claude checks `## Deviation Register` and shows a one-line digest of unresolved approved deviations (entries not yet marked ✓) before the first step (non-blocking — no re-approval needed):
> "Carrying forward N approved deviation(s): [list]"

### When handoff adds value

| Scenario | Frontmatter enough? | Need handoff? |
|---|---|---|
| Clean step boundary (checkpoint just ran) | ✅ Yes | Not really |
| Mid-step (stopped before checkpoint) | ❌ No — progress lost | Yes |
| Forward-looking deviations exist | ✅ Yes — written to `## Deviation Register` | Not really |
| Long BUILD with many files touched | ❌ No — Claude won't know what to re-read | Yes |

### Triggers

| How | When |
|---|---|
| `pause` at a Step Checkpoint | Stops after the step, runs handoff |
| `"handoff"` / `"save progress"` / `"pause"` anywhere | Runs immediately |
| ~60–80% context reached | Claude offers once (non-blocking) |
| **Context compaction during BUILD** | PreCompact hook auto-injects an AUTO-HANDOFF TRIGGER — `pkl:handoff` runs as the first post-compaction action so mid-step progress is never lost |

`pkl:handoff` writes a `## Handoff` section into `.plans/gh-<N>.md` with files modified, deviations, and a ready-to-paste resume prompt. The resume prompt includes the full `## Deviation Register` so a new conversation has complete context. Copy it into a new conversation to pick up with full context.

---

## Full Skill Reference

| Phase | Skills |
|---|---|
| PLAN | `pkl:fetch-issue`, `pkl:plan` (creates branch on approval), `sc:brainstorm`, `sc:design`, `sc:troubleshoot`, `sc:improve`, `sc:cleanup` |
| BUILD | `pkl:build` (MSBuild compile check) |
| SHIP | `pkl:commit`, `pkl:qa-list`, `pkl:issue-comment`, `pkl:issue-close`, `pkl:retro` |
| Manual / Anytime | `pkl:handoff`, `pkl:review-pr`, `pkl:security-review`, `pkl:static-analyze`, `pkl:test-plan`, `pkl:commit-only`, `pkl:create-issue`, `pkl:issue-attach`, `pkl:remove-irrelevant-comments` |
