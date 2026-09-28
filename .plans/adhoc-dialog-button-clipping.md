---
issue: adhoc-dialog-button-clipping
title: Fix dialog button clipping on tall content
type: bug
component: Views/Dialogs
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-28 13:03
---

# Plan: adhoc-dialog-button-clipping — Fix dialog button clipping on tall content

## Spec
### Problem Statement
Three modal dialogs — `ConfirmDialog`, `ErrorDialog`, and `TextPromptDialog` (`PickleGit/Views/Dialogs/`) — size themselves via `SizeToContent="Height"` with every content-`Grid` row set to `Auto`, and no `ScrollViewer` around their variable-length text region. When the bound message/prompt text is long enough, the bottom button row (OK/Cancel/Copy) can end up invisible:

- **ConfirmDialog** has no `MaxHeight` at all. Confirmed via live repro (a throwaway harness instantiating the real class with a long `MessageText`): the window grew to `ActualHeight=1100` while the screen's usable work area was `1032` — the button row rendered off the visible desktop.
- **ErrorDialog** has `MaxHeight="560"`, so the window itself is capped, but the header/message region has no `*` row or `ScrollViewer`. Confirmed via live repro: once the message content's natural height exceeded the 560px cap, the trailing button row (Copy/OK) was pushed past the window's own client area and never rendered at all — worse than the ConfirmDialog case, since it's not just off-screen, it's unreachable inside the window's own bounds regardless of screen size.
- **TextPromptDialog** has the identical structure to ConfirmDialog (`SizeToContent="Height"`, no `MaxHeight`, no `ScrollViewer` around `PromptText`) — same failure mode is reachable with a long prompt or checkbox label.

Every other dialog/window in the codebase (`RemoteDialog`, `NewBranchDialog`, `CredentialsDialog`, `CloneDialog`, `CreatePullRequestDialog`, `SettingsWindow`, `InteractiveRebaseDialog`, `HelpWindow`, `CommandPaletteWindow`) already uses the correct pattern — a genuine `*` Grid row and/or an internal `ScrollViewer` that keeps the button row pinned regardless of content length — so this fix brings the three affected dialogs in line with the rest of the codebase rather than introducing a new pattern.

### Requirements
- FR1: `ConfirmDialog`'s Cancel/OK button row stays visible and clickable regardless of `MessageText` length, by capping window growth to the screen's usable work area and scrolling the message internally instead of growing off-screen.
- FR2: `ErrorDialog`'s Copy/OK button row stays visible and clickable regardless of `MessageText` length (with "Show details" collapsed or expanded), by making the message region the flexible/scrollable row instead of letting it silently push the button row past the window's own client area.
- FR3: `TextPromptDialog`'s Cancel/OK button row stays visible and clickable regardless of `PromptText`/checkbox-label length, using the same pattern.
- FR4: Existing short-message behavior (the window shrinking to fit small content via `SizeToContent="Height"`) is unchanged for all three dialogs.

