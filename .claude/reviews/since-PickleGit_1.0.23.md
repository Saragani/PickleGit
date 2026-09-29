# Code Review: PickleGit_1.0.23..HEAD

**Date:** 2026-09-29
**Range:** `PickleGit_1.0.23..HEAD` (branch `main`)
**Scope:** `PickleGit/` application source only (30 files, ~1130 changed lines). Excludes `.claude/`, `.plans/`, `docs/adr/`.
**Nature:** Cumulative re-check across 25 already-individually-reviewed/merged commits — not a fresh single-PR review.

---

## Commit format

`git log --oneline PickleGit_1.0.23..HEAD` shows 25 commits, all plain imperative subject lines, no WIP/temp/fixup commits, no invented issue-number/type prefixes. Compliant with `commit-format.md`.

---

## Findings

### [MEDIUM] Error handling — swallowed exception with no logging
**File:** `PickleGit/Services/GitService.cs:1990-2016` (`GetMergeConflictedFilePaths`)
**Issue:** The new method's `catch { return result; }` swallows any exception (e.g. an `IOException` from `File.ReadLines` if MERGE_MSG is locked/deleted mid-read) with no call to `Services/AppLog.cs`. `code-style.md` is explicit: "Do not swallow exceptions with empty catch blocks — log via `Services/AppLog.cs` at minimum." This is also the exact class of bug `PickleGit/CLAUDE.md`'s own war-story log warns about repeatedly (a silent failure with zero diagnostic trail, e.g. the `$"..."` syntax-highlighter incident and the bisect-regex incident) — if this path ever silently returns an empty list instead of the real conflict-file list, the pre-filled merge commit message documented in `RepositoryViewModel.Rebase.cs`'s `ContinueOperationAsync` degrades silently to just the source description, with no signal anything went wrong.
**Fix:** `catch (Exception ex) { AppLog.Warn("GetMergeConflictedFilePaths failed to read MERGE_MSG.", ex); return result; }`

