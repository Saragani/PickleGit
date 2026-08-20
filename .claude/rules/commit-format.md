# Commit Format — PickleGit

Shared format spec for `pkl:commit` and `pkl:commit-only`. Both skills reference this — never duplicate.

## Before drafting

Run `git diff --cached` and read every hunk before writing — do not draft from filenames or conversation context alone.

## Subject line

```
<imperative description>
```

- Target **≤ 50 chars**, hard cap **72 chars**
- Imperative mood: **Add** / **Fix** / **Remove** — not "Added", "Fixes"
- **No `Co-Authored-By` lines**
- **No issue-number or type/scope prefix in the subject** — this repo's history is plain imperative sentences (`Fix crash in Unstage All when a conflicted file exists elsewhere`, `Persist the sidebar and detail-panel widths across restarts`). Keep that style; link an issue via a trailer instead (see below), never by prefixing the subject.

## Atomicity

One logical change per commit. If the staged diff spans unrelated concerns, stop and ask the user to split.

## Body — four tiers

### Trivial — subject only

1-line change, no behavior shift (typo, rename, dep bump, comment fix).

```
Bump Newtonsoft.Json to 13.0.3
```

### Standard — subject + plain prose paragraph

Most fixes and small features. 2–6 lines, wrap at 72. Explain why *and* what in natural prose — no section headers. This is the default tier and matches most of this repo's existing history.

```
Fix commit list staying short after the detail panel collapses

CommitListView's height was measured against the grid row it occupied
while the detail panel was open; collapsing the panel didn't trigger
a remeasure, so the list kept the old (shorter) height. Invalidate
the measure on DetailPanelVisible changes.
```

### Complex single-area — `## Why` + `## What`

One area of the app, but the change is non-trivial enough to warrant separating rationale from mechanism.

```
Replace GridSplitter with a plain Thumb for pane resizing

## Why
GridSplitter's default drag behavior fought with the Auto-sized
columns backing the sidebar/detail-panel widths, producing visible
jitter during drag on every third or fourth resize.

## What
Swapped both splitters for a plain Thumb driving Width directly on
the adjacent view, with drag delta clamped to the existing min/max
constants.
```

### Complex multi-area — `## Why` + named area sections

Two or more distinct areas of the app change behavior. Replace `## What` with a section per area (named after the view/service, behavioral prose, no file lists) — see `ee0c672` (`Address code-review findings on the UnstageAll fix and pane resizing`) in this repo's own history for the shape.

```
Fix Unstage All conflict crash and pane-resizing jitter

## Why
...reason...

## UnstageAll
...behavioral change...

## Pane resizing
...behavioral change...
```

## Tier triage

| Tier | When |
|------|------|
| Trivial | 1 file, no behavior change |
| Standard | Most fixes and small features — single area, default choice |
| Complex single-area | One area, behavior change warrants Why/What separation |
| Complex multi-area | Two or more areas with distinct behavioral changes |

When ambiguous → **Standard**.

## Optional sections

Add before trailers when there's concrete signal:
- **`## Tested`** — concrete validation evidence (e.g. "Verified live via UI Automation against a scratch test instance"). Omit when "tested" would be filler.
- **`## Risk`** — non-obvious failure modes
- **`## Breaking`** — what breaks and how to migrate (settings.json shape changes, etc.)

## Trailers — GitHub issue linking

Last block of the body, after a blank line. Use git-trailer convention — `Key: value`, one per line, no blank lines between trailers. Only add these when the work is actually tied to a tracked GitHub issue — most of this repo's history has none, and that's fine.

```
Refs: #42
Fixes: #17
```

- `Refs: #N` — related issue, no auto-close on merge to the default branch
- `Fixes: #N` / `Closes: #N` / `Resolves: #N` — GitHub's closing keywords; merging a commit with one of these into the default branch auto-closes issue `#N`. Use `Fixes`/`Closes` only when this commit is genuinely the complete fix — not for partial progress.
- `BREAKING CHANGE: <what breaks and how to migrate>` — required for any breaking settings/config-format change
