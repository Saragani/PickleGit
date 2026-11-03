---
issue: adhoc-background-auto-fetch
title: Fetch all open tabs in the background on startup and on a configurable interval
type: feature
component: AppViewModel / RepositoryViewModel (remote operations) / Settings / App startup
phase: SHIP
step: done
next: done
run_mode: auto
updated: 2026-11-03 16:51
---

# Plan: adhoc-background-auto-fetch — Background auto-fetch of all open tabs

## Spec
### Problem Statement
Remote-tracking refs and ahead/behind counts go stale until the user clicks Fetch in each tab.
Wanted: on startup, fetch every open tab's repo sequentially in the background; repeat on an
interval (default 5 min) configurable in Settings.

### Requirements
- FR1: Setting `AutoFetchIntervalMinutes` in `settings.json` (default 5; 0 = auto-fetch off;
  clamped to 0..1440 on load). Exposed in Settings window as a numeric TextBox, bound through
  `AppViewModel.AutoFetchIntervalMinutes` (same pattern as `DefaultCommitLoadLimit`).
  Changing it takes effect immediately (timer rescheduled, no restart).
- FR2: `RepositoryViewModel.BackgroundFetchAsync()` fetches the repo's eligible remotes silently:
  no credentials dialog, no error dialog, failures logged via `AppLog.Warn` only.
- FR3: Eligible remote = SSH URL, or HTTPS URL where `GitCli.HasConfiguredCredentialHelperAsync`
  is true. Others are skipped (logged). Repo is skipped entirely when it has no remotes, no
  eligible remote, or `_git.Cli` is unavailable.
- FR4: The background fetch does NOT set `IsBusy`/`IsRefreshing` (sidebar/toolbar stay enabled,
  no spinner, no "working" state), does not touch `StatusMessage`/Cancel button, and does NOT
  affect `CanClose` — a tab can be closed at any time.
- FR4a: Closing a tab mid-fetch cancels it: `RepositoryViewModel` owns a
  `_backgroundFetchCts`; `Dispose()` sets `_closed = true` and cancels it before `_git.Dispose()`.
  The token is passed to `_git.Cli.RunAsync`, killing git.exe so the executor's `Join` returns
  fast (no UI freeze). The work item checks `_closed` / the token after each remote and skips
  remaining remotes and `_git.Reopen()`; the post-fetch refresh is skipped when `_closed`.
  `OperationCanceledException`/`ObjectDisposedException` after close are swallowed silently.
- FR5: All remotes of one repo are fetched inside ONE `GitService.Executor` work item (+
  `_git.Reopen()` at its end). Because the executor is a serial queue, any user operation
  started mid-fetch is queued behind it and starts automatically as soon as the fetch finishes.
- FR6: If a user operation is in progress (`IsBusy`) when the pass reaches the tab, the tab is
  skipped for this pass.
- FR7: After a successful fetch of a loaded tab (`IsLoaded`), run
  `RefreshAsync(false, FetchRefreshScope, isBackgroundTrigger: true)`; unloaded tabs only get
  refs updated and show them on first load.
- FR8: `AppViewModel` scheduler: one pass right after startup, then every N minutes
  (`DispatcherTimer`). A pass iterates a snapshot of `Tabs`, awaiting each tab in turn
  (sequential), skipping tabs closed mid-pass. Passes never overlap (reentrancy guard).
  `App.OnStartup` starts it after tabs are opened and `ActiveTab` is set.

### Acceptance Criteria
- AC1: Given ≥2 open tabs, When the app starts, Then each repo is fetched one after another
  (AppLog entries sequential, non-overlapping), with no dialog and no sidebar disable.
- AC2: Given interval N, When N minutes pass, Then another sequential pass runs.
- AC3: Given interval changed in Settings, Then the next pass follows the new interval; 0 stops passes.
- AC4: Given a repo with no remotes or only HTTPS-without-helper remotes, Then it is skipped, no dialog.
- AC5: Given a fetch fails (offline/unreachable), Then no dialog, warning logged, next tab still fetched.
- AC6: Given the user clicks e.g. Pull while a background fetch runs, Then Pull starts right
  after the fetch completes and succeeds normally.
- AC7: Given a remote-tracking branch moved upstream, After a pass the loaded tab's sidebar/graph show it.
- AC8: Given a background fetch is running in a tab, When the user closes that tab, Then it
  closes immediately (no freeze, no error dialog) and the pass continues with the next tab.

## Constraints
- C# 7.3 / .NET 4.7.2. Git work only on `GitService.Executor` (blocking `Cli.RunAsync(...).GetResult()`
  inside the work item, matching existing fetch code). Args via `CliGitService.Quote`.
- Settings write via existing `AppSettings.Save` (temp-file + replace); value clamped after load.
- Manual Fetch / Fetch All paths unchanged.

## Tests
### Manual / Black-Box
- [ ] Start app with ≥2 tabs → sequential background fetches in AppLog, no dialog, sidebar stays enabled  <!-- AC1 -->
- [ ] Set interval to 1 min → pass runs each minute; set 0 → no more passes  <!-- AC2, AC3 -->
- [ ] Repo with no remote / HTTPS-no-helper remote → skipped, logged, no dialog  <!-- AC4 -->
- [ ] Unreachable remote URL → warning logged, next tab still fetched  <!-- AC5 -->
- [ ] Click Pull during a background fetch → Pull runs after fetch finishes  <!-- AC6 -->
- [ ] Push to remote from another clone → after pass, loaded tab shows the new remote commit  <!-- AC7 -->
- [ ] Close a tab during its background fetch (slow remote) → closes instantly, no error, git.exe gone  <!-- AC8 -->

