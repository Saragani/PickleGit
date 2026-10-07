---
issue: adhoc-primary-remote-selection
title: Use origin (not first-alphabetical remote) for fetch/pull/push
type: bug
component: RepositoryViewModel (remote operations)
phase: BUILD
step: 2
next: BUILD complete — SHIP when ready
run_mode: manual
updated: 2026-10-07 19:38
---

# Plan: adhoc-primary-remote-selection — Use origin (not first-alphabetical remote) for fetch/pull/push

## Spec
### Problem Statement
In repo `unilogicDev`, PickleGit shows the credentials dialog on Fetch although SourceTree and
`git fetch` work. The repo has two remotes: `master_braid_…uni_errors_h` (URL is a local path under
`~/.braid/cache`) and `origin` (Bitbucket HTTPS). Single-remote Fetch/Pull/Push use
`Remotes.FirstOrDefault()`, which returns the braid remote. `git config --get-urlmatch
credential.helper <local path>` exits 128, so `HasConfiguredCredentialHelperAsync` is false and
PickleGit falls into its manual credential path (dialog) instead of git's Credential Manager.

### Requirements
- FR1: Add `RepositoryViewModel.PrimaryRemote` returning the remote named `origin`, else the first remote, else null.
- FR2: Replace every `Remotes.FirstOrDefault()` used to pick the remote for fetch/pull/push/tag-push/tag-delete/credential save/purge with `PrimaryRemote`.

### Acceptance Criteria
- AC1: Given unilogicDev, When Fetch is clicked, Then no credentials dialog appears and fetch targets `origin` (log shows `git fetch origin`). [derived]
- AC2: Given a repo whose only remote is not named origin, When Fetch is clicked, Then that remote is used. [derived]

## Constraints
- C# 7.3 / .NET 4.7.2.
- Fetch All path (`allRemotes`) iterates all remotes and stays unchanged.

## Tests
### Manual / Black-Box
- [ ] Open unilogicDev, click Fetch → no dialog, log shows `fetch "origin"`  <!-- AC1 -->
- [ ] Open a single-remote repo with non-origin name, Fetch → works  <!-- AC2 -->

## Steps
### Step 1: Add PrimaryRemote helper ✓
**What**: Add `public RemoteInfo PrimaryRemote => Remotes?.FirstOrDefault(r => r.Name == "origin") ?? Remotes?.FirstOrDefault();` next to `Remotes`.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Project builds  (manual)
**Risk**: Property not re-raised when `Remotes` changes — it is only read at operation time, so None in practice.
**Deviations**: None

### Step 2: Use PrimaryRemote at all call sites ✓
**What**: Replace `Remotes.FirstOrDefault()` with `PrimaryRemote` in `RepositoryViewModel.Remote.cs` (lines ~157, 346, 368, 424, 476, 668, 784), `RepositoryViewModel.Branches.cs` (~294, 339) and `RepositoryViewModel.cs` (~2146). Leave HostingViewModel (already prefers origin) and Fetch-All untouched.
**Touches**: `PickleGit/ViewModels/RepositoryViewModel.Remote.cs`, `PickleGit/ViewModels/RepositoryViewModel.Branches.cs`, `PickleGit/ViewModels/RepositoryViewModel.cs`
**verification stubs** *(verify each before marking step ✓)*:
- [x] Build succeeds  (manual)
- [x] unilogicDev Fetch shows no credentials dialog; log shows fetch of origin  (manual)
**Risk**: Behavior change for repos whose first remote was intentionally non-origin.
**Mitigation**: Falls back to first remote when no `origin` exists.
**Deviations**: None

## After Implementation
None.

## Deviation Register

## Handoff
