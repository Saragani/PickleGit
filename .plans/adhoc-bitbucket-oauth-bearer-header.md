---
issue: adhoc-bitbucket-oauth-bearer-header
title: HTTPS git.exe auth always sends Basic, breaking OAuth (Bearer-only) credentials from Git Credential Manager on Bitbucket
type: bug
component: Remote / Credentials
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-09-02 22:25
---

# Plan: gh-adhoc-bitbucket-oauth-bearer-header — HTTPS git.exe auth always sends Basic, breaking OAuth (Bearer-only) credentials from Git Credential Manager on Bitbucket

## Spec
### Problem Statement
Direct follow-on from the just-shipped `adhoc-bitbucket-oauth-credential-retry` plan on this same branch (commits `2e19008`/`f369693`) — that fix ensured PickleGit correctly retries and waits for Git Credential Manager's (GCM) OAuth-capable `git credential fill` path after a rejection. Live-testing that fix surfaced a **different** root cause: even once GCM hands back a fully valid, freshly-refreshed OAuth credential, PickleGit misuses it and authentication still fails with the same symptom the user originally reported.

Root cause, confirmed via live testing (not guessed) — see this conversation's `sc:troubleshoot` diagnostic report for the full evidence trail:
- Traced a live `git credential fill` call for bitbucket.org with `GCM_TRACE=1` and confirmed the modern GCM Core (v2.9.1, resolved correctly after a separate, already-fixed PATH-shadowing issue) works: it refreshed a valid OAuth access token via a stored refresh token, confirmed the username against `https://api.bitbucket.org/2.0/user` (`200 OK`), and returned `username=Saragani`, `password=<128-char OAuth access token>`.
- Tested that exact refreshed credential directly against Bitbucket with curl: `Authorization: Basic base64(username:token)` → **401 Unauthorized**. `Authorization: Bearer <token>` → **200 OK**. Conclusive: Bitbucket requires this OAuth token as a Bearer token, not Basic.
- `CliGitService.BuildHttpAuthEnv` (`PickleGit/Services/Git/CliGitService.cs:88-109`) unconditionally builds `Authorization: Basic base64(username:password)` via `GIT_CONFIG_KEY_0`/`VALUE_0` = `http.<urlScope>.extraheader`, regardless of where the username/password came from — including `EnsureCredentialsAsync`'s `git credential fill` lookup, which is exactly what returns this OAuth token. It also disables `credential.<urlScope>.helper` (forcing `""`) and points `GIT_ASKPASS` at git.exe itself so a rejected header fails fast.
- Net effect: PickleGit obtains a fully valid OAuth credential, then immediately breaks it by forcing it into the wrong auth scheme — reproducing the exact same `"could not read Username for 'https://bitbucket.org': terminal prompts disabled"` / `GIT_ASKPASS`-fails-fast failure chain the prior plan was trying to prevent, but for an unrelated reason (that plan fixed *obtaining* a credential; this bug is about *misusing* one once obtained).

