# Syncing (Fetch, Pull, Push)

The middle of the toolbar has three sync buttons, each with a small dropdown arrow for extra
options:

- **Fetch** (`Ctrl+Shift+F`) — downloads new commits/refs from the remote without touching your
  working directory or current branch. Use this to see what's changed upstream before deciding
  what to do about it.
- **Pull** (`Ctrl+Shift+L`) — fetches and then integrates the remote branch into your current
  branch (merge by default; the dropdown lets you pull with rebase instead, when git.exe is
  available).
- **Push** (`Ctrl+Shift+P`) — uploads your local commits to the remote. The dropdown offers
  force-push and push-with-lease variants for the cases where a normal push is rejected because
  history diverged — use these carefully, since they can overwrite remote commits.

Each dropdown also lets you target a specific remote/branch instead of the current branch's
default upstream, which is useful when a local branch tracks a different name on the remote, or
when you want to push to a fork.

If a fetch/pull/push fails because of authentication, PickleGit will prompt for credentials and can
store them for next time (see [Settings](09-settings.md) → Integrations for configured hosts and
tokens).
