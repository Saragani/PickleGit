# /pkl:qa-list — Black-Box Test List

Generates a user-facing test list based on what changed. No internal details — written for someone testing the feature without knowing the code. Output is plain GitHub-flavored Markdown, ready to paste into a GitHub issue comment or keep for yourself.

---

## Instructions

### Step 1 — Get changed files

**Default — full branch:**

```bash
eval "$(.claude/scripts/detect-base-branch.sh)"
git diff "$MERGE_BASE" --name-only
```

**If the user says "list for this commit only" / "just the last commit":**

```bash
git show --name-only --format="" HEAD
```

Use this when multiple tasks land on the same branch as separate commits and the user wants a list scoped to the latest one only.

### Step 2 — Check for a plan file

Look for `.plans/gh-<N>.md` where `<N>` matches the current branch name (`gh-<N>-*`).

**If plan found**: Read the `### Manual / Black-Box` section under `## Tests`. Use those scenarios as the base list — they are spec-derived and already written for this purpose. Then enrich with any observable behaviors visible from changed files that are not already covered.

**If no plan found**: Infer the user-visible feature or behavior from changed files alone and generate the full list from scratch.

### Step 3 — Generate the list

Rules:
- No file paths, no service/class names, no internal technical details
- Every item must be an observable action with a visible expected result
- Include happy path, edge cases, and error conditions
- Group by feature area or screen when multiple areas are affected
- Write as if the tester has never seen the code

## Output format

```
Test List — gh-<N>: <title>

<Feature area / screen name>
- <user action> → <expected visible result>
- <user action> → <expected visible result>

Edge Cases
- <user action with boundary value or error condition> → <expected visible result>
- <user action> → <expected error message or fallback behavior>
```

Present the list and say: *"Want me to post this as a comment on the GitHub issue, or is this just for you?"*
