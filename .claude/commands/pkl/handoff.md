---
description: Save progress mid-session by writing a Handoff section to the plan file. At ~60% context offer once (non-blocking). Run when user says "handoff" / "save progress" / "pause".
---

# /pkl:handoff — Save Progress

## Steps

### 1. Find active plan
Read the active `.plans/gh-<N>.md`. If multiple plans exist, ask the user which issue is active.

### 2. Assess current state
From the plan file and conversation context, identify:
- Completed steps (marked ✓)
- Current step (marked ← current) and what's done vs. what remains
- Pending steps
- Files modified so far
- Deviations — read the **Deviations** field from each completed step (already written at checkpoint); if stopped mid-step, note deviations so far for the current step since they haven't been written to the plan yet
- Deviation Register — copy the full `## Deviation Register` section if it has any entries; if stopped mid-step and the current step has deviations that affect upcoming steps, add those entries to `## Deviation Register` in the plan file now (format: `[Step N → affects Step M] <what changed and why> → Step M: <what to do differently>`) before writing the handoff
- Any new findings not yet captured in the plan

### 3. Write ## Handoff section
Replace or append the `## Handoff` section in `.plans/gh-<N>.md`:

```markdown
## Handoff
<!-- Context saved: <YYYY-MM-DD HH:MM> -->

**Completed:** <step list>
**In progress:** Step <N> — <description> *(omit if between steps — use "Next" instead)*
- Done: <what was completed in this step>
- Remaining: <what still needs to be done>
**Next:** Step <N+1> — <title> *(use when invoked from checkpoint, no step currently in progress)*
**Pending:** <remaining steps>

**Files modified:**
- `<path>` — <brief description>

**Deviations (current step):** <only include if stopped mid-step and the current step's Deviations field hasn't been written yet — omit entirely if at a clean checkpoint boundary>
**New findings:** <edge cases, decisions, or surprises not yet written into a step — "none" if all captured>

**Deviation Register:** <copy all entries from `## Deviation Register` verbatim — omit section entirely if register is empty>

---
Paste into a new conversation to resume:

> Continue issue gh-<N>. Read `.plans/gh-<N>.md`.
> Completed: [<step list>]. Next: Step <N+1> — <title>. *(or: Currently at: Step <N> — <description>. Specifically: <what remains>.)*
> Run mode: <manual|auto> *(from frontmatter — auto continues without per-step approval)*
> Approved deviations carrying forward: <list entries from Deviation Register, one per line — omit if register is empty>
> Continue from there.
```

### 4. Update frontmatter
Update `phase`/`step`/`next`/`updated` to reflect current state via the atomic
frontmatter lib (single home; preserves the rest of the file byte-for-byte):
```bash
.claude/scripts/plan-frontmatter.sh set-phase-step-next .plans/gh-<N>.md <phase> <step> "<next title>"
```
(`set-phase-step-next` also stamps `updated`. For a single key use `set-field <file> <key> <value>`.)

### 5. Output resume block
Display the ready-to-paste resume block to the user, then stop. Do not continue implementation.
