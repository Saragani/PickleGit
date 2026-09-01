# Code Review — fix-bitbucket-oauth-credential-retry

Base: `d3763a6` · Scope: `PickleGit/ViewModels/RepositoryViewModel.cs` (committed `b7f6348`),
`PickleGit/ViewModels/RepositoryViewModel.Remote.cs`, `PickleGit/Views/CredentialsDialog.xaml`
(uncommitted working-tree changes).

---

[LOW] Commit format
File: commit b7f6348 (subject line)
Issue: Subject "Fix intermittent NullReferenceException from a shared CTS race in RunWorkAsync" is 78 characters — over commit-format.md's 72-char hard cap (target ≤50, hard cap 72).
Fix: No action needed retroactively (rewriting a pushed/shared commit isn't warranted for a message-length nit), but keep the upcoming commit for the Remote.cs/CredentialsDialog.xaml changes under 72 chars, e.g. "Keep OAuth credential lookup running after a rejection retry".

[MEDIUM] Threading / Architecture — residual race in the just-fixed `_opCts` mechanism — **FIXED**
File: PickleGit/ViewModels/RepositoryViewModel.cs:1936 (`OpToken`), 2023-2194 (`RunWorkAsync`)
Issue: The b7f6348 fix correctly stops the *disposal/nulling* side of `RunWorkAsync` from clobbering a reentrant nested call's `_opCts` (via the local `cts` capture + `ReferenceEquals` guard). The *read* side is unchanged: `OpToken => _opCts?.Token ?? CancellationToken.None` still reads the live, shared `_opCts` field at the moment a queued work `Action` actually executes on the executor thread — not the token that was current when that operation's own CTS was created. `RunAsync`'s reentrant branch (`if (IsBusy) return await RunWorkAsync(status, work);`, used when e.g. Commit's CanExecute doesn't check IsBusy and fires while a Push is mid-flight) still lets a second `RunWorkAsync` call overwrite `_opCts` on the UI thread before the first operation's queued executor work item reaches its own `OpToken` evaluation (e.g. inside `RunCliAsync`'s `_git.Cli.RunAsync(args, options, OpToken)`). If that interleaving happens, operation A's CLI call silently captures operation B's token: clicking Cancel while B is "current" would kill B's process instead of A's, and A's own `_opCts` is never actually wired to A's running git.exe — the cancel button becomes a no-op for A. This is a narrow window (the executor thread normally picks up a freshly-queued item well before a UI round-trip for another click completes) and pre-dates this PR (the property itself is unchanged), but it's the same shared-mutable-field root cause the fix targeted, left half-addressed.
Fix: Capture the operation's own token once, locally, and thread it explicitly into the `work` Action instead of having the Action re-read the shared `_opCts` field for its own identity — e.g. change `Action work` call sites to `Action<CancellationToken> work` (or capture `var token = cts.Token;` in a closure passed to `RunCliAsync`/`RunCliAllowingConflictAsync` instead of referencing the `OpToken` property inside their lambdas).
**Resolution**: Rather than the suggested `Action<CancellationToken>` refactor (which would've touched all 13 `OpToken` call sites across 3 files), added a `[ThreadStatic] private static CancellationToken? t_executingOpToken` (`RepositoryViewModel.cs`) set/cleared by `RunWorkAsync` around its actual invocation of `work` on the executor thread — since `GitExecutor` runs exactly one queued item at a time on one dedicated OS thread, this scopes `OpToken` reads to whichever operation is actually executing right now, independent of what `_opCts` gets reassigned to afterward on the UI thread. `OpToken` now checks `t_executingOpToken` first, falling back to `_opCts?.Token`. Zero call-site changes needed. Note: this fixes the read side (the wrong-token-captured-by-git.exe hazard) but not `CancelOperation()`'s own target selection (`_opCts?.Cancel()` still cancels whatever `_opCts` currently references, which — under the same reentrancy window — may not be the operation actually executing) — a narrower, UX-only residual left as a possible future follow-up, not a correctness/data-safety issue.

[LOW] Error handling — new retry loop swallows exceptions with no logging — **FIXED**
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:540-554 (`TryAutoResolveCredential`'s new backoff loop)
Issue: Each of the 3 retry attempts wraps `Services.CredentialStore.LoadViaGitCredentialHelper(remoteUrl)` in a bare `catch { }` with no `AppLog` call, per-attempt — code-style.md: "Do not swallow exceptions with empty catch blocks — log via Services/AppLog.cs at minimum." This matches the pre-existing bare-catch style used by every other step in this same file (steps 2/4 in `EnsureCredentialsAsync`, the pre-existing single-shot version of this same call), so it's consistent rather than a new deviation from this file's own convention — but this is exactly the path added to chase a hard-to-reproduce, timing-sensitive Bitbucket bug, so a diagnostic log line would help if the symptom recurs.
Fix: Optionally add `AppLog.Warn($"LoadViaGitCredentialHelper attempt {attempt} failed: {ex.GetType().Name}")` (or similar) in the loop's catch — never log the credential value itself, per security.md.
**Resolution**: Added `AppLog.Warn` calls for both the "attempt returned no credential" and "attempt threw" cases, logging only the attempt number and exception type/message — never the credential value.

[INFO] CredentialsDialog.xaml — fixed-size window with two new rows
File: PickleGit/Views/CredentialsDialog.xaml:4, 46-48
Issue: Row restructuring was checked and is correct — no index collisions: the new hint `TextBlock` lands in the new `Auto` row (Grid.Row="10"), `ValidationText` correctly keeps the flexible `*` row (now Grid.Row="11", shifted down by the 2 inserted rows), and the button `StackPanel` keeps the final `Auto` row (now Grid.Row="12"). Window `Height` grew 280→310 to fit the two added rows (a 6px gap + the new hint text). Since `ResizeMode="NoResize"`, there's no user recourse if the actual rendered hint-text height turns out to be underestimated at 420px width.
Fix: No code change — recommend a quick visual check (e.g. via the `run` skill) once this branch is validated in-app, to confirm the hint text doesn't clip or crowd the validation-error text when both are visible simultaneously.

[INFO] Restructured `EnsureCredentialsAsync` control flow — verified correct
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:569-701
Notes (no fix needed): Traced the full new control flow. `remoteUrl` is now hoisted above the `skipCachedLookup` check so step 3 (the OAuth-capable `git credential fill` wait) always runs whenever a remote URL exists, regardless of `skipCachedLookup` — this is the actual bug fix. Steps 2 and 4 remain correctly gated on `!skipCachedLookup`. The cancellation check (`credentialHelperToken.IsCancellationRequested` → abort with `return false`) still sits between step 3 and step 4, exactly as before reindentation — no skipped `return`, no double execution, no path that reaches step 4 or the dialog after a cancel. `_credentialWaitCts` is still set immediately before the wait and cleared in a `finally`, with `RaisePropertyChanged(nameof(CanCancel))` on both sides, unchanged from before the restructuring. `_forceCredentialDialog`/`skipCachedLookup` are plain fields/locals (not bound), so `BaseViewModel.Set` doesn't apply here — correctly not used.

[INFO] `TryAutoResolveCredential` backoff loop — thread-safety verified correct
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:512-567
Notes (no fix needed): Confirmed all three call sites of `TryAutoResolveCredential` (`FetchAsync`'s multi-remote branch, `RunCliAsync`, `RunCliAllowingConflictAsync`) invoke it from inside a `work`/`Action` lambda that runs via `_git.Executor.RunAsync(work)` — i.e. always on the dedicated executor thread, never the UI thread — so the plain blocking `Thread.Sleep(500)`/`Thread.Sleep(1000)` in the new retry loop is correct as-is and does not block the UI. Total added worst-case latency (~1.5s of sleep plus 3 process spawns) is bounded and only occurs on the already-slow-path (post-rejection recovery).

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 0 |
| MEDIUM | 1 |
| LOW | 2 |
| INFO | 3 |

**Verdict: READY TO MERGE** (no CRITICAL or HIGH findings; one MEDIUM architecture observation and two LOW nitpicks, none blocking).

**Post-review**: the MEDIUM and the actionable LOW (retry-loop logging) were both fixed before commit — see their **Resolution** notes above. The remaining LOW (commit-message length on already-committed b7f6348) needs no action.
