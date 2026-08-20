# /pkl:issue-comment — Add Comment to GitHub Issue

Adds a comment to the GitHub issue without closing it.

## Steps

### 1. Identify the issue
Derive the issue number from the current branch name (`gh-<N>-*`):
```bash
git branch --show-current
```

### 2. Compose the comment

Always use this fixed structure — no variations:

```
Branch: <current-branch-name>
Commit: <short SHA — git rev-parse --short HEAD>

<commit subject line>

Summary
<content depends on issue type — see rules below>

Changes
- <filename> — <what changed>
- <filename> — <what changed>
```

**Summary content rules by type:**

| Type | Summary content |
|------|----------------|
| `bug` | Root cause: \<what caused the bug\> / Fix: \<what was done\> |
| `feature` | \<what was added and why it's needed\> |
| `refactor` | Motivation: \<why the refactor was needed\> / \<what changed structurally\> |
| `chore` / `script` | \<what was done\> |

Derive issue type from the plan file's `type:` frontmatter if available, otherwise from the commit content. Derive body content from the commit diff and plan file if available. Plain GitHub-flavored Markdown — `##` headings, `-`/`*` bullets, `` `code` `` all render natively, no special encoding needed.

### 3. Post the comment
Write the composed body (from step 2) to a temp file, then:
```bash
.claude/scripts/github.sh comment <N> <body-file>
```

- **Non-zero exit** → tell the user the comment failed (`gh` not installed/authenticated *and* `$GITHUB_TOKEN` not set as a fallback, or a permissions issue) and show them the composed body so they can paste it manually.
