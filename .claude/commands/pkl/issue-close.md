# /pkl:issue-close — Close GitHub Issue

Closes the GitHub issue (GitHub has no separate "resolved" state — closed is the terminal state).

## Steps

### 1. Identify the issue
Derive the issue number from the current branch name (`gh-<N>-*`):
```bash
git branch --show-current
```

### 2. Close it
```bash
.claude/scripts/github.sh close <N>
```
- **Non-zero exit** → tell the user the close failed (`gh` not installed/authenticated *and* `$GITHUB_TOKEN` not set as a fallback, or a permissions issue) and ask them to close it manually on GitHub.

Note: if the commit(s) on this branch already carried a `Fixes: #N` / `Closes: #N` trailer (see `commit-format.md`) and have been merged to the default branch, GitHub will have auto-closed the issue already — check `github.sh view <N>` status first if unsure, and skip silently if already closed.
