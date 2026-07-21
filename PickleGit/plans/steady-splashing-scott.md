# PickleGit → Git Client — Master Plan

## Context

PickleGit (WPF, .NET 4.7.2, LibGit2Sharp 0.27.2, MVVM) has strong foundations: custom commit graph with Bézier lanes and ref pills, animated drag-reorder tabs, dark theme, staging panel, clone/fetch/pull/push with a smart credential chain, per-repo startup cache. But a full audit shows it is missing the majority of what makes Git a daily driver — both git capabilities (no rebase, revert, reset, amend, conflict resolution, hunk staging, stash apply/drop…) and UX surfaces (zero keyboard shortcuts, almost no context menus, no search, no settings window, VB InputBox prompts).

**Goal:** implement everything, phase by phase (checking in between phases), to make PickleGit a top-tier client. Decisions confirmed with user: **hybrid git backend** (LibGit2Sharp + git.exe), **Bitbucket Cloud + GitHub + GitLab** hosting integration, **full scope (all phases)**, **dark theme only**.

---

## Part 1 — Complete Gap Analysis (what's missing today)

### Git operations (missing entirely)
| Area | Missing |
|---|---|
| History rewriting | Rebase (any), **interactive rebase**, amend, reset (soft/mixed/hard), revert, undo |
| Branches | Rename, checkout remote→local tracking, detached checkout UX, delete-force (param ignored today) |
| Push/pull | Force-with-lease, set-upstream/push new branch, push tags, pull --rebase, fetch --prune / all remotes |
| Stash | Apply, drop (pop exists on VM but **unreachable from UI**), stash diff preview, partial stash |
| Tags | Delete, push, checkout; annotated tag uses **hardcoded "user"/"user@email.com" signature** (bug) |
| Remotes | Edit URL, remove, rename (only list + add exist) |
| Conflicts | **No conflict UI at all** — merge silently half-works, cherry-pick throws "resolve manually" |
| Files | Blame, single-file history, .gitignore actions, open/reveal in Explorer, copy path |
| Advanced | Submodules, worktrees, reflog, GPG signing, SSH remotes, LFS awareness, bisect |

### UX / UI gaps
- **Zero keyboard shortcuts** (no F5, Ctrl+Enter, Ctrl+W, Ctrl+P… nothing). No command palette.
- **Context menus missing** on: remote branches, tags, stashes, remotes, files (staged/unstaged/commit), tabs, and commit menu is minimal (no revert/reset/copy-sha/rebase-onto).
- Diff viewer: unified only — no side-by-side, no syntax highlighting, no word-level diff, no **hunk/line staging**, no next/prev hunk nav, no image diff.
- No commit search/filter. No "Load more" past the hard 2000-commit cap.
- No settings/preferences window. Create-Tag & Stash prompts use **Microsoft.VisualBasic.InputBox** (non-dark, ugly); confirms use raw MessageBox.
- No drag-and-drop branch operations.
- No PR/hosting awareness, no avatars (initials only), no submodule/worktree/reflog panels.
- No undo, no commit-and-push, no amend checkbox, no 50 message guides.

### Performance / correctness debt
- **Two full history walks per refresh** (second walk just builds branch-membership set).
- LibGit2Sharp calls on the **UI thread**: StageFile/UnstageFile, LoadWorkingDir, ComputeAggregatedFiles (loops per selected commit — freezes on big multi-select).
- No FileSystemWatcher — 2-minute polling only; external changes invisible for up to 2 min.
- Graph fully recomputed + cache fully re-serialized every refresh even when nothing changed.
- Dead code: MainViewModel.cs (empty), RepositoryAccount/AuthType enum, vestigial DiffHunks collection, `#if false` block in MainWindow.xaml.cs.

---

## Part 2 — Architecture Decisions

**D1. Hybrid backend (composition, no interface).** Keep `Services/GitService.cs` (LibGit2Sharp) as the single VM-facing entry point. Add:
- `Services/Git/GitCli.cs` — process runner: `Task<GitCliResult> RunAsync(workDir, args, opts, ct)`; git.exe discovery (`where git` → Program Files → settings override); always `--no-optional-locks`, `GIT_TERMINAL_PROMPT=0`; stderr → progress. `IsGitAvailable` gates CLI-backed features (disabled menu + tooltip when absent).
- `Services/Git/CliGitService.cs` — typed ops (rebase, revert continue/abort, pull --rebase, apply, GPG commit, SSH push/pull, worktree, submodule update). Exposed as `GitService.Cli`.
- After any CLI mutation, dispose/reopen the `Repository` handle (libgit2 ref-cache staleness).

