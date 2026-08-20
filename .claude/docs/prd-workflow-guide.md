# PRD Creation Workflow — Team Guide

This guide explains how to use the Claude Code skills in `.claude/skills/` to go from a raw idea to a developer-ready GitHub issue with full acceptance criteria.

---

## What Are Skills?

In Claude Code, **Skills** are structured markdown playbooks (`SKILL.md`) that lock Claude into a disciplined workflow. Instead of free-form prompting, each skill enforces defined phases, gates, and output formats. This shifts Claude from a passive text generator into an active, opinionated collaborator.

---

## The Workflow at a Glance

```
Raw idea / rough notes
        │
        ▼
  1. /grill-me          ← Discovery: stress-test the idea, one Q at a time
     /grill-with-docs   ← (variant: same interview + updates CONTEXT.md / ADRs inline)
        │
        ▼
  2. /to-prd            ← Documentation: synthesize → publish PRD (parent issue)
        │
        ▼
  3. /to-issues         ← Execution planning: break PRD into vertical-slice child issues
        │
        ▼
  4. /close-the-gaps    ← Refinement: hunt gaps → Gherkin ACs → refined issue
     (run on each          (repeat per child issue)
      child issue)
        │
        ▼
  Child issues are dev-ready ────────────────────────────────────────────────────┐
                                                                                 │
  Optional quality gates (run before handing to dev):                            │
  /zoom-out                  ← anytime you need to orient in unfamiliar code     │
  /improve-codebase-         ← before large features; find shallow modules       │
  architecture                                                                   │
                                                                                 │
  ── Handoff to developer workflow ───────────────────────────────────────────►  │
  pkl:fetch-issue → pkl:plan → BUILD phase                                       ┘
```

---

## Skill Reference

### 1. `/grill-me` — Discovery

**What it does:**
Flips the dynamic. Claude becomes the interviewer. It walks every branch of the decision tree, asking one question at a time and always providing its own recommended answer. If a question can be answered by reading the codebase, it reads the codebase instead of asking you.

**Why it's valuable:**
Prevents vague planning. You can't fake your way through it — edge cases, trade-offs, and dependencies all surface before a single word of PRD is written. The one-question-at-a-time discipline prevents overload and keeps decisions explicit.

| | |
|---|---|
| **Input** | Raw idea or rough design (typed or pasted in chat) |
| **Output** | Shared understanding recorded in conversation context |
| **Invoke** | `/grill-me` |

---

### 1b. `/grill-with-docs` — Discovery with Docs

**What it does:**
Same relentless one-question-at-a-time interview as `/grill-me`, but with two extra responsibilities: it challenges your language against an existing `CONTEXT.md` glossary (if one exists), and it updates that glossary — and creates ADRs — inline as decisions crystallise. If no `CONTEXT.md` exists yet, it creates one lazily when the first term is resolved.

**Why it's valuable:**
Prevents terminology drift across the codebase. When you describe a plan using a word that conflicts with the established glossary, it catches the conflict immediately. The session leaves a durable documentation trail rather than just shared understanding in conversation context.

**ADR (Architecture Decision Record):** A short document that captures a single architectural decision — what was decided, why, and what alternatives were rejected. Stored in `docs/adr/`.

**ADR creation rule:** An ADR is only written when all three are true: the decision is hard to reverse, surprising without context, and the result of a real trade-off. If any condition is missing, no ADR is created.

| | |
|---|---|
| **Input** | Raw idea or rough design (typed or pasted in chat) |
| **Output** | Shared understanding + updated `CONTEXT.md` + ADRs (as needed) |
| **Invoke** | `/grill-with-docs` |

---

### 2. `/to-prd` — Documentation

**What it does:**
Takes the current conversation (typically the output of `/grill-me`) and synthesizes it into a structured PRD. It does **not** re-interview you. Before writing, it explores the codebase to ground the PRD in real module names, existing patterns, and domain vocabulary. It sketches the modules to build/modify, checks with you, then writes and publishes the PRD as a GitHub issue with a `ready-for-agent` label.

**Why it's valuable:**
Consistency. Every PRD produced by this skill has the same sections — nothing is ever forgotten. The module-sketch step also surfaces "we need to build X from scratch" surprises before the PRD is finalized.