### [MEDIUM] MVVM pattern — `ReselectSameCommit` bypasses `BaseViewModel.Set` and duplicates setter logic
**File:** `PickleGit/ViewModels/RepositoryViewModel.cs:1654-1670`
**Issue:** `ReselectSameCommit` directly mutates the private backing fields (`_selectedNode = restored; _selectedNodes.Clear(); _selectedNodes.Add(restored);`) instead of routing through the `SelectedNode` property setter (lines 157-171), which normally does the equivalent `Set(ref _selectedNode, value)` + collection sync. The bypass is deliberately documented (avoiding the setter's `OnSelectedNodesChanged()` → `LoadCommitDetail` side effect, which would otherwise clear `SelectedFile`/`CommitFiles` on every unrelated refresh) and is a reasonable design call, but it:
  - Violates the stated convention ("Every ViewModel... use `BaseViewModel.Set`, don't hand-roll the guard") literally, even though the equality-guard itself isn't really the point here.
  - Duplicates the setter's `_syncingFromSelectedNode` + `_selectedNodes.Clear()/Add()` sequence verbatim in a second place — if that sync logic ever changes (e.g. a new field needs to be kept in step), it's easy to update one copy and miss the other.
**Fix (optional, non-blocking):** Extract the `_syncingFromSelectedNode`-guarded collection-sync block into a private `SyncSelectedNodesTo(GraphNode node)` helper called from both the `SelectedNode` setter and `ReselectSameCommit`, so there's exactly one place that knows how to keep `_selectedNodes` in sync with a single selection.

### [LOW] Error handling — swallowed exceptions with no logging (two more, lower-impact instances)
**Files:**
- `PickleGit/Services/Git/GitCli.cs:117-120` (`StripUserInfo`'s `catch (UriFormatException) { return url; }`)
- `PickleGit/Services/DialogSizing.cs:240-243` (`ForOwner`'s `catch { return SystemParameters.WorkArea.Height - BottomMargin; }`)
**Issue:** Both are typed/scoped fallbacks with a documented "safe default" rationale (much more defensible than a blanket swallow), but still don't log per `code-style.md`'s "at minimum" wording. Real-world impact is low: `StripUserInfo` only affects a debug-log string, and `DialogSizing.ForOwner` only affects a cosmetic `MaxHeight` cap.
**Fix:** Add `AppLog.Warn(...)` (or a comment explicitly noting why logging was deliberately omitted here, e.g. "expected/frequent, not worth log noise") if these are ever revisited.

### [LOW] Performance — sequential subprocess spawn per remote in "Fetch All Remotes"
**File:** `PickleGit/ViewModels/RepositoryViewModel.Remote.cs:97-102` (`FetchAsync`)
**Issue:** `foreach (var r in remotes) { ... await GitCli.HasConfiguredCredentialHelperAsync(...) }` awaits one `git config --get-urlmatch` subprocess per remote, sequentially, before the actual fetch work begins. Each spawn has real process-start overhead (tens of ms). For the typical 2-3 remote case this is negligible; for a repo with many remotes it adds up linearly and blocks the UI thread's `await` chain a little longer before the busy-scope fetch starts.
**Fix (optional):** `await Task.WhenAll(remotes.Select(...))` to resolve all remotes' native-auth capability concurrently, if this ever becomes noticeable in practice.

### [LOW] Code-style — CLI arguments built via interpolated string, not a discrete array
**File:** `PickleGit/Services/Git/GitCli.cs:80-101` (`HasConfiguredCredentialHelperAsync`)
**Issue:** `security.md`/`code-style.md` both state CLI arguments should be "a discrete argument array... never concatenated into a single shell-interpreted command string." This new method (like effectively every other `GitCli.RunAsync` call site in the codebase, e.g. `RepositoryViewModel.Remote.cs`'s `$"fetch {(prune ? "--prune " : "")}{CliGitService.Quote(r.Name)}"`) instead builds one interpolated string with `CliGitService.Quote()` escaping. This is not a real security hole — `ProcessStartInfo.UseShellExecute = false` means there's no `cmd.exe`/shell involved, and `Quote()` correctly implements Windows argv-quoting (backslash/quote escaping verified in `CliGitService.cs:135+`) — but it is a literal deviation from the documented rule, applied consistently project-wide, not something newly introduced by this diff. Flagging for awareness only; not asking for a rewrite of an established, working, project-wide pattern in this review.

### [INFO] Behavior change — Merge "Continue" no longer runs `git merge --continue`
**Files:** `PickleGit/ViewModels/RepositoryViewModel.Rebase.cs:287-302`, `PickleGit/Models/ConflictState.cs:165-169`
**Observation:** For `ConflictOperation.Merge`, `ContinueOperationAsync` now only pre-fills `CommitMessage` (source description + resolved-file list, built from the new `GetMergeConflictedFilePaths`) and returns — it no longer invokes `git merge --continue` (that case was removed from the switch). The user must now click a separate Commit action to actually finish the merge. This is intentional and matches the paired UI-copy change in `ConflictState.RemainingFilesLabel` ("click Continue, then Commit, to complete the merge"). Verified this is functionally sound: `CommitAsync` (`RepositoryViewModel.Staging.cs:404`) calls `_git.CreateCommit(...)`, and LibGit2Sharp's `Repository.Commit` auto-detects an in-progress merge via `MERGE_HEAD` and supplies the second parent + cleans up merge state, so the plain commit path still produces a correct 2-parent merge commit. No bug found, just flagging the semantic change for the record since "Continue" no longer means what it used to for this one operation type.

### [INFO] Graph rendering — uncommitted pseudo-commit no longer draws an edge into real history
**File:** `PickleGit/ViewModels/RepositoryViewModel.cs:1564-1570` (and the equivalent second call site near line 1635)
**Observation:** `ParentShas` for the synthetic "Uncommitted changes" `GraphNode` changed from `new List<string> { commits[0].Sha }` to `new List<string>()`. Traced through `GraphLayout.Compute` (`Models/GraphNode.cs:100-103`): with an empty `ParentShas`, the lane is freed immediately after the pseudo-commit row instead of continuing into `commits[0]`'s lane, so no connecting line is drawn from "Uncommitted changes" down to HEAD anymore, and `commits[0]` starts a fresh lane with `HasIncomingLine = false` rather than continuing the pseudo-node's lane. This is a deliberate, well-commented fix ("this is a pseudo-commit, not actually on top of commits[0] yet") with no logic bug in the traced path — noting it here only because it's a visible UI behavior change (one fewer connecting line in the graph) that a reviewer should be aware is intentional, not a regression.

---

## Category checklist results

- **Security:** No path-traversal, credential-leak, or injection issues found. `GitCli.HasConfiguredCredentialHelperAsync`'s `StripUserInfo` is a genuine, well-targeted improvement — it prevents an embedded `user:token@host` remote URL from ever reaching `AppLog.Info`'s verbatim `args` log line. Credential values are never logged anywhere in the new code (confirmed in `TryAutoResolveCredential`'s new backoff-retry logging, which logs only pass/fail, never the value). `BuildHttpAuthEnv` continues to pass secrets via process environment variables, not command-line args or logs.
- **WPF/MVVM:** No `MessageBox.Show`/`InputBox` introduced. No converters added outside `App.xaml`. All new/changed brush references (`DiffSearchMatchBrush` via `Converters.ThemeBrushes.Get`) are theme-aware. `SidebarView.xaml.cs`'s new stash-selection `else if` branch mirrors the existing Tag/Branch dispatch pattern in the same `SelectionChanged` handler — consistent, not new code-behind logic.
- **Threading/git backend:** All new/changed CLI-mutating call sites (`FetchAsync`'s all-remotes loop, single-remote fetch/pull/push paths) call `_git.Reopen()` after the CLI operation, confirmed by tracing each branch. All new `GitCli`-only calls (no `_repo`/LibGit2Sharp involved) are appropriately made directly from `async` UI-thread methods without executor routing — consistent with the existing project convention that only LibGit2Sharp/`_repo` access requires `GitService.Executor`. The new `[ThreadStatic] t_executingOpToken` mechanism in `RepositoryViewModel.cs` (RunWorkAsync reentrancy fix) is correct given `GitExecutor` runs exactly one queued item at a time on its single dedicated thread — verified the try/finally correctly restores the previous token even on exception, and the `cts.Dispose()` move outside the `ReferenceEquals` check correctly avoids leaking the CTS for a reentrant caller.
- **Rendering/virtualization:** `CommitGraphControl.cs`'s only change is a `Typeface` fallback font-family string (no new allocations in `OnRender`). `DiffView.xaml.cs`'s new `ScrollDiffItemToTop` and `GoToLine` methods don't allocate brushes/pens. No virtualization settings changed.

---

## Verdict

**READY TO MERGE** — no CRITICAL or HIGH findings. Two MEDIUM findings are both narrow, well-reasoned deviations from documented conventions (one missing log call on a rare I/O failure path, one deliberate setter bypass with a documented rationale); neither blocks merge.

```
Verdict: READY TO MERGE
CRITICAL: 0  HIGH: 0  MEDIUM: 2  LOW: 4  INFO: 2
```
