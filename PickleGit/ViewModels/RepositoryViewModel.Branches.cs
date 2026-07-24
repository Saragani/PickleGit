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
    /// <summary>Branch, tag, reflog and remote-management operations (incl. merge + drag-and-drop).</summary>
    public partial class RepositoryViewModel
    {
        // ── Branch operations ─────────────────────────────────────────────────

        private async void CreateBranch(object param)
        {
            // "here" = context-menu invocation → branch from the selected commit, not HEAD
            var startPoint = (param as string) == "here" ? SelectedNode?.Commit?.Sha : null;
            if (string.IsNullOrEmpty(startPoint)) startPoint = null;
            var dlg = new Views.NewBranchDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                var name = dlg.BranchName;
                // Every other git GUI (and git checkout -b) switches to a newly created branch
                // immediately — staying on the old branch meant new commits (and a PR) could
                // silently land on the wrong branch since nothing else in the UI signals that
                // the "new" branch was created but never actually checked out.
                var (preHead, preBranch) = await CaptureCheckoutStateAsync();
                if (await RunThenRefreshCheckout($"Creating branch {name}…",
                        () => { _git.CreateBranch(name, startPoint); _git.Checkout(name); }))
                    await CaptureCheckoutUndo($"Create branch '{name}'", preHead, preBranch);
            }
        }

        private void RenameBranch(object param)
        {
            if (!(param is BranchInfo bi) || bi.IsRemote) return;
            var newName = DialogService.Prompt("Rename Branch", $"New name for '{bi.Name}':", bi.Name, "Rename");
            if (string.IsNullOrWhiteSpace(newName) || newName == bi.Name) return;
            _ = RunThenRefresh($"Renaming {bi.Name} → {newName}…", () => _git.RenameBranch(bi.Name, newName));
        }

        private void CheckoutRemoteBranch(object param)
        {
            if (!(param is BranchInfo bi) || !bi.IsRemote) return;
            _ = RunThenRefreshCheckout($"Checking out {bi.Name}…", () => _git.CheckoutRemoteBranch(bi.Name));
        }

        private async void CheckoutCommit(object _)
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (string.IsNullOrEmpty(sha)) return;
            if (!DialogService.Confirm("Checkout Commit",
                    $"Check out commit {sha.Substring(0, 7)} in detached HEAD state?\n\n" +
                    "You will not be on any branch — create a branch here if you plan to make commits.",
                    "Checkout"))
                return;
            var (preHead, preBranch) = await CaptureCheckoutStateAsync();
            if (await RunThenRefreshCheckout($"Checking out {sha.Substring(0, 7)}…", () => _git.CheckoutCommit(sha)))
                await CaptureCheckoutUndo($"Checkout {sha.Substring(0, 7)} (detached)", preHead, preBranch);
        }

        private async void ResetToCommit(object param)
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (string.IsNullOrEmpty(sha)) return;
            var mode = param as string ?? "mixed";
            string effect;
            switch (mode)
            {
                case "soft": effect = "Commits after it become staged changes."; break;
                case "hard": effect = "ALL local changes and commits after it are DISCARDED."; break;
                default:     effect = "Commits after it become unstaged changes."; break;
            }
            if (!DialogService.Confirm($"Reset ({mode})",
                    $"Reset '{CurrentBranch}' to {sha.Substring(0, 7)}?\n\n{effect}",
                    "Reset", danger: mode == "hard"))
                return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (await RunThenRefreshWorkingDir($"Resetting to {sha.Substring(0, 7)} ({mode})…",
                    () => _git.ResetTo(sha, mode)))
                await CaptureUndo(UndoKind.HeadHardMove, $"Reset to {sha.Substring(0, 7)} ({mode})", preHead);
        }

        private async void RevertCommit(object _)
        {
            var sha = SelectedNode?.Commit?.Sha;
            if (string.IsNullOrEmpty(sha)) return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (await RunThenRefreshWorkingDir($"Reverting {sha.Substring(0, 7)}…", () => _git.Revert(sha)))
                await CaptureUndo(UndoKind.HeadHardMove, $"Revert {sha.Substring(0, 7)}", preHead);
        }

        private static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }

        private async void CheckoutBranch(object param)
        {
            if (!(param is BranchInfo bi)) return;
            if (bi.IsHead) return;
            var (preHead, preBranch) = await CaptureCheckoutStateAsync();
            if (await RunThenRefreshCheckout($"Checking out {bi.Name}…", () => _git.Checkout(bi.Name)))
                await CaptureCheckoutUndo($"Checkout '{bi.Name}'", preHead, preBranch);
        }

        private async void DeleteBranch(object param)
        {
            if (!(param is BranchInfo bi)) return;
            bool merged;
            try { merged = await _git.Executor.RunAsync(() => _git.IsBranchMerged(bi.Name)); }
            catch { merged = true; }
            var message = merged
                ? $"Delete branch '{bi.Name}'?"
                : $"Branch '{bi.Name}' is NOT fully merged into the current branch.\n\n" +
                  "Deleting it may lose commits. Delete anyway?";
            if (DialogService.Confirm("Delete Branch", message, "Delete", danger: true))
            {
                if (await RunThenRefresh($"Deleting branch {bi.Name}…",
                        () => _git.DeleteBranch(bi.Name, force: !merged)))
                {
                    LastUndo = new UndoEntry
                    {
                        Kind = UndoKind.BranchDelete,
                        Description = $"Delete branch '{bi.Name}'",
                        BranchName = bi.Name,
                        Sha = bi.TipSha
                    };
                }
            }
        }

        private async void MergeBranch(object param) => await MergeBranchCoreAsync(param, GitMergeMode.Default);
        private async void MergeBranchNoFf(object param) => await MergeBranchCoreAsync(param, GitMergeMode.NoFastForward);
        private async void MergeBranchFfOnly(object param) => await MergeBranchCoreAsync(param, GitMergeMode.FastForwardOnly);

        private async Task MergeBranchCoreAsync(object param, GitMergeMode mode)
        {
            if (!(param is BranchInfo bi)) return;
            if (!EnsureIdentity()) return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (await RunThenRefresh($"Merging {bi.Name}…",
                    () => _git.Merge(bi.Name, AuthorName ?? "User", AuthorEmail ?? "user@example.com", mode)))
                await CaptureUndo(UndoKind.HeadHardMove, $"Merge '{bi.Name}'", preHead);
            await LoadWorkingDirAsync();
        }

        /// <summary>`git merge --squash` — stages the squashed result; the user reviews and commits it.</summary>
        private async void MergeBranchSquash(object param)
        {
            if (!(param is BranchInfo bi)) return;
            if (await RunCliAsync($"Squash-merging {bi.Name}…", $"merge --squash {CliGitService.Quote(bi.Name)}",
                    "Squash merge"))
            {
                StatusMessage = $"Squash merge of '{bi.Name}' staged — review and commit.";
                await RefreshAsync();
                await LoadWorkingDirAsync();
            }
        }

        // ── Drag-and-drop branch/commit operations (SidebarView/CommitListView code-behind) ──

        /// <summary>Checks out <paramref name="branchName"/> first if it isn't already HEAD.</summary>
        private async Task<bool> EnsureCheckedOutAsync(string branchName)
        {
            if (string.Equals(CurrentBranch, branchName, StringComparison.Ordinal))
                return true;
            var (preHead, preBranch) = await CaptureCheckoutStateAsync();
            if (!await RunThenRefreshCheckout($"Checking out {branchName}…", () => _git.Checkout(branchName)))
                return false;
            await CaptureCheckoutUndo($"Checkout '{branchName}'", preHead, preBranch);
            return true;
        }

        /// <summary>Drag branch A onto branch B, "Merge into" choice: checkout B, merge A into it.</summary>
        public async void DragMergeBranches(string sourceBranch, string targetBranch)
        {
            if (!EnsureIdentity()) return;
            if (!DialogService.Confirm("Merge",
                    $"Checkout '{targetBranch}' and merge '{sourceBranch}' into it?", "Merge"))
                return;
            if (!await EnsureCheckedOutAsync(targetBranch)) return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            if (await RunThenRefresh($"Merging {sourceBranch}…",
                    () => _git.Merge(sourceBranch, AuthorName ?? "User", AuthorEmail ?? "user@example.com")))
                await CaptureUndo(UndoKind.HeadHardMove, $"Merge '{sourceBranch}'", preHead);
            await LoadWorkingDirAsync();
        }

        /// <summary>Drag branch A onto branch B, "Rebase onto" choice: checkout B, rebase it onto A.</summary>
        public async void DragRebaseBranch(string targetBranch, string ontoBranch)
        {
            if (!await EnsureCheckedOutAsync(targetBranch)) return;
            await RebaseOntoAsync(ontoBranch, ontoBranch);
        }

        /// <summary>Drag a commit onto a branch: checkout that branch, cherry-pick the commit onto it.</summary>
        public async void DragCherryPick(string sha, string targetBranch)
        {
            var shortSha = sha.Substring(0, Math.Min(7, sha.Length));
            if (!DialogService.Confirm("Cherry-pick",
                    $"Checkout '{targetBranch}' and cherry-pick {shortSha} onto it?", "Cherry-pick"))
                return;
            if (!await EnsureCheckedOutAsync(targetBranch)) return;
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunAsync($"Cherry-picking {shortSha}…", () => _git.CherryPick(sha));
            if (ok) await CaptureUndo(UndoKind.HeadHardMove, $"Cherry-pick {shortSha}", preHead);
            await LoadWorkingDirAsync();
            await RefreshAsync();
        }

        private async void CreateTag(object param)
        {
            // "here" = context-menu invocation → tag the selected commit, not HEAD
            var sha = (param as string) == "here" ? SelectedNode?.Commit?.Sha : null;
            if (string.IsNullOrEmpty(sha)) sha = null;
            var canSign = _git.Cli != null && _git.Cli.IsAvailable;
            var dlg = new Views.Dialogs.TextPromptDialog
            {
                Owner = Application.Current.MainWindow,
                DialogTitle = "Create Tag",
                HeaderText = "Create Tag",
                PromptText = "Tag name:",
                InputText = string.Empty,
                OkText = "Create",
                RequireInput = true,
                CheckboxText = canSign ? "Sign tag with GPG (annotated, requires user.signingkey)" : null
            };
            if (dlg.ShowDialog() != true) return;
            var name = dlg.InputText?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (canSign && dlg.IsCheckboxChecked)
            {
                // Signed tags must be annotated; use the tag name as the message so no editor opens
                var shaArg = sha != null ? " " + CliGitService.Quote(sha) : "";
                if (await RunCliAsync($"Creating signed tag {name}…",
                        $"tag -s -m {CliGitService.Quote(name)} {CliGitService.Quote(name)}{shaArg}", "Signed tag"))
                    await RefreshAsync();
            }
            else
            {
                await RunThenRefresh($"Creating tag {name}…", () => _git.CreateTag(name, sha));
            }
        }

        private void DeleteTag(object param)
        {
            if (!(param is TagInfo ti)) return;
            if (DialogService.Confirm("Delete Tag", $"Delete tag '{ti.Name}'?\n(Only locally — the remote keeps it unless you push the deletion.)",
                    "Delete", danger: true))
                _ = RunThenRefresh($"Deleting tag {ti.Name}…", () => _git.DeleteTag(ti.Name));
        }

        private async void DeleteTagOnRemote(object param)
        {
            if (!(param is TagInfo ti)) return;
            var remoteName = Remotes.FirstOrDefault()?.Name ?? "origin";
            if (!DialogService.Confirm("Delete Tag on Remote",
                    $"Delete tag '{ti.Name}' from '{remoteName}'?\n(The local tag is kept.)",
                    "Delete", danger: true))
                return;
            if (await RunCliAsync($"Deleting tag {ti.Name} on {remoteName}…",
                    $"push {CliGitService.Quote(remoteName)} --delete {CliGitService.Quote("refs/tags/" + ti.Name)}",
                    "Delete remote tag"))
                await RefreshAsync();
        }

        /// <summary>Deletes the branch on its remote (`git push &lt;remote&gt; --delete &lt;branch&gt;`).</summary>
        private async void DeleteRemoteBranch(object param)
        {
            if (!(param is BranchInfo bi) || !bi.IsRemote) return;
            // Remote branch names come through as "<remote>/<branch>"
            var slash = bi.Name.IndexOf('/');
            if (slash <= 0 || slash >= bi.Name.Length - 1) return;
            var remoteName = bi.Name.Substring(0, slash);
            var branchName = bi.Name.Substring(slash + 1);
            if (!DialogService.Confirm("Delete Remote Branch",
                    $"Delete '{branchName}' on '{remoteName}'?\n\nThis removes the branch for everyone using that remote.",
                    "Delete", danger: true))
                return;
            if (await RunCliAsync($"Deleting {branchName} on {remoteName}…",
                    $"push {CliGitService.Quote(remoteName)} --delete {CliGitService.Quote(branchName)}",
                    "Delete remote branch"))
                await RefreshAsync();
        }

        private async void CheckoutTag(object param)
        {
            if (!(param is TagInfo ti)) return;
            if (!DialogService.Confirm("Checkout Tag",
                    $"Check out tag '{ti.Name}' in detached HEAD state?",
                    "Checkout"))
                return;
            var (preHead, preBranch) = await CaptureCheckoutStateAsync();
            if (await RunThenRefreshCheckout($"Checking out {ti.Name}…", () => _git.CheckoutTag(ti.Name)))
                await CaptureCheckoutUndo($"Checkout tag '{ti.Name}'", preHead, preBranch);
        }

        private async Task PushTagAsync(object param)
        {
            if (!(param is TagInfo ti)) return;
            var remoteName = Remotes.FirstOrDefault()?.Name ?? "origin";
            if (GitCli.IsSshUrl(Remotes.FirstOrDefault()?.Url))
            {
                await RunCliAsync($"Pushing tag {ti.Name} to {remoteName}…",
                    $"push {CliGitService.Quote(remoteName)} {CliGitService.Quote("refs/tags/" + ti.Name)}", "Push tag");
                return;
            }
            if (!await EnsureCredentialsAsync()) return;
            var ok = await RunAsync($"Pushing tag {ti.Name} to {remoteName}…",
                () => _git.PushTag(ti.Name, remoteName, RemoteUsername, RemotePassword));
            if (ok && _credentialsFromDialog) SaveCredentials();
        }

        // ── Remote management ─────────────────────────────────────────────────

        private void AddRemote(object _)
        {
            var dlg = new Views.Dialogs.RemoteDialog
            {
                Owner = Application.Current.MainWindow,
                DialogTitle = "Add Remote",
                HeaderText = "Add Remote",
                OkText = "Add",
                RemoteName = Remotes.Count == 0 ? "origin" : string.Empty
            };
            if (dlg.ShowDialog() == true)
                _ = RunThenRefresh($"Adding remote {dlg.RemoteName}…",
                    () => _git.AddRemote(dlg.RemoteName, dlg.RemoteUrl));
        }

        private void EditRemote(object param)
        {
            if (!(param is RemoteInfo ri)) return;
            var dlg = new Views.Dialogs.RemoteDialog
            {
                Owner = Application.Current.MainWindow,
                DialogTitle = "Edit Remote",
                HeaderText = $"Edit Remote '{ri.Name}'",
                OkText = "Save",
                RemoteName = ri.Name,
                RemoteUrl = ri.Url,
                IsNameEditable = false
            };
            if (dlg.ShowDialog() == true)
                _ = RunThenRefresh($"Updating remote {ri.Name}…",
                    () => _git.UpdateRemote(ri.Name, dlg.RemoteUrl));
        }

        private void RemoveRemote(object param)
        {
            if (!(param is RemoteInfo ri)) return;
            if (DialogService.Confirm("Remove Remote",
                    $"Remove remote '{ri.Name}' ({ri.Url})?\nIts remote-tracking branches will be removed locally.",
                    "Remove", danger: true))
                _ = RunThenRefresh($"Removing remote {ri.Name}…", () => _git.RemoveRemote(ri.Name));
        }
    }
}
