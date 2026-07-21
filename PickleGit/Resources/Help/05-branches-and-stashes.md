# Branches & Stashes

## Branches

The sidebar lists local branches, remote branches, and tags. From there you can:

- **Switch** to a branch by double-clicking it (or via its context menu).
- **Create** a new branch with the **⎇ Branch** toolbar button (`Ctrl+B`) — pick a starting point
  (usually the current commit or another branch) and a name.
- **Delete**, **rename**, or **merge** a branch via its right-click menu.

Branch reachability (which commits belong to which branch) is computed once per refresh and reused
throughout the app — by the branch filter in the commit graph, by the
[hover-highlight feature](02-commit-graph.md), and by branch-membership checks elsewhere — so
these operations stay fast even in large repositories.

## Stashes

The **📦 Stash** toolbar button stashes your current uncommitted changes (staged and unstaged),
setting your working directory back to a clean state so you can switch branches or pull without
committing half-finished work. Stashes appear in the sidebar; from there you can:

- **Apply** a stash (restore its changes, keeping the stash itself for later).
- **Pop** a stash (restore its changes and remove it from the stash list).
- **Drop** a stash (delete it without applying).
- **View** a stash's diff the same way you'd view a commit's diff.

Stashes are a good way to quickly switch context without a throwaway commit — but they're local
only and easy to forget about, so treat them as short-term, not a substitute for committing.
