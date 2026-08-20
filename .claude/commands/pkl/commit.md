# /pkl:commit — Commit with Correct Format

## Steps

### 0. Run review-pr if not already done
If `pkl:review-pr` has not been run for the current changes, ask:
> "Review-PR hasn't been run yet — want me to run it now before committing?"

**BLOCKING — stop here and wait for the user's reply. Do not proceed to step 1 until they answer.**

User confirms → run `pkl:review-pr`, then continue to step 1.
User skips → continue to step 1.

### 1. Read staged changes
```bash
git diff --cached --stat
git diff --cached
git status
```
**Read every hunk** in the diff — do not draft a message from filenames or conversation context alone.
If nothing is staged, warn the user and stop: *"Nothing is staged — please `git add` the files you want to commit."*

### 2. Suggest commit message
Format spec: read [.claude/rules/commit-format.md](../../rules/commit-format.md) if not already in context.

Check if there's an active plan for the current branch (`.plans/gh-<N>.md` where `<N>` matches the branch name), and if it names an issue this commit closes.

Determine tier (trivial / standard / complex) per the triage table, then draft subject + body following the format. Only add a `Fixes: #N` / `Refs: #N` trailer if there's a real linked issue — most commits in this repo have none, and that's the norm, not an omission.

Present the suggested message and ask: *"Commit message OK, or do you want to adjust it?"*

### 3. Commit
Once confirmed:
```bash
git commit -m "<message>"
```

If the user says "don't commit" or skips the commit — skip steps 3 and 4 only, then continue to step 5.

### 4. Remind user to push manually
> "Committed. Please review the diff and push manually — never ask Claude to push."

### 5. Offer QA test list
> "Want me to generate a QA test list to share, or to keep for yourself?"

User confirms → run `pkl:qa-list`.
User skips → continue.

### 6. Offer issue comment
Only if a linked GitHub issue exists for this branch/plan:
> "Want me to post a summary comment to the GitHub issue?"

User confirms → run `pkl:issue-comment`.
User skips → continue.

### 7. Offer close
Only if a linked GitHub issue exists:
> "Close the issue?"

User confirms → run `pkl:issue-close`.
User skips → continue.

### 7.5 Offer retro
> "Append a retro to the plan?"

Offer only if `.plans/gh-<N>.md` exists. User confirms → run `pkl:retro` (appends a compact `## Retro` to the plan file — deviations summary, planned-vs-actual step count, and one process improvement copied to `.claude/retro-log.md`). Offer it, never force it.
User skips → continue.

### 8. Mark plan done (if exists)
Before offering cleanup, check if `.plans/gh-<N>.md` exists:
```bash
ls .plans/gh-<N>.md 2>/dev/null
```
- **Exists** → update its frontmatter: set `phase: SHIP`, `step: done`, `next: done`. Do this silently — no user prompt needed.
- **Not found** → skip silently (plan was never created or already deleted).

### 9. Offer cleanup
> "Delete local plan and review files?"
- `.plans/gh-<N>.md` — always offer
- `.claude/reviews/<branch>.md` — offer only if the file exists
- Delete only what the user confirms; skip silently if files don't exist

**Note: always continue from step 5 onwards**, even if the commit was skipped.
