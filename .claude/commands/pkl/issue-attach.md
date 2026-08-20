# /pkl:issue-attach — Attach a File to a GitHub Issue

GitHub has **no REST API endpoint for issue attachments** — the drag-and-drop upload in the web UI hits an undocumented, unauthenticated-for-automation endpoint that isn't a supported integration point. Don't attempt to reverse-engineer it. Use one of the fallbacks below instead.

## Inputs

- **Issue number** — passed as argument, or derived from `git branch --show-current` (`gh-<N>-*`)
- **File path** — absolute or repo-relative path to the file to attach

## Steps

### 1. Decide the right fallback for the file type

| File is... | Fallback |
|---|---|
| Text (log, diff, patch, small config) | Inline it in a comment as a fenced code block via `pkl:issue-comment` / `github.sh comment` — no upload needed |
| A screenshot/image the user is about to paste themselves | Tell the user to drag it into the GitHub web UI comment box directly — that's the only way to get a real CDN-hosted image URL |
| A binary artifact that's part of the repo's history (a build output, a repro project) | Ask whether it belongs committed to the repo (on a scratch branch or under a `repro/` path) and link to it with a permalink instead of "attaching" it |
| Something that must exist as a real downloadable attachment | Tell the user there is no automatable path — they need to attach it manually via the web UI |

### 2. For the inline-text case
Read the file, wrap it in a fenced code block (with a language hint if useful), and post it via:
```bash
.claude/scripts/github.sh comment <N> <body-file>
```

### 3. For every other case
Stop and tell the user what you found, and which fallback applies. Do not guess at an unofficial upload endpoint.