**PRD sections produced:**
- Problem Statement
- Solution
- User Stories *(exhaustive, numbered list)*
- Implementation Decisions *(decisions only — no file paths)*
- Testing Decisions *(what to test and where prior art lives)*
- Out of Scope
- Further Notes

| | |
|---|---|
| **Input** | Active conversation context (post-`/grill-me`, or your own notes) |
| **Output** | Published GitHub issue with PRD content and `ready-for-agent` label |
| **Invoke** | `/to-prd` |

---

### 3. `/to-issues` — Execution Planning

**What it does:**
Takes the PRD (or any spec, plan, or existing issue) and breaks it into independently-grabbable child issues using **vertical slices** (tracer bullets). Each slice cuts end-to-end through all integration layers — commands, ViewModel, view, and manual verification together — so every issue is demoable or verifiable on its own. It quizzes you on the proposed breakdown (granularity, dependencies, HITL vs AFK classification) before publishing anything.

**Why it's valuable:**
PRDs describe what to build; `/to-issues` decides how to sequence the work. A vertical slice is always preferable to a horizontal one. This means each issue can be picked up, built, and merged independently — no "backend done, waiting for UI" blockers.

**HITL vs AFK:**
- **AFK** (Away From Keyboard) — the slice can be implemented and merged by an agent without human interaction. Preferred.
- **HITL** (Human In The Loop) — requires a decision, design review, or approval step before it can be closed.

**Issue template produced:**
- Parent reference (`#N` — GitHub auto-links it)
- What to build *(end-to-end behavior, no file paths)*
- Acceptance criteria *(checkbox list)*
- Blocked by *(dependency on other slices, as `#N`)*

| | |
|---|---|
| **Input** | Active conversation context, or a PRD issue number/URL passed as argument |
| **Output** | Child GitHub issues published in dependency order, each with `ready-for-agent` label |
| **Invoke** | `/to-issues` or `/to-issues 42` |

---

### 4. `/close-the-gaps [ISSUE-NUMBER]` — Refinement

**What it does:**
A Product Analyst session. Give it a GitHub issue number or paste issue content. It fetches the issue, loads relevant domain skills, explores the codebase areas the issue touches, then silently identifies gaps across 10 categories. It interviews you one multiple-choice question at a time (with a recommended answer for each), then writes a refined issue file with added Gherkin acceptance criteria.

**Why it's valuable:**
Catches "hallucinations of readiness" — the feeling that an issue is complete when it's full of ambiguity that will block the developer mid-sprint. The Gherkin output gives developers unambiguous, testable criteria.

**Gap types it hunts:**
Unclear language · Missing definitions · Unstated assumptions · Conflicting requirements · Skill conflicts · Code conflicts · Missing edge cases · Missing acceptance criteria · Scope ambiguity · Missing actor/trigger

| | |
|---|---|
| **Input** | GitHub issue number (e.g., `42`) or pasted issue text |
| **Output** | `[ISSUE-NUMBER]-refined.md` — original issue + Gherkin scenarios + TBD list |
| **Invoke** | `/close-the-gaps 42` |

---

### 5. `/improve-codebase-architecture` — Architecture Quality Gate

**What it does:**
Explores the codebase for shallow modules — code where the interface is nearly as complex as the implementation, providing no leverage. Surfaces a numbered list of deepening opportunities, then drops into a grilling conversation for whichever candidate you pick.

**Why it's valuable:**
Use before writing implementation decisions in your PRD, or before a large feature build. It prevents the codebase from accumulating structural debt as features are added quickly.

**Key concepts:**
- **Deep module** — high leverage: lots of behavior behind a small interface
- **Shallow module** — interface nearly as complex as the implementation
- **Deletion test** — would deleting this module concentrate complexity, or just spread it across callers?

| | |
|---|---|
| **Input** | Active conversation + codebase (spawns an Explore agent internally) |
| **Output** | Numbered list of deepening candidates → grilling conversation → optional ADR |
| **Invoke** | `/improve-codebase-architecture` |

---

### 6. `/zoom-out` — Navigation Aid

**What it does:**
A one-shot context expander. Tells Claude: go up a layer of abstraction, map all relevant modules and callers, use the project's domain vocabulary. Fires once and returns a module map — it does not start an extended conversation.

