# PR Review — adhoc-fix-dialog-button-clipping

Scope: ConfirmDialog / ErrorDialog / TextPromptDialog — cap dialog growth to
`SystemParameters.WorkArea.Height - 40`, turn the message/prompt row into a
`*`-sized `ScrollViewer` row so long bound text scrolls instead of pushing the
button row off-screen. No commits exist yet on this branch (all changes are
staged/unstaged working-tree edits) — commit-format checklist items (subject
line, no WIP/fixup commits) are not yet applicable; note this before commit.

---

[INFO] WPF Layout — Star row under `SizeToContent="Height"` Window
File: PickleGit/Views/Dialogs/ConfirmDialog.xaml:24, PickleGit/Views/Dialogs/ErrorDialog.xaml:24, PickleGit/Views/Dialogs/TextPromptDialog.xaml:24
Issue: Changing the message row from `Auto` to `*` inside a `Grid` whose owning `Window` has `SizeToContent="Height"` is the specific shape of WPF layout trap this codebase's own `PickleGit/CLAUDE.md` history repeatedly documents getting bitten by (VirtualizingStackPanel/Grid measurement mismatches), so it deserves scrutiny rather than a rubber stamp. Reasoned through the actual mechanics: this is safe. During the Window's `SizeToContent` measure pass, the Grid is handed effectively infinite available height, and Grid's own measure algorithm resolves a lone Star row similarly to an Auto row in that situation — the ScrollViewer/TextBlock gets to report its full natural (unclamped) desired height, so the Window's computed natural size still reflects the true content height. WPF's Window then clamps that natural size against `MaxHeight` *before* the Arrange pass. Arrange, unlike Measure, gives the Grid a genuinely finite height (the real, capped client area), and only then does the Star row's proportional-remaining-space allocation kick in for real, handing the ScrollViewer whatever is actually left after the fixed/Auto rows — which is exactly when it starts clipping/scrolling. This differs from the classic "Grid `*` row collapses to 0 inside a StackPanel" folklore bug, because that one is rooted in `StackPanel.ArrangeOverride` specifically arranging children at their measured `DesiredSize` along the stack axis rather than handing them a real finite Arrange size — a `Window` does not do that; its Arrange pass always uses its own final (already-clamped) size. Net: the mechanism the fix relies on is sound, and matches the plan's live-verified screenshots (short content unchanged, long content capped+scrollable, in all three dialogs).
Fix: None required. Optional: if this pattern is reused elsewhere, worth a one-line note in `PickleGit/CLAUDE.md`'s WPF-pitfalls log distinguishing "Window SizeToContent + Star row" (safe) from "StackPanel + Star row" (collapses) since both look superficially similar and the codebase already tracks this class of gotcha.

---

