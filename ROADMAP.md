# PickleGit — Improvement Roadmap

Prioritized by value-per-effort: Phase 1 = quick wins (bug fixes + low-effort polish),
Phase 2 = core UX + missing operations, Phase 3 = larger features, Phase 4 = infrastructure/refactors.
Items are checked off as they are completed.

---

## Phase 1 — Quick wins (correctness fixes & low-effort polish)

### Correctness bugs
- [x] **1.1 Merge-editor wrong-block resolution** — `MergeConflictEditorViewModel.ResolveCurrent` finds blocks via `ResultText.IndexOf(block.RawText)`; two identical conflict blocks always resolve the first. Track running offsets instead.
- [x] **1.2 AvatarService thread-safety** — `Cache`/`NoAvatar` dictionaries written from background continuations, read without a lock. Guard all access with the existing lock.
- [x] **1.3 CredentialStore robustness** — `ListAll` splits credential targets on `:` (breaks on hosts/usernames containing colons); `LoadViaGitCredentialHelper` hardcodes `"git"` instead of `GitCli.ResolveGitPath()`.
- [x] **1.4 Atomic settings writes** — every `AppSettings` save is a full read-modify-write with `File.WriteAllText` (non-atomic, can corrupt settings.json on crash) wrapped in silent `catch {}`. Write temp file + `File.Replace`, log failures.
- [x] **1.5 Ctrl+F duplicate wiring** — commit search opens both from a hardcoded `OnPreviewKeyDown` check and ShortcutManager. Make ShortcutManager the single source of truth (new rebindable `FindCommits` action).
- [x] **1.6 Hunk-navigation fixes** — `_currentHunkIndex` not reset when the diff reloads (first F7 after switching files jumps unexpectedly); scroll-to-hunk targets the unified ListView even in side-by-side mode. Reset index on reload; make side-by-side navigation scroll the side-by-side list.

### Diff polish
- [x] **1.7 Binary-file diff notice** — non-image binary files show a completely blank diff pane. Detect binary patches and show "Binary file — contents not shown" with old/new sizes.
- [x] **1.8 Line-number gutter** — `OldLineNumber`/`NewLineNumber` are computed but never rendered. Add gutters to unified view, side-by-side view, and the file-history diff.