**Why it's valuable:**
Use this as a support tool at any point when you or Claude are lost. Especially useful before `/close-the-gaps` (understand what the issue touches) or before `/improve-codebase-architecture` (understand the existing shape before improving it).

| | |
|---|---|
| **Input** | Current conversation context (assumes you've mentioned the area you're in) |
| **Output** | Map of relevant modules, callers, and relationships in domain vocabulary |
| **Invoke** | `/zoom-out` |

---

### 7. `/diagnose` — Bug & Regression Investigation

**What it does:**
A disciplined six-phase debugging loop: **Build a feedback loop → Reproduce → Hypothesise → Instrument → Fix → Cleanup**. The core insight is that the entire skill lives in Phase 1 — if you have a fast, deterministic, agent-runnable pass/fail signal for the bug, you will find the cause. Everything else is mechanical.

**Phases in brief:**
1. **Feedback loop** — build the fastest possible reproducible signal (failing test, CLI fixture, replay trace, throwaway harness, property loop, bisection harness, or the `run` skill driving the app to a repro state)
2. **Reproduce** — confirm the loop produces exactly the failure the user described
3. **Hypothesise** — generate 3–5 ranked, falsifiable hypotheses before testing any of them; show the ranked list to the user
4. **Instrument** — one variable at a time; prefer debugger over logs; tag all debug logs with a unique prefix for easy cleanup
5. **Fix + regression test** — write the test before the fix at the correct seam (once a test project exists); watch it fail, apply the fix, watch it pass
6. **Cleanup** — remove all instrumentation, confirm original repro no longer reproduces, state the winning hypothesis in the commit message