**D2. Threading:** new `Services/Git/GitExecutor.cs` — dedicated background thread + `BlockingCollection` queue; ALL libgit2 and CLI ops funnel through it (serializes libgit2 vs git.exe naturally). Rule: no Dispatcher calls inside executor work items.

**D3. Hunk/line staging via patch text:** `Services/Git/PatchBuilder.cs` builds unified-diff text (recomputing `@@` counts for partial line selection) → pipe to `git apply --cached -` (stdin; `--reverse` to unstage; without `--cached` to discard hunks). Delegates CRLF/filters/mode edge cases to git.

**D4. Conflict flow:** `Models/ConflictState.cs` detects op from `.git/MERGE_HEAD`, `CHERRY_PICK_HEAD`, `rebase-merge/` (+step N/M), `REVERT_HEAT`→`REVERT_HEAD`; conflicted files from `repo.Index.Conflicts`. Merge/cherry-pick stop throwing — return result enums. Stage 1: banner UI with per-file Take Ours/Theirs/Mark Resolved + Continue/Abort (via CLI with `GIT_EDITOR=true`). Stage 2: 3-pane merge editor window.

**D5. FileSystemWatcher:** `Services/RepositoryWatcher.cs` — Watcher A (workdir, ignores `.git`), Watcher B (`.git` HEAD/index/packed-refs + `refs/` recursive); 400 ms coalescing timer → `Changed(WorkingDirOnly | RefsChanged)`. Self-suppression via `GitService.BeginOperation()` ref-count wrapped by RunAsync. Event-storm cutoff → fall back to polling. Keep a 5-min failsafe poll (network drives); retire the 2-min timer.

**D6. Single-walk history + paging:** assign branch tips bit indices; during the one topological walk, propagate a ref-mask sha→mask to parents; `CommitInfo.RefMask` powers smart-visibility with **zero extra walks**. Paging: walk reports batches via `IProgress` every 500 commits; ceiling becomes a setting (default 10 000) + "Load more…" footer row. Skip graph recompute + cache write when tips/HEAD/count unchanged (hash check).