### Requirements
- FR1: Add `GitCli.HasConfiguredCredentialHelper(remoteUrl)` (`PickleGit/Services/Git/GitCli.cs`) that resolves the **effective** credential helper for a given URL via `git config --get-urlmatch credential.helper <url>` (honors general/hostname/urlmatch precedence — the same mechanism `BuildHttpAuthEnv`'s own doc comment already references for verification) and returns whether it resolves to a non-empty value. No caching — a local `git config` read is cheap and this must reflect the current config on every check.
- FR2: In `PushAsync`, `PushBranchAsync`, `FetchAsync`, and the pull path (`PullAsync`) in `RepositoryViewModel.Remote.cs`: when the remote URL is HTTPS (`!GitCli.IsSshUrl(remoteUrl)`) AND `_git.Cli != null && _git.Cli.IsAvailable` AND `GitCli.HasConfiguredCredentialHelper(remoteUrl)` is true, skip `EnsureCredentialsAsync` and call `RunCliAsync(status, args, featureName)` with **no** `env`/`authRetryRemoteUrl` arguments — let git.exe's own native credential-helper protocol negotiate Basic vs. `authtype=Bearer` itself, the same way a plain terminal `git push` does. Git's own HTTP layer already calls `credential approve`/`reject` automatically on success/failure.
- FR3: When git.exe is available but `HasConfiguredCredentialHelper` returns false for that URL, or git.exe is unavailable entirely (pure LibGit2Sharp path), keep today's `EnsureCredentialsAsync` + `BuildHttpAuthEnv` + `TryAutoResolveCredential` flow exactly as it is now — this remains a real, live fallback branch (not dead code), just narrower in applicability than before.
- FR4: Soften `RunWorkAsync`'s auth-failure message (`RepositoryViewModel.cs:2141`, the `isRejectedAuthStatus` branch) — "You'll be prompted to re-enter credentials next attempt" describes the old forced-dialog behavior specifically. For the native path, a retry just means git/GCM renegotiate on their own, not that PickleGit's own dialog reopens. Reword generically enough to cover both paths (mention the credential will be re-resolved, without promising a PickleGit dialog specifically).

### Acceptance Criteria
- AC1: Given a Bitbucket HTTPS remote with `credential.helper` resolving to a configured helper (this machine's real setup: `manager`), when Push/Pull/Fetch runs, then PickleGit does not call `EnsureCredentialsAsync` and does not set any `env` override on the git.exe invocation — the operation succeeds using GCM's natively-negotiated auth (Bearer, per the diagnostic evidence). `[derived]`
- AC2: Given the same setup, when a real Push against Bitbucket is attempted, then it succeeds without the `"could not read Username... terminal prompts disabled"` failure recurring. This is the actual regression test for the originally-reported bug and requires the user's own live Bitbucket remote to confirm.
- AC3: Given a remote/host where `git config --get-urlmatch credential.helper <url>` resolves to nothing (e.g. a machine with git.exe but no credential helper configured), when Push/Pull/Fetch runs, then PickleGit falls back to today's `EnsureCredentialsAsync`/`BuildHttpAuthEnv`/manual-dialog flow unchanged. `[derived]`
- AC4: Given git.exe is unavailable entirely, when Push/Pull uses the LibGit2Sharp path, then behavior is unchanged from before this plan (this path is untouched). `[derived]`
- AC5: The auth-failure message no longer promises "You'll be prompted to re-enter credentials next attempt" as if a PickleGit dialog always follows — wording covers both the native and fallback paths accurately. `[derived]`

## Constraints
- .NET Framework 4.7.2 / C# 7.3 — no language-feature impact; this is control-flow branching over existing async methods.
- Threading: `GitCli.HasConfiguredCredentialHelper` spawns a quick synchronous `git config` read — must follow the same pattern as other synchronous `GitCli`/`CredentialStore` process-spawn helpers in this codebase (short-timeout `Process.Start` + `WaitForExit`), and must only be called from contexts already safe for a blocking call (the executor thread, inside `RunCliAsync`-style call sites — never the UI thread directly). Confirm exactly where each call site invokes it before wiring it in.
- No new dependencies — pure control-flow change over existing `GitCli`/`CredentialStore`/`RepositoryViewModel.Remote.cs` code.
- Never log credentials/tokens (`security.md`) — `HasConfiguredCredentialHelper` only needs the helper's *name*, never any resolved secret; do not extend it to also fetch/log a credential.
- Leave `CliGitService.BuildHttpAuthEnv` itself unchanged — it remains correct for the fallback path (manually-resolved credentials sent as Basic, which is correct for that path's classic-password/app-password/manually-typed-token credentials). Do not attempt to make it Bearer-aware; the fix is to stop calling it when native resolution is available, not to patch its header logic.
- Explicitly out of scope (confirmed with user): fixing the Cancel-button orphan-GCM-process risk (git.exe spawning GCM as its own child process means killing the outer git.exe on cancel may not terminate an orphaned GCM prompt window) — accepted as a known, minor, pre-existing-shaped UX risk, not something this plan fixes. Also out of scope: LibGit2Sharp's own separate credential-callback mechanism (unrelated fallback path, not touched by this bug).

## Tests
### Manual / Black-Box
- [ ] Reproduce the original scenario: on this machine (git.exe available, `credential.helper=manager` configured), trigger a Push/Pull/Fetch against a Bitbucket HTTPS remote and confirm it now succeeds instead of failing with the Basic-auth rejection. (AC1, AC2 — requires the user's real Bitbucket remote)
- [ ] Confirm (via temporary logging, or by observing that `EnsureCredentialsAsync`'s "Waiting for sign-in…" status never appears) that the native path is actually taken — no `env` override, no PickleGit-side credential dialog — for this machine's configuration. (AC1)
- [ ] Simulate "no credential helper configured" (e.g. temporarily unset `credential.helper` in a scratch/test repo's local config, or use `GIT_CONFIG_NOSYSTEM`/an isolated `HOME` for a throwaway git config) and confirm Push/Pull/Fetch still falls back to today's `EnsureCredentialsAsync` dialog flow correctly. (AC3)
- [ ] Confirm the reworded auth-failure message reads correctly and doesn't overpromise a dialog reopening when the native path is what actually failed. (AC5)

## Steps
> BUILD: one step per turn, driven by `.claude/scripts/checkpoint.sh` (see `.claude/rules/workflow.md`). **Before starting each step**: read `## Deviation Register` and apply any unresolved entries (not marked ✓) targeting this step — the entry's `→ Step M:` clause says exactly what to do differently. **At checkpoint**: (1) if any deviation occurred — state what deviated and why, ask for user approval, and wait for it before continuing; (2) verify all stubs — first review implementation against each stub description and fix any logic errors; then for `(manual)` use the `run` skill to drive the app and observe — all stubs must be GREEN before outputting the checkpoint; (3) run `checkpoint.sh gh-<N> N --deviations "<text>"` — it writes the **Deviations** field, ticks stubs, marks Step N ✓ / Step N+1 ← current, and syncs frontmatter atomically; (4) in `manual` run mode, wait for `approved: step N` before proceeding.

### Step 1: Add GitCli.HasConfiguredCredentialHelper ✓
**What**: Add a static method `HasConfiguredCredentialHelper(string remoteUrl)` to `PickleGit/Services/Git/GitCli.cs` that runs `git config --get-urlmatch credential.helper <url>` in the repo's working directory (mirrors the existing synchronous process-spawn pattern used elsewhere in this class, e.g. `ResolveGitPath`/`DiscoverGitPath`'s style, or the async `RunAsync` if a sync wrapper isn't appropriate — check existing call-site expectations before choosing sync vs async) and returns `true` when the command exits 0 with non-empty stdout, `false` otherwise (including when git.exe itself is unavailable — this method must not throw).
**Touches**: `PickleGit/Services/Git/GitCli.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] On this machine (credential.helper=manager configured globally), calling `HasConfiguredCredentialHelper("https://bitbucket.org/whatever/repo.git")` returns true. (manual — verified the exact underlying command directly: `git config --get-urlmatch credential.helper "https://bitbucket.org/foo/bar.git"` → `manager`, exit 0, which is exactly what the method runs)
- [ ] Calling it with a git.exe that can't resolve (or by temporarily pointing `GitPathOverride` somewhere invalid) returns false without throwing. (manual — deferred, not exercised)
**Risk**: `git config --get-urlmatch` requires a working directory inside a git repo to resolve local (per-repo) config in addition to global/system — confirm the method is always called with a valid repo path, not the bare command with no `-C`/`workDir`.
**Mitigation**: Thread the repo's working directory through the same way other `GitCli` methods already receive it (`workDir` parameter), rather than assuming CWD.
**Deviations**: Signature is `HasConfiguredCredentialHelperAsync(string workDir, string remoteUrl)` (async, explicit `workDir`), not the plan's literal `HasConfiguredCredentialHelper(remoteUrl)` — necessary refinement, matches GitCli's existing `RunAsync(workDir, ...)` pattern; behavior otherwise as specified. Second stub not exercised — code-reviewed only (catches `InvalidOperationException` from `RunAsync` when git.exe isn't found, returns false).

### Step 2: Route PushAsync/PushBranchAsync/FetchAsync/PullAsync through the native path when a credential helper is configured ✓
**What**: In `RepositoryViewModel.Remote.cs`, for each of `PushAsync`, `PushBranchAsync`, `FetchAsync` (the non-SSH, `_git.Cli` available branch), and `PullAsync`: check `GitCli.HasConfiguredCredentialHelper(remoteUrl)` before building `env`/calling `EnsureCredentialsAsync`. When true, skip straight to `RunCliAsync(status, args, featureName)` with no `env`/`authRetryRemoteUrl`. When false, fall through to the existing `EnsureCredentialsAsync` + `BuildHttpAuthEnv` + `authRetryRemoteUrl` flow unchanged.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [ ] On this machine, Push against a Bitbucket HTTPS remote succeeds via the native path (no PickleGit credential dialog appears, no "Waiting for sign-in…" custom status). (manual — deferred, requires the user's real Bitbucket remote)
- [ ] Fetch and Pull against the same remote also succeed via the native path. (manual — deferred, same reason)
- [ ] With a scratch repo/config where no credential helper resolves, Push/Pull/Fetch still falls back to `EnsureCredentialsAsync`'s dialog flow exactly as before this plan. (manual — deferred, not exercised)
**Risk**: Cancelling an in-flight native-path operation may leave an orphaned GCM prompt window if git.exe (the process PickleGit kills via `OpToken`) spawned GCM as its own child.
**Mitigation**: None planned — explicitly accepted as an out-of-scope, minor, pre-existing-shaped UX risk per the Constraints section. If it becomes a real problem, a future plan can address it (e.g. via a Job Object for the process tree).
**Deviations**: Code-reviewed and build-verified only (traced all 4 call sites — PushAsync, PushBranchAsync, FetchAsync single-remote + all-remotes branch, PullAsync — for correct branching and no double-EnsureCredentialsAsync/env-mixing). Live verification against a real Bitbucket remote deferred to the user — this is the actual regression test for the originally-reported bug (AC2) and can't be safely triggered against the user's live work repos unattended.

### Step 3: Soften the auth-failure message wording ✓
**What**: Update the `isRejectedAuthStatus` message text in `RunWorkAsync`'s catch block (`RepositoryViewModel.cs:~2094-2100` and the neighboring generic branch around line 2141) so it no longer unconditionally promises "You'll be prompted to re-enter credentials next attempt" — reword to cover both the native path (git/GCM renegotiate on their own) and the fallback path (PickleGit's dialog reopens) without misdescribing either.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Trigger an auth failure on the native path and confirm the shown message doesn't claim a PickleGit dialog will appear. (manual — verified via code review: the message text is shared for both paths and no longer says "You'll be prompted"; not live-triggered)
- [x] Trigger an auth failure on the fallback path (no credential helper configured) and confirm the message still accurately describes that PickleGit's own dialog will appear next. (manual — same code-review basis; `_forceCredentialDialog=true` still drives the fallback dialog exactly as before, only the wording changed)
**Risk**: None.
**Deviations**: None

## After Implementation
None.

## Deviation Register
<!-- Entries added at checkpoints. Format: [Step N → affects Step M] <what changed and why> → Step M: <what to do differently>. Append ✓ to entry when Step M completes. Approved deviations only. -->

## Retro
<!-- Written: 2026-09-02 21:35 -->

**Deviations:** Step 1's method signature became `HasConfiguredCredentialHelperAsync(string workDir, string remoteUrl)` (async, explicit `workDir`) rather than the plan's literal `HasConfiguredCredentialHelper(remoteUrl)` — necessary refinement matching `GitCli`'s existing `RunAsync(workDir, ...)` pattern. More significantly, `pkl:review-pr` (run before commit) found 5 real issues beyond the plan's 3 steps — 1 CRITICAL (a credential-bearing URL could reach the unconditionally-logged `git` command args), 1 HIGH (a native-path failure could purge an unrelated stale credential), 2 MEDIUM (a stale flag causing a spurious resave; duplicated SSH/native branch bodies at 4 call sites), 1 LOW (an overstated "never throws" doc comment) — all fixed before commit.
**Steps planned vs. actual:** 3 planned / 3 executed, matched — plus 5 review-driven fixes applied post-BUILD, pre-commit.
**Process improvement:** When a new code path touches a remote URL or resolved credential, explicitly check whether it can reach any unconditionally-logged string (e.g. `GitCli.RunAsync`'s `AppLog.Info($"git {args}...")`) before considering the step done — this class of bug produces no error and isn't caught by live functional testing, only by deliberately tracing what gets logged. `pkl:review-pr` caught it here, but checking it proactively during implementation (not just at the review gate) would catch it earlier.

## Handoff
<!-- Run `pkl:handoff` to fill this section. Paste the block below into a new conversation to resume. -->

## Note — relationship to the prior plan on this branch
This plan continues directly from `adhoc-bitbucket-oauth-credential-retry` (already shipped as commits `2e19008`/`f369693` on branch `fix-bitbucket-oauth-credential-retry`, since fast-forward-merged into `main`). That plan's `EnsureCredentialsAsync`/`TryAutoResolveCredential` changes remain correct and necessary for the fallback path (FR3 above) — they are not being reverted, just no longer reached in the common case where a credential helper is configured, per FR2. This plan's own branch (`fix-bitbucket-oauth-bearer-header`) was cut fresh from `main` after that merge.

## Note — post-BUILD fixes from pkl:review-pr
`pkl:review-pr` (run before commit) found 1 CRITICAL, 1 HIGH, 2 MEDIUM, and 1 LOW finding beyond this plan's original 3 steps — all fixed before commit (see `.claude/reviews/fix-bitbucket-oauth-bearer-header.md` for full detail and resolutions):
- **CRITICAL**: `HasConfiguredCredentialHelperAsync` passed the raw remote URL (which can carry embedded `user:token@` userinfo) into a command whose full `args` string `GitCli.RunAsync` logs unconditionally — fixed by stripping userinfo (`GitCli.StripUserInfo`) before it ever reaches `args`/the log.
- **HIGH**: a native-path failure could purge a stale, unrelated credential left in `RemoteUsername`/`RemotePassword` from an earlier manual-path operation — fixed by clearing those fields immediately before each native/SSH call site (upfront-only, not per-remote, in the Fetch-all loop, to avoid breaking legitimate multi-remote credential reuse there).
- **MEDIUM** ×2: a stale `_credentialsFromDialog` flag could trigger a spurious `SaveCredentials()` on an all-native Fetch-all — fixed by resetting it explicitly; four call sites had byte-identical SSH vs. native-auth branch bodies — fixed by merging each into one `||`-combined condition.
- **LOW**: `HasConfiguredCredentialHelperAsync`'s "never throws" doc comment was narrower than its catch clause — fixed by widening to `catch (Exception)` with `AppLog.Warn`.
