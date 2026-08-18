# Decouple background refreshes from the exclusive busy lock

## Context

Reported by the user after the Phase 10 release: PickleGit currently treats *every* git-executor
activity — from a full Fetch down to the watcher noticing one file changed — as the same single
"busy" state. `IsBusy=true` today:

1. Disables every toolbar action (Fetch, Pull, Push, Branch, Stash, Commit, …) — `MainWindow.xaml`
   binds their `IsEnabled` to `ActiveTab.IsBusy` (inverted).
2. Covers the sidebar (branches/tags/stashes) with a hit-test-blocking scrim +
   "Working…" spinner — `SidebarView.xaml`'s `Grid.Row="2"` overlay.
3. Shows a non-blocking progress bar above the commit list — `CommitListView.xaml`'s row-2
   overlay (already `IsHitTestVisible="False"`, so this one's fine as-is).
4. Shows a spinning icon on the repo's tab — `MainWindow.xaml`'s `TabBusyIndicator`.
5. Drives the taskbar progress state — `TaskbarProgressState` converter.
6. Gates re-entrancy: `TryEnterBusyScope()` refuses a second command while `IsBusy` is already
   true ("Another operation is already in progress").
7. Gates tab-close safety: `AppViewModel`'s close/close-others/close-to-the-right, and the explicit
   `CloseTab` guard, all refuse to close a tab while `IsBusy` — this is load-bearing, not
   cosmetic: it's what the disposal-race fix earlier this cycle (`GitExecutor.Dispose()`) and the
   `OnRepoChangedExternally` `IsBusy`-before-`Reopen()` ordering both depend on.

(1) and (2) are the parts the user actually finds disruptive, and they fire for things like: the
watcher noticing a file was created/edited/deleted, the 5-minute failsafe auto-refresh tick, and
(after this cycle's ignored-path fix) any *non-ignored* workdir change — e.g. a real edit made in
another editor while PickleGit is open. None of these need to block Fetch/Pull/Push or freeze the
sidebar; they're read-only status refreshes racing nothing the user is about to click.

**Update, added after the ignored-path fix shipped:** that fix doesn't fully solve "PickleGit stays
busy while compiling" — Visual Studio's build (and its IntelliSense/design-time-build machinery)
writes files under `.vs/` and `obj/` with names that vary from build to build (e.g. per-build temp
files, random-suffixed intermediates), not just a stable set of ignored *directories*. The prefix
check added this cycle still correctly recognizes these — they're still *under* an already-known
ignored directory (`.vs/`, `obj/`) regardless of the leaf filename — so it isn't wrong, but the
user's actual request goes further than "filter out the ignored ones and show a lighter indicator
for what's left": **for working-directory changes specifically (files added, removed, changed, or
renamed), show no refresh/busy indication at all, ignored or not.** See the new "silent" tier
below — this fully covers the compiling case regardless of what the changed files are named or
whether every one of them happens to be gitignored, so it's a strictly stronger fix than trying to
make the ignored-path matching more exhaustive. The ignored-path fix itself stays: it still saves
the real work of running `git status` and walking the refresh pipeline at all for a known-ignored
write, which is worth keeping even though the *visible* flicker it was chasing is now also fixed
by the tier below regardless of whether a given write happens to be ignored.

## The constraint that shapes the design

All git work — heavy or light — already funnels through **one dedicated executor thread**
(`GitService.Executor`, see CLAUDE.md). Relaxing the UI lock does **not** need to (and must not
try to) make git operations run concurrently — the executor already serializes them safely. The
only thing being relaxed is *when the user is allowed to queue up the next command*, not the
actual execution order. A Fetch click during a background refresh should be accepted immediately
and simply queue behind whatever the refresh's own git call is still doing — invisible in
practice, since these background calls (`git status`, a branch/tag list) are fast.

## Proposed design

### Three tiers, not two

| Tier | Flag | Trigger | Visible? | Exclusive? | Blocks tab-close? |
|---|---|---|---|---|---|
| Heavy | `IsBusy` | Commit, Checkout, Fetch/Pull/Push, Merge, Rebase, … | Full lock + spinner | Yes (`TryEnterBusyScope`) | Yes |
| Light | `IsRefreshing` | External branch/HEAD change (Refs), 5-min failsafe timer | Tab spinner + commit-list progress bar only | No | Yes |
| Silent | `IsSilentlyRefreshing` | Working-dir change (file added/removed/changed/renamed) | **Nothing at all** | No | Yes |

The middle row is what last round's summary already covered. The bottom row is the new addition:
`RefreshWorkingDirStatusAsync` — the path that fires for ordinary file adds/edits/deletes/renames,
compiling included — sets a *third*, purely internal flag instead of `IsRefreshing`. It still needs
to be tracked (tab-close safety and re-entrancy both still apply — see below), it just never drives
any visible UI element. Concretely: `IsActive` (the tab-spinner/progress-bar/taskbar binding) is
`IsBusy || IsRefreshing` — **deliberately not** `|| IsSilentlyRefreshing` — while the close-safety
check (see further down) checks all three.

A branch/HEAD change (Refs) staying in the *visible* light tier rather than also going silent is a
deliberate distinction, not an oversight: an external checkout/commit is a more significant, less
frequent event than a routine file edit, and the existing UI already has a place to show it
(the tab spinner) without it reading as intrusive the way the sidebar-blocking scrim did. Revisit
this if it turns out to be equally annoying in practice — nothing about the design below prevents
folding it into the silent tier too later.

### The three flags, in detail

- **`IsBusy`** — unchanged meaning and unchanged trigger sites: set only around genuinely
  exclusive, user-initiated operations (Commit, Stage/Unstage/Discard, Checkout\*, Merge,
  CherryPick, Revert, Reset\*, Rebase, Stash apply/pop/drop/create, Fetch/Pull/Push/Clone, branch/
  tag/remote mutations, LFS pull, bisect steps). `TryEnterBusyScope`/`RunAsync`/`RunWorkAsync` stay
  exactly as they are today. Because of this, **the toolbar `IsEnabled` bindings and the sidebar
  scrim need zero XAML changes** — they already only care about `IsBusy`, and `IsBusy` will simply
  stop being set for the cases below.

- **`IsRefreshing`** (new) — set only around the *visible* light, non-exclusive,
  background-triggered refresh paths:
  - `RefreshAsync`/`RefreshOnceAsync`, when invoked from `OnRepoChangedExternally(Refs)` (an
    external branch/HEAD change) or the 5-minute failsafe timer (`StartAutoRefresh`) — **not**
    when it's the tail of a `RunThenRefresh*` sequence for a user-initiated mutation, which should
    keep showing the full lock, since the user explicitly triggered it and is watching for it to
    finish.
  - `RefreshIgnoredPathCacheAsync` (added this cycle) — already doesn't touch `IsBusy` at all
    today; almost certainly belongs with the *silent* tier below rather than this one (it's a
    single fast CLI call with nothing user-facing to report).

  Does **not** call `TryEnterBusyScope()` at all, so it can never collide with — or be blocked
  by — a real command. Needs its own light re-entrancy guard so two background refreshes don't
  overlap each other (see below).

- **`IsSilentlyRefreshing`** (new) — set only around `RefreshWorkingDirStatusAsync`, when invoked
  from `OnRepoChangedExternally(WorkingDir)` — i.e. every ordinary working-directory change: a file
  added, removed, edited, or renamed, compiling included, ignored path or not. Never drives any
  UI element (see `IsActive` below) and, like `IsRefreshing`, never calls `TryEnterBusyScope()`.
  Exists purely so tab-close safety and re-entrancy still have something to check (see below) —
  functionally it's "the same as `IsRefreshing`, minus being wired to anything visible".

- **`IsActive`** (new, computed: `IsBusy || IsRefreshing` — **not** `IsSilentlyRefreshing`) — what
  the tab spinner, the commit-list progress bar, and the taskbar indicator bind to instead of bare
  `IsBusy`. This is the *only* binding change needed in `MainWindow.xaml` (`TabBusyIndicator`'s
  `Visibility`, and the `TaskbarProgressState` multi-binding's first leg) and `CommitListView.xaml`
  (the row-2 overlay).

### Re-entrancy and exclusivity for the light/silent paths

- Add a small `RunLightAsync(Action work, bool silent)` (or fold into `RefreshOnceAsync`'s existing
  `if (IsBusy) … else …` dispatch — see below) that sets `IsRefreshing` or `IsSilentlyRefreshing`
  (never both) to `true`, runs `_git.Executor.RunAsync(work)`, and resets it in a `finally` —
  deliberately *not* going through `TryEnterBusyScope`.
- `RefreshAsync` already has its own `_refreshInFlight`/`_refreshPending` coalescing, independent
  of `IsBusy` — that logic is reused as-is; it just also needs to flip `IsRefreshing` instead of
  going through `RunAsync`'s `IsBusy` scope for the standalone (non-reentrant) case.
- `RefreshWorkingDirStatusAsync` has no such guard of its own today — it relies entirely on the
  outer `OnRepoChangedExternally`/timer callers' `if (IsBusy) return;` check to avoid overlap. That
  check needs to become `if (IsBusy || IsRefreshing || IsSilentlyRefreshing) return;` (checking
  *all three* flags) so two background refreshes still can't run concurrently, while a heavy op
  already running still correctly defers the background one exactly as today.
- `RefreshOnceAsync`'s existing dual-mode dispatch —
  `if (IsBusy) await RunWorkAsync(...); else await RunAsync(...);` — currently uses "is a heavy
  scope already open" as a proxy for "am I reentrant, or standalone". That distinction still holds
  (a `RunThenRefresh*` sequence always has `IsBusy` true first), but the standalone branch needs to
  pick between the heavy path (`RunAsync`, e.g. an explicit user-pressed F5 "Refresh") and the new
  light path (background trigger). Cleanest option: thread an explicit `isBackgroundTrigger` bool
  through `RefreshAsync(force, scope, isBackgroundTrigger)` from each of its three call families
  (watcher, failsafe timer, explicit user action) rather than trying to infer it after the fact.

### Tab-close safety

`AppViewModel`'s close/close-others/close-to-the-right and `CloseTab`'s own guard all switch from
`!tab.IsBusy` to a single new `RepositoryViewModel.CanClose` property
(`=> !IsBusy && !IsRefreshing && !IsSilentlyRefreshing`), so a tab mid-background-refresh —
visible or silent — is exactly as protected against the disposal race as one mid-Fetch. This is
the main reason the silent tier still needs a real (if UI-invisible) flag instead of just skipping
`IsBusy` with no bookkeeping at all: closing a tab mid-`Reopen()`/mid-`git status` is still the
same disposal race regardless of whether anything was ever shown on screen for it.

### What does *not* change in this pass (explicit scope boundary)

- Stage/Unstage/Discard stay on the heavy `IsBusy` path. They mutate the index directly from a
  user click; reclassifying them is a separate, later decision, not bundled into this one.
- Fetch/Pull/Push/Commit/etc. themselves are **not** becoming lighter-weight or losing their
  exclusive lock — the ask is that they stay *clickable* while something else (a background
  refresh) is quietly running, not that they themselves stop locking the UI once *they're* running.
- `RepositoryViewModel.Staging.cs`'s `.gitignore`-add flow calls `RefreshWorkingDirStatusAsync()`
  directly with **no** busy scope at all today (a pre-existing, unrelated small gap — that action
  is user-initiated and arguably should be wrapped in the heavy scope like every other explicit
  mutation). Worth fixing alongside this work since it's the same code path, not because it's part
  of the background-refresh problem.
- Whether `LoadSidebarAsync`/`RefreshLfsStatusAsync` currently ever run from a background trigger
  (as opposed to only from explicit user actions / initial load) needs to be re-verified against
  the code at implementation time rather than assumed from this plan — if they're only ever
  user-triggered today, they simply stay on the heavy path unchanged.

## Verification plan (once implemented)

- Edit/create/delete/rename a tracked file outside PickleGit while a repo tab is open: **no**
  indicator at all — no tab spinner, no commit-list progress bar, no sidebar scrim — while the
  staged/unstaged file lists and the "Uncommitted changes" node still update correctly a moment
  later. This is the main new guarantee from this round's addition.
- Switch branches (or commit) from the command line while a PickleGit tab has that repo open: tab
  spinner + commit-list progress bar appear (the visible light tier); toolbar buttons and the
  sidebar stay clickable throughout.
- While either kind of background refresh is running, click Fetch/Push/Create Branch — it should
  start immediately, not show "Another operation is already in progress".
- Start a real Fetch/Push, and confirm the toolbar still locks and the sidebar scrim still appears
  for its actual duration — the heavy path must be completely unchanged.
- Try to close a tab during a silent working-dir refresh (e.g. right after saving a file) and
  during a visible one (e.g. right after an external checkout) — both must still be refused (or
  deferred), matching today's behavior for a tab mid-Fetch, even though the first case shows
  nothing on screen.
- Build/compile a real project in the open repo (the original complaint): confirm zero visible
  indication throughout the whole build, regardless of which files the build touches or how their
  names are generated — this no longer depends on the ignored-path list being complete.
