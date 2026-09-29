# PickleGit v1.0.24

A stability and polish release — no new headline feature, just 28 commits closing out crashes, two Bitbucket authentication bugs chased end-to-end, a run of merge-conflict-editor fixes, and smaller papercuts across the commit graph, diff view, blame view, and modal dialogs.

## Fixed

**Crashes**
- Fixed a crash (`ArgumentOutOfRangeException`) when selecting text on the empty side of a side-by-side diff pair (a pure addition/deletion row).
- Fixed an intermittent `NullReferenceException` caused by a shared cancellation-token race when an operation (e.g. Commit) started while another (e.g. Push) was still running.
- Fixed a related race where a reentrant operation could capture the wrong operation's cancellation token, potentially cancelling — or failing to cancel — the wrong git process.

**Bitbucket authentication**
- Fixed Bitbucket push/pull/fetch getting stuck re-prompting for a password it wouldn't accept: the OAuth-capable Git Credential Manager lookup was being skipped after a credential rejection, along with two lookups that should have been.
- Fixed Bitbucket rejecting valid, freshly-refreshed OAuth tokens: they were being sent as `Authorization: Basic` instead of the `Bearer` Bitbucket requires. Push/pull/fetch now detect a configured native credential helper and let `git.exe` negotiate the auth scheme itself, exactly like a plain terminal `git push` already does.

**Merge conflict editor**
- Fixed the merge commit message silently losing its resolved-file list when clicking Continue (git's default `--cleanup=strip` was stripping the `# Conflicts:` block).
- Fixed "Select All Mine/Theirs" silently reverting with no effect on a conflict block whose target side has zero lines.
- Fixed ligatures (e.g. `!=`) rendering only in the manual-edit pane, inconsistent with the read-only panes.
- Fixed an open conflict resolution (or diff/file selection) getting silently replaced by the Staged/Unstaged view on an unrelated background refresh.
- Replaced the manual-edit pane's find UI (previously a separate, unthemed popup) with the same themed find bar the other three panes use — Ctrl+F/Esc, live match highlighting, and Next/Prev navigation now match everywhere in the editor.

**Commit graph, list & sidebar**
- Fixed the "Uncommitted changes" row drawing a connecting line into history as if it were already committed.
- Fixed the commit list's DATE/TIME column showing each commit's own recorded UTC offset instead of local time, and sorting by author date (which a rebase/cherry-pick/amend can leave far from a commit's real position) instead of committer date.
- Fixed stash rows in the sidebar showing extra blank lines under the title, and not responding to a left-click selection.
- Added a tooltip showing the full branch name when the sidebar truncates it.

**Diff, blame & dialogs**
- Added Ctrl+G (go to line) to the diff and blame views.
- Fixed hunk navigation scrolling the target hunk to the bottom of the viewport instead of the top.
- Fixed long dialog content (confirm/prompt/error dialogs) potentially pushing the OK/Cancel buttons off-screen, including on a secondary monitor.
- Fixed icon glyphs rendering as blank space on Windows Server 2019/2022 and other machines without Windows 11's Segoe Fluent Icons font.

## Internal

- Closed out several small code-quality findings from a project-wide review: added logging to two previously-silent exception handlers, de-duplicated a selection-sync helper, and parallelized a per-remote credential check during "Fetch All Remotes".

---
*Full diff: [`PickleGit_1.0.23...HEAD`](../../compare/PickleGit_1.0.23...HEAD)*
