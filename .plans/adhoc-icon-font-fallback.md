---
issue: adhoc-icon-font-fallback
title: Missing icon glyphs on Windows Server (no Segoe Fluent Icons font)
type: bug
component: UI / Icon rendering
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-08-24 20:47
---

# Plan: gh-adhoc-icon-font-fallback — Missing icon glyphs on Windows Server (no Segoe Fluent Icons font)

## Spec
### Problem Statement
On Windows Server (2019/2022) and other machines lacking Windows 11's Segoe Fluent Icons font, most icon glyphs across PickleGit's UI render as blank space. Every icon glyph in the app is declared with a single-font `FontFamily="Segoe Fluent Icons"` (no fallback chain). Segoe Fluent Icons only ships starting with Windows 11; on machines without it, WPF's font-fallback resolves the glyph's Private-Use-Area codepoint (e.g. U+E8EC, U+E76B) against the theme's default font, which has no glyph at that codepoint, so nothing draws. Reproduced by the user on a Windows Server machine (screenshot showing missing icons across the sidebar/toolbar). Root cause confirmed via a full-codebase grep: 20 `FontFamily="Segoe Fluent Icons"` declarations across 13 distinct codepoints in 4 files (`SidebarView.xaml`, `CommitDetailView.xaml`, `MainWindow.xaml`, `CommitGraphControl.cs`), all missing a fallback font.

A migration to a fully open icon set (e.g. Bootstrap Icons, as vector `Path` geometry) was considered as a longer-term, zero-font-dependency alternative, but was explicitly deferred by the user — it requires re-authoring all 13 glyphs as XAML geometry with no exact visual match guaranteed, which is a much larger effort than this bug warrants. This plan is scoped to the minimal, zero-visual-risk fallback fix only.

### Requirements
- FR1: Every `FontFamily="Segoe Fluent Icons"` XAML declaration gets a fallback chain to `Segoe MDL2 Assets`, which shares the same PUA codepoints and has shipped since Windows 10 / Server 2016.
- FR2: The one code-behind `new FontFamily("Segoe Fluent Icons")` construction (`CommitGraphControl.cs:256`, used for the commit-graph tag badge glyph) gets the same fallback.
- FR3: No visible change on any machine that already has Segoe Fluent Icons installed (Windows 11) — Fluent Icons remains the first-choice font in the fallback list.
- FR4: Correct the stale comment at `SidebarView.xaml:342-347` (claims the ahead/behind indicators are "hand-drawn filled triangles"; they are actually `Segoe Fluent Icons` glyphs `U+F0AD`/`U+F0AE`) while touching those same lines for FR1.

### Acceptance Criteria
- AC1: Given a Windows 11 (or any Fluent-Icons-equipped) machine, when PickleGit runs after the fix, then every icon renders pixel-identical to before the change.
- AC2: Given a machine with Segoe MDL2 Assets but not Segoe Fluent Icons (Windows Server 2019/2022, older Windows 10), when PickleGit runs after the fix, then every icon renders via the MDL2 fallback instead of blank space. `[derived]` — cannot be reproduced on the dev machine; verified by the user on the originally-affected Server machine.
- AC3: A repo-wide search for `FontFamily="Segoe Fluent Icons"` (or `new FontFamily("Segoe Fluent Icons")`) with no accompanying `, Segoe MDL2 Assets` returns zero matches.

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no language-feature impact, this is a data-only string change.
- WPF's `FontFamily` supports a comma-separated fallback list natively in both XAML attribute syntax and the `FontFamily(string)` constructor — no code-structure change needed beyond the string literal.
- Scope explicitly excludes any icon-set migration (e.g. Bootstrap Icons) — evaluated and deferred to a possible future refactor; this plan is the minimal, zero-visual-risk fix for the reported bug only.
- Windows 7/8 support is explicitly not a target (confirmed by user) — no further/older fallback font needed.

## Tests
### Manual / Black-Box
- [ ] Grep the repo for `FontFamily="Segoe Fluent Icons"` / `new FontFamily("Segoe Fluent Icons")` not followed by `, Segoe MDL2 Assets` → zero matches (AC3)
- [ ] Build succeeds with no new warnings/errors (AC1)
- [ ] Launch PickleGit on this dev machine (has Segoe Fluent Icons) and visually compare: sidebar section headers (Remote Branches, Tags, Stashes, Remotes, Pull Requests, Worktrees), branch ahead/behind indicators, tab-bar scroll chevrons, commit-detail folder/flat-list/delete icons, and the commit-graph tag badge — all identical to pre-change baseline (AC1)
- [ ] (user-owned, off-machine) Re-check the originally-affected Windows Server machine after this fix ships — icons should now render via the MDL2 fallback (AC2)

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(unit)` run the relevant test project (once one exists); for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Add Segoe MDL2 Assets fallback to every XAML icon glyph ✓
**What**: In `SidebarView.xaml` (8 occurrences), `CommitDetailView.xaml` (8 occurrences), and `MainWindow.xaml` (2 occurrences), change every `FontFamily="Segoe Fluent Icons"` to `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"`. While touching `SidebarView.xaml:342-347`, correct the stale comment describing the ahead/behind indicators as "hand-drawn" — they are Segoe Fluent Icons glyphs like every other icon here.
**Touches**: `PickleGit/Views/SidebarView.xaml`, `PickleGit/Views/CommitDetailView.xaml`, `PickleGit/MainWindow.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Grep these three files for bare `FontFamily="Segoe Fluent Icons"` (no fallback) → zero matches (manual)
- [x] Build succeeds (manual)
**Risk**: None — purely additive string change; first font in the list is unchanged so rendering is identical when Fluent Icons is present.
**Deviations**: None

### Step 2: Add Segoe MDL2 Assets fallback to the code-behind FontFamily construction ✓
**What**: In `CommitGraphControl.cs:256`, change `new FontFamily("Segoe Fluent Icons")` to `new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")`.
**Touches**: `PickleGit/Controls/CommitGraphControl.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Build succeeds (manual)
- [x] Commit-graph tag badge still renders correctly on this dev machine, via the `run` skill (manual)
**Risk**: None
**Deviations**: None

### Step 3: Full visual regression pass ✓
**What**: Build the app, launch via the `run` skill, and visually confirm every icon location listed in the Tests section still renders identically to the pre-change baseline; run a final repo-wide grep confirming no bare `Segoe Fluent Icons` declaration remains anywhere.
**Touches**: None (verification only)
**verification stubs** *(verify each before marking step ✓)*:
- [x] Screenshot sidebar section headers, a branch row with ahead/behind indicators, the tab bar, commit-detail panel (folder/flat-list/delete icons), and the commit-graph tag badge; confirm all render identically to baseline (manual)
- [x] Repo-wide grep for `Segoe Fluent Icons` shows only fallback-chain declarations (or comments) (manual)
**Risk**: None
**Deviations**: Verified 8 of 13 icon codepoints visually (sidebar headers, ahead/behind indicators, commit-graph tag badge, commit-detail flat-list icon) rather than all 13 — remaining 4 (Pull Requests header, tab-scroll chevrons, folder icon, delete icon) require app state (hosting config, tab overflow, nested folder, working-dir view) not easily reachable without further live-desktop automation. User accepted this level of evidence given Risk=None for all steps and the identical, already-proven fallback mechanism.

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
