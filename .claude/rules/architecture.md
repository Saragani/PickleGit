---
description: PickleGit architecture — solution layout, hybrid LibGit2Sharp + git.exe backend, threading model, change detection, commit-graph visibility, settings/cache.
---

# Architecture — PickleGit

## Solution Structure (single project)

PickleGit is one WPF project (`PickleGit.csproj`) inside `PickleGit.sln` — no MEF, no plugin architecture, no separate service/domain assemblies.

| Folder | Role |
|--------|------|
| `App.xaml` / `App.xaml.cs` | Startup, theme bootstrap, single-instance mutex + activation pipe. All `IValueConverter`s live in `App.xaml`'s `Application.Resources` — never in a Window/UserControl (see code-style.md) |
| `MainWindow.xaml(.cs)` | Shell: toolbar, tab control, status bar |
| `ViewModels/` | `AppViewModel` (tabs, global settings), `RepositoryViewModel` (+ partial-class split: `.Staging`, `.Diff`, `.Branches`, `.Remote`, `.Rebase`, `.Detail`, `.Integrations`, `.Bisect`, `.Blame`, `.Compare`) — one `RepositoryViewModel` per open tab, `BranchNodeViewModel`, `HostingViewModel`, `InteractiveRebaseViewModel`, `MergeConflictEditorViewModel` |
| `Views/` | `*View.xaml` UserControls (`SidebarView`, `CommitListView`, `CommitDetailView`, `DiffView`) + `Views/Dialogs/` for modal windows |
| `Services/` | `GitService.cs` (LibGit2Sharp entry point), `AppSettings.cs`, `CredentialStore.cs`, `AvatarService.cs`, `RepositoryWatcher.cs`, `DialogService.cs`, `ShortcutManager.cs`, `AppCommandRegistry.cs`, `UndoService.cs`, `AppLog.cs` |
| `Services/Git/` | The hybrid git backend — `GitCli.cs`, `CliGitService.cs`, `GitExecutor.cs`, `PatchBuilder.cs`, `RebaseTodoBuilder.cs`, `StagingService.cs`, `WorktreeService.cs` |
| `Services/Hosting/` | `IHostingProvider` + `GitHubProvider`, `GitLabProvider`, `BitbucketCloudProvider` (PR/hosting integrations a repo can configure — unrelated to this repo's own GitHub issue tracker) |
| `Services/Highlighting/` | `SyntaxHighlighter.cs` — custom per-line lexer, not a full editor control |
| `Models/` | Plain data classes — `CommitInfo`, `BranchInfo`, `GraphNode`, `ConflictState`, `RebaseTodoItem`, `ReflogEntry`, `BisectState`, `MergeConflictBlock`, `SidebarRow`, `SyntaxSpan`, `UndoEntry` |
| `Controls/` | `CommitGraphControl.cs` — custom `DrawingVisual`-based renderer for the commit graph and ref badges |
| `Converters/` | All `IValueConverter` / `IMultiValueConverter` implementations |
| `Behaviors/` | `ListViewMultiSelectBehavior.cs` |
| `Themes/` | `DarkTheme.xaml`, `LightTheme.xaml`, `PaletteDark.xaml`, `PaletteLight.xaml` — merged into `App.xaml`, swapped at runtime |

See [PickleGit/CLAUDE.md](../../PickleGit/CLAUDE.md) for the authoritative file-by-file map, namespaces, and an extensive running log of WPF pitfalls already debugged in this codebase (theming, virtualization, drag-drop tabs, diff rendering). Read it before touching `CommitGraphControl`, the theme dictionaries, diff virtualization, or the tab drag-reorder logic — most non-obvious bugs in this project have already been hit once and are documented there.

## Build Commands

```bash
# Build (MSBuild must be on PATH, or invoke via its full Visual Studio path)
msbuild PickleGit.sln /p:Configuration=Debug /p:Platform="Any CPU"

# Or open PickleGit.sln in Visual Studio 2022 and press F5
```

Configurations: `Debug`, `Release`. Platform: `Any CPU` (single project, no per-arch split). Output: `WinExe`, target framework `net472`, C# 7.3.

To launch and drive the app for manual verification, use the `run` skill rather than starting it ad hoc — it knows this project's launch/attach pattern.

## Hybrid Git Backend

`GitService` (LibGit2Sharp 0.27.2) is the single entry point for reads, status, and index operations — reachable from ViewModels as `_git`. Operations LibGit2Sharp cannot do (rebase, `pull --rebase`, hunk/line staging via `git apply`, SSH remotes, GPG signing, patch import/export) go through `GitService.Cli` → `Services/Git/CliGitService.cs` → `Services/Git/GitCli.cs` (the process runner).

- Check `GitCli.IsGitAvailable` before any CLI-backed feature and degrade gracefully (disable the menu item, don't throw) when git.exe is missing.
- **After any CLI operation that mutates refs or the index, call `GitService.Reopen()`.** libgit2 caches ref state internally and will not see the CLI's writes otherwise.
- `PatchBuilder.cs` builds unified-diff text for hunk/line-level staging and pipes it to `git apply --cached -` (add `--reverse` to unstage/discard). Line-level patch construction is direction-dependent — see the `PatchBuilder.BuildLinePatch` entry in `PickleGit/CLAUDE.md` before touching it.

## Threading Model

**All LibGit2Sharp calls run on `GitService.Executor`** (`Services/Git/GitExecutor.cs`) — a single dedicated background thread with a `BlockingCollection` work queue. Never call into `GitService`/`_repo` directly from the UI thread, and never via a bare `Task.Run` — the `Repository` object is not thread-safe, and serializing every access through one executor is what makes libgit2 and git.exe calls safe to interleave.

- `RepositoryViewModel.RunAsync` already routes mutating operations through the executor and wraps them in `IsBusy` + `RepositoryWatcher.Suppress()`.
- Ad-hoc reads: `await _git.Executor.RunAsync(() => _git.Xxx())`.
- A property setter must never call git synchronously — clear the visible state immediately, then populate it from a fire-and-forget async method backed by the executor (see the `LoadDiffAsync` pattern in `PickleGit/CLAUDE.md`).
- Work items must never synchronously block on the Dispatcher; the UI thread only ever `await`s executor tasks, never blocks on them.

## Change Detection

`Services/RepositoryWatcher.cs` watches the working directory and `.git` (400 ms debounce) and classifies changes as `WorkingDir` vs `Refs`. App-initiated mutations are wrapped in `watcher.Suppress()` scopes (done inside `RunAsync`) so PickleGit's own writes don't trigger a redundant refresh. `RefreshAsync` computes a state signature and skips the graph/UI/cache rebuild entirely when nothing changed — this is required, not an optimization to remove: rebuilding `GraphNodes` resets scroll position and selection.

## Commit Graph / Branch Visibility

Branch membership for the "smart visibility" filter comes from `CommitInfo.RefMask`, computed once during the single history walk in `GitService.GetHistory` (bit 0 = reachable from HEAD; bits 1–63 = other branch tips). Never add a second history walk just to determine branch membership — the mask makes the filter an O(1) bitwise check per commit.

## Settings & Cache

- Settings: `%APPDATA%\PickleGit\settings.json` (Newtonsoft.Json), written via a temp-file + `File.Replace` for atomicity — never `File.WriteAllText` directly over the live file.
- Commit cache: `%APPDATA%\PickleGit\cache\<hash>.json`, keyed by repo path.
- Credentials: Windows Credential Manager (via `advapi32.dll` P/Invoke in `CredentialStore.cs`), target prefix `PickleGit:`. Hosting provider tokens (GitHub/GitLab/Bitbucket PATs) go through the same store — never persisted to `settings.json` in plaintext.

## Dependencies

| Package | Purpose |
|---|---|
| LibGit2Sharp 0.27.2 | Git operations without requiring git.exe (reads/status/index) |
| Microsoft.Xaml.Behaviors.Wpf | XAML behavior attachments (`ListViewMultiSelectBehavior`) |
| Newtonsoft.Json | Settings + commit-cache serialization |
| AvalonEdit 6.3.1.120 | The merge-conflict editor's editable RESULT pane only (`MergeConflictEditorWindow.xaml`) — real text editing (undo, tab handling) that isn't worth hand-rolling |

No general-purpose third-party control library (Telerik/Infragistics/DevExpress-style suite) — every other control is a styled stock WPF control or a hand-rolled one (`CommitGraphControl`, the diff view's custom flattened/virtualized `ListView`). AvalonEdit is the one deliberate exception, scoped narrowly — see `docs/adr/0003-no-third-party-controls.md`.