[MEDIUM] WPF Layout — ErrorDialog's Star row can be squeezed to near-zero while the expanded Details box is protected
File: PickleGit/Views/Dialogs/ErrorDialog.xaml:24, PickleGit/Views/Dialogs/ErrorDialog.xaml:64-71
Issue: Row 2 (message, now `*` + `ScrollViewer`) and Row 5 (`DetailsText` TextBox, still `Auto`, own `MaxHeight="220"`) both compete for space once "Show details" is expanded, under the outer Window's `MaxHeight` cap. Grid's Arrange-time algorithm always grants `Auto` rows their full desired height first (up to their own local constraints, e.g. the 220px cap) and only then divides *whatever is left* among `Star` rows. If `HeaderAuto + 14 + spacer(10) + DetailsToggleAuto + DetailsTextBox(up to 220) + spacer(16) + ButtonsAuto` already meets or exceeds the Window's capped total height, the leftover for the message row can hit 0 — the message `ScrollViewer` silently renders at zero/near-zero height (no scrollbar, no visible error text) while the 220px details box directly below it still renders in full. This is a real, if narrow, correctness gap: a long `MessageText` combined with an expanded long `DetailsText` on a modest-height monitor (or a small `WorkArea`) can hide the message entirely with no error, crash, or visual cue that anything is wrong.
Fix: Give the message `ScrollViewer` (row 2) a small `MinHeight` (e.g. 40-60px) so it never fully collapses even when the details box is expanded and space is tight, and/or give the message row a higher effective priority than the details box (e.g. cap `DetailsText`'s own `MaxHeight` more conservatively, or make the details box also flex within a nested `*`/`Auto` split so both share the squeeze proportionally instead of the message row absorbing all of it).
Note on test coverage: the plan's live verification (screenshots with details collapsed and expanded) would only have caught a *fully* collapsed state, not this partial-squeeze edge case — it requires simultaneously long `MessageText` *and* long `DetailsText` *and* a monitor/WorkArea short enough to force the squeeze, which is a narrower combination than what was apparently exercised. Recommend adding that specific combination to manual verification before merge, or accepting this as a known, documented edge case.

---

[MEDIUM] Multi-monitor — `SystemParameters.WorkArea` reflects the primary display, not necessarily the dialog's actual monitor
File: PickleGit/Views/Dialogs/ConfirmDialog.xaml.cs:23, PickleGit/Views/Dialogs/ErrorDialog.xaml.cs:21, PickleGit/Views/Dialogs/TextPromptDialog.xaml.cs:28
Issue: `MaxHeight = SystemParameters.WorkArea.Height - 40` is computed once in the constructor, before the window is shown or positioned, and `SystemParameters.WorkArea` always reports the *primary* monitor's work area (WPF has no monitor-aware overload of this static property). These dialogs use `WindowStartupLocation="CenterOwner"`, so they render on whatever monitor the owner window currently occupies — which can be a secondary monitor with a smaller (shorter) work area than the primary. In that case `MaxHeight` is computed too generously, and the exact bug this PR fixes (buttons pushed off the visible desktop) can reproduce on a multi-monitor setup with a shorter secondary display, directly undercutting this fix's stated purpose. This is a real, non-hypothetical gap (not merely pre-existing — the PR's own goal is "cap growth to the screen's usable work area," which this code comment states but doesn't fully deliver on multi-monitor setups).
Fix: Use the owner window's actual monitor instead of the primary display — e.g. via `System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(Owner ?? this).Handle).WorkingArea` (requires a `System.Windows.Forms` reference, already implicitly available in many WPF apps, or P/Invoke `MonitorFromWindow`/`GetMonitorInfo` if that reference is undesirable). If out of scope for this fix, at minimum note it as a known limitation in the plan/PR description rather than silently shipping a partial fix.

---

[INFO] Code-behind timing — `MaxHeight` assignment vs `InitializeComponent()` / `SizeToContent`
File: PickleGit/Views/Dialogs/ErrorDialog.xaml.cs:21, PickleGit/Views/Dialogs/ConfirmDialog.xaml.cs:23, PickleGit/Views/Dialogs/TextPromptDialog.xaml.cs:28
Issue: Verified this is not a real risk. `InitializeComponent()` only parses/instantiates the visual tree; it does not trigger a measure/arrange/layout pass (that only happens once the caller shows the window, e.g. via `DialogService`'s `ShowDialog()`). Setting `MaxHeight` immediately after `InitializeComponent()` in the constructor — before any caller could plausibly call `Show`/`ShowDialog` on the same instance — guarantees the property is in place before the first layout pass. Removing the old hardcoded `MaxHeight="560"` from `ErrorDialog.xaml` in favor of this constructor assignment is safe and behaviorally equivalent (just now dynamic instead of a fixed literal).
Fix: None required.

---

[LOW] Code duplication — magic number and comment repeated identically across three files
File: PickleGit/Views/Dialogs/ConfirmDialog.xaml.cs:21-23, PickleGit/Views/Dialogs/ErrorDialog.xaml.cs:19-21, PickleGit/Views/Dialogs/TextPromptDialog.xaml.cs:26-28
Issue: The exact same three-line comment + `MaxHeight = SystemParameters.WorkArea.Height - 40;` statement is copy-pasted into all three dialog constructors. The `40` (and the whole clamp policy) has no single source of truth — a future change to the margin, or the multi-monitor fix above, would need to be applied in three places and could easily drift (one file updated, others forgotten).
Fix: Extract a small shared helper, e.g. a `static` method on a common `DialogWindow` base class or a `DialogSizing` helper (`public static double MaxDialogHeight(Window owner) => ...`), and call it from all three constructors. Low priority for a small ad-hoc fix, but worth doing before a fourth dialog copies the same pattern.

---

[INFO] XAML well-formedness
File: PickleGit/Views/Dialogs/ConfirmDialog.xaml, PickleGit/Views/Dialogs/ErrorDialog.xaml, PickleGit/Views/Dialogs/TextPromptDialog.xaml
Issue: Confirmed by reading the full files (not just the diff hunks) — all three are well-formed: `RowDefinitions` counts match the highest `Grid.Row` index used, the new `ScrollViewer` wrapping is correctly opened/closed around each `TextBlock`, and `Grid.Row` placements are unchanged elsewhere (buttons, details toggle/box, checkbox all still line up with their original rows). No dangling tags or renumbering errors.
Fix: None required.

---

[INFO] Security checklist
File: N/A (all three Views/Dialogs/*.xaml and *.xaml.cs files)
Issue: Reviewed against `.claude/rules/security.md`. This is a pure UI-layout change — no user-controlled paths, no process execution, no credentials, no logging, no JSON deserialization, no patch application. Nothing in the checklist applies.
Fix: None required.

---

[INFO] Commit format — not yet applicable
File: N/A
Issue: No commits exist yet on `adhoc-fix-dialog-button-clipping` (all changes are staged/unstaged working-tree edits per `git status`), so subject-line and WIP/fixup-commit checks can't be evaluated yet.
Fix: Apply `.claude/rules/commit-format.md` when committing — this reads as a "Standard" tier commit (single-area bug fix, ~2-6 lines of prose expected), e.g. "Fix dialog button rows clipping off-screen on long messages."

---

## Summary

- CRITICAL: 0
- HIGH: 0
- MEDIUM: 2
- LOW: 1
- INFO: 5

**READY TO MERGE** (only MEDIUM/LOW/INFO findings — the two MEDIUM items are real but narrow edge cases (ErrorDialog message-row squeeze under expanded details + tight height; multi-monitor `MaxHeight` computed from the primary display) that degrade gracefully rather than crash or regress the common case; recommend addressing or explicitly accepting them before merge, but they do not block).
