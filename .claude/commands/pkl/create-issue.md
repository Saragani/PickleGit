---
description: Create a GitHub issue in this repo via github.sh.
---

# /pkl:create-issue — Create GitHub Issue

## Steps

### 1. Parse arguments
Accept from `$ARGUMENTS` (all optional — prompt for missing required fields):
- `--summary` / `-s` — issue title (required)
- `--body` / `-d` — issue body in plain text/Markdown
- `--label` / `-l` — one or more labels (comma-separated)
- `--assignee` / `-a` — GitHub username

If `--summary` is not provided, ask the user for it before proceeding.

### 2. Run the create command
```bash
.claude/scripts/github.sh create --summary "<summary>" [--body "<body>"] [--label "<label>"] [--assignee "<assignee>"]
```

Omit any flag whose value was not provided.

### 3. Report result

On success, `github.sh` prints `Created: https://github.com/<owner>/<repo>/issues/<N>` — echo it to the user.

- **Non-zero exit** → tell the user the create failed (`gh` not installed/authenticated *and* `$GITHUB_TOKEN` not set as a fallback, no `repo` remote pointing at GitHub, or a permissions issue) and ask them to create it manually.