### Dialog & shell polish
- [x] **1.9 Dialog consistency pass** — CloneDialog / CredentialsDialog / NewBranchDialog / SettingsWindow / InteractiveRebaseDialog + Views/Dialogs/*: replace hardcoded `#FF23282E`-family hex with theme resources (they visibly mismatch the main window), add `IsCancel` so Esc closes, set initial focus, set the pickle window icon, and replace remaining `MessageBox.Show` validation with themed inline/DialogService errors.
- [x] **1.10 Relative dates + tooltips** — show "2h ago"-style dates in the Date column (per an existing `AuthorDateRelative`), absolute date in tooltip; add tooltips to truncated commit message and author cells; setting to switch between relative/absolute.
- [x] **1.11 Empty & loading states** — "No commits match your search" / "No commits yet" empty state in the commit list; subtle loading indicator over the commit list during refresh.
- [x] **1.12 Tab context menu** — add "Close All" and "Close Tabs to the Right".
- [x] **1.13 Remember window geometry** — persist window size/position/state instead of hardcoded `Maximized`.
- [x] **1.14 About/version** — show app version in Settings (About section) so users can tell which build they run.
- [x] **1.15 Per-monitor DPI** — add `app.manifest` with PerMonitorV2 DPI awareness (currently none exists; app blurs on mixed-DPI monitors).

## Phase 2 — Core UX & missing git operations

- [x] **2.1 Cancel long operations** — `GitCli.RunAsync` already supports CancellationToken but nothing passes one. Add a CancellationTokenSource per operation and a Cancel button (status bar / busy overlay) for fetch/pull/push/clone/rebase.
- [x] **2.2 Merge options** — merge submenu/dialog with `--no-ff`, `--squash`, `--ff-only` (currently only default merge exists).
- [x] **2.3 Delete on remote** — "Delete remote branch" (`push --delete`) from remote-branch context menu; "Delete tag on remote".
- [x] **2.4 Whitespace-ignore toggle** — diff toolbar toggle for ignore-whitespace, persisted as a setting.
- [x] **2.5 Identity guard** — block commits that would fall back to `"User" <user@example.com>`; prompt to set identity instead (scattered fallbacks today).
- [x] **2.6 Ref badge overflow** — badges are silently clipped; add "+N" overflow indicator with tooltip listing all refs.
- [x] **2.7 Search upgrades** — match count display, search author email, SHA substring (not just prefix), optional file-path search (`path:` qualifier).
- [x] **2.8 Single instance** — mutex + pipe: opening a repo from command line activates the running instance and adds a tab instead of launching a second process racing on settings.json.
- [x] **2.9 Clone improvements** — remember default clone parent directory; branch selection; optional `--recurse-submodules`; shallow depth option.
- [x] **2.10 Large-diff / large-image guards** — cap eager parse/render with "Large diff — click to load"; skip decoding huge images.
- [x] **2.11 Commit-list keyboard flow** — Enter opens/focuses detail panel, explicit shortcut to focus the commit list, j/k optional.
- [x] **2.12 Multi-select actions** — "Revert N commits" done. (Compare A..B and squash-N deferred to Phase 3 — they need a compare view / rebase machinery.)
- [x] **2.13 Stash by path** — stash only selected files (`git stash push -- <paths>`) from staging context menu.
- [x] **2.14 Side-by-side partial staging** — hunk-level Stage/Discard/Unstage buttons added to side-by-side headers. (Line checkboxes remain unified-only by design — per-side selection is ambiguous.)

## Phase 3 — Bigger features

- [x] **3.1 Theme consolidation + light theme** — move all inline hex to DarkTheme resources, add LightTheme.xaml + settings toggle with runtime switch.
- [x] **3.2 Interactive rebase upgrades** — Edit/Break actions done (resume via conflict banner). Remaining (minor): editable squash message, drag-to-reorder rows (Move Up/Down buttons exist).
- [x] **3.3 File history & blame upgrades** — DONE: follow renames (`git log --follow` with libgit2 fallback), lazy-load blame. REMAINING: load-more past the 500 cap, blame→commit navigation, reblame at parent.
- [x] **3.4 Merge editor upgrades** — BASE pane + Take Base, both Keep Both orders, BOM-aware encoding preserved. Remaining (needs AvalonEdit-style control): marker/syntax highlighting inside panes.
- [x] **3.5 Hosting upgrades** — GitHub Enterprise + self-managed GitLab via domain mapping in Settings → Integrations. Remaining: PR CI status / detail pane.
- [x] **3.6 Patch workflow** — "Save commit as patch" (format-patch), "Apply patch file" (git apply / am).
- [x] **3.7 Progress bars** — parse git progress percentages (clone/fetch/push) into a determinate progress bar in the toolbar area.
- [x] **3.8 Submodule lifecycle** — add / deinit / sync from the sidebar section.
- [x] **3.9 Find-in-diff** — Ctrl+F while diff focused searches within the loaded diff with next/prev.
- [x] **3.10 Commit message template** — load `.gitmessage` / `commit.template` when composing.

## Phase 4 — Infrastructure & architecture

- [x] **4.1 Logging** — AppLog rolling file logger; settings, unhandled/unobserved exceptions, watcher recreation, repo-open and reflog failures all logged.
- [x] **4.2 Accessibility pass** — AutomationProperties.Name on all symbol-only buttons; low-contrast hint text fixed (Phase 1). Remaining (minor): label→field associations, access-key mnemonics.
- [x] **4.3 Incremental refresh** — search now uses a cheap single-lane layout (no full graph recompute per keystroke); scroll restored to the selected commit after refresh. Load-more still re-walks by design: RefMask branch bits come from the single history walk (see CLAUDE.md).
- [x] **4.4 RepositoryViewModel decomposition** — rebase todo machinery in Services/Git/RebaseTodoBuilder.cs; RepositoryViewModel split into cohesive partial-class files (Staging, Diff, Branches, Remote, Rebase, Detail, Integrations); StagingService and WorktreeService extracted as stateless services; HostingViewModel extracted as a real sub-view-model (RepositoryViewModel.Hosting) since it owns bindable PR state/commands. Verified live via UI Automation against a scratch test instance.
- [x] **4.5 SSH diagnostics** — detect passphrase/agent failures (GIT_TERMINAL_PROMPT=0 makes them opaque) and show actionable guidance.
- [x] **4.6 Force-push safety** — qualified `--force-with-lease=<ref>:<expected>`.
- [x] **4.7 Signature verification** — signature status (%G? + signer) shown in the commit detail pane; Create Tag offers GPG-signed annotated tags.