**Why it's valuable:**
Prevents the common failure mode of staring at code without a reproducible signal. The hypothesis ranking step surfaces domain knowledge early. The feedback-loop-first discipline works for both deterministic and flaky bugs (raise the flake rate until it's debuggable).

| | |
|---|---|
| **Input** | Bug report, error description, or performance regression |
| **Output** | Root cause identified, fix applied, regression test in place |
| **Invoke** | `/diagnose` |

---

### 8. `/write-a-skill` — Skill Authoring

**What it does:**
Guides you through creating a new Claude Code skill with the correct structure and progressive disclosure. Gathers requirements (domain, use cases, need for scripts/reference files), drafts the skill, reviews it with you, then writes the final files.

**Skill structure it produces:**
```
skill-name/
├── SKILL.md           # Main instructions (required, target < 100 lines)
├── REFERENCE.md       # Detailed docs split out when SKILL.md grows too large
├── EXAMPLES.md        # Usage examples (optional)
└── scripts/           # Utility scripts for deterministic operations (optional)
```

**Key rules it enforces:**
- The `description:` field in frontmatter is the only thing the agent sees when choosing a skill — it must include "Use when [specific triggers]"
- Split into separate files when `SKILL.md` exceeds 100 lines or content has distinct domains
- Add scripts only for deterministic operations that would otherwise be regenerated repeatedly

| | |
|---|---|
| **Input** | Description of the task/domain the new skill should cover |
| **Output** | New skill directory with `SKILL.md` (and supporting files if needed) |
| **Invoke** | `/write-a-skill` |

---

## Key Differences

### `/grill-me` vs `/grill-with-docs`

Both interview you one question at a time and explore the codebase when needed:

| | `/grill-me` | `/grill-with-docs` |
|---|---|---|
| Interview loop | ✅ | ✅ |
| Explores codebase | ✅ | ✅ |
| Challenges against existing glossary | ❌ | ✅ |
| Updates `CONTEXT.md` inline | ❌ | ✅ |
| Creates ADRs for hard decisions | ❌ | ✅ |
| Output | Shared understanding in context | Shared understanding + updated docs |

Use `/grill-me` for a quick design stress-test with no doc side-effects. Use `/grill-with-docs` when the session should also maintain and sharpen the project's domain language.

---

### `/grill-me` vs `/close-the-gaps`

Both ask you questions one at a time, but they work in opposite directions:

| | `/grill-me` | `/close-the-gaps` |
|---|---|---|
| Starting point | Raw idea, no issue | Existing issue |
| Questions about | Design decisions, trade-offs, "why" | Requirement gaps, ambiguity, edge cases |
| Output | Shared understanding in context | Gherkin-format refined issue file |
| Use when | Before the PRD exists | After the PRD/issue exists |

### `/to-prd` vs `/to-issues` vs `/close-the-gaps`

These three are the core creation chain — each operates at a different level of granularity:

| | `/to-prd` | `/to-issues` | `/close-the-gaps` |
|---|---|---|---|
| Level | Feature (parent) | Slice (child) | Individual issue |
| Creates or refines? | Creates PRD from scratch | Breaks PRD into child issues | Refines one existing issue |
| Asks questions? | No — synthesizes from context | Yes — breakdown review | Yes — gap-by-gap Q&A |
| Output | Published parent GitHub issue | Multiple child GitHub issues | `[N]-refined.md` with Gherkin ACs |
| Explores codebase? | Yes — to ground the PRD | Optional — for domain vocab | Yes — to find code conflicts |
| Use when | You have a validated idea | You have a PRD and need to plan work | You have an issue and need to harden it |

### `/improve-codebase-architecture` vs all others

The only skill focused on the **codebase structure**, not the **requirement**. The first four skills define what to build and how to sequence it. This skill makes the place where you'll build it structurally sound before you start.

---

## Tips

**Don't skip `/grill-me`.**
Going straight to `/to-prd` produces generic PRDs. The grilling session gives Claude the specificity needed to write user stories and implementation decisions that actually match your system.

**Let `/grill-me` read the codebase.**
If Claude asks a question and you don't know the answer, say *"check the codebase for the current behavior."* The skill explicitly supports this.

**Use `/to-issues` to find hidden dependencies.**
When the quiz step asks "are the dependency relationships correct?", take it seriously. Cycles (A blocks B blocks A) usually mean a slice is too fat and needs splitting. The dependency order is also the publishing order — blockers go first so child issues reference real issue numbers.

**`/close-the-gaps` is a quality gate, not a rubber stamp.**
Run it on each child issue produced by `/to-issues`, not just the parent PRD. Each slice has its own edge cases that the parent PRD never covered at that level of detail.

**Run `/improve-codebase-architecture` before sprint planning, not after.**
Architectural improvements found during a sprint cause mid-sprint scope churn. Treat it as part of refinement.

**Use `/zoom-out` anytime you're disoriented.**
It costs nothing and reorients both you and Claude in under a minute.

---

## Connection to the Developer Workflow

The full chain from raw idea to developer plan looks like this:

```
/grill-me  →  shared understanding in context
                    │
                    ▼
/to-prd    →  parent GitHub issue (PRD)
                    │
                    ▼
/to-issues →  child GitHub issues (vertical slices, in dependency order)
                    │
              ┌─────┴──────┐
              │ per child  │  (run /close-the-gaps on each one)
              ▼            ▼
/close-the-gaps  →  [N]-refined.md
                         │
                         │  (Gherkin ACs become Acceptance Criteria)
                         ▼
             pkl:fetch-issue  ←  reads the child issue from GitHub,
                                  including refinements
                         │
                         ▼
             pkl:plan  ←  writes .plans/gh-<N>.md with:
                           • Spec (Problem Statement + Requirements)
                           • Acceptance Criteria (from Gherkin, or derived)
                           • Constraints (technology stack, threading, no-DI-container)
                           • Manual / Black-Box test scenarios
                           • Atomic Steps with verification stubs
                         │
              BLOCKING GATE — user approves plan
                         │
                         ▼
             pkl:create-branch  →  BUILD phase (one step at a time)
```

**Why this chain matters:**

Each step amplifies the next. The Gherkin acceptance criteria from `/close-the-gaps` flow directly into the `## Spec / Acceptance Criteria` section of the `pkl:plan` file, which then drives two concrete outputs:

1. **Manual / Black-Box tests** in `## Tests` — one observable scenario per acceptance criterion
2. **Verification stubs** inside each `## Steps` entry — the check that the criterion is satisfied

Skipping `/grill-me` produces a vague PRD. Skipping `/to-issues` means the developer gets a monolithic issue and has to decompose it themselves. Skipping `/close-the-gaps` means `pkl:plan` falls back to "derived" acceptance criteria instead of explicit Gherkin — which produces weaker stubs and slower, more ambiguous BUILD phases.

The stronger the upstream work, the less interpretation the developer (or agent) has to do downstream.
