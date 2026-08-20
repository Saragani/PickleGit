# /pkl:plan — Write Enriched Plan

Writes the enriched `.plans/gh-<N>.md` file. Called after SC analysis is complete (per the type guide), or standalone when the user invokes it directly.

---

## Triggers
- End of a type guide flow (bug.md, feature.md, etc.) — type guide calls this
- User invokes `/pkl:plan` directly with an issue number, pasted requirements, or free-form prompt
- Called from workflow after `pkl:fetch-issue` when the user wants to skip SC analysis

---

## Instructions

### 1. Gather context

Priority order:
1. SC analysis output already in session + GitHub issue data from `pkl:fetch-issue` → proceed
2. Issue number provided but not yet fetched → invoke `pkl:fetch-issue` now
3. Pasted text or free-form prompt → use as Problem Statement; set `issue:` frontmatter to `adhoc-<slug>`

Determine plan type from context (needed for section gating below).

**Load context docs** — load `architecture.md` and `code-style.md` if not already in context (needed for accurate Constraints and Steps sections).

### 2. Ask for spec additions — BLOCKING

Ask once:
> "Anything to add to the spec — extra requirements, constraints, or context the issue doesn't capture?"

Wait for the user's response (or "no" / "nothing") before continuing.

### 3. Build the `## Spec` section

- **Problem Statement**: GitHub issue description or user prompt
- **Requirements**: functional requirements extracted or inferred, numbered FR1, FR2…
- **Acceptance Criteria**: copy from the issue body if present; otherwise derive from requirements and mark each `[derived]`

### 4. Build the `## Constraints` section

List any non-obvious constraints that affect implementation choices:
- .NET Framework 4.7.2 / C# 7.3 restriction (no newer language features)
- No MEF/DI container — View/ViewModel wiring is `DataTemplate` or DataContext-property based
- Threading: all git operations must route through `GitService.Executor` (see `architecture.md`)
- No third-party WPF control library — custom controls or styled stock controls only
- Hybrid git backend split (LibGit2Sharp vs `GitService.Cli`) — check which side an operation belongs on

If none apply, write: `None identified.`

### 5. Build the `## Tests` section

Skip entirely for `investigation` and `script` types.

**Manual / Black-Box only** — one observable scenario per item, exercised via the `run` skill or the user's own testing.
Unit stubs (where a test project eventually exists) live inside each Step (see Step format below).

### 6. Build the `## Steps` section

Each step must be:
- **Atomic** — one logical change per step
- **Reversible** — can be undone without affecting other steps
- **Independently testable** — has its own verification stubs

Use the Step format defined in the template in step 8. Never merge steps. Never omit a required field.

Field guidance:
- **Why** — omit unless the approach is non-obvious or a specific design decision was made over alternatives.
- **verification stubs** — each stub ends with a type tag:
  - `(unit)` — behavior exercisable via an automated test project. **PickleGit currently has no test project** — use this tag only once one exists; until then, prefer `(manual)`.
  - `(manual)` — exercise inside the running app (via the `run` skill: launch, drive to the relevant state, screenshot) and observe behavior/logs.
- **Risk** — one line or "None". **Mitigation** — omit if Risk is "None".

### 7. Build the `## After Implementation` section

Skip entirely for `investigation` and `script` types.

Only add items that are genuinely needed for this specific plan — things that must be verified or run after all steps are done but before committing. Leave the section empty (`None.`) if nothing applies.

Examples of valid items: run `pkl:static-analyze` if compiled files changed and there is a real risk of regression; a manual smoke-test pass via the `run` skill if the change affects a broad UI surface; verify a settings-migration path.

Do **not** add items as boilerplate. If in doubt, leave it empty.

### 8. Write the plan file

File: `.plans/gh-<N>.md` (or `.plans/adhoc-<slug>.md`). Set `updated` to the current date and time when writing.

The **first line of the file body** (after frontmatter) MUST be exactly:
```
# Plan: gh-<N> — <title>
```

Use this exact template:

```markdown
---
issue: gh-<N>
title: <title>
type: <bug|feature|refactor|investigation|script>
component: <component>
phase: <PLAN|BUILD|SHIP>
step: <number>
next: <skill or action>
run_mode: manual
updated: <YYYY-MM-DD HH:MM>
---

# Plan: gh-<N> — <title>

## Spec
### Problem Statement
<from the GitHub issue or user input>

### Requirements
- FR1: ...
- FR2: ...

### Acceptance Criteria
- AC1: Given / When / Then
<!-- Mark [derived] if inferred from requirements, not from the issue -->

## Constraints
- <.NET/C# version / threading / no-DI-container / third-party-control constraint>
<!-- or: None identified. -->

## Tests  <!-- OMIT entire section if type is investigation or script -->
### Manual / Black-Box
- [ ] <observable action> → <expected visible result>  <!-- AC1 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.
### Step 1: <imperative name>
**Why**: <omit unless approach is non-obvious or a design choice was made over alternatives>
**What**: <atomic action>
**Touches**: `path/to/file`
**verification stubs** *(verify each before marking step ✓)*:
- [ ] <plain-English test>  (unit|manual)
**Risk**: <one line or "None">
**Mitigation**: <what to do if risk fires — omit if Risk is "None">
**Deviations**: <filled in at checkpoint — "None" or description + reason. If the deviation affects an upcoming step, also add it to `## Deviation Register`>

### Step N: <imperative name>
*(repeat Step 1 format for every subsequent step)*

## After Implementation  <!-- OMIT entire section if type is investigation or script -->
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
```


### 9. Gate — BLOCKING

Read `.claude/rules/workflow.md` if not already loaded (needed for BUILD phase per-step checkpoint rules).

Output exactly:
```
Issue: gh-<N> — <title> | Type: <type> | Component: <component>
Plan: .plans/gh-<N>.md
```

Stop. Do not modify any source file, call `Edit`, `Write`, or run any build command until the user gives an approval signal: `proceed` / `approved` / `yes` / `go` / `ok` / `1`.

### 10. Create branch — MANDATORY (runs immediately after approval)

As soon as the approval signal is received, invoke `pkl:create-branch` before doing anything else.
Skip only if type is `investigation` (no commit required for investigations).

After `pkl:create-branch` succeeds:
1. Mark Step 1 as `← current` in the plan body.
2. Update frontmatter: `phase=BUILD`, `step=1`, `next=<Step 1 title>`, `updated=<current YYYY-MM-DD HH:MM>`.
3. Load `architecture.md`, `code-style.md`, and `security.md` if not already in context.
