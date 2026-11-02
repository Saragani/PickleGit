# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PickleGit is a WPF Git client for Windows (like GitKraken/SourceTree) built on .NET Framework 4.7.2. Single-project solution, MVVM, hybrid LibGit2Sharp + git.exe backend.

For deep implementation notes — WPF theming pitfalls, virtualization gotchas, threading rules, and a running log of hard-won bug fixes — see [PickleGit/CLAUDE.md](PickleGit/CLAUDE.md). Read it before touching `CommitGraphControl`, the theme dictionaries, diff virtualization, or anything under `Controls/`.

## Critical Restrictions
- **NEVER push to remote** — always ask user to review and push manually
- **NEVER mark tasks complete without validation** — test everything, require proof

## Session Start

When the user mentions a `gh-XXXX` issue number or says "continue" / "status" / "where were we", read [workflow.md](.claude/rules/workflow.md) and follow its **Session Resume** instructions.

No issue, no plan → skip workflow entirely, just answer.

## Hard Gate: Verification Stubs Must Pass Before Step is Marked Done

**Before marking any BUILD step `✓` — you MUST:**
Walk through every verification stub listed in that step and confirm the implementation satisfies it.
If any stub is not satisfied, fix the code first. Do not mark the step done until all stubs pass.

## Hard Gate: BUILD Step Checkpoint is BLOCKING

**After completing every BUILD step — before calling ANY tool — you MUST:**
1. Verify all stubs. If any deviation occurred — state it, get user approval, and wait before continuing.
2. Write approved deviations into the **Deviations** field ("None" if none); if it affects upcoming steps, add to `## Deviation Register`.
3. Mark the step `✓` in the plan file.
4. Output the Step Checkpoint message (format in workflow.md).
5. STOP and wait for approval signal (`ok` / `next` / `go` / `1`).

**Never start the next step in the same response that finishes the current one.**

## Hard Gate: No Code Without an Approved PLAN

**Never call `Edit`, `Write`, or `Bash` to modify any source file until BOTH:**
1. A `.plans/<issue>.md` exists for the current work
2. The user has approved it — valid signals: `proceed`, `1`, `approved`, `yes`, `go`, `ok`

**Exception — skip plan** if the user explicitly says `skip plan`, `no plan`, or `just implement it` AND the change is ≤ 2 files with an obvious, self-contained fix. Proceed directly to `pkl:create-branch` + implementation.

**Note:** Confirming a GitHub issue's description is NOT plan approval — the gate opens only after the user sees the plan summary and gives an approval signal.

## Doc Loading Rules

Everything under `.claude/rules/**` (architecture, code-style, commit-format, security, workflow, plan-type guides) auto-loads every session — no manual load step needed.

**No active workflow** (no plan, no issue) → nothing further to read.

**Active workflow** → read [workflow.md](.claude/rules/workflow.md) once for the phase-specific process (Session Resume, BUILD checkpoint, SHIP steps).

## Technology Stack

- **Language**: C# / .NET Framework 4.7.2, C# 7.3
- **UI**: WPF, MVVM — no general-purpose third-party control library; every control is stock WPF or hand-rolled, except AvalonEdit which is scoped narrowly to the merge-conflict editor's editable text pane (see `docs/adr/0003-no-third-party-controls.md`)
- **DI**: None — no MEF/IoC container. Views resolve their ViewModel via `DataTemplate` + `ContentControl`, or a parent VM exposes a child VM property bound as `DataContext`. See [architecture.md](.claude/rules/architecture.md).
- **Build**: MSBuild, single project (`PickleGit.csproj` / `PickleGit.sln`) — see [architecture.md](.claude/rules/architecture.md) for build commands

## Specialized Agents

| Subsystem | Agent | When to use |
|-----------|-------|-------------|
| WPF / MVVM / UI | `frontend-architect` | Bindings, commands, `DataTemplate` view wiring, theme resource dictionaries, virtualization |

Other domain agents (`quality-engineer`, `refactoring-expert`, `root-cause-analyst`, `security-engineer`, `system-architect`, etc.) are general-purpose and apply as usual — see the full roster in the Agent tool listing.

## Version Control

- **Repository**: GitHub (`Saragani/PickleGit`)
- **Branch naming**: `gh-XXXX-short-description` (GitHub issue number)
- **Main branch**: `main`
- **Release branches**: None currently — all work lands on `main`

---

## Domain Model

PickleGit has no database/compiler domain model — it's a thin MVVM shell over `Services/GitService.cs` (LibGit2Sharp) and `Services/Git/CliGitService.cs` (git.exe for operations LibGit2Sharp can't do). One `RepositoryViewModel` per open tab holds that repo's commits, branches, diff, and staging state.

See [architecture.md](.claude/rules/architecture.md) for the solution layout, the hybrid git backend, and the threading model. See [PickleGit/CLAUDE.md](PickleGit/CLAUDE.md) for implementation-level conventions and known WPF pitfalls.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

Setup after cloning — `graphify-out/graph.json` is committed, so `query`/`path`/`explain` and the PreToolUse hook work with no extra step. Local state and caches are git-ignored and rebuilt on demand:
1. Install graphify: `uv tool install graphifyy` (or `pipx install graphifyy`). Without it, `.claude/hooks/graphify-guard.sh` silently does nothing.
2. Optional — rebuild the AST cache and local state (free, no LLM): `graphify update .`
3. Optional — keep the graph current automatically: `graphify hook install` (post-commit/post-checkout rebuild hooks, plus a union merge driver for `graph.json` so branches that both touch it don't conflict).

The semantic cache for docs and screenshots (`graphify-out/cache/semantic`) is not rebuilt by any of these. Only a full `/graphify .` run in Claude Code recreates it (~1.1M tokens for this corpus). It isn't needed for normal use: doc/image nodes are already in the committed `graph.json`; it only matters when re-running full extraction after docs change.
