# Workflow

## Session Resume

When an issue is mentioned or the user says "continue" / "status" / "where were we":
- Issue mentioned → check if `.plans/<issue>.md` exists → found: read the **full** `.plans/<issue>.md` — frontmatter (`phase`, `step`, `next`, `run_mode`) + all step content + `✓`/`← current` markers + `## Deviation Register` + `## Handoff` section if present. (`architecture.md`, `code-style.md`, and `security.md` are already in context — everything under `.claude/rules/**` auto-loads every session.) **Frontmatter is the single authoritative state**: resume at `step N` from frontmatter. The `← current` marker is a derived convenience re-synced on each write — if it disagrees with frontmatter, frontmatter wins (no ambiguity rule, no reconciliation needed). **Honor `run_mode` from frontmatter** — `auto` → continue running steps without re-asking (still stop only on deviation/blocker/human-verification-stub, per BUILD Phase); `manual` (or absent) → gated per-step checkpoints. The auto/manual *behavior* is defined in this file (auto-loaded every session) — only the per-plan choice lives in frontmatter, so a fresh session resumes the same cadence. → not found: user can choose to start the workflow or skip it
- No issue mentioned → scan `.plans/` directory for existing plan files → found: list them and ask which to resume → none found: skip workflow entirely, just answer

When resuming a BUILD-phase plan, check `## Deviation Register` — if it has **unresolved** entries (not marked ✓), output a one-line digest before the first step (non-blocking, no re-approval needed):
> "Carrying forward N approved deviation(s): [list unresolved entries]"

Also on BUILD resume: re-offer the **run mode** (`manual` / `auto`) unless `run_mode` is already set in frontmatter.

## General Rules

**Blocking questions** — when any step ends with a question to the user, stop completely. Do not run tools, suggest next steps, or generate content until the user replies.

**Approval grammar (named verdicts)** — gates advance on a verdict that names the gate it advances:
- `approved: <gate>` — advance the named gate
- `rejected: <gate>` — do not advance; revise per the user's feedback, then re-present the gate
- `parked` — pause work (equivalent to the pause signal: run `pkl:handoff`, then halt)

where `<gate>` ∈ {`plan`, `step N`, `build`, `ship`}.

