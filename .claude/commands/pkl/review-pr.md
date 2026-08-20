---
description: Activate when the user confirms changes work ("it works", "approved", "looks good", "done"), asks to review a pull request, says "review my PR", "check before I push", "is this ready to merge", "review my changes", or when workflow step verification is complete. Run a comprehensive project-specific code review.
---

# PR Review Skill

When activated, perform a structured code review applying PickleGit project standards.

## Steps

### 0. Load context (if not already in window)
If `.claude/rules/security.md` is not already loaded, read it now.
If `.claude/rules/code-style.md` is not already loaded, read it now.
If `.claude/rules/architecture.md` is not already loaded, read it now.

### 1. Get the Diff
Detect the base branch dynamically — never hardcode:
```bash
eval "$(.claude/scripts/detect-base-branch.sh)"
git diff "$MERGE_BASE"
```
This compares the working tree against the merge-base, capturing committed, staged, and unstaged changes.

### 1b. Get branch name
```bash
git rev-parse --abbrev-ref HEAD
```

### 1c. Spawn review agent
Always spawn a `general-purpose` agent with:
- The full diff
- The full checklist from step 2
- The loaded contents of `security.md`, `code-style.md`, `architecture.md`
- Instruction: write the complete findings to `.claude/reviews/<branch-name>.md` using the output format from step 3, then return only the verdict line and severity summary counts.

After the agent completes, report to the user:
```
Review written to .claude/reviews/<branch-name>.md
Verdict: <READY TO MERGE | NEEDS CHANGES | BLOCKED>
CRITICAL: X  HIGH: X  MEDIUM: X  LOW: X  INFO: X
```

### 2. Review Checklist

**Commit format**
- [ ] Subject line is a plain imperative sentence, ≤ 72 chars, no invented issue-number/type-prefix (see `commit-format.md`)
- [ ] No "WIP", "temp", or "fixup" commits in the branch

**Code style and C# compliance** (apply full checklist from `.claude/rules/code-style.md`)
- [ ] Every item in the loaded code-style.md checklist

**Security** (apply full checklist from `.claude/rules/security.md`)
- [ ] Every item in the loaded security.md checklist

**WPF / MVVM**
- [ ] No business logic in XAML code-behind — all logic in ViewModel
- [ ] `INotifyPropertyChanged` goes through `BaseViewModel.Set<T>` — no hand-rolled equality guard that duplicates it
- [ ] `ObservableCollection<T>` mutations happen on the UI thread; background work rebuilds and reassigns rather than mutating in place from a non-UI thread
- [ ] New/changed brush references in view XAML use `{DynamicResource}`, not `{StaticResource}`, if they're theme-colored
- [ ] Converters are declared in `App.xaml`, not in a Window/UserControl's own `Resources`
- [ ] No `MessageBox.Show` / VB `InputBox` introduced — use `Services/DialogService.cs`

**Threading / git backend**
- [ ] No direct `GitService`/LibGit2Sharp call from the UI thread or a bare `Task.Run` — routed through `GitService.Executor`
- [ ] Any new CLI-backed mutation (`GitService.Cli`) calls `GitService.Reopen()` afterward
- [ ] New CLI-backed features check `GitCli.IsGitAvailable` and degrade gracefully rather than throwing
- [ ] New `GitCli`/`Process.Start` argument construction uses a discrete argument list, never a concatenated shell string

**Rendering / virtualization** (only if `Controls/`, `*.xaml`, or diff/graph rendering code changed)
- [ ] New `OnRender` brush/pen/geometry allocations are cached/frozen, not allocated per call
- [ ] New virtualized lists use `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"` + `ScrollUnit="Pixel"` unless there's a documented reason not to (see `PickleGit/CLAUDE.md`'s variable-row-height caveat)
- [ ] `RelayCommand`-backed buttons whose enabled state depends on a bulk collection reassignment call `CommandManager.InvalidateRequerySuggested()` afterward

### 3. Output Format

For each finding:
```
[SEVERITY] <Category>
File: <path>:<line>
Issue: <description>
Fix: <recommended change>
```

**Severity rubric:**
- `CRITICAL` — security vulnerability, data loss, or crash risk
- `HIGH` — build break, wrong runtime behavior, or architecture violation
- `MEDIUM` — style/naming/MVVM pattern violation that could cause subtle bugs
- `LOW` — nitpick, readability, or minor convention miss
- `INFO` — observation, no action required

**Verdict:**
- `BLOCKED` — any CRITICAL finding
- `NEEDS CHANGES` — any HIGH finding, no CRITICAL
- `READY TO MERGE` — only MEDIUM/LOW/INFO findings

End with summary count by severity and the overall verdict.
