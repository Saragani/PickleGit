---
description: Append a compact retrospective to the active plan file at SHIP. Offered (non-forcing) after a ticket's work is done. Run when the user says "retro" / "retrospective" / accepts the SHIP retro offer.
---

# /pkl:retro — Append Plan Retrospective

Appends a short `## Retro` block to the active `.plans/gh-<N>.md`, closing the loop on what the plan predicted vs. what actually happened. Lightweight by design — three fields, no story points, no dashboards.

## Triggers
- SHIP phase offer accepted (workflow lists `pkl:retro` as an optional step)
- User invokes `/pkl:retro` directly, or says "retro" / "retrospective" / "do a retro"

## Steps

### 1. Find active plan
Read the active `.plans/gh-<N>.md`. If multiple plans exist, ask the user which issue is active.

### 2. Gather the three inputs
From the plan file and conversation context:
- **Deviations summary** — collect every non-"None" entry from each step's **Deviations** field plus all `## Deviation Register` entries. Summarize in 1–3 lines (or "No deviations — plan held as written").
- **Planned vs. actual step count** — count the steps in `## Steps` (planned) and the steps actually executed/marked ✓ (actual). Note any added, dropped, or merged steps.
- **One process improvement** — a single concrete change to how the next plan/workflow should run, drawn from what slowed this one down or what worked well. Exactly one — keep it actionable, not a wish list.

### 3. Append the ## Retro section
Add (or replace) a `## Retro` section near the end of `.plans/gh-<N>.md`, before `## Handoff`:

```markdown
## Retro
<!-- Written: <YYYY-MM-DD HH:MM> -->

**Deviations:** <1–3 line summary, or "No deviations — plan held as written">
**Steps planned vs. actual:** <N planned / M actual> — <one line on what changed, or "matched">
**Process improvement:** <one concrete, actionable change for next time>
```

### 4. Feed the improvement forward
The `## Retro` block alone is inert — a new plan never re-reads an old plan file. To close the loop, append the **process-improvement line only** to the persistent rolling log `.claude/retro-log.md`, which `pkl:plan` reads at context-gather (step 1) for every new plan. Create the file with a `# Retro Log` header if it does not exist; otherwise append one line:

```
- <YYYY-MM-DD> gh-<N>: <the one process improvement, verbatim>
```

Keep it to the single improvement line — do not dump the whole retro into the log. If the log grows past ~30 lines, note that older entries can be pruned (oldest first); do not prune automatically.

### 5. Confirm
Output the appended block and the log line to the user, then stop. This is non-forcing — it records learning; it does not gate commit, resolve, or cleanup.