Bare aliases `ok` / `next` / `go` / `1` (and `proceed` / `yes` at the PLAN gate) remain valid **only when the target gate is unambiguous** — i.e. exactly one checkpoint is open. If more than one gate could be meant, the agent MUST ask which gate is being approved before advancing — never guess. (This is also why confirming a GitHub issue's description is not plan approval: the approved gate must be named, or unambiguous.)

---

## Phase Map

| Phase | Skills | Gate |
|---|---|---|
| PLAN | `pkl:fetch-issue` → type guide (`plans/*.md`) → `pkl:plan` | BLOCKING — wait for `approved: plan` |
| BUILD | `pkl:create-branch` + plan steps + After Implementation | BLOCKING — per-step checkpoint |
| SHIP | `pkl:commit` (includes review-pr, issue comment, close, cleanup) | — |

**Exception — `investigation` type**: no `pkl:create-branch`, no SHIP phase. Plan only; findings go in the plan file itself.

---

## Plan File Format

Every issue gets `.plans/gh-<N>.md` (GitHub issue number, `gh-` prefix — matches the branch-naming convention). Format is defined in `pkl:plan` — see `.claude/commands/pkl/plan.md`.

Handoff: run `pkl:handoff` to write the `## Handoff` section, then stop.
Resume: read `phase`, `step`, `next` from frontmatter and paste the Handoff block into a new conversation.

---

## PLAN Phase

Sequence:
1. Run `pkl:fetch-issue` — it handles type detection, reads and displays the type guide, and asks the user to choose (follow guide / write directly / skip)
2. If following the guide: execute SC commands per the guide, then invoke `pkl:plan`; if writing directly: invoke `pkl:plan` immediately
3. `pkl:plan` writes the enriched file and holds the BLOCKING gate — wait for `approved: plan` (or an unambiguous bare `proceed` / `yes` / `go` / `ok` / `1`)

`pkl:plan` can also be invoked standalone (no prior `pkl:fetch-issue` or type guide needed) — it accepts an issue number, pasted text, or a free-form prompt as its context source.

## BUILD Phase

Run `pkl:create-branch` first — unless `pkl:plan` already ran it on plan approval (it does so automatically). If you skipped the plan, run it now.

**Run-mode offer** — before the first step, offer once:
> "How should BUILD run?
> - **manual** (default) — stop at each step's checkpoint for your approval (`approved: step N`).
> - **auto** — run steps consecutively without per-step approval; stop only on a deviation, a hard blocker, a `(manual)` stub the `run` skill can't complete on its own, or a genuine blocking question. I show a consolidated report at the end and commit once at SHIP."

Store the choice as the **run mode** in frontmatter via the atomic lib (one home for frontmatter writes — `plan.md`'s scaffold already seeds `run_mode: manual`, so the key exists):
```bash
.claude/scripts/plan-frontmatter.sh set-field .plans/gh-<N>.md run_mode <manual|auto>
```
Default `manual` if the user gives no preference. Re-offer on BUILD resume (per Session Resume above).

Then follow plan steps in order, one at a time.

**Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently.

After EVERY completed step — before doing anything else:
1. Walk through each verification stub in the completed step and confirm the implementation satisfies it — fix code if any stub fails. For `(unit)` stubs: run the relevant automated test project and confirm all tests pass (GREEN) — note that PickleGit currently has no test project, so `(unit)` stubs should be rare until one exists; prefer `(manual)` for anything not genuinely unit-testable today. For `(manual)` stubs: use the `run` skill first — launch PickleGit, drive it to the relevant state, screenshot, and confirm the expected behavior; fall back to asking the user only if the skill can't reach the relevant control (e.g. a native dialog, drag-and-drop interaction, or a Windows-integration surface it can't drive). **(Judgment — never skipped, in either run mode.)**
2. Compare what was actually implemented against the plan step's **What** and **Touches** fields — identify any deviations
3. **If any deviation occurred** — BLOCKING in **both** run modes: state what deviated and why, ask for user approval, wait for it before continuing
4. Decide the deviation text (judgment — "None" if none) and, if a deviation affects an upcoming step, the register entry (`[Step N → affects Step M] <what changed and why> → Step M: <what to do differently>`). **Do not hand-edit the plan** — pass these to `checkpoint.sh` in step 5.
5. Run the checkpoint helper — it does **all** the plan-state writes atomically (via `plan-frontmatter.sh`): marks Step N `✓`, ticks Step N's `- [ ]` stubs to `- [x]`, marks Step N+1 `← current`, resolves register entries targeting Step N, writes the **Deviations** field, optionally appends a new register entry, and syncs frontmatter:
   ```bash
   .claude/scripts/checkpoint.sh gh-<N> N --mode <run_mode> --deviations "<text or None>" [--add-register "<entry>"]
   ```
   Because `checkpoint.sh` owns every plan write, the model never `Edit`s the plan file directly (avoids the read-before-edit churn after the script's external write).
6. Then, by **run mode**:
   - **manual** — the helper prints the Step Checkpoint skeleton; fill in the Tests/Changed/Deviations/Register lines (your judgment) and output it. **BLOCKING** — do not proceed until the user gives a named verdict (`approved: step N`) or an unambiguous bare approval signal.
   - **auto** — the helper records a one-line step summary (no blocking prompt). Continue straight to Step N+1 — **unless** this step hit a gate (next paragraph).

**What still gates in `auto` mode** (stop and wait, even in auto): a **deviation** (step 3); a stub that can't be made GREEN after retries (hard blocker); a `(manual)` stub that needs a person (a native Windows dialog, drag-and-drop, credential prompt, or anything the `run` skill can't drive on its own); a genuine BLOCKING question in the step. Verification (steps 1–2) is **never** skipped in auto — every stub must be GREEN before a step advances; only the per-step *approval handshake* is dropped.

**End of an `auto` run** — after the last step, emit the consolidated report, then go to `## After Implementation` / SHIP:
```bash
.claude/scripts/checkpoint.sh gh-<N> --final-report
```

**Step Checkpoint format:**
> Step N done: \<step title\>
> Plan updated: phase=BUILD, step=N+1, next=\<Step N+1 title\>
> Tests: \<per stub — "(unit) GREEN" / "(manual) GREEN" — if a stub was RED and fixed before this checkpoint, note: "(manual) RED → fixed → GREEN"\>
> Changed: \<one line per requirement touched this step — "FR\<n\> — \<one-line what changed\> (files …)"\>
> Deviations: \<list each deviation and why — or "None"\>
> Register: \<entry added: "[Step N → affects Step M] ..." — or "None"\>
> Proceed to Step N+1? (`approved: step N` / `rejected: step N` / `parked` — or an unambiguous bare `ok` / `next` / `go` / `1`)

**Tests line rules**: All stubs must be GREEN before the checkpoint is output. For every stub type, Claude first reviews the implementation against the stub description and confirms the logic is correct — fix code if not. Then:
- `(unit)` — run the relevant automated test project; if RED fix code and re-run until GREEN. (No test project currently exists in PickleGit — treat any `(unit)` stub written into a plan as aspirational until one is added, and prefer `(manual)` in the meantime.)
- `(manual)` — use the `run` skill: launch PickleGit, drive it to the relevant state, screenshot, and confirm the expected behavior; if RED fix code and re-run until GREEN. If the skill can't reach the relevant control (native dialog, drag-and-drop, credential prompt, or another Windows-integration surface it can't drive), fall back to asking the user to verify in the running app and report GREEN or RED — this fallback gates even in `auto` mode (see above).

A fix that was not in **What** or **Touches** is a deviation — follow the deviation gate.

A deviation is any of: touching a file not listed in **Touches**, skipping part of **What**, doing something not described in **What**, or changing approach mid-step. Always state the reason (discovered dependency, bug found, plan was wrong, etc.).

Approval signals: `approved: step N` (named verdict) — or bare `ok` / `next` / `go` / `1` when the target gate is unambiguous. `rejected: step N` sends the step back for revision.
Pause signal: `parked` (or `pause`) → run `pkl:handoff` then halt

After all steps complete, check `## After Implementation` in the plan — if it contains items, offer each in order and wait for the user's decision before proceeding to the next. If the section is empty (`None.`), skip it. Then stop — the user initiates the commit when ready.

## SHIP Phase

Run `pkl:commit` when the user asks to commit. Each step is an **offer** — nothing is forced: review-pr → commit → QA list → issue comment → close → **retro** (`pkl:retro` — appends a compact `## Retro` to the plan file; offer it, never force it) → cleanup.

---

## SuperClaude

Follow `plans/*.md` instructions exactly, including user override paths. Invoke `/sc:` commands via Skill tool only.