### Acceptance Criteria
- AC1: Given a `ConfirmDialog` opened with an extremely long `MessageText`, when it renders, then the OK/Cancel buttons are visible within the screen's work area and clickable. [derived]
- AC2: Given an `ErrorDialog` opened with an extremely long `MessageText` (details collapsed or expanded), when it renders, then the OK/Copy buttons are visible inside the window's own client area and clickable. [derived]
- AC3: Given a `TextPromptDialog` opened with an extremely long `PromptText`, when it renders, then the OK/Cancel buttons are visible and clickable. [derived]
- AC4: Given any of the three dialogs opened with a short message (today's typical usage), when it renders, then its size and layout are visually unchanged from before this fix. [derived]

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no newer language features.
- Theme-aware brush bindings must stay `DynamicResource` in any touched XAML (already true here — don't regress).
- No third-party control library — fix uses stock WPF `ScrollViewer` + `Grid` only, matching `SettingsWindow`'s existing pattern.
- `SizeToContent="Height"` must be preserved for short content — this is a targeted fix, not a switch to a fixed-size/resizable window design.
- These are `ShowDialog()`-modal windows built by `DialogService` with simple CLR properties + `DataContext = this` in code-behind (no ViewModel/DI wiring) — a code-behind-only change (setting `MaxHeight` after `InitializeComponent`) is consistent with the existing pattern in these three files.

## Tests
### Manual / Black-Box
- [ ] Open `ConfirmDialog` via `DialogService.Confirm` with a short message (e.g. delete-branch confirmation) → sizes normally, buttons visible, matches pre-fix appearance.  <!-- AC4 -->
- [ ] Open `ConfirmDialog` with an artificially long `MessageText` (30+ wrapped lines, via the same scratch-harness technique used in this investigation, run outside the tracked source tree) → dialog caps at the screen's usable work-area height, message scrolls internally, OK/Cancel remain visible and clickable.  <!-- AC1 -->
- [ ] Trigger `ErrorDialog` via `DialogService.ShowError` with a short message → unchanged appearance, OK/Copy visible.  <!-- AC4 -->
- [ ] Trigger `ErrorDialog` with a long `MessageText`, both with "Show details" collapsed and expanded → OK/Copy remain visible and clickable in both states.  <!-- AC2 -->
- [ ] Open `TextPromptDialog` via `DialogService.Prompt` with a short prompt → unchanged appearance.  <!-- AC4 -->
- [ ] Open `TextPromptDialog` with an artificially long `PromptText` → OK/Cancel remain visible and clickable.  <!-- AC3 -->

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step. **At checkpoint**: (1) if any deviation occurred — state it, ask for approval, wait; (2) verify all stubs — for `(manual)`, use the `run` skill (or the scratch-harness technique from the investigation, since no current production call site naturally produces a message long enough to trigger this) to drive the dialog into both a short- and long-content state and screenshot both; (3) run `checkpoint.sh adhoc-dialog-button-clipping N --deviations "<text>"`; (4) in `manual` run mode, wait for `approved: step N`.

### Step 1: Fix ConfirmDialog button clipping ✓
**What**: Add a dynamic `MaxHeight` clamp (`SystemParameters.WorkArea.Height` minus a small margin) in `ConfirmDialog`'s constructor after `InitializeComponent`. In the XAML, wrap the `MessageText` `TextBlock` (Grid.Row 2) in a `ScrollViewer VerticalScrollBarVisibility="Auto"` and change that row's `RowDefinition` from `Auto` to `*`.
**Touches**: `PickleGit/Views/Dialogs/ConfirmDialog.xaml`, `PickleGit/Views/Dialogs/ConfirmDialog.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Short message (existing usage, e.g. a delete-branch confirm) renders identically to before this change. (manual)
- [x] A long synthetic `MessageText` (30+ wrapped lines) caps the window height at the screen's work area, scrolls the message internally, and leaves OK/Cancel visible and clickable. (manual)
**Risk**: A `*` Grid row inside a `SizeToContent="Height"` window measures under effectively-infinite available height during the sizing pass (same as `Auto` would), so the row only actually gets squeezed to the leftover space once `MaxHeight` constrains the real window size at arrange time — this is the standard WPF technique for "auto-size up to a cap, then scroll," but it's worth confirming empirically rather than assuming.
**Mitigation**: If the `*` row doesn't get squeezed as expected, give the `ScrollViewer` itself a `MaxHeight` binding instead of relying on the row's star sizing.
**Deviations**: None

### Step 2: Fix TextPromptDialog button clipping ✓
**Why**: Identical structure and root cause to Step 1 (`SizeToContent="Height"`, no `MaxHeight`, no scroll region) — same fix pattern, applied to a different dialog, kept as its own step so it's independently testable and revertible.
**What**: Add the same dynamic `MaxHeight` clamp in `TextPromptDialog`'s constructor. In the XAML, wrap the `PromptText` `TextBlock` (Grid.Row 2) in a `ScrollViewer VerticalScrollBarVisibility="Auto"` and change that row's `RowDefinition` from `Auto` to `*`.
**Touches**: `PickleGit/Views/Dialogs/TextPromptDialog.xaml`, `PickleGit/Views/Dialogs/TextPromptDialog.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Short prompt (existing usage, e.g. "Rename Branch") renders identically to before this change. (manual)
- [x] A long synthetic `PromptText` caps the window height at the screen's work area, scrolls the prompt text internally, and leaves OK/Cancel visible and clickable. (manual)
**Risk**: None beyond Step 1's (same pattern).
**Deviations**: None

### Step 3: Fix ErrorDialog button clipping ✓
**Why**: Same root cause, but `ErrorDialog` already has a hardcoded `MaxHeight="560"` and a second, already-correctly-scrollable region (the collapsible `DetailsText` box, `MaxHeight="220"` + its own scrollbar) — the fix only needs to touch the `MessageText` row, replacing the fixed 560 cap with the same dynamic work-area clamp used in Steps 1–2 for consistency across all three dialogs.
**What**: Replace the hardcoded `MaxHeight="560"` with a dynamic clamp set in the constructor after `InitializeComponent` (same formula as Steps 1–2). In the XAML, wrap the `MessageText` `TextBlock` (Grid.Row 2) in a `ScrollViewer VerticalScrollBarVisibility="Auto"` and change that row's `RowDefinition` from `Auto` to `*`. Leave the `DetailsText` row (row 5, already `Auto` with its own internal scrollbar) untouched.
**Touches**: `PickleGit/Views/Dialogs/ErrorDialog.xaml`, `PickleGit/Views/Dialogs/ErrorDialog.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Short error message (existing usage) renders identically to before this change, "Show details" toggle still works. (manual)
- [x] A long synthetic `MessageText`, with "Show details" collapsed, caps the window height and leaves Copy/OK visible and clickable. (manual)
- [x] Same long `MessageText`, with "Show details" expanded (both the message and the details box competing for space), still leaves Copy/OK visible and clickable — details box keeps scrolling internally at its own 220px cap. (manual)
**Risk**: With both the message `ScrollViewer` (`*` row) and the details `TextBox` (`Auto` row, own 220px `MaxHeight`) present at once, the two flexible-ish regions could compete for space in an unexpected way under the outer `MaxHeight` clamp.
**Mitigation**: If the details box gets squeezed below a usable size, give the message `ScrollViewer` its own `MaxHeight` (e.g. half the dialog's cap) so both regions get a guaranteed minimum share.
**Deviations**: None

## After Implementation
None.

## Post-Ship Review Fixes
`pkl:review-pr` (verdict: READY TO MERGE, 0 CRITICAL/HIGH, 2 MEDIUM, 1 LOW, 5 INFO — see `.claude/reviews/adhoc-fix-dialog-button-clipping.md`) flagged two real edge cases, both fixed before commit:
- **Multi-monitor gap**: `SystemParameters.WorkArea` always reports the primary display, not the monitor the dialog's `Owner` is actually on. Fixed by adding `PickleGit/Services/DialogSizing.cs` (`DialogSizing.ForOwner(Window owner)`), computed via `Screen.FromHandle` + `VisualTreeHelper.GetDpi` (app is already Per-Monitor-V2 DPI aware) and applied in each dialog's `SourceInitialized` handler instead of the constructor, since `Owner` is only assigned by `DialogService`'s object initializer *after* the constructor returns. This also resolved the LOW finding (the same clamp formula was previously copy-pasted into all three constructors).
- **ErrorDialog message-row squeeze**: a long `MessageText` combined with an expanded long `DetailsText` under a tight `MaxHeight` could squeeze the message `ScrollViewer` row to near-zero height with no visible cue. Fixed by giving that `RowDefinition` a `MinHeight="60"` floor.

Rebuilt and reverified all four scenarios (confirm / prompt / error / error-details) after these fixes — `MaxHeight` capping still correct at the (changed, smaller) session resolution encountered during this pass.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-09-28 14:20 -->

**Deviations:** No in-BUILD deviations — all 3 steps matched their **What**/**Touches** exactly. Post-BUILD, `pkl:review-pr` surfaced 2 MEDIUM findings (MaxHeight computed from the primary monitor instead of the dialog's actual monitor; ErrorDialog's message row could be squeezed to near-zero by its own Details box) that were fixed before commit — see `## Post-Ship Review Fixes`.
**Steps planned vs. actual:** 3 planned / 3 actual — matched. The two review-driven fixes were handled as a documented addendum rather than new plan steps, since BUILD had already completed.
**Process improvement:** When a plan involves computed screen/geometry values (window sizing, DPI, monitor bounds), call out multi-monitor/DPI-awareness explicitly in `## Constraints` up front — this fix's plan didn't, and the resulting gap only surfaced at PR review instead of during BUILD.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->
