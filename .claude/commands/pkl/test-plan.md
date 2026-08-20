# /pkl:test-plan — App Test Plan

Generates a specific manual test checklist for execution in the running PickleGit app (via the `run` skill, or by the user directly).

---

## Instructions

Get the changed files:
```bash
git show --name-only --format="" HEAD
```

Based on the diff, generate a **specific** checklist — not generic steps. Derive each item from what actually changed.

| Component changed | What to test |
|------------------|-------------|
| `GitService.cs` / `Services/Git/*` | Exercise the affected git operation (stage, commit, rebase, push, etc.) against a real or scratch repo; verify `GitService.Reopen()` picks up CLI-side mutations |
| `RepositoryViewModel.*` | Open a repo tab, drive the affected flow (staging, diff, branches, remote, rebase, detail, integrations), verify UI state matches git state after the operation |
| `Views/*.xaml` + code-behind | Exercise the control: click, input, scroll, resize — check for binding errors, layout jumps, or virtualization glitches |
| `Controls/CommitGraphControl.cs` | Open a repo with branching history; scroll the graph; verify lanes/curves/ref badges render correctly and don't flicker |
| `Services/RepositoryWatcher.cs` | Make an external change (another terminal: commit, checkout, branch) while the repo tab is open; verify the UI refreshes without excessive re-renders or busy-flicker |
| `Services/Hosting/*` | Open a repo with a configured hosting remote (GitHub/GitLab/Bitbucket); verify PR list/detail loads and any new action round-trips |
| `Themes/*.xaml` / palette changes | Toggle Settings → UI → Theme at runtime; verify every affected element updates live (no frozen `StaticResource` brush) |
| `AppSettings.cs` / settings shape change | Change the setting, restart the app, verify it persisted; if the shape changed, verify an old-format `settings.json` still loads without crashing |
| Diff / merge-conflict editor | Create a real conflict or multi-hunk diff in a scratch repo; exercise stage/unstage/discard at hunk and line level; verify the result matches `git status` |
| Command palette / shortcuts | Trigger the affected command via both its shortcut and the palette; verify `CanExecute` state matches actual availability |

## Output format

```
App Test Plan — gh-<N>
================================
[ ] <specific action>: <expected result>
[ ] <specific action>: <expected result>
[ ] Restart the app: verify persisted state matches before-restart state (if settings/cache touched)
[ ] Check AppLog for unexpected warnings/errors after exercising the change
```

Present the checklist and say: *"Want me to run this via the `run` skill, or would you rather verify it yourself?"*
