---
issue: adhoc-bitbucket-oauth-credential-retry
title: Credential-rejection retry bypasses OAuth (Git Credential Manager) and forces a plain-password dialog Bitbucket rejects
type: bug
component: Remote / Credentials
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-01 18:15
---

# Plan: gh-adhoc-bitbucket-oauth-credential-retry — Credential-rejection retry bypasses OAuth (Git Credential Manager) and forces a plain-password dialog Bitbucket rejects

## Spec
### Problem Statement
User reports frequent "login errors" on Push/Pull/Fetch against Bitbucket, where the server rejects the attempt with a message to the effect of "login with username and password is no longer supported." No GitHub issue exists for this — found via live investigation of the credential flow, prompted by the user's report, and root-caused via `sc:troubleshoot`.

Root cause, confirmed by reading `EnsureCredentialsAsync`, `TryAutoResolveCredential`, and `RunWorkAsync`'s catch block (`PickleGit/ViewModels/RepositoryViewModel.Remote.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`):

`EnsureCredentialsAsync` (`RepositoryViewModel.Remote.cs:554-668`) has five steps: (1) already-in-memory, (2) PickleGit's own credential store, (3) `git credential fill` with a 300-second timeout — the real OAuth-capable path, routed through whatever credential helper is configured (Git Credential Manager does real browser OAuth for Bitbucket/GitHub here), shown with a visible "Waiting for sign-in… (check your browser)" status and a cancel button wired to `_credentialWaitCts`, (4) a raw Windows Credential Manager read, (5) PickleGit's own plain username/password dialog (`Views/CredentialsDialog.xaml`).

