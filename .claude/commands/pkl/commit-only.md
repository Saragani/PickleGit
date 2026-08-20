# /pkl:commit — Commit with Correct Format

## Steps

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

Determine tier (trivial / standard / complex) per the triage table, then draft subject + body following the format. Only add a `Fixes: #N` / `Refs: #N` trailer if there's a real linked GitHub issue for this branch — most commits in this repo have none.

Present the suggested message and ask: *"Commit message OK, or do you want to adjust it?"*

### 3. Commit
Once confirmed:
```bash
git commit -m "<message>"
```

### 4. Remind user to push manually
> "Committed. Please review the diff and push manually — never ask Claude to push."
