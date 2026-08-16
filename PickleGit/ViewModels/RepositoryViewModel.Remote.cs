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
    /// <summary>Remote transfer operations: fetch, pull, push, credentials, CLI helpers.</summary>
    public partial class RepositoryViewModel
    {
        // ── Remote operations ─────────────────────────────────────────────────

        /// <summary>Re-reads HEAD directly from disk right before a fetch/pull/push, in case an
        /// external change — another tab open on the same repo, or a git command run outside the
        /// app — moved the current branch since the last refresh. Acting on a stale cached
        /// <see cref="CurrentBranch"/> here could pull into, or push, the wrong branch.
        /// <see cref="GitService.Reopen"/> forces a fresh libgit2 handle so the read can't be
        /// served from an in-memory ref cache that predates the external change.</summary>
        private async Task<string> RefreshCurrentBranchAsync()
        {
            var branch = await _git.Executor.RunAsync(() =>
            {
                _git.Reopen();
                return _git.GetCurrentBranch();
            });
            if (branch != CurrentBranch)
                CurrentBranch = branch;
            return branch;
        }

        // A fetch never touches the working tree, but it CAN introduce brand-new commit objects
        // reachable from a branch tip that just moved — including a non-checked-out branch's tip
        // (that's the whole point of a fetch) — and by default also auto-follows any new tags that
        // point at them. Confirmed the hard way: excluding History here left the commit graph
        // missing newly-fetched commits until some later Full refresh caught up, and that later
        // refresh's own (correctly) different signature meant it never got skipped, effectively
        // just deferring the exact same GetHistory cost to a second, confusing-looking refresh
        // moments after the first. Only WorkingDir (genuinely untouched) is safe to skip.
        private const RefreshScope FetchRefreshScope =
            RefreshScope.History | RefreshScope.Branches | RefreshScope.Tags | RefreshScope.Remotes;

        // FetchBranchAsync's refspec fetch runs with --no-tags (matching SourceTree's equivalent
        // command for the same "update one non-checked-out branch" operation) — tags genuinely
        // cannot change from it, so unlike the general FetchRefreshScope above, Tags is safe to
        // drop here too, not just WorkingDir.
        private const RefreshScope UpdateBranchRefreshScope =
            RefreshScope.History | RefreshScope.Branches | RefreshScope.Remotes;

        // Unlike fetch, a push is purely outbound — no new local objects, branches, or tags ever
        // appear from pushing, so History/Tags/WorkingDir/Stashes are all safe to skip; only
        // Branches (ahead/behind, upstream tracking) and the cheap Remotes matter.
        private const RefreshScope PushRefreshScope = RefreshScope.Branches | RefreshScope.Remotes;

        private async Task FetchAsync(bool prune = false, bool allRemotes = false)
        {
            await RefreshCurrentBranchAsync();
            var progress = new Progress<string>(ReportProgress);
            // One continuous busy scope spanning the whole "fetch → refresh" sequence — RunAsync/
            // RunCliAsync are busy-scope-reentrant (see RunAsync), so nesting them here no longer
            // flickers IsBusy false in between (previously visible as the "Working" indicator
            // blinking off for under half a second before the trailing refresh's own busy scope
            // reopened it — confirmed, not just a rare race, whenever this gap outlasted the
            // RepositoryWatcher's ~400ms debounce).
            if (!TryEnterBusyScope()) return;
            try
            {
                if (allRemotes && Remotes.Count > 1)
                {
                    var remotes = Remotes.ToList();
                    if (remotes.Any(r => !GitCli.IsSshUrl(r.Url)) && !await EnsureCredentialsAsync()) return;
                    var allOk = await RunAsync("Fetching all remotes…", () =>
                    {
                        foreach (var r in remotes)
                        {
                            if (GitCli.IsSshUrl(r.Url))
                            {
                                var args = $"fetch {(prune ? "--prune " : "")}{CliGitService.Quote(r.Name)}";
                                var result = _git.Cli.RunAsync(args, new GitCliOptions { Progress = progress }).GetAwaiter().GetResult();
                                _git.Reopen();
                                if (!result.Success) throw new InvalidOperationException(result.ErrorText);
                            }
                            else if (_git.Cli != null && _git.Cli.IsAvailable)
                            {
                                var args = $"fetch {(prune ? "--prune " : "")}{CliGitService.Quote(r.Name)}";
                                var env = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, r.Url);
                                var result = _git.Cli.RunAsync(args, new GitCliOptions { Progress = progress, Env = env }).GetAwaiter().GetResult();
                                if (!result.Success && IsAuthRejectionSignature(result.ErrorText))
                                {
                                    var rejectedUser = PurgeRejectedCredentialForRetry(r.Url);
                                    if (TryAutoResolveCredential(r.Url, rejectedUser))
                                    {
                                        var freshEnv = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, r.Url);
                                        result = _git.Cli.RunAsync(args, new GitCliOptions { Progress = progress, Env = freshEnv }).GetAwaiter().GetResult();
                                    }
                                }
                                _git.Reopen();
                                if (!result.Success) throw new InvalidOperationException(result.ErrorText);
                            }
                            else
                            {
                                _git.Fetch(r.Name, RemoteUsername, RemotePassword, progress, prune, OpToken);
                            }
                        }
                    });
                    if (allOk && _credentialsFromDialog) SaveCredentials();
                    await RefreshAsync(false, FetchRefreshScope);
                    return;
                }

                var remote = Remotes.FirstOrDefault();
                var remoteName = remote?.Name ?? "origin";
                var status = prune ? $"Fetching from {remoteName} (prune)…" : $"Fetching from {remoteName}…";
                if (GitCli.IsSshUrl(remote?.Url))
                {
                    await RunCliAsync(status, $"fetch {(prune ? "--prune " : "")}{CliGitService.Quote(remoteName)}", "Fetch");
                    await RefreshAsync(false, FetchRefreshScope);
                    return;
                }
                if (!await EnsureCredentialsAsync()) return;
                if (_git.Cli != null && _git.Cli.IsAvailable)
                {
                    var env = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, remote?.Url);
                    var cliOk = await RunCliAsync(status,
                        $"fetch {(prune ? "--prune " : "")}{CliGitService.Quote(remoteName)}", "Fetch", env: env,
                        authRetryRemoteUrl: remote?.Url);
                    if (cliOk && _credentialsFromDialog) SaveCredentials();
                    await RefreshAsync(false, FetchRefreshScope);
                    return;
                }
                var ok = await RunAsync(status, () => _git.Fetch(remoteName, RemoteUsername, RemotePassword, progress, prune, OpToken));
                if (ok && _credentialsFromDialog) SaveCredentials();
                await RefreshAsync(false, FetchRefreshScope);
            }
            finally { IsBusy = false; }
        }

        /// <summary>Updates a local branch other than the checked-out one to match its upstream,
        /// without switching to it — via a plain refspec fetch (`fetch &lt;remote&gt; &lt;upstream&gt;:&lt;local&gt;`),
        /// which git only allows when it's a fast-forward and the branch isn't currently checked out.
        /// That's exactly the safety net we want: no merge commit is possible here anyway since there's
        /// no working tree to merge into, so a plain "pull" only ever makes sense as a fast-forward.
        /// --no-tags (matching SourceTree's own equivalent command) means tags genuinely cannot
        /// change from this operation, so the refresh below can safely skip RefreshScope.Tags too —
        /// without it, git's default auto-follow-tags behavior could bring in a new tag, and skipping
        /// the tag refresh anyway would've been a real (if narrow) staleness bug, not just an
        /// optimization.</summary>
        private async Task FetchBranchAsync(object param)
        {
            if (!(param is BranchInfo bi) || bi.IsRemote || bi.IsHead || string.IsNullOrEmpty(bi.TrackedBranchName))
                return;
            var remoteName = bi.RemoteName;
            if (string.IsNullOrEmpty(remoteName)) return;
            var upstreamShort = bi.TrackedBranchName.StartsWith(remoteName + "/")
                ? bi.TrackedBranchName.Substring(remoteName.Length + 1)
                : bi.TrackedBranchName;
            var refspec = CliGitService.Quote($"{upstreamShort}:{bi.Name}");
            if (!TryEnterBusyScope()) return;
            try
            {
                if (await RunCliAsync($"Updating {bi.Name}…",
                        $"fetch --no-tags {CliGitService.Quote(remoteName)} {refspec}", "Update branch"))
                    await RefreshAsync(false, UpdateBranchRefreshScope);
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// Runs a git.exe-backed operation with the standard busy/error handling.
        /// Credentials come from git's own credential helpers (GCM). Reopens the
        /// libgit2 handle afterwards because the CLI may have mutated refs/index.
        ///
        /// When <paramref name="authRetryRemoteUrl"/> is set (an HTTPS remote URL) and the first
        /// attempt fails with a credential-rejection signature, this purges the stale credential and
        /// makes one silent retry with a freshly auto-resolved one before giving up — see
        /// TryAutoResolveCredential's remarks. Only if that retry also fails does the exception
        /// propagate to RunWorkAsync's catch, which shows the error and forces the interactive
        /// dialog on the *next* explicit attempt, exactly as before.
        /// </summary>
        private async Task<bool> RunCliAsync(string status, string args, string featureName, string stdIn = null,
            IDictionary<string, string> env = null, string authRetryRemoteUrl = null)
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError(featureName,
                    "This feature requires Git for Windows (git.exe), which was not found on this machine. " +
                    "Install it from https://git-scm.com and try again.");
                return false;
            }
            return await RunAsync(status, () =>
            {
                // Blocking wait is intentional: the executor thread owns all git work,
                // so nothing else can touch the repo while the CLI process runs.
                var result = _git.Cli.RunAsync(args, new GitCliOptions
                {
                    StdIn = stdIn,
                    Env = env,
                    Progress = new Progress<string>(ReportProgress)
                }, OpToken).GetAwaiter().GetResult();

                if (!result.Success && authRetryRemoteUrl != null && IsAuthRejectionSignature(result.ErrorText))
                {
                    var rejectedUser = PurgeRejectedCredentialForRetry(authRetryRemoteUrl);
                    if (TryAutoResolveCredential(authRetryRemoteUrl, rejectedUser))
                    {
                        var freshEnv = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, authRetryRemoteUrl);
                        result = _git.Cli.RunAsync(args, new GitCliOptions
                        {
                            StdIn = stdIn,
                            Env = freshEnv,
                            Progress = new Progress<string>(ReportProgress)
                        }, OpToken).GetAwaiter().GetResult();
                    }
                }

                _git.Reopen();
                if (!result.Success)
                    throw new InvalidOperationException(result.ErrorText);
            });
        }

        /// <summary>
        /// Like RunCliAsync, but for ops that can legitimately "fail" (non-zero exit) by
        /// stopping for conflict resolution — rebase, pull --rebase. In that case the conflict
        /// banner takes over instead of an error dialog; only a genuine failure still throws.
        /// See RunCliAsync's remarks for the <paramref name="authRetryRemoteUrl"/> silent-retry behavior.
        /// </summary>
        private async Task<bool> RunCliAllowingConflictAsync(string status, string args, string featureName,
            IDictionary<string, string> env = null, string authRetryRemoteUrl = null)
        {
            if (_git.Cli == null || !_git.Cli.IsAvailable)
            {
                DialogService.ShowError(featureName,
                    "This feature requires Git for Windows (git.exe), which was not found on this machine. " +
                    "Install it from https://git-scm.com and try again.");
                return false;
            }
            return await RunAsync(status, () =>
            {
                var result = _git.Cli.RunAsync(args, new GitCliOptions
                {
                    Env = env,
                    Progress = new Progress<string>(ReportProgress)
                }, OpToken).GetAwaiter().GetResult();

                if (!result.Success && authRetryRemoteUrl != null && IsAuthRejectionSignature(result.ErrorText))
                {
                    var rejectedUser = PurgeRejectedCredentialForRetry(authRetryRemoteUrl);
                    if (TryAutoResolveCredential(authRetryRemoteUrl, rejectedUser))
                    {
                        var freshEnv = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, authRetryRemoteUrl);
                        result = _git.Cli.RunAsync(args, new GitCliOptions
                        {
                            Env = freshEnv,
                            Progress = new Progress<string>(ReportProgress)
                        }, OpToken).GetAwaiter().GetResult();
                    }
                }

                _git.Reopen();
                if (!result.Success && !_git.GetConflictState().HasConflicts)
                    throw new InvalidOperationException(result.ErrorText);
            });
        }

        private async Task PullRebaseAsync()
        {
            await RefreshCurrentBranchAsync();
            var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
            var ok = await RunCliAllowingConflictAsync("Pulling (rebase)…", "pull --rebase --autostash", "Pull (rebase)");
            if (ok)
            {
                var conflict = await _git.Executor.RunAsync(() => _git.GetConflictState());
                if (!conflict.HasConflicts)
                    await CaptureUndo(UndoKind.HeadHardMove, "Pull (rebase)", preHead);
                await LoadWorkingDirAsync();
                await RefreshLfsStatusAsync();
            }
            await RefreshAsync();
        }

        private async Task ForcePushAsync()
        {
            var branch = await RefreshCurrentBranchAsync();
            if (string.IsNullOrEmpty(branch) || branch.StartsWith("detached")) return;
            if (!DialogService.Confirm("Force Push",
                    $"Force-push '{branch}' (with lease)?\n\nThis rewrites the remote branch to match your local one. " +
                    "The lease check aborts if someone else pushed in the meantime.",
                    "Force Push", danger: true))
                return;
            var remote = Remotes.FirstOrDefault()?.Name ?? "origin";
            // Qualified lease: pin the expected remote tip so the push only succeeds if the
            // remote still points where our tracking ref says. An unqualified --force-with-lease
            // silently trusts whatever the last fetch brought in.
            var expected = await _git.Executor.RunAsync(() => _git.GetBranchTipSha($"{remote}/{branch}"));
            var lease = expected != null
                ? $"--force-with-lease={CliGitService.Quote(branch + ":" + expected)}"
                : "--force-with-lease";
            if (!TryEnterBusyScope()) return;
            try
            {
                if (await RunCliAsync($"Force-pushing {branch}…",
                        $"push {lease} {CliGitService.Quote(remote)} {CliGitService.Quote(branch)}",
                        "Force push"))
                    await RefreshAsync(false, PushRefreshScope);
            }
            finally { IsBusy = false; }
        }

        private async Task PullAsync()
        {
            await RefreshCurrentBranchAsync();
            var remoteUrl = Remotes.FirstOrDefault()?.Url;
            var isSsh = GitCli.IsSshUrl(remoteUrl);
            var cliAvailable = _git.Cli != null && _git.Cli.IsAvailable;
            if (isSsh || cliAvailable)
            {
                IDictionary<string, string> env = null;
                if (!isSsh)
                {
                    if (!await EnsureCredentialsAsync()) return;
                    env = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, remoteUrl);
                }
                var preHead = await _git.Executor.RunAsync(() => _git.GetHeadSha());
                var ok = await RunCliAllowingConflictAsync("Pulling…", "pull --autostash", "Pull", env,
                    authRetryRemoteUrl: isSsh ? null : remoteUrl);
                if (ok)
                {
                    var conflict = await _git.Executor.RunAsync(() => _git.GetConflictState());
                    if (!conflict.HasConflicts)
                        await CaptureUndo(UndoKind.HeadHardMove, "Pull", preHead);
                    await LoadWorkingDirAsync();
                    await RefreshLfsStatusAsync();
                }
                if (ok && !isSsh && _credentialsFromDialog) SaveCredentials();
                await RefreshAsync();
                return;
            }
            if (!await EnsureCredentialsAsync()) return;
            var pullOk = await RunAsync("Pulling…", () =>
            {
                _git.Pull(AuthorName ?? "User", AuthorEmail ?? "user@example.com",
                    RemoteUsername, RemotePassword,
                    new Progress<string>(ReportProgress), OpToken);
            });
            if (pullOk && _credentialsFromDialog) SaveCredentials();
            if (pullOk) await RefreshLfsStatusAsync();
            await RefreshAsync();
        }

        /// <summary>Pushes the current branch. Returns whether the push succeeded, so callers
        /// (e.g. HostingViewModel's push-before-PR prompt) can decide whether to proceed.</summary>
        public async Task<bool> PushAsync()
        {
            var branch = await RefreshCurrentBranchAsync();
            var remoteName = Remotes.FirstOrDefault()?.Name ?? "origin";
            var remoteUrl = Remotes.FirstOrDefault()?.Url;
            if (!TryEnterBusyScope()) return false;
            try
            {
                if (GitCli.IsSshUrl(remoteUrl))
                {
                    var cliOk = await RunCliAsync($"Pushing to {remoteName}…",
                        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(branch)}", "Push");
                    if (cliOk) await RefreshAsync(false, PushRefreshScope);
                    return cliOk;
                }
                if (!await EnsureCredentialsAsync()) return false;
                if (_git.Cli != null && _git.Cli.IsAvailable)
                {
                    var env = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, remoteUrl);
                    var cliOk = await RunCliAsync($"Pushing to {remoteName}…",
                        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(branch)}", "Push", env: env,
                        authRetryRemoteUrl: remoteUrl);
                    if (cliOk && _credentialsFromDialog) SaveCredentials();
                    if (cliOk) await RefreshAsync(false, PushRefreshScope);
                    return cliOk;
                }
                var ok = await RunAsync($"Pushing to {remoteName}…", () =>
                {
                    _git.Push(remoteName, branch, RemoteUsername, RemotePassword,
                        new Progress<string>(ReportProgress), OpToken);
                });
                if (ok && _credentialsFromDialog) SaveCredentials();
                await RefreshAsync(false, PushRefreshScope);
                return ok;
            }
            finally { IsBusy = false; }
        }

        /// <summary>Pushes an arbitrary local branch, not necessarily the checked-out one — the
        /// sidebar's per-branch "Push to remote" context-menu item, and HostingViewModel's
        /// push-before-PR step when the PR's source branch isn't CurrentBranch. Mirrors
        /// <see cref="PushAsync"/> exactly, just parameterized on <paramref name="param"/>'s branch
        /// name instead of always reading CurrentBranch.</summary>
        public async Task<bool> PushBranchAsync(object param)
        {
            if (!(param is BranchInfo bi) || bi.IsRemote) return false;
            var remoteName = Remotes.FirstOrDefault()?.Name ?? "origin";
            var remoteUrl = Remotes.FirstOrDefault()?.Url;
            if (!TryEnterBusyScope()) return false;
            try
            {
                if (GitCli.IsSshUrl(remoteUrl))
                {
                    var cliOk = await RunCliAsync($"Pushing {bi.Name} to {remoteName}…",
                        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(bi.Name)}", "Push");
                    if (cliOk) await RefreshAsync(false, PushRefreshScope);
                    return cliOk;
                }
                if (!await EnsureCredentialsAsync()) return false;
                if (_git.Cli != null && _git.Cli.IsAvailable)
                {
                    var env = CliGitService.BuildHttpAuthEnv(RemoteUsername, RemotePassword, remoteUrl);
                    var cliOk = await RunCliAsync($"Pushing {bi.Name} to {remoteName}…",
                        $"push -u {CliGitService.Quote(remoteName)} {CliGitService.Quote(bi.Name)}", "Push", env: env,
                        authRetryRemoteUrl: remoteUrl);
                    if (cliOk && _credentialsFromDialog) SaveCredentials();
                    if (cliOk) await RefreshAsync(false, PushRefreshScope);
                    return cliOk;
                }
                var ok = await RunAsync($"Pushing {bi.Name} to {remoteName}…", () =>
                {
                    _git.Push(remoteName, bi.Name, RemoteUsername, RemotePassword,
                        new Progress<string>(ReportProgress), OpToken);
                });
                if (ok && _credentialsFromDialog) SaveCredentials();
                await RefreshAsync(false, PushRefreshScope);
                return ok;
            }
            finally { IsBusy = false; }
        }

        // ── Credential helpers ────────────────────────────────────────────────

        /// <summary>Mirrors the credential-rejection signature check in RunWorkAsync's catch block
        /// (RepositoryViewModel.cs) — duplicated rather than shared because that check runs strictly
        /// AFTER an operation has already been given up on, while this one runs on the git.exe CLI
        /// path's first failure to decide whether a silent retry is worth attempting at all. Keep
        /// the two lists of signatures in sync if either changes.</summary>
        private static bool IsAuthRejectionSignature(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            return msg.IndexOf("authentication replays", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("status code: 401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("status code: 403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("status code: 410", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("returned error: 401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("returned error: 403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("returned error: 410", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("could not read username", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("could not read password", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Clears a rejected HTTPS credential from memory and PickleGit's own store, and
        /// tells the system credential helper (GCM, wincred, ...) it was rejected — the same cleanup
        /// RunWorkAsync's catch does for a definitive failure, but WITHOUT forcing the interactive
        /// dialog, since the caller is about to give TryAutoResolveCredential a chance to find a
        /// fresh one first. Safe to call from the executor thread — RemoteUsername/RemotePassword
        /// are plain auto-properties with no change notification, so there's no cross-thread hazard.
        /// Returns the username that was actually rejected (or null), so the caller can hand it to
        /// TryAutoResolveCredential and avoid silently picking a DIFFERENT stored account for the
        /// same host.</summary>
        private string PurgeRejectedCredentialForRetry(string remoteUrl)
        {
            var failedUser = RemoteUsername;
            var failedPassword = RemotePassword;
            RemoteUsername = null;
            RemotePassword = null;
            if (string.IsNullOrEmpty(failedUser) || string.IsNullOrEmpty(remoteUrl)) return failedUser;
            try
            {
                if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
                    Services.CredentialStore.Delete(uri.Host, failedUser);
            }
            catch { }
            Services.CredentialStore.RejectViaGitCredentialHelper(remoteUrl, failedUser, failedPassword);
            return failedUser;
        }

        /// <summary>Synchronous variant of EnsureCredentialsAsync's auto-lookup chain (own store →
        /// git credential helper → raw Credential Manager), safe to call from the executor thread —
        /// never shows the interactive dialog. Used for one silent recovery attempt right after
        /// PurgeRejectedCredentialForRetry: a token-based credential manager (GCM) can rotate/refresh
        /// a rejected token in the background with no visible prompt, so re-asking immediately often
        /// already has a fresh one ready — confirmed this happens in practice against Bitbucket.
        ///
        /// <paramref name="expectedUser"/> is the username that was just rejected (from
        /// PurgeRejectedCredentialForRetry) — own-store entries are host-keyed only, so on a machine
        /// with two stored accounts for the same host (e.g. personal + work) a bare host match would
        /// take whichever happens to come back first, silently retrying — and possibly succeeding —
        /// as a different identity than the one actually configured for this remote. Matching on
        /// (host, expectedUser) keeps this path to "the same account came back", which is exactly
        /// what the GCM-rotation scenario above needs; anything else falls through to the git
        /// credential helper below, which resolves per-URL/protocol via git's own logic rather than
        /// PickleGit's ambiguous multi-account list.</summary>
        private bool TryAutoResolveCredential(string remoteUrl, string expectedUser = null)
        {
            if (string.IsNullOrEmpty(remoteUrl)) return false;
            try
            {
                if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(expectedUser))
                {
                    foreach (var (host, user) in Services.CredentialStore.ListAll())
                    {
                        if (!string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(user, expectedUser, StringComparison.Ordinal)) continue;
                        var pw = Services.CredentialStore.Load(host, user);
                        if (pw != null) { RemoteUsername = user; RemotePassword = pw; return true; }
                    }
                }
            }
            catch { }
            try
            {
                var (gitUser, gitPass) = Services.CredentialStore.LoadViaGitCredentialHelper(remoteUrl);
                if (!string.IsNullOrEmpty(gitUser) && !string.IsNullOrEmpty(gitPass))
                {
                    RemoteUsername = gitUser;
                    RemotePassword = gitPass;
                    return true;
                }
            }
            catch { }
            try
            {
                var (gcmUser, gcmPass) = Services.CredentialStore.LoadFromGitCredentialManager(remoteUrl);
                if (!string.IsNullOrEmpty(gcmUser) && !string.IsNullOrEmpty(gcmPass))
                {
                    RemoteUsername = gcmUser;
                    RemotePassword = gcmPass;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private async Task<bool> EnsureCredentialsAsync()
        {
            _credentialsFromDialog = false;

            // 1. Already have credentials in memory
            if (!string.IsNullOrEmpty(RemoteUsername) && !string.IsNullOrEmpty(RemotePassword))
                return true;

            // Previous attempt failed — skip auto-lookup and go straight to the dialog so the
            // user can correct credentials instead of replaying the same bad ones from GCM/store
            var skipAutoLookup = _forceCredentialDialog;
            _forceCredentialDialog = false;

            if (!skipAutoLookup)
            {
                var remoteUrl = Remotes.FirstOrDefault()?.Url;
                if (!string.IsNullOrEmpty(remoteUrl))
                {
                    try
                    {
                        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
                        {
                            // 2. Check our own credential store (saved by this app)
                            foreach (var (host, user) in Services.CredentialStore.ListAll())
                            {
                                if (!string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                var pw = Services.CredentialStore.Load(host, user);
                                if (pw != null) { RemoteUsername = user; RemotePassword = pw; return true; }
                            }
                        }
                    }
                    catch { }

                    // 3. Ask git's own configured credential helper (GCM, wincred, store, ...) — the
                    // exact same resolution git.exe itself performs, including any per-host/per-path
                    // username config. Tried before the raw Credential Manager read below because
                    // that raw read just grabs whatever's cached under one fixed generic key and
                    // can't disambiguate between multiple accounts stored for the same host (e.g. a
                    // shared machine where a build service has also authenticated) the way the
                    // helper's own credential matching can.
                    //
                    // A long timeout here (not LoadViaGitCredentialHelper's short default) matters:
                    // with nothing cached yet, a helper like GCM runs its own interactive sign-in for
                    // this host — for Bitbucket/GitHub that's a real browser tab for OAuth, same as
                    // plain `git push` from a terminal. Only this primary lookup should wait that
                    // long; TryAutoResolveCredential's silent post-rejection retry deliberately keeps
                    // the short default so it never shows a surprise browser tab of its own.
                    //
                    // CanCancel (the status-bar Cancel button) also watches _credentialWaitCts
                    // (see its declaration) specifically so this wait — which can now run for
                    // minutes — has an escape besides closing the app. Deliberately its own field,
                    // never _opCts: RunWorkAsync can run reentrantly while IsBusy is already true
                    // (e.g. CommitCommand's CanExecute doesn't check IsBusy), which would overwrite
                    // _opCts with that unrelated call's own token; nulling _opCts back out here
                    // afterward would then null out THAT still-running operation's token instead.
                    _credentialWaitCts = new System.Threading.CancellationTokenSource();
                    RaisePropertyChanged(nameof(CanCancel));
                    var credentialHelperToken = _credentialWaitCts.Token;
                    var previousStatusMessage = StatusMessage;
                    StatusMessage = "Waiting for sign-in… (check your browser)";
                    try
                    {
                        var (gitUser, gitPass) = await Task.Run(
                            () => Services.CredentialStore.LoadViaGitCredentialHelper(remoteUrl, timeoutMs: 300_000, ct: credentialHelperToken));
                        if (!string.IsNullOrEmpty(gitUser) && !string.IsNullOrEmpty(gitPass))
                        {
                            RemoteUsername = gitUser;
                            RemotePassword = gitPass;
                            return true;
                        }
                    }
                    catch { }
                    finally
                    {
                        _credentialWaitCts = null;
                        RaisePropertyChanged(nameof(CanCancel));
                    }
                    // Cancelling this wait means "stop trying to sign in", not "fall through to
                    // PickleGit's own dialog instead" — abort the whole Push/Pull/Fetch here.
                    if (credentialHelperToken.IsCancellationRequested)
                    {
                        StatusMessage = "Sign-in cancelled";
                        return false;
                    }
                    StatusMessage = previousStatusMessage;

                    // 4. Fall back to a raw Windows Credential Manager read — only reachable when
                    // git.exe isn't on PATH, since step 3 already covers this same store when it is.
                    try
                    {
                        var (gcmUser, gcmPass) = Services.CredentialStore.LoadFromGitCredentialManager(remoteUrl);
                        if (!string.IsNullOrEmpty(gcmUser) && !string.IsNullOrEmpty(gcmPass))
                        {
                            RemoteUsername = gcmUser;
                            RemotePassword = gcmPass;
                            return true;
                        }
                    }
                    catch { }
                }
            }

            // 5. Nothing found — ask the user; only save these credentials since GCM/helper creds rotate
            _credentialsFromDialog = true;
            var dlg = new Views.CredentialsDialog
            {
                Owner = Application.Current.MainWindow,
                Username = RemoteUsername ?? string.Empty
            };
            if (dlg.ShowDialog() != true) { _credentialsFromDialog = false; return false; }
            RemoteUsername = dlg.Username;
            RemotePassword = dlg.Password;
            return true;
        }

        private void SaveCredentials()
        {
            if (string.IsNullOrEmpty(RemoteUsername) || string.IsNullOrEmpty(RemotePassword)) return;
            var remoteUrl = Remotes.FirstOrDefault()?.Url;
            if (string.IsNullOrEmpty(remoteUrl)) return;
            try
            {
                if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
                    Services.CredentialStore.Save(uri.Host, RemoteUsername, RemotePassword);
            }
            catch { }
        }
    }
}
