# Code Review — fix-bitbucket-oauth-bearer-header

Scope: `PickleGit/Services/Git/GitCli.cs`, `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`,
`PickleGit/ViewModels/RepositoryViewModel.cs` (the native credential-helper auth path for
Push/PushBranch/Fetch/Fetch-all/Pull, plus the softened error-message wording).

---

[CRITICAL] Security — Credential Logging (CWE-532) — **FIXED**
File: PickleGit/Services/Git/GitCli.cs:80-95 (new `HasConfiguredCredentialHelperAsync`), interacting with the existing unconditional log at GitCli.cs:198
Issue: `HasConfiguredCredentialHelperAsync` builds `args` as `$"config --get-urlmatch credential.helper {CliGitService.Quote(remoteUrl)}"` and passes the **raw remote URL** straight into `GitCli.RunAsync`. `RunAsync` unconditionally logs the full `args` string via `AppLog.Info($"git {args} (in {workDir})")` (line 198) before the process even runs. Every prior use of a remote URL in this codebase kept it out of logged command args — `BuildHttpAuthEnv` only ever put the credential into an env var (`GIT_CONFIG_VALUE_0`), which `RunAsync` never logs. This new call site is the first place a full remote URL flows into `args`. `security.md`'s checklist explicitly calls out exactly this shape of bug: "AppLog output never includes credentials, tokens, or full remote URLs with embedded basic-auth (`https://user:pass@host/...`)". A remote configured as `https://user:token@bitbucket.org/...` (a very common way to store a Bitbucket/GitHub token — arguably the same OAuth-token scenario this whole PR is fixing) will have that token written verbatim to `%APPDATA%\PickleGit`'s log on every Fetch/Pull/Push, twice (the "git {args}" line and, since this call always succeeds/fails quickly, effectively every operation).
Fix: Strip user-info out of the URL before it ever reaches `args`/`AppLog`. `git`'s credential URL-matching for `credential.<url>.helper` does not consider the password component at all, so at minimum replace the password with a placeholder (or drop userinfo entirely if a plain host-scoped match is acceptable) before calling `RunAsync`, e.g.:
```csharp
var safeUrl = RedactUserInfo(remoteUrl); // build a UriBuilder with Password = null (and optionally UserName = null)
var result = await RunAsync(workDir, $"config --get-urlmatch credential.helper {CliGitService.Quote(safeUrl)}")...
```
Alternatively (more robust long-term), have `GitCli.RunAsync` redact `user:pass@`/`user:token@` patterns before logging `args`, so any future URL-bearing call site is covered by construction, not by each caller remembering to sanitize.
**Resolution**: Added `GitCli.StripUserInfo(url)` (builds a `UriBuilder` with `UserName`/`Password` cleared) and applied it before the URL ever reaches `args`. Confirmed git's `--get-urlmatch` doesn't consider the password component, so this can't change the resolved answer.

---

