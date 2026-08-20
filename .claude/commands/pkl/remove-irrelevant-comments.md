---
description: Remove stale/irrelevant comments from cs/xaml/md files — comments that explain a past change instead of the current code, or describe code that no longer exists. Run manually with /pkl:remove-irrelevant-comments.
---

# /pkl:remove-irrelevant-comments — Prune Stale Comments

Remove comments and prose that no longer describe the **current** code — the kind AI tends to
leave behind: long explanations of a change, of what the code "used to" do, or of a decision that
no longer applies. Keep comments that explain the code as it stands now.

Default scope: the working-tree diff (`git diff HEAD` + staged). Pass a path/glob to scope to
specific files. Applies to `.cs` `.xaml` and `.md`.

---

## What to REMOVE

- **Change-narrating comments** — explain an edit, not the code: "now we also handle X", "changed
  to use Y", "added a check for Z", "this used to be `% 1000`".
- **History / legacy notes** — "earlier version did…", "previously…", "the old scheme…",
  references to code or files that no longer exist.
- **Restating the obvious** — a comment that just re-says the line below it (`i++  // increment i`).
- **Dead-code commentary** — comments describing code that was deleted, or commented-out code with
  no clear reason to keep it.
- **Self-referential meta** — "as discussed", "per the review", issue/PR chatter (`#1234`) in
  code comments, "TODO from last week".
- **In `.md`** — "evolved from…", "(Verified on device…)", "this section was added…", and any
  paragraph documenting *how the doc got here* rather than the current behavior.

## What to KEEP

- Comments that explain **why** the current code does something non-obvious (intent, a gotcha, a
  constraint, a chosen trade-off) — see `.claude/rules/code-style.md`.
- `// SECURITY:` comments for non-obvious security decisions (required by project style — see
  `.claude/rules/security.md`).
- `// NOLINT`-style lint/analyzer suppression markers, license headers.
- Comments naming a real, current invariant, unit, or `[NonSerialized]`/POCO-clone gotcha.

When unsure whether a comment is still accurate → **keep it** and flag it, don't guess.

---

## Steps

1. **Collect files.** Default: `git diff --name-only HEAD` + staged, filtered to
   `.cs .xaml .md`. If the user passed a path/glob, use that instead.
2. **Read each file** and judge every comment against KEEP vs REMOVE above. Judge against the code
   *as it is now* — a comment is stale if you have to look at history to make sense of it.
3. **Edit out** the irrelevant comments. Remove the whole comment (and its now-empty line); never
   delete code. If trimming leaves an awkward blank-line gap, tidy it.
4. **Verify nothing else changed:** `git diff` must show only comment/prose deletions — no code
   lines touched. For `.cs` files, if a build is cheap, run `pkl:build` as a sanity check.
5. **Report**: list each file with a one-line count ("N comments removed") and, for anything you
   were unsure about, name it and say you kept it.

## Rules

- **Comments and prose only — never change code or behavior.**
- One pass, conservative: if a comment might still be true, keep it.
- Don't touch generated files (e.g. `.g.cs`, `.designer.cs`), vendored/third-party code, or license headers.
- Match surrounding style after a removal (indentation, blank lines).
