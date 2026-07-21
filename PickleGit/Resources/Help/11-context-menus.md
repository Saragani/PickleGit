# Context Menus

Right-clicking almost anything in PickleGit opens a context menu with actions specific to what you
clicked. This page is a reference for what each one does.

## Tabs

Right-click a repository tab:

- **Close** / **Close others** / **Close tabs to the right** / **Close all** — the usual tab
  cleanup actions.
- **Copy repository path** — copies the repo's local folder path to the clipboard.
- **Open in Explorer** — opens the repo's folder in Windows Explorer.
- **Open terminal here** — opens Windows Terminal (or a plain `cmd` window if Windows Terminal
  isn't installed) at the repo's folder.

## Commit graph rows

Right-click a commit in the [commit graph](02-commit-graph.md):

- **Checkout commit (detached)** — check out that exact commit without being on a branch (see the
  "detached HEAD" warning banner that appears afterward).
- **Create branch here…** / **Create tag here…** — start a new branch/tag pointing at this commit.
- **Revert commit** — create a new commit that undoes this commit's changes (safe for commits
  already pushed/shared, unlike editing history).
- **Reset current branch to here** — moves the current branch pointer back to this commit, with
  three sub-options for what happens to the commits/changes in between:
  - **Soft** — keeps those changes staged, ready to re-commit.
  - **Mixed** — keeps those changes in the working directory, unstaged.
  - **Hard** — discards them entirely. This is destructive; PickleGit confirms before proceeding.
- **Rebase current branch onto here** / **Interactive rebase current branch onto here…** — replay
  the current branch's commits on top of this one; interactive rebase lets you reorder, squash, or
  edit commits along the way.
- **Start bisect: mark bad** / **Mark as good (start bisect from here)** — begin a
  [bisect](08-history-tools.md) session using this commit as one endpoint.
- **Copy SHA** / **Copy commit message** — clipboard shortcuts.
- **Save as patch…** — export this commit as a `.patch` file you can share or apply elsewhere.

## Branches (sidebar)

Right-click a **local branch**:

- **Checkout** — switch to this branch.
- **Create Pull Request…** — see [Pull Requests](07-pull-requests.md).
- **Fetch this branch** — fetch just this branch's remote-tracking updates.
- **Diff against current branch** — compare this branch with whatever you're currently on (see
  [History Tools](08-history-tools.md)).
- **Merge into current** / **…(no fast-forward)** / **…(fast-forward only)** / **Squash-merge into
  current** — bring this branch's changes into your current branch; the variants control whether a
  merge commit is always created (no fast-forward), only allowed if it's a clean fast-forward, or
  whether all of the branch's commits are collapsed into a single new commit (squash).
- **Rebase current branch onto this** / **Interactive rebase…** — same as the graph's rebase
  actions, using this branch as the target.
- **Rename…** / **Copy branch name** / **Delete** — the usual housekeeping (delete asks for
  confirmation, and warns if the branch isn't fully merged).

Right-click a **remote branch**: **Checkout as local branch**, **Copy branch name**, **Delete from
remote…** (this deletes it on the server, not just locally — used carefully).

## Tags

Right-click a tag: **Checkout (detached)**, **Push to remote**, **Delete** (local),
**Delete on remote…**.

## Stashes

Right-click a stash: **Apply (keep stash)**, **Pop (apply and remove)**, **Drop** — see
[Branches & Stashes](05-branches-and-stashes.md).

## Remotes

Right-click a remote: **Edit URL…**, **Add remote…**, **Remove**. Right-click a remote branch:
**Open repository in browser**, **Add remote…**, **Refresh**, **Create Pull Request…**.

## Submodules & worktrees

Right-click a submodule: **Init and update all**, **Add submodule…**, **Init / update**,
**Open as tab**, **Sync URL and update**, **Deinit…**. Right-click a worktree: **Add worktree…**,
**Open as tab**, **Remove**.

## Files (commit details / staging area)

Right-click a file in a commit's file list or the working-directory staging view:

- **Stage** / **Unstage** — only shown for the applicable side (unstaged files offer Stage, staged
  files offer Unstage).
- **Discard changes** — revert the file to its last-committed state. Destructive; confirmed first.
- **Stash selected files…** — stash just the selected file(s) instead of everything.
- **Add to .gitignore** — add this file's path (or pattern) to `.gitignore`.
- **Track with Git LFS…** — start tracking this file (or its extension) with Git LFS, for large
  binary files that shouldn't be stored directly in git history.
- **Open** / **Reveal in Explorer** / **Copy path** — the usual file shortcuts.
- **View File History** — see every commit that touched this file, across the whole repository.
- **Blame** — see who last changed each line of the file and in which commit.
