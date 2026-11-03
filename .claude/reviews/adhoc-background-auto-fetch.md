# Code Review: adhoc-background-auto-fetch (vs main)

Scope: App.xaml.cs, Services/AppSettings.cs, Services/Git/GitCli.cs, ViewModels/AppViewModel.cs,
ViewModels/RepositoryViewModel.Remote.cs, ViewModels/RepositoryViewModel.cs, Views/SettingsWindow.xaml.
graphify-out/* is generated and out of scope. The changes are uncommitted, so the commit-format check was skipped.

---

[HIGH] Behavior / "never shows a dialog" contract
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:225 (eligibility) and :250 (fetch invocation)
Issue: An HTTPS remote counts as "eligible" when `git config --get-urlmatch credential.helper` returns anything. A default Git for Windows install configures `credential.helper=manager` (Git Credential Manager) system-wide, so in practice every HTTPS remote qualifies, including hosts the user never signed in to and hosts whose stored token has expired. The fetch then runs with `new GitCliOptions()`: no Env, only GitCli's default `GIT_TERMINAL_PROMPT=0`. That variable stops git's own terminal prompt. It does not stop GCM from opening its GUI sign-in/OAuth window (GCM's interactivity is controlled by `GCM_INTERACTIVE` / `credential.interactive`). So the "silent" background pass can pop an unrequested auth window over the user's work every interval, for every such remote in every open tab. While that window is open, the fetch holds the tab's executor until the 2-minute timeout kills it, and user operations on the tab queue behind it. SSH remotes have the same blind spot: nothing forces `BatchMode`. The eligibility checks also look at the *configured* URL, so an `url.<base>.insteadOf` rewrite (e.g. `git@github.com:` -> `https://github.com/`) makes an IsSshUrl==true remote actually run over HTTPS through the helper.
Fix: Pass a non-interactive environment for the background path only, e.g. `Env = { ["GCM_INTERACTIVE"] = "never", ["GIT_ASKPASS"] = "", ["SSH_ASKPASS"] = "" }`. Add `-c credential.interactive=never` to the fetch args, and use `-o BatchMode=yes` for SSH when the user hasn't configured their own `core.sshCommand`/`GIT_SSH_COMMAND`. With interaction disabled, a missing or expired credential fails fast with an auth error (logged and redacted) instead of showing UI and consuming the timeout. This also covers the insteadOf case no matter which transport git actually picks.

[MEDIUM] Process management / PID reuse
File: PickleGit/Services/Git/GitCli.cs:292-321 (KillProcessTree), registered at :269
Issue: KillProcessTree reads `pid` and then runs `taskkill /T /F /PID <pid>` on a Task.Run that outlives both the `ct.Register` scope and `using (proc)`. Take the case where the process exits naturally at about the same moment the token fires (most likely the 2-minute CancelAfter racing a fetch that just finished, or a user cancel near completion). RunAsync can then drain, throw/return, and dispose `proc` before the threadpool item runs. Once the Process handle is closed, Windows may reuse the PID, and `taskkill /T /F` would then force-kill an unrelated process and its entire tree. While the handle is held open the PID cannot be reused, which is why the gap between the handle closing and taskkill starting matters. The fallback `proc.HasExited`/`proc.Kill()` on a disposed Process just throws and is swallowed, which is harmless.
Fix: Keep the handle alive until the kill completes: store the kill Task and await it (with a bound) before leaving `using (proc)`, or check `proc.HasExited` again right before spawning taskkill while the handle is still owned. Better: put each git.exe in a Win32 Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and kill it with `TerminateJobObject`. That removes the PID race and the taskkill process spawn, and it also catches grandchildren that /T can miss once an intermediate parent has exited.

[MEDIUM] Threading / UX: executor held for long silent stretches
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:236-267
Issue: All of one repo's remote fetches run in a single executor work item, with up to 2 minutes per remote (N remotes = up to 2N minutes). Mutating user operations queue behind it with an IsBusy spinner, as designed. But non-IsBusy reads go through the same executor and stall with no indicator: diff loading (LoadDiffAsync), commit-detail and file-list loads on commit selection, blame, and the watcher's Reopen. The user sees blank or stale panes until the fetch finishes. The project rules call out "silently blank pane" as the failure shape to avoid. This is also the reason the HIGH finding above matters: a prompt-blocked helper turns into a 2-minute stall.
Fix: Release the executor between remotes (one work item per remote, so queued user work interleaves). Surface a light indicator while a background fetch holds the executor, e.g. the existing IsRefreshing tab spinner or a status-bar "Fetching in background..." message. Consider a shorter per-remote timeout (for example 30-60 s) for background passes, since the user did not ask for this fetch.

[LOW] Error handling: partial success lost on a non-cancellation exception
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:250-264
Issue: Only OperationCanceledException is caught per remote. Any other exception from `_git.Cli.RunAsync` (e.g. InvalidOperationException "Failed to start git.exe", or git.exe disappearing between passes) aborts the whole work item. That skips the remaining remotes and the `_git.Reopen()` for remotes that already fetched successfully, so libgit2 keeps serving pre-fetch refs until some later Reopen.
Fix: Catch and log (`AppLog.Warn`) per remote and `continue`. Or move the `if (any) Reopen()` into a `finally` inside the work item, still guarded by `!token.IsCancellationRequested`.

[LOW] CLI argument safety
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:250
Issue: The remote name goes in positionally as `fetch "<name>"`. A remote name that starts with `-` (git's refname checks do not forbid it) would be parsed as a fetch option, e.g. `--upload-pack=...`. The name comes from the local .git/config, so exploiting this needs local write access. The new part is that the command now runs automatically every interval with no user action. The single-string argument API itself predates this branch (manual FetchAsync uses the same pattern), and Quote is applied consistently.
Fix: Use `fetch --end-of-options <name>` (git >= 2.24), or reject/skip remote names that start with `-` in the background path.

[LOW] Settings input validation / binding feedback
File: PickleGit/ViewModels/AppViewModel.cs:370-380 and PickleGit/Views/SettingsWindow.xaml:71
Issue: The setter clamps to 0..1440 but only raises PropertyChanged when the clamped value differs from the current value. If the value is already 1440 and the user types 99999, Set() returns false and the TextBox keeps showing "99999" while 1440 is in effect. A negative entry silently becomes 0, which turns the feature off with no feedback. Each keystroke (UpdateSourceTrigger=PropertyChanged) also rewrites settings.json and restarts the timer. Typing "15" saves and reschedules at 1 minute first. This matches the existing DefaultCommitLoadLimit pattern.
Fix: Raise `RaisePropertyChanged(nameof(AutoFetchIntervalMinutes))` whenever `v != value`, so the box shows the clamped value. Consider `UpdateSourceTrigger=LostFocus` for this field.

[LOW] Unused x:Name
File: PickleGit/Views/SettingsWindow.xaml:71
Issue: `Name="AutoFetchIntervalBox"` is not referenced from code-behind. It only adds a generated field. It mirrors the existing (also unused) CommitLimitBox.
Fix: Drop the Name attribute.

[LOW] Interval-change semantics
File: PickleGit/ViewModels/AppViewModel.cs:400-413
Issue: Changing the interval from 0 to a value above 0 starts the timer but runs no immediate pass, so the first fetch comes a full interval later. Setting it to 0 stops the timer but does not stop a pass already running over the remaining tabs.
Fix: Optionally kick off a pass when going from 0 to >0. In RunBackgroundFetchPassAsync, check `_autoFetchIntervalMinutes > 0` before each tab.

[LOW] Executor join timeout when the kill fails
File: PickleGit/Services/Git/GitCli.cs:313-321 together with PickleGit/Services/Git/GitExecutor.cs:104
Issue: If taskkill fails, the fallback `proc.Kill()` reproduces the orphaned-children case the new comment describes: the pumps never reach EOF and the executor thread stays blocked inside the work item. Tab close (RepositoryViewModel.Dispose -> GitService.Dispose -> Executor.Dispose) then freezes the UI for the full 5 s Join, and the thread and git processes leak. The 5 s bound existed before this branch, but this branch makes "tab closed with a long-running work item" a routine case.
Fix: A Job Object (see the MEDIUM finding) makes this path unreachable. Otherwise, log at Warn level when the Join times out.

[INFO] Dispose vs. in-flight fetch: verified correct
File: PickleGit/ViewModels/RepositoryViewModel.cs:2345
Issue: None. Dispose runs on the UI thread. It sets `_closed`, then Cancel() synchronously runs the linked per-remote registration, which calls KillProcessTree (fast; taskkill is offloaded). The executor work item's GetResult then throws OCE (not swallowed by the `when (!token.IsCancellationRequested)` filter), the work item ends, and Executor.Dispose's Join returns before `_repo.Dispose()`. Reopen() is gated on the token, and the Join also orders it before the repo dispose. If the work item had not started yet, it breaks on the token, or the executor's CompleteAdding path makes RunAsync return a cancelled Task. Either way the outer `catch ... when (_closed && OCE/ODE)` absorbs it.
Fix: None.

[INFO] `_backgroundFetchCts` lifetime and Register timing: verified correct
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:202-287
Issue: None. Every write to, read of, and dispose of the field happens on the UI thread: the continuations resume on the Dispatcher context and Dispose runs on the UI thread. Each linked CTS is disposed on the executor thread only after its own RunAsync has completed. `using (ct.Register)` is disposed before `using (proc)`, and registration Dispose waits for an in-flight callback, so the callback never sees a disposed Process. Only the Task.Run it spawns can outlive the registration (see the MEDIUM finding).
Fix: None.

[INFO] Scheduler races: verified correct
File: PickleGit/ViewModels/AppViewModel.cs:391-436
Issue: None. Settings load assigns the backing field directly. RescheduleBackgroundFetch returns early until StartBackgroundFetch has set `_backgroundFetchStarted`, and StartBackgroundFetch is called exactly once, at the end of App.OnStartup. The DispatcherTimer tick, the setter, and the pass all run on the UI thread, so the `_fetchPassRunning` guard is race-free and a tick during a pass is a no-op. The pass catches all exceptions, and BackgroundFetchAsync has its own catch-all, so the `_ =` fire-and-forget calls cannot leak unobserved exceptions. Tabs closed mid-pass are skipped via `Tabs.Contains`.
Fix: None.

[INFO] IsLocalRemoteUrl correctness
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:293-301
Issue: The logic is correct and conservative. It accepts file://, drive paths (C:\ and C:/), UNC, posix-absolute, and ./ or ../ relative paths. It rejects every other scheme, including git:// and transport-helper `<x>::` forms, and SCP-like SSH. A bare relative path such as "repo" is not recognized, so that remote gets skipped (safe). One consequence: a UNC remote (`\\host\share`) is now contacted automatically every interval, and Windows sends NTLM credentials to that host without asking. This is acceptable because the user configured the remote, but it is worth knowing.
Fix: None required.

[INFO] Redaction regex correctness
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:305-309
Issue: `://[^/\s@]+@` correctly redacts `scheme://user:token@` and `scheme://user@` in git's error text. Valid URLs must percent-encode `/` and `@` inside userinfo, so stopping at those characters is sound, and an `@` in the path is never matched. It does not cover tokens in query strings (`?access_token=`), which git does not normally echo. It overlaps conceptually with GitCli.StripUserInfo and lives in the ViewModel rather than at the GitCli/AppLog boundary.
Fix: Optional: move it next to StripUserInfo in GitCli as a shared `RedactCredentials(string text)` helper.

[INFO] Log volume
File: PickleGit/ViewModels/RepositoryViewModel.Remote.cs:217-268
Issue: Each pass writes several Info lines per tab ("skipped remote", start/done, plus GitCli's own command and exit lines for both `config --get-urlmatch` and `fetch`). At the 5-minute default this grows app.log steadily.
Fix: Optional: log skips only when the eligibility result changes, or at a debug level.

[INFO] App shutdown
File: PickleGit/App.xaml.cs:145
Issue: Tabs are not disposed on app exit, so a background `git fetch` in flight at shutdown keeps running as an orphaned process until it finishes. This is harmless for a fetch.
Fix: None required.

[INFO] Project rules compliance
File: (all changed files)
Issue: BaseViewModel.Set is used. No ObservableCollection is mutated off the UI thread. The new TextBlock brush uses DynamicResource, and the Style uses StaticResource (correct for a non-color resource). No converters, MessageBox or code-behind logic were added. All GitService calls go through the Executor. Reopen() follows the CLI mutation. Cli availability is checked. Settings are clamped after deserialization. The taskkill path is absolute (System32) with an integer-only argument. No credentials are logged.
Fix: None.

---

## Summary

| Severity | Count |
|----------|-------|
| CRITICAL | 0 |
| HIGH     | 1 |
| MEDIUM   | 2 |
| LOW      | 6 |
| INFO     | 8 |

**Verdict: NEEDS CHANGES**

---

## Resolution (fixed before commit 3e7743a)

- HIGH, GCM sign-in window: fixed. Background fetches run with `GCM_INTERACTIVE=never` and `-c credential.interactive=never`.
- MEDIUM, PID reuse in KillProcessTree: fixed. `HasExited` is re-checked inside the kill task, and RunAsync waits for the kill before disposing `proc`.
- MEDIUM, executor held for long stretches: fixed. Each remote is now one work item, and the per-remote timeout is 60 s.
- LOW, partial success lost: fixed. Errors are handled per remote, and `Reopen()` runs after each successful remote.
- LOW, CLI argument safety: fixed. Remote names starting with `-` are skipped.
- LOW, settings feedback: fixed. The box saves on LostFocus and the clamped value is re-raised.
- LOW, interval change from 0 to >0: fixed. Turning it on starts a pass right away.
- LOW, unused x:Name: not changed (matches the existing CommitLimitBox).
- LOW, executor join timeout when the kill fails: not changed (needs a Job Object).
- Static analysis: empty catch blocks now have comments or log via AppLog, and exception messages are redacted.

Re-verified live after the fixes.