After any credential rejection (401/403/410, or libgit2's "authentication replays"), `RunWorkAsync`'s catch (`RepositoryViewModel.cs:2064-2100`) purges the stale credential and sets `_forceCredentialDialog = true` for the next attempt. `EnsureCredentialsAsync` reads that flag into `skipAutoLookup` and, when true, skips steps 2-4 as one block — including step 3, the *only* OAuth-capable path — and drops straight to the plain-text dialog (step 5). That dialog can never satisfy Bitbucket's current auth requirements (account-password/basic-auth is retired; a personal access token is required), so a user who types their normal password there gets the same rejection again, with no indication that OAuth was ever an option.

Separately, the one automatic retry that does exist — `TryAutoResolveCredential` (`RepositoryViewModel.Remote.cs:512-552`), invoked inline from `RunCliAsync`/`FetchAsync` immediately after a rejected git.exe call — uses a deliberately short 5-second timeout on `git credential fill`, by design, so a routine silent check never pops a surprise browser tab. That's correct for "did the credential helper already silently rotate the token in the background," but it's a single attempt with no retry/backoff, so it does nothing for the common case of a token needing a brief moment to propagate after `git credential reject`.

### Requirements
- FR1: After a credential rejection, the next `EnsureCredentialsAsync` call still performs the long-timeout, visible, cancellable `git credential fill` OAuth-capable lookup (step 3) — it must not be skipped just because `_forceCredentialDialog` is set. Steps 2 (PickleGit's own store) and 4 (raw Credential Manager read) remain skipped on this path, since both are already-purged/likely-stale shortcuts that would just replay the same rejected value.
- FR2: PickleGit's own plain-text dialog (step 5) is only reached if the OAuth-capable step 3 lookup comes back empty or is cancelled by the user — not automatically, just because a previous attempt failed.
- FR3: `TryAutoResolveCredential`'s `git credential fill` step retries up to 3 total attempts with a short blocking backoff between attempts (e.g. 500ms then 1000ms), instead of one shot — covers transient token-propagation delay right after a `git credential reject`. The raw `LoadFromGitCredentialManager` fallback step and `EnsureCredentialsAsync`'s own long-timeout step 3 are explicitly NOT wrapped in this retry — retrying an instant static-store read is pointless, and automatically replaying a slow interactive browser flow multiple times in one operation is a UX regression, not a fix.
- FR4: `Views/CredentialsDialog.xaml` shows a static hint that hosts requiring a personal access token instead of the account password (e.g. Bitbucket, GitHub) should have that token pasted into the password field — so a user who does land in the manual dialog isn't guessing.

### Acceptance Criteria
- AC1: Given a previously-rejected/purged Bitbucket credential, when the user retries Push/Pull/Fetch, then PickleGit attempts the long-timeout `git credential fill` (OAuth-capable) lookup again — visible via the "Waiting for sign-in… (check your browser)" status — before ever showing PickleGit's own dialog. `[derived]`
- AC2: Given that OAuth-capable lookup succeeds (e.g. GCM already has a valid rotated credential, or the user completes browser sign-in), when it returns, then the operation proceeds without ever showing PickleGit's own username/password dialog. `[derived]`
- AC3: Given that OAuth-capable lookup comes back empty or is cancelled, when it finishes, then PickleGit's own dialog appears as the final fallback, now including the personal-access-token hint text. `[derived]`
- AC4: Given `TryAutoResolveCredential`'s inline retry fires after a rejection, when the first `git credential fill` attempt fails, then it retries up to 2 more times with backoff before giving up — confirmed via logging/instrumentation during manual verification, since this path runs silently. `[derived]`
- AC5: A user opening PickleGit's own credentials dialog for any host sees the personal-access-token hint text without needing to hit an error first.

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no async/await inside `TryAutoResolveCredential`'s retry loop (it runs synchronously on the dedicated `GitService.Executor` thread, called from inside a work `Action` — see `architecture.md`'s Threading Model); use `System.Threading.Thread.Sleep` for backoff, consistent with this file's existing blocking `.GetAwaiter().GetResult()` call pattern, not `Task.Delay`.
- `EnsureCredentialsAsync` runs on the UI thread (it's `await`ed directly from Push/Pull/Fetch before any executor work begins) — its existing `Task.Run(...)` + `_credentialWaitCts` cancellation pattern for step 3 must be preserved as-is; do not change its threading shape, only the condition that gates reaching it.
- No new dependencies — this is entirely a control-flow change over existing `CredentialStore`/`GitCli` calls.
- Theme-aware brushes: any new `CredentialsDialog.xaml` text must use `{DynamicResource ...Brush}` per `code-style.md`, not `StaticResource`, to stay correct across a live theme switch.
- Never log credentials/tokens (`security.md`) — the added retry loop must not log the password/token value, only attempt counts/timing if any diagnostic logging is added.

## Tests
### Manual / Black-Box
- [ ] Reproduce the original bug: with a Bitbucket remote and a stored credential that gets rejected (or by manually deleting/corrupting the stored PickleGit credential and retrying Push), confirm that before the fix the flow drops straight to PickleGit's own dialog; after the fix, confirm the "Waiting for sign-in… (check your browser)" status appears again first. (AC1)
- [ ] With a valid credential available via `git credential fill` (e.g. GCM already signed in), trigger the forced-dialog retry path and confirm the operation completes without PickleGit's own dialog appearing. (AC2)
- [ ] Cancel the OAuth-capable wait (via the status-bar Cancel button) during a forced retry and confirm PickleGit's own dialog appears as fallback, showing the new hint text. (AC3)
- [ ] Open PickleGit's own credentials dialog on a fresh (non-rejected) first attempt and confirm the personal-access-token hint text is visible. (AC5)
- [ ] Instrument `TryAutoResolveCredential`'s retry loop temporarily (or step through in the debugger) against a Bitbucket remote whose credential was just rejected, and confirm it makes up to 3 attempts with the expected backoff before giving up. (AC4)

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Narrow the forced-dialog skip in EnsureCredentialsAsync to preserve the OAuth-capable lookup ✓
**Why**: The whole bug is that `_forceCredentialDialog` currently skips step 3 (the only OAuth-capable path) along with steps 2/4. Splitting the skip so only steps 2/4 are conditional — step 3 always runs — is the minimal change that fixes the reported symptom.
**What**: Restructure `EnsureCredentialsAsync` (`RepositoryViewModel.Remote.cs:554-668`) so the `skipAutoLookup`/`_forceCredentialDialog` check gates only the own-store lookup (step 2) and the raw Credential Manager fallback (step 4); the `git credential fill` long-timeout block (step 3, including its `_credentialWaitCts` setup/wait/cancel handling) runs unconditionally whenever a remote URL is available, regardless of `_forceCredentialDialog`. Reset `_forceCredentialDialog = false` at the same point it happens today (consumed exactly once).
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [ ] With `_forceCredentialDialog` true and a remote configured, confirm step 3 (visible "Waiting for sign-in…" status) still runs before any dialog appears. (manual — deferred, see Deviations)
- [ ] With `_forceCredentialDialog` false (first-time request), confirm behavior is unchanged from before this step (steps 2→3→4→5 in order). (manual — deferred, see Deviations)
**Risk**: Could regress the "don't replay a known-bad credential silently" intent behind the original skip if steps 2/4 aren't actually still skipped.
**Mitigation**: Keep steps 2 and 4 explicitly gated on the same flag; only step 3's gate is removed.
**Deviations**: Code-reviewed and build-verified only. Live verification requires a real rejected Bitbucket credential + browser OAuth round-trip against the user's actual remotes — not something to trigger unattended against their live work repos. Deferred to the user; see the note at the end of this plan.

### Step 2: Add retry/backoff to TryAutoResolveCredential's git-credential-helper lookup ✓
**What**: In `TryAutoResolveCredential` (`RepositoryViewModel.Remote.cs:512-552`), wrap the `git credential fill` call (the `Services.CredentialStore.LoadViaGitCredentialHelper(remoteUrl)` try block) in a loop of up to 3 attempts, sleeping (`Thread.Sleep`) 500ms before the 2nd attempt and 1000ms before the 3rd, returning as soon as any attempt yields a non-empty username+password. Leave the `ListAll()`-based own-store check and the `LoadFromGitCredentialManager` fallback untouched (no retry).
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [ ] Force a rejection scenario and confirm (via debugger step-through or temporary logging) that up to 3 attempts occur with the expected delays before `TryAutoResolveCredential` returns false. (manual — deferred, see Deviations)
- [ ] Confirm a credential that resolves successfully on the 2nd attempt returns immediately without waiting for a 3rd. (manual — deferred, see Deviations)
**Risk**: Blocking `Thread.Sleep` on the executor thread delays other queued git work by up to ~1.5s total in the worst case.
**Mitigation**: This only fires on the already-slow-path (post-rejection retry before falling back further), and the executor is already busy with this same operation — acceptable per the existing 5s-timeout-per-attempt precedent in this method.
**Deviations**: Code-reviewed and build-verified only. Same live-testing constraint as Step 1 — deferred to the user.

### Step 3: Add personal-access-token hint text to CredentialsDialog ✓
**What**: Add a `TextBlock` to `PickleGit/Views/CredentialsDialog.xaml` (below the password field, using `{DynamicResource TextSecondaryBrush}` per `code-style.md`) with wrapped text noting that hosts requiring a personal access token instead of the account password (e.g. Bitbucket, GitHub) should have that token pasted into the password field. Adjust the `Grid.RowDefinitions`/window `Height` as needed to fit it without crowding the existing validation-text row.
**Touches**: `PickleGit/Views/CredentialsDialog.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [ ] Open the dialog (trigger any credential prompt) and confirm the hint text is visible, correctly wrapped, and doesn't overlap the OK/Cancel buttons or the validation-text row. (manual — deferred, see Deviations)
- [ ] Switch theme (Settings → UI → Theme) while the dialog concept is styled and confirm the hint text brush follows the theme (DynamicResource, not frozen). (manual — deferred, see Deviations)
**Risk**: Fixed window `Height="280"` may need to grow to fit the new text without clipping.
**Mitigation**: Increase `Height` alongside the new row; verify visually via the `run` skill.
**Deviations**: Code-reviewed and build-verified; confirmed the rebuilt app launches and renders correctly (screenshot against the user's real running repo). Did not trigger a real credential prompt to see the dialog itself — same live-remote constraint as Steps 1/2. Deferred to the user.

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-09-01 18:10 -->

**Deviations:** No deviations from the planned approach itself. All 3 steps' verification stubs were code-reviewed and build-verified but not live-tested (deferred to the user — see the notes below); `checkpoint.sh` initially auto-ticked those stubs to `[x]` on call, which was inaccurate and had to be manually corrected back to unticked with explicit deferral notes. Additionally, after this plan's BUILD phase, `pkl:review-pr` surfaced one MEDIUM finding (a residual read-side race in the unrelated, already-committed `_opCts`/`OpToken` mechanism) and one LOW (missing retry-loop logging) — both fixed and committed alongside this plan's work, expanding scope slightly beyond the plan's original 3 steps' `Touches` fields.
**Steps planned vs. actual:** 3 planned / 3 executed, matched — plus 2 unplanned fixes (OpToken thread-static race, retry-loop logging) added post-BUILD in response to code review, before commit.
**Process improvement:** When a step's verification stubs are `(manual)` and genuinely can't be safely exercised in-session (e.g. require a live external service/real user credentials), don't call `checkpoint.sh` to mark the step done until either real verification happens or the stub text/deviation note is written first — `checkpoint.sh` auto-ticks stubs unconditionally, so calling it prematurely produces a false-GREEN plan state that then has to be manually un-ticked and corrected.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->

## Note — branch history
The `_opCts`/`RunWorkAsync` race fix (unrelated to this plan) was committed to this branch's parent (`fix-opcts-race-in-runworkasync`) before this plan's branch (`fix-bitbucket-oauth-credential-retry`) was cut from it, per the user's choice.

## Note — live verification deferred to the user
All 3 steps are code-complete and build-verified (`msbuild` succeeds; the rebuilt app was launched and confirmed to render correctly against a real repo). None of the `## Tests` scenarios or per-step verification stubs were exercised live: they all require a real rejected Bitbucket credential and/or an actual `git credential fill` round-trip (possibly a real browser OAuth prompt) against the user's real remotes — not safe to trigger unattended against their live work repos/credential store.

**Before treating this as done, the user should, next time a credential prompt is needed for real:**
1. Confirm the "Waiting for sign-in… (check your browser)" status appears (not PickleGit's own dialog immediately) after a credential rejection retry.
2. Confirm PickleGit's own dialog, when it does appear, shows the new personal-access-token hint text below the password field.
3. Report back GREEN/RED so the `## Tests` checkboxes above can be ticked truthfully.