[HIGH] Wrong runtime behavior — native-path failure purges an unrelated, stale credential — **FIXED**
File: PickleGit/ViewModels/RepositoryViewModel.cs:2107-2136
Issue: `RunWorkAsync`'s catch block, on a 401/403/410/"authentication replays", unconditionally does `failedUser = RemoteUsername; failedPassword = RemotePassword; failedUrl = Remotes.FirstOrDefault()?.Url;` and then deletes/rejects that credential from `CredentialStore` and via `RejectViaGitCredentialHelper`. Before this PR, every HTTPS operation went through the manual path, so `RemoteUsername`/`RemotePassword` always reflected the credential actually used in the operation that just failed. Now that Push/PushBranch/Fetch/Fetch-all/Pull can take the new native path (which never touches `RemoteUsername`/`RemotePassword` at all — see `RepositoryViewModel.Remote.cs`'s `useNativeAuth`/`isNative` branches), a native-path 401 can reach this same catch block while `RemoteUsername`/`RemotePassword` still hold **stale values left over from an earlier, unrelated manual-path operation** (e.g. an earlier op against a different remote, or a credential that predates the credential helper being configured). This purges/rejects a credential that had nothing to do with the failure that just occurred — a legitimate, still-valid stored credential can be deleted and told to the system credential helper as "rejected" for no reason tied to the actual error.
Fix: Gate the purge on whether the failing operation actually used the manual path (e.g. thread a flag/marker through `RunWorkAsync`'s exception, or simply skip the purge when `RemoteUsername`/`RemotePassword` don't correspond to the credential that was in play for this specific call — the cleanest fix is to have the native-path call sites clear `RemoteUsername`/`RemotePassword` to null right before invoking `RunCliAsync`, so a native-path failure sees `failedUser == null` and correctly skips the purge, same as a first-ever native call already does today).
**Resolution**: Cleared `RemoteUsername`/`RemotePassword` immediately before each single-operation native/SSH call (`PushAsync`, `PushBranchAsync`, single-remote `FetchAsync`, `PullAsync`). For the "Fetch all remotes" loop specifically, cleared them once upfront only when `!needsManualCredential` (i.e. every remote in that call resolves to native/SSH auth) — deliberately NOT per-remote inside the loop, since a manual remote later in iteration order still needs the single credential `EnsureCredentialsAsync` resolved once, upfront, for the whole call; nulling mid-loop would have broken that legitimate multi-remote reuse.

---

[MEDIUM] Wrong runtime behavior — stale `_credentialsFromDialog` can trigger a spurious `SaveCredentials()` on an all-native "Fetch all remotes" — **FIXED**
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:83-144 (specifically line 99 `needsManualCredential` gate and line 142 `if (allOk && _credentialsFromDialog) SaveCredentials();`)
Issue: Previously, `remotes.Any(r => !GitCli.IsSshUrl(r.Url))` guaranteed `EnsureCredentialsAsync()` ran (and therefore reset `_credentialsFromDialog = false` at its top) whenever any HTTPS remote was involved. Now `needsManualCredential` can be `false` even when HTTPS remotes exist, as long as all of them resolve to native auth — in that case `EnsureCredentialsAsync()` is skipped entirely for this call, so `_credentialsFromDialog` is never reset and can still hold `true` from a completely different, earlier operation (any prior Push/Pull/Fetch that used the manual dialog). The final `if (allOk && _credentialsFromDialog) SaveCredentials();` then fires based on that stale flag, redundantly re-saving whatever is currently in `RemoteUsername`/`RemotePassword` (possibly unrelated/stale, per the HIGH finding above) even though no credential was resolved during this call at all.
Fix: Reset `_credentialsFromDialog = false` explicitly at the top of the `allRemotes` branch (or compute a local `anyCredentialsFromDialog` scoped to this call instead of relying on the shared field), so a call that never touches `EnsureCredentialsAsync` can't inherit a stale flag from a prior unrelated call.
**Resolution**: Added `_credentialsFromDialog = false;` at the top of the `allRemotes` branch, exactly as suggested.

---

