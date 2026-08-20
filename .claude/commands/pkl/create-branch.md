---
description: Create the gh-XXXX feature branch for a GitHub issue. Run after pkl:fetch-issue confirms understanding.
---

# /pkl:create-branch — Branch Setup

## Steps

### 1. Check current branch
```bash
git branch --show-current
```
- Already on `gh-<N>-*` matching the issue → skip branch creation, confirm to user
- Otherwise:
```bash
git checkout -b gh-<N>-short-description
```

Rules:
- Base off the current branch — run `.claude/scripts/detect-base-branch.sh` to confirm, **never assume a fixed name** (this repo currently only has `main`, but don't hardcode that)
- Naming: lowercase, hyphens, ≤ 5 words after the issue number
- Examples: `gh-42-fix-merge-editor-block-offset`, `gh-58-add-submodule-sync`

### 2. Optional: mark the issue in-progress

GitHub issues have no built-in "In Progress" status transition like Jira. If the repo has an `in-progress` label configured, apply it via the API; otherwise skip silently — don't invent a label that doesn't exist in this repo.

```bash
.claude/scripts/github.sh view <N>
```
Check the `Labels:` line for an existing `in-progress` (or similarly named) label convention before attempting to add one. If none exists, skip this step entirely — it's not worth inventing a labeling scheme the user didn't ask for.
