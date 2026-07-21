using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PickleGit.Models;
using PickleGit.Services;
using PickleGit.Services.Git;

namespace PickleGit.ViewModels
{
    /// <summary>Git LFS, submodules and worktrees.</summary>
    public partial class RepositoryViewModel
    {
        private async Task TrackWithLfsAsync(FileChange fc)
        {
            if (fc == null) return;
            var ext = Path.GetExtension(fc.Path);
            var suggested = string.IsNullOrEmpty(ext) ? fc.Path : "*" + ext;
            var pattern = DialogService.Prompt("Track with Git LFS",
                "Glob pattern to track with Git LFS (added to .gitattributes):", suggested);
            if (string.IsNullOrWhiteSpace(pattern)) return;

            if (!await RunCliAsync($"Tracking {pattern} with Git LFS…",
                    $"lfs track {CliGitService.Quote(pattern)}", "Git LFS"))
                return;
            await _git.Executor.RunAsync(() => _git.StageFile(".gitattributes"));
            await LoadWorkingDirAsync();
        }

        /// <summary>Best-effort re-smudge of Git LFS pointer files after a checkout, using objects already
        /// present in the local LFS cache (no network fetch — that's `git lfs pull`). LibGit2Sharp checkout
        /// has no awareness of git-lfs's smudge filter, so a libgit2 checkout leaves LFS-tracked files as
        /// raw pointer text; git-lfs missing, or the repo not using LFS at all, both degrade silently here —
        /// this must never surface an error dialog for a plain non-LFS repo.</summary>
        private async Task TryLfsCheckoutAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) return;
            try { await _git.Cli.RunAsync("lfs checkout"); }
            catch { /* best-effort only — must never affect the checkout that already succeeded */ }
        }

        // ── Submodules ───────────────────────────────────────────────────────

        public async Task LoadSubmodulesAsync()
        {
            if (!_git.IsOpen) return;
            var list = await _git.Executor.RunAsync(() => _git.GetSubmodules());
            Submodules = new ObservableCollection<SubmoduleInfo>(list);
        }

        private async Task InitAllSubmodulesAsync()
        {
            if (await RunCliAsync("Initializing submodules…", "submodule update --init --recursive", "Submodules"))
                await LoadSubmodulesAsync();
        }

        private async Task UpdateSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (await RunCliAsync($"Updating {sm.Name}…",
                    $"submodule update --init -- {CliGitService.Quote(sm.Path)}", "Submodule update"))
                await LoadSubmodulesAsync();
        }

        /// <summary>`git submodule add &lt;url&gt; [path]` — path defaults to the repo name.</summary>
        private async Task AddSubmoduleAsync()
        {
            var url = DialogService.Prompt("Add Submodule", "Repository URL of the submodule:", "", "Next");
            if (string.IsNullOrWhiteSpace(url)) return;
            var defaultPath = Path.GetFileNameWithoutExtension(url.TrimEnd('/'));
            var path = DialogService.Prompt("Add Submodule", "Path inside this repository:", defaultPath ?? "", "Add");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (await RunCliAsync($"Adding submodule {path}…",
                    $"submodule add {CliGitService.Quote(url.Trim())} {CliGitService.Quote(path.Trim())}",
                    "Add submodule"))
            {
                await LoadSubmodulesAsync();
                await LoadWorkingDirAsync();
                await RefreshAsync();
            }
        }

        /// <summary>`git submodule sync` then update — re-reads the URL from .gitmodules.</summary>
        private async Task SyncSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (!await RunCliAsync($"Syncing {sm.Name}…",
                    $"submodule sync -- {CliGitService.Quote(sm.Path)}", "Submodule sync"))
                return;
            if (await RunCliAsync($"Updating {sm.Name}…",
                    $"submodule update --init -- {CliGitService.Quote(sm.Path)}", "Submodule update"))
                await LoadSubmodulesAsync();
        }

        private async Task DeinitSubmoduleAsync(SubmoduleInfo sm)
        {
            if (sm == null) return;
            if (!DialogService.Confirm("Deinit Submodule",
                    $"Deinitialize '{sm.Name}'?\n\nIts working directory content is removed (the entry stays in .gitmodules; local changes inside it are lost).",
                    "Deinit", danger: true))
                return;
            if (await RunCliAsync($"Deinitializing {sm.Name}…",
                    $"submodule deinit -f -- {CliGitService.Quote(sm.Path)}", "Submodule deinit"))
                await LoadSubmodulesAsync();
        }

        // ── Worktrees ────────────────────────────────────────────────────────

        public async Task LoadWorktreesAsync()
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable) { Worktrees = new ObservableCollection<WorktreeInfo>(); return; }
            var result = await _git.Cli.RunAsync("worktree list --porcelain");
            Worktrees = new ObservableCollection<WorktreeInfo>(result.Success
                ? WorktreeService.ParsePorcelain(result.StdOut)
                : Enumerable.Empty<WorktreeInfo>());
        }

        private async Task AddWorktreeAsync()
        {
            var branch = DialogService.Prompt("Add Worktree", "Branch name for the new worktree:", "");
            if (string.IsNullOrWhiteSpace(branch)) return;
            var parentDir = Path.GetDirectoryName(RepoPath.TrimEnd('\\', '/'));
            var suggested = Path.Combine(parentDir ?? RepoPath, $"{RepoName}-{branch.Replace('/', '-')}");
            var path = DialogService.Prompt("Add Worktree", "Path for the new worktree:", suggested);
            if (string.IsNullOrWhiteSpace(path)) return;

            var branchExists = LocalBranches.Any(b => string.Equals(b.Name, branch, StringComparison.OrdinalIgnoreCase));
            var args = WorktreeService.BuildAddArgs(path, branch, branchExists);

            if (await RunCliAsync($"Adding worktree at {path}…", args, "Add worktree"))
                await LoadWorktreesAsync();
        }

        private async Task RemoveWorktreeAsync(WorktreeInfo wt)
        {
            if (wt == null || wt.IsMain) return;
            if (!DialogService.Confirm("Remove Worktree",
                    $"Remove the worktree at '{wt.Path}'? This deletes its working directory.", "Remove"))
                return;
            if (await RunCliAsync($"Removing worktree {wt.Name}…",
                    WorktreeService.BuildRemoveArgs(wt.Path), "Remove worktree"))
                await LoadWorktreesAsync();
        }
    }
}