[MEDIUM] Code duplication — byte-identical SSH vs. native-auth branches, four call sites — **FIXED**
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:104-118 (Fetch-all loop), 150-165 (single-remote Fetch), 412-429 (PushAsync), 466-483 (PushBranchAsync)
Issue: In each of these four places, the `GitCli.IsSshUrl(...)` branch and the new native-auth branch (`HasConfiguredCredentialHelperAsync(...) == true`) run the exact same command with the exact same options — only the boolean condition differs. E.g. in `PushAsync` (`RepositoryViewModel.Remote.cs:412-429`):
```csharp
if (GitCli.IsSshUrl(remoteUrl))
{
    var cliOk = await RunCliAsync($"Pushing to {remoteName}…",
        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(branch)}", "Push");
    if (cliOk) await RefreshAsync(false, PushRefreshScope);
    return cliOk;
}
if (_git.Cli != null && _git.Cli.IsAvailable &&
    await GitCli.HasConfiguredCredentialHelperAsync(_git.Cli.WorkingDirectory, remoteUrl))
{
    var nativeOk = await RunCliAsync($"Pushing to {remoteName}…",
        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(branch)}", "Push");
    if (nativeOk) await RefreshAsync(false, PushRefreshScope);
    return nativeOk;
}
```
The two bodies are identical. This is a real maintenance hazard, not just a nitpick: a future change to one branch (e.g. adding a flag to the push args, or changing the status text) can easily be applied to only one of the two copies, silently making SSH and native-HTTPS pushes diverge in behavior. This same pattern repeats 4 times across the diff.
Fix: Combine the condition into one branch per call site, e.g. `if (GitCli.IsSshUrl(remoteUrl) || await GitCli.HasConfiguredCredentialHelperAsync(...)) { ...single body... }` — being careful to short-circuit so the credential-helper check isn't evaluated for SSH remotes (`||` already does this). Same treatment applies to the Fetch-all loop's `if (GitCli.IsSshUrl(r.Url)) {...} else if (...isNative...) {...}`.
**Resolution**: Merged all four sites (plus the Fetch-all loop) into a single `if (GitCli.IsSshUrl(...) || await/TryGetValue(...))` branch each, exactly as suggested.

---

[LOW] Doc-comment overstates guarantee — `HasConfiguredCredentialHelperAsync`'s "never throws" contract is narrower than its catch clause — **FIXED**
File: PickleGit/Services/Git/GitCli.cs:77-95
Issue: The XML doc says "Never throws: returns false for any failure, including git.exe being unavailable," but the implementation only catches `InvalidOperationException` (which `GitCli.RunAsync` throws specifically for "git.exe not found" / "failed to start"). Other exception types are possible from the underlying `Process` machinery (e.g. a `Win32Exception` from `Process.Start()` failing for a reason other than "not found" — permissions, AV interference) and would propagate out uncaught, contradicting the doc comment. In practice this is caught by `App.xaml.cs`'s global `DispatcherUnhandledException` handler (shows a generic "Unexpected Error" dialog rather than crashing), so the blast radius is small, but a caller relying on the documented "never throws" guarantee — including this PR's own call sites, which don't wrap the call in try/catch — could be surprised by a rare unhandled-looking error dialog instead of a silent, graceful fallback to the manual auth path.
Fix: Either widen the catch to `catch (Exception)` (matching the doc's actual promise) with an `AppLog.Warn` for diagnostics, or narrow the doc comment to say it only guards against git.exe being missing/unable to start.
**Resolution**: Widened to `catch (Exception)` with an `AppLog.Warn` call (matches the doc's actual promise and code-style.md's "never swallow exceptions silently" rule).

---

[INFO] `PullAsync`'s native-auth check runs before any busy-scope claim
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:356-367
Issue: `await GitCli.HasConfiguredCredentialHelperAsync(...)` (and the pre-existing `EnsureCredentialsAsync()` call it sits beside) run before `TryEnterBusyScope()` is ever claimed in `PullAsync` — busy-scope entry only happens inside `RunCliAllowingConflictAsync` further down. This mirrors a pre-existing pattern (`EnsureCredentialsAsync` was already called pre-busy-scope here before this PR), so it's not a new regression, just an extension of an existing narrow window where a second command could start concurrently. Not blocking, but worth knowing if `Pull` is ever revisited.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 1 |
| HIGH | 1 |
| MEDIUM | 2 |
| LOW | 1 |
| INFO | 1 |

**Verdict: BLOCKED** (one CRITICAL finding — credential material can reach the persisted AppLog file via the new `git config --get-urlmatch` call when a remote URL carries embedded userinfo).

**Post-review**: all 5 actionable findings (CRITICAL, HIGH, both MEDIUMs, LOW) were fixed before commit — see each finding's **Resolution** note above. Build-verified after all fixes. The INFO item needs no action.