## Steps
### Step 1: Auto-fetch interval setting ✓
**What**: Add `AutoFetchIntervalMinutes` (default 5) to `SettingsData` with
`LoadAutoFetchIntervalMinutes()` (clamp 0..1440) / `SaveAutoFetchIntervalMinutes(int)` in
`AppSettings`. Add `AppViewModel.AutoFetchIntervalMinutes` property (loaded in ctor, saved in setter,
raises an internal change hook used by Step 3). Add a labeled TextBox in `SettingsWindow.xaml`
("Auto-fetch interval (minutes, 0 = off)") next to the commit-limit field.
**Touches**: `PickleGit/Services/AppSettings.cs`, `PickleGit/ViewModels/AppViewModel.cs`, `PickleGit/Views/SettingsWindow.xaml`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Build succeeds  (manual)
- [x] Settings window shows the field with 5; changing it persists to settings.json across restart  (manual)
- [x] Hand-edited negative/huge value in settings.json loads clamped, no crash  (manual)
**Risk**: None notable.
**Deviations**: None (empty RescheduleBackgroundFetch stub added as the Step 3 change hook, per What)

### Step 2: Silent per-repo background fetch ✓
**What**: In `RepositoryViewModel.Remote.cs` add `public async Task BackgroundFetchAsync()` per
FR2–FR7. In `RepositoryViewModel.cs` add `_closed` + `_backgroundFetchCts` and cancel them in
`Dispose()` before `_git.Dispose()` (FR4a); `CanClose` unchanged. Eligibility checks (`HasConfiguredCredentialHelperAsync`) run before enqueueing; the
fetch loop for all eligible remotes + `_git.Reopen()` runs as one `_git.Executor.RunAsync` item
under `_watcher?.Suppress()`. Whole method wrapped in try/catch → `AppLog.Warn`; never throws.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Build succeeds  (manual)
- [x] On an SSH/helper repo: remote refs update, no dialog, sidebar not disabled, no busy spinner  (manual)
- [x] Repo with no remotes / no eligible remote: returns immediately, skip logged  (manual)
- [x] User op started mid-fetch runs after fetch completes  (manual)
- [x] Close tab mid-fetch → instant close, no exception/dialog, no orphan git.exe  (manual)
**Risk**: A slow/hanging fetch delays queued user ops on that tab.
**Mitigation**: `GIT_TERMINAL_PROMPT=0` prevents prompt hangs; network timeouts are git's own.
**Deviations**: Approved: (1) Services/Git/GitCli.cs - cancel now kills the whole git process tree via taskkill /T (bare Kill() orphaned git children, hung the pipe pumps and the pass forever; also fixes toolbar Cancel). (2) 2-minute per-remote timeout. (3) Local-path/file:// remotes are also fetched. Verified together with Step 3, since nothing calls BackgroundFetchAsync until the scheduler exists.

### Step 3: Scheduler in AppViewModel + startup hook ✓
**What**: In `AppViewModel` add `StartBackgroundFetch()` (immediate pass + `DispatcherTimer` with
interval from `AutoFetchIntervalMinutes`; 0 → timer stopped, no startup pass),
`RescheduleBackgroundFetch()` called from the setting's setter, and
`private async Task RunBackgroundFetchPassAsync()` (`_fetchPassRunning` guard, sequential
`await tab.BackgroundFetchAsync()` over `Tabs.ToList()` for tabs still in `Tabs`). Call
`viewModel.StartBackgroundFetch()` at the end of `App.OnStartup`.
**Touches**: `PickleGit/ViewModels/AppViewModel.cs`, `PickleGit/App.xaml.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Build succeeds  (manual)
- [x] App start → sequential background fetch of every tab in AppLog, no dialog  (manual)
- [x] Interval 1 min → repeated passes, never overlapping; 0 → stops  (manual)
**Risk**: Startup pass competes with active tab's initial load on the executor.
**Mitigation**: Executor serializes; active tab's load is enqueued before the pass starts.
**Deviations**: None

## After Implementation
None.

## Deviation Register

## Retro
<!-- Written: 2026-11-03 17:20 -->

**Deviations:** Step 2 grew three approved changes: process-tree kill in `GitCli.cs` (a bare `Kill()` orphaned git's children and hung the pass forever), a per-remote timeout, and fetching local/`file://` remotes. Post-review fixes then made Git Credential Manager non-interactive, used one executor item per remote with a 60 s timeout, and closed the PID-reuse race.
**Steps planned vs. actual:** 3 planned / 3 actual. Steps 2 and 3 were verified together, since nothing calls `BackgroundFetchAsync` until the scheduler exists.
**Process improvement:** For any feature that runs git unattended, put two hostile-remote stubs in the plan's Tests up front: a TCP listener that accepts and never answers, and an HTTPS remote that needs a login with none stored. The first exposed the orphaned-process hang, and the second (the GCM sign-in window) was only caught at review.

## Handoff
