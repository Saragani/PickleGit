---
status: accepted
---

# Hybrid LibGit2Sharp + git.exe backend

PickleGit needs git operations LibGit2Sharp 0.27.2 either can't do at all or does noticeably worse than git.exe: interactive rebase, `pull --rebase`, hunk/line-level staging via `git apply`, SSH remotes, and GPG signing. Rather than choosing one backend exclusively, `GitService` (LibGit2Sharp) stays the entry point for reads, status, and index operations — the fast, in-process path exercised on every refresh — and `GitService.Cli` (`Services/Git/CliGitService.cs` → `GitCli.cs`) shells out to git.exe for everything LibGit2Sharp can't cover.

## Considered Options

- **Pure LibGit2Sharp** — rejected: no interactive rebase, no SSH-agent support without bundling extra native libraries, no GPG signing.
- **Pure git.exe wrapper for everything** — rejected: loses the in-process speed of LibGit2Sharp for reads/status/history, which run on every `RepositoryWatcher`-triggered refresh; would also make git.exe a hard requirement for basic read-only functionality instead of an optional enhancement.

## Consequences

- Any CLI-backed mutation must call `GitService.Reopen()` afterward — libgit2 caches ref state internally and won't see the CLI's writes otherwise.
- Both backends must be serialized through the same `GitService.Executor` thread (`GitExecutor.cs`) to keep libgit2 and git.exe calls from interleaving unsafely against the same repository.
- CLI-backed features must check `GitCli.IsGitAvailable` and degrade gracefully (disable the menu item) rather than throw when git.exe isn't on the machine.