**D7. Syntax highlighting: custom lightweight tokenizer** (NOT AvalonEdit — would destroy the virtualized FlatDiffItems architecture that hunk widgets depend on). `Services/Highlighting/SyntaxHighlighter.cs`: per-line regex lexers with carried `LexerState`, ~10 languages by extension (C#, XAML/XML, JS/TS, JSON, Python, C/C++, CSS, HTML, Markdown, PowerShell); render as cached-brush Runs.

**D8. Dialog infrastructure:** `Views/Dialogs/TextPromptDialog.xaml`, `ConfirmDialog.xaml` (danger button + "don't ask again"), `ErrorDialog.xaml` (expandable git stderr) + `Services/DialogService.cs`. Kill VB InputBox and drop the Microsoft.VisualBasic reference.

---

## Part 3 — Implementation Phases

Sizes: S < ½ day-equivalent, M ~1, L ~2-3, XL ~4+. Each phase ends with a build + manual verification checkpoint with you.

### Phase 0 — Foundations (perf + plumbing everything else needs)
1. **GitExecutor** + move all UI-thread libgit2 calls onto it (M) — new `Services/Git/GitExecutor.cs`; modify GitService, RepositoryViewModel (StageFile/UnstageFile/LoadWorkingDir/ComputeAggregatedFiles become async).
2. **Single-walk ref-mask** smart visibility (M) — GitService.GetCommits, CommitInfo (+RefMask), RepositoryViewModel.BuildDisplayList.
3. **Paged loading + "Load more" + skip-unchanged graph/cache** (M) — GitService, RepositoryViewModel, CommitListView footer.
4. **RepositoryWatcher** + suppression, retire 2-min poll (M) — new `Services/RepositoryWatcher.cs`.
5. **GitCli + CliGitService skeleton + discovery** (M) — new `Services/Git/GitCli.cs`, `CliGitService.cs`.
6. **Dialog infrastructure** (S) — 3 dark dialogs + DialogService; migrate Create Tag/Stash prompts.
7. **Keyboard shortcut baseline** (S) — MainWindow InputBindings: F5 refresh, Ctrl+O open, Ctrl+W close tab, Ctrl+Tab next tab, Ctrl+Enter commit, Ctrl+F search (reserved), Ctrl+Shift+P palette (reserved).
8. **Fixes/cleanup** (S) — annotated-tag signature from repo config; delete dead MainViewModel/`#if false` block/DiffHunks vestige.

### Phase 1 — Core git-operation completeness
1. **Amend** checkbox (prefills HEAD message) + **sign-off** + **Commit & Push** split button (M) — CommitDetailView, GitService.CreateCommit(amend:).
2. **Push options**: auto set-upstream for new branches, **force-with-lease** via CLI with danger confirm, push tag (M).
3. **Pull --rebase** dropdown on Pull + per-repo default; **fetch --prune / all remotes** (M).
4. **Checkout remote branch as local tracking** (dbl-click + context menu) (S); **branch rename** (S); fix delete-force (S); **detached checkout** with HEAD-detached badge (S).
5. **Reset soft/mixed/hard** + **revert** on commit context menu (M).
6. **Stash completeness**: apply/pop/drop context menu on stash items, stash diff preview on select, keep-index option (M).
7. **Tags**: delete/push/checkout context menu (S). **Remotes**: add/edit/remove context menu + dialog (S).
8. **File context menus** (staged/unstaged/commit files): open, reveal in Explorer, copy path, add to .gitignore, discard (M).
9. **Commit context menu expansion**: copy SHA, copy message, checkout (detached), revert, reset here, rebase onto (Phase 4 wiring), create branch/tag here (exists) (S).
10. **Commit search/filter bar** (Ctrl+F): substring over message/author/sha, filters graph list live (M).
11. **Tab context menu**: close, close others, copy path, open in Explorer/terminal (S).

New dialogs: `Views/Dialogs/RemoteDialog.xaml`, reset-mode picker inside ConfirmDialog. Everything follows existing RunThenRefresh + Tag-trick context-menu patterns.

### Phase 2 — Conflicts + Undo (the trust threshold)
1. **ConflictState detection** + non-throwing merge/cherry-pick/revert results (M).
2. **Conflict banner** in CommitDetailView working-dir mode (M):
```
┌─ MERGE IN PROGRESS: feature/x → master ─────────────┐
│ ⚠ 3 conflicted files            [Continue] [Abort]  │
│  ⛔ src/Foo.cs   [Take Ours][Take Theirs][Open Editor]│
│  ✅ src/Bar.cs   resolved                             │
└──────────────────────────────────────────────────────┘
```
3. **3-pane merge editor** `Views/MergeConflictEditorWindow.xaml` (XL): conflict-marker parser (diff3-aware) → alternating clean/conflict blocks; OURS | THEIRS read-only synced panes, RESULT editable below, per-block `<<` `>>` `both` buttons; Save = write + stage. External-merge-tool fallback setting.
4. **Undo system** `Services/UndoService.cs` (L): record {PreOpHead, PostOpHead, branch, description} around mutating ops; status-bar toast "Merged X ↩ Undo"; inverse ops (reset --soft for commit, recreate deleted branch, reset --hard w/ dirty-tree guard for merge/reset, re-checkout). Refuses if HEAD moved externally (watcher makes that visible).
5. **rerere opt-in** setting (S).

### Phase 3 — Diff & staging power features
1. **Hunk staging** — [Stage hunk]/[Discard hunk] buttons on hunk headers in working-dir diffs, via PatchBuilder + `git apply --cached` (L).
2. **Line staging** — checkbox gutter on lines, "Stage selected lines" (M).
3. **Word-level intra-line diff** — LCS over word tokens on paired remove/add lines, stronger tint sub-spans; extract ParsePatch from GitService into `Services/Git/DiffService.cs` (M).
4. **Side-by-side diff toggle** — two synced virtualized lists with filler alignment rows (L):
```
│ 12  old line removed      ║ 12  new line added        │
│ 13  context               ║ 13  context               │
```
5. **Syntax highlighting** tokenizer per D7 (L).
6. **Image diff** — old/new preview side-by-side for png/jpg/gif/bmp/ico blobs (S).
7. **File history + blame** — file context menu → panel of commits touching path (`QueryBy(path)`), blame gutter via `repo.Blame` (L) — new `Views/FileHistoryView.xaml` + VM.
8. **Diff navigation** — next/prev hunk buttons + F7/Shift+F7 (S).

### Phase 4 — Rebase + drag-drop
1. **Rebase onto** (branch/commit context menus) via `Cli.RebaseAsync` with `--autostash`; conflicts flow into Phase 2 banner showing "step 3/7" from rebase-merge/msgnum (M).
2. **Interactive rebase** `Views/InteractiveRebaseDialog.xaml` (XL):
```
┌─ Interactive Rebase onto master (6 commits) ─────────┐
│ ⠿ [pick ▾]  a1b2c3  Add login form                   │
│ ⠿ [squash▾] d4e5f6  fix typo            (drag ⠿ to  │
│ ⠿ [reword▾] 789abc  Update API client     reorder)  │
│ ⠿ [drop ▾]  cdef01  WIP                              │
│                              [Cancel]  [Start Rebase]│
└──────────────────────────────────────────────────────┘
```
   Mechanism: write todo file to %TEMP%; `GIT_SEQUENCE_EDITOR` = generated helper cmd that copies our todo over git's `%~1`; rewords via `exec git commit --amend -F <msgfile>` lines (quoting-safe, no GIT_EDITOR dance). Reuse the tab-drag ghost/slide animation approach for row reordering.
3. **Drag-drop branch operations** (L): drag branch onto branch (sidebar or graph ref pill) → popup menu {Merge into, Rebase onto, Create PR (Phase 6)}; drag commit onto branch = cherry-pick. `DropActionPopup` control.
4. **Reflog view** (M) — parse `.git/logs/HEAD` text (libgit2 0.27 reflog API too weak); hidden-by-default sidebar section; checkout/reset from entries — the safety net behind Undo.

### Phase 5 — UX platform: palette, settings, SSH/GPG (dark-only per decision)
1. **Command palette** Ctrl+Shift+P (L) — `Services/AppCommandRegistry.cs` {Category, Name, Gesture, ICommand}; borderless fuzzy-search popup; Ctrl+P flavor = quick branch checkout / file lookup.
2. **Settings window** `Views/SettingsWindow.xaml` (L) — tabs: General (git.exe path, clone dir, commit cap), Profile (default + per-repo identity), UI (columns, date format, confirmations), Integrations (host tokens), Shortcuts (editable map via `Services/ShortcutManager.cs`, persisted overrides). Add settings.json `Version` field + migration shim first.
3. **SSH support** (M) — route fetch/pull/push through CLI when remote URL is ssh:// or git@ (system OpenSSH/agent handles keys); HTTPS keeps LibGit2Sharp + CredentialStore. Delete dead RepositoryAccount/AuthType.
4. **GPG signing** (M) — when `commit.gpgsign` or setting on, commit via `Cli.CommitAsync(msgFile, amend, signoff)`.
5. **Commit message polish** (S) — subject/body split with 50/72 guides, per-repo template.

### Phase 6 — Hosting integration + ecosystem
1. **Provider abstraction** `Services/Hosting/IHostingProvider` + `BitbucketCloudProvider`, `GitHubProvider`, `GitLabProvider` (XL total, one M each after the first): detect from remote URL; auth = PAT/app-password via existing CredentialStore (`PickleGit:host:<domain>`); `GetPullRequestsAsync`, `GetCreatePrUrl(source, target)` (browser compose first; in-app create dialog later).
2. **PR sidebar section** (M) — PULL REQUESTS: #, title, author, source→target, open-in-browser; PR badge on branches; "Create PR" in drag-drop menu + branch context menu.
3. **Avatars** (M) — `Services/AvatarService.cs`: gravatar MD5 + GitHub noreply-id URLs, disk cache %APPDATA%\PickleGit\avatars, async decode; CommitGraphCell draws avatar circle with initials fallback.
4. **Submodules sidebar section** (M) — list via repo.Submodules, init/update via CLI, dirty badges.
5. **Worktrees section** (M) — CLI `git worktree list/add/remove`, open-as-tab.
6. **LFS awareness** (S) — pointer-file badge in diff, track/install passthrough.
7. **"Open terminal here"** button (S) — true embedded ConPTY terminal deferred (XL, low value vs cost on net472).

---

## Part 4 — Cross-cutting risks
- **libgit2 ↔ git.exe interop**: GitExecutor serializes both; reopen Repository after CLI mutations.
- **GitExecutor deadlock rule**: no Dispatcher waits inside executor items.
- **FSW storms** (node_modules): rate cutoff → poll fallback.
- **Interactive-rebase conflict mid-todo**: conflict banner must show rebase step progress and Continue must resume the todo.
- **Undo safety**: verify HEAD == recorded PostOpHead + clean-tree guard before destructive inverse.
- Settings schema growth → Version + migration before Phase 5.

## Part 5 — Verification (every phase)
1. `msbuild PickleGit.sln` (or Rebuild in VS) — zero warnings introduced.
2. Launch app, open a real repo + a scratch repo created for testing (script conflicts: two branches editing same lines; stash stacks; remote on disk via `git init --bare` for push/pull/force tests without network).
3. Per-phase manual checklist (e.g., Phase 2: create conflict → banner appears → Take Ours → Continue → history correct → Undo merge → back to pre-merge HEAD).
4. Perf spot-checks: open repo with >10k commits (e.g., clone of a large OSS repo), verify single-walk timing, scroll smoothness, watcher-driven refresh <1 s after external `git commit` from a terminal.
5. Regression: existing features (tabs drag, column settings, smart visibility, credential chain) after each phase.

## Suggested first milestone
Start with **Phase 0** (all 8 items) → checkpoint build + review with user → Phase 1.
