# Code Review — branch `adhoc-lfs-clean-filter-staging`
Diff scope: `PickleGit/Services/GitService.cs` only (29 insertions, 0 deletions vs merge-base) — routes `StageFile`/`StageFiles`/`StageAll` through `git.exe` (`git add`) so git-lfs's clean filter runs on staged content, with fallback to `Commands.Stage` when the CLI is unavailable.

## Findings

```
[INFO] Process / CLI Invocation — security.md compliance
File: PickleGit/Services/GitService.cs:963-971 (StageCli), :915, :932, :953
Issue: `Cli.RunAsync("add " + args)` passes a single concatenated string as the git.exe argument
line rather than a discrete `string[]`/`ArgumentList`. security.md's checklist item literally reads
"arguments … passed as a discrete argument array — never concatenated into a single shell-interpreted
string."
Fix: None needed. Verified in GitCli.cs:191-203 that `ProcessStartInfo.UseShellExecute = false` and
`Arguments` is handed directly to the child process (git.exe) via Win32 CreateProcess — no cmd.exe
shell ever parses this string, so shell metacharacters (`&`, `|`, backticks, `>`) are inert. .NET
Framework 4.7.2 has no `ProcessStartInfo.ArgumentList` (added in .NET Core 2.1 only), so a
single-string `Arguments` field is the only mechanism available on this target framework — the
codebase's actual safety net is `CliGitService.Quote()`, which implements the correct
CommandLineToArgvW/MSVCRT escaping convention per argument token before concatenation (verified by
reading its implementation, CliGitService.cs:135-165), and every user-controlled value in this diff
(file paths) is individually wrapped in `Quote(...)` before being joined. This exactly matches the
pre-existing pattern in `CheckoutRefCli` (line 540), `DiscardPathsCli` (line 1842), and `CherryPick`
(line 1890-1891) — not a new or diff-introduced risk. Additionally, `--` precedes the pathspec in
both `StageFile` and `StageFiles` (StageCli receives `"-- " + Quote(...)`), so a filename that begins
with `-` cannot be misread as a flag (CWE-88 argument injection is closed off correctly).

[INFO] Threading — GetAwaiter().GetResult() on the executor thread
File: PickleGit/Services/GitService.cs:967
Issue: `StageCli` calls `Cli.RunAsync(...).GetAwaiter().GetResult()`, a synchronous blocking wait.
architecture.md/CLAUDE.md mandate that work items must never synchronously block on the Dispatcher,
and ask to confirm this isn't a UI-thread block.
Fix: None needed. Verified all three call paths — `StagingService.StageAsync`/`UnstageAsync`
(Services/Git/StagingService.cs:30-31, used for single/multi-file staging) and
`RepositoryViewModel.RunWorkAsync` (ViewModels/RepositoryViewModel.cs:2069, used for `StageAll`'s
"Staging all…" caller at RepositoryViewModel.Staging.cs:353) — route the `Action` into
`_git.Executor.RunAsync(...)` before it ever reaches `GitService.StageFile/StageFiles/StageAll`.
GitExecutor.cs's Loop() runs on one dedicated background thread and drains its queue serially, so by
the time `StageCli` executes, it is already running ON that executor thread, not the UI thread. This
is the identical pattern already used by `CheckoutRefCli` (line 540), `DiscardPathsCli` (line
1842-1843), and `CherryPick` (line 1890-1891) elsewhere in the same file — no new deadlock risk
introduced.

[INFO] Error handling consistency
File: PickleGit/Services/GitService.cs:968
Issue: `StageCli` throws `InvalidOperationException(result.ErrorText)` on failure — confirm this
matches how sibling CLI-routed methods surface errors so the UI's existing dialog plumbing displays
it correctly.
Fix: None needed. Identical to `CheckoutRefCli` (line 542) and `DiscardPathsCli` (line 1844).
Verified `RepositoryViewModel.RunWorkAsync` (RepositoryViewModel.cs:2044-2085) catches any Exception
from the executor task and reports it via the existing status/error-dialog path — this is generic
exception handling, not type-specific, so `InvalidOperationException` flows through the same as every
other CLI-routed failure in this file.

[INFO] ChunkPathsByLength budget headroom
File: PickleGit/Services/GitService.cs:931-932, :1852-1870
Issue: The plan's own checklist asks to confirm the `-- ` prefix (prepended per-batch, after chunking)
doesn't push a batch over the ~32K Windows command-line limit, since `ChunkPathsByLength`'s per-path
length sum doesn't account for the `-- ` prefix or Quote()'s added quote/escape characters.
Fix: None needed — not introduced by this diff. `PathArgBudgetChars = 20000` (line 1852) already
leaves ~12.7K chars of headroom under the ~32,767 limit, comfortably covering the few extra bytes
`"-- "` and per-path quoting add. `DiscardPathsCli` (line 1832-1848) already uses this exact same
chunk-then-prefix pattern (`"checkout HEAD -- " + string.Join(...)`) with the same budget constant,
so this diff's `StageFiles` usage (line 931-932) is consistent with pre-existing, presumably
already-validated behavior, not a new risk.

[INFO] Empty-collection edge case
File: PickleGit/Services/GitService.cs:925-936
Issue: Confirm `StageFiles` with an empty `filePaths` doesn't call `StageCli("-- ")` with nothing
after it.
Fix: None needed. `ChunkPathsByLength` (line 1853-1870) only `yield return`s a batch when
`batch.Count > 0`; for an empty input collection the foreach body never executes and the trailing
`if (batch.Count > 0)` is false, so zero batches are produced and `StageCli` is never called. No
git.exe invocation happens at all for an empty `filePaths`, which is the correct behavior (matches
`Commands.Stage(_repo, filePaths)`'s own no-op-on-empty behavior in the fallback path).

[LOW] XML doc tag nesting
File: PickleGit/Services/GitService.cs:908-911
Issue: The new `<summary>` on `StageFile` is fine, but note the pre-existing `CliGitService.Quote`
region above it in CliGitService.cs (lines 53-87) has an orphaned/duplicate `<summary>` structure
(two consecutive `/// <summary>` blocks with no member between the first and the Quote-adjacent
BuildHttpAuthEnv) — pre-existing, unrelated to this diff, noted only as an observation since it was
read in the course of this review. Not a defect in the reviewed diff itself.
Fix: N/A for this PR — mention to the team as a follow-up cleanup, not blocking.

[INFO] WPF/MVVM
File: PickleGit/Services/GitService.cs
Issue: N/A — this diff touches only GitService.cs (a Services-layer class), no ViewModel/View/XAML
boundary crossed.
Fix: N/A.

[INFO] Rendering/virtualization
Issue: N/A — no XAML/Controls changed in this diff.
Fix: N/A.

[INFO] Naming/style conventions
File: PickleGit/Services/GitService.cs:912-971
Issue: Checked against code-style.md — private method `StageCli` (PascalCase), local `args`/`result`
(camelCase), `EnsureOpen()` call retained at the top of each public method, one-line
`if (...) { ...; return; }` short-circuit blocks — all match the established style already used by
`CheckoutRefCli`/`DiscardPathsCli`/`PopStash`/`CheckoutRemoteBranch` in the same file (e.g. line 599:
`if (Cli != null && Cli.IsAvailable) { CheckoutRefCli(...); return; }`). No naming or style
violations found.
Fix: N/A.
```

## Verdict

READY TO MERGE

## Severity summary

CRITICAL: 0  HIGH: 0  MEDIUM: 0  LOW: 1  INFO: 7
