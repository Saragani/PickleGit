---
description: Fetch and summarize a GitHub issue. Activated when the user mentions gh-XXXX at the start of a task.
---

# /pkl:fetch-issue — GitHub Issue Intake

## Steps

### 1. Extract issue number
Parse the number out of `gh-XXXX` (or a bare `#XXXX` / `XXXX`) from `$ARGUMENTS` or the current conversation.

### 2. Fetch the issue
```bash
.claude/scripts/github.sh view <N>
```
Prints labeled plain-text blocks: `Title:`, `Status:`, `Labels:`, `Assignee:`, `Description:`, `Comments (n):`.

- **Non-zero exit** → tell the user the fetch failed (`gh` not installed/authenticated *and* `$GITHUB_TOKEN` not set as a fallback, no network, or the issue doesn't exist) and ask them to paste the issue body instead. There is no MCP fallback for GitHub issues in this project.

Summarize inline — always, regardless of description length:
- **Summary**: issue title
- **Status**: open/closed — flag if already closed
- **Labels**: as returned
- **Description**: key requirements (condensed)
- **Acceptance Criteria**: if present in the body, list all items in full
- **Linked issues**: list any `#N` references found in the body as possible blockers

### 3. Confirm understanding
Ask: *"Does this match what you want to work on? Any clarifications needed?"*
Wait for confirmation before continuing.

### 4. Suggest type and show plan guide

Based on the issue content, suggest the type:

| Type | Trigger |
|---|---|
| `bug` | crash, wrong value, regression |
| `feature` | new capability, UI, service |
| `refactor` | restructure without behavior change |
| `investigation` | no symptom yet — profiling, pre-feature research |
| `script` | shell/PowerShell/config — no compiled binary |

Read `.claude/rules/plans/<type>.md` and display it to the user:

```
Suggested type: <type>

Your guide for this issue (.claude/rules/plans/<type>.md):
─────────────────────────────────────
<contents of the file>
─────────────────────────────────────
```

Then ask — BLOCKING, wait for answer:
> "Want me to follow the guide, write the plan directly, or skip the workflow?"

- **Follow guide** → execute the instructions from the guide shown above, then write `.plans/gh-<N>.md`
- **Write directly** → write `.plans/gh-<N>.md` from the issue information alone
- **Skip** → stop here. Answer questions normally, no plan file, no workflow.
