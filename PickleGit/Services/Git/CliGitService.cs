using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// High-level typed git.exe operations for one repository — the half of the
    /// hybrid backend that LibGit2Sharp cannot cover (rebase, pull --rebase,
    /// hunk staging via apply, SSH remotes, GPG signing, worktrees, submodules).
    /// Owned by <see cref="GitService"/> and exposed as <c>GitService.Cli</c>.
    ///
    /// IMPORTANT: after any CLI operation that mutates refs/index, the caller must
    /// invalidate LibGit2Sharp's view via <c>GitService.Reopen()</c> — the wrapper
    /// methods on GitService take care of this.
    /// </summary>
    public sealed class CliGitService
    {
        private readonly string _workDir;

        public CliGitService(string workingDirectory)
        {
            _workDir = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        }

        public bool IsAvailable => GitCli.IsGitAvailable;

        public string WorkingDirectory => _workDir;

        /// <summary>Raw escape hatch — run any git command in this repo.</summary>
        public Task<GitCliResult> RunAsync(string args, GitCliOptions opts = null,
            CancellationToken ct = default(CancellationToken))
            => GitCli.RunAsync(_workDir, args, opts, ct);

        /// <summary>Runs a git command and throws with git's stderr on failure.</summary>
        public async Task<GitCliResult> RunCheckedAsync(string args, GitCliOptions opts = null,
            CancellationToken ct = default(CancellationToken))
        {
            var result = await GitCli.RunAsync(_workDir, args, opts, ct).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorText);
            return result;
        }

        public async Task<string> GetVersionAsync()
        {
            var r = await RunAsync("--version").ConfigureAwait(false);
            return r.Success ? r.StdOut.Trim() : null;
        }

        /// <summary>
        /// Quotes a single command-line argument for git.exe, following the Win32/MSVCRT
        /// argv convention (the same one CommandLineToArgvW uses to split ProcessStartInfo.Arguments):
        /// a run of N backslashes must become 2N before a literal embedded quote, or 2N right
        /// before the closing quote. A naive Replace("\"", "\\\"") gets embedded quotes right but
        /// leaves a trailing backslash run un-doubled, which lets it escape the closing quote
        /// instead of terminating the argument (e.g. a path ending in "\" with a space in it).
        /// </summary>
        /// <summary>
        /// Builds env vars that authenticate an HTTPS git.exe invocation without ever putting the
        /// credential in ProcessStartInfo.Arguments (which GitCli.RunAsync logs verbatim via AppLog)
        /// or in the remote URL. GIT_CONFIG_KEY/VALUE injects an Authorization header via
        /// http.&lt;url&gt;.extraheader — verified directly that git sends it on the very first request, so
        /// nothing prompts on the success path.
        ///
        /// Both the extraheader and the credential.helper override are scoped to <paramref
        /// name="remoteUrl"/>'s scheme+host+port (via git's URL-prefix config matching, verified
        /// directly with `git config --get-urlmatch`) rather than the bare unscoped keys. A single git.exe
        /// invocation can talk to more than one host — e.g. a push to Bitbucket that also triggers an LFS
        /// lock-verify request against a *different* server. An unscoped http.extraheader/credential.helper
        /// applies to every HTTP request that process makes, so it was leaking this host's Basic-auth header
        /// (and disabling the credential helper) onto that unrelated host too, breaking its own independent
        /// auth. Scoping to this remote's origin only reproduced in a real repo with a separate LFS remote.
        ///
        /// credential.helper is cleared (for this host only) and GIT_ASKPASS is pointed at git.exe itself (a
        /// fast, always-present binary that just fails as an invalid subcommand) so a REJECTED header fails
        /// fast instead of falling through to the system credential manager. Verified directly: with this
        /// machine's configured credential.helper=manager (Git Credential Manager) left enabled, a
        /// rejected credential made git.exe hang indefinitely (>60s, force-kill required) waiting on an
        /// invisible GCM prompt; disabling both here instead fails in under a second with a clear
        /// "could not read Username ... terminal prompts disabled" / "returned error: 4xx" stderr message.
        /// GIT_ASKPASS itself can't be scoped per-host (git has no such mechanism) but in practice is only
        /// reached when a host's credential.helper doesn't resolve anything — untouched for every host
        /// other than remoteUrl's, so this doesn't affect them.
        /// </summary>
        public static IDictionary<string, string> BuildHttpAuthEnv(string username, string password, string remoteUrl)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            string urlScope;
            try
            {
                urlScope = new Uri(remoteUrl).GetLeftPart(UriPartial.Authority) + "/";
            }
            catch (Exception)
            {
                urlScope = remoteUrl;
            }
            return new Dictionary<string, string>
            {
                ["GIT_CONFIG_COUNT"] = "2",
                ["GIT_CONFIG_KEY_0"] = $"http.{urlScope}.extraheader",
                ["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + token,
                ["GIT_CONFIG_KEY_1"] = $"credential.{urlScope}.helper",
                ["GIT_CONFIG_VALUE_1"] = "",
                ["GIT_ASKPASS"] = GitCli.ResolveGitPath() ?? "",
            };
        }

        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOf(' ') < 0 && arg.IndexOf('"') < 0 && arg.IndexOf('\t') < 0) return arg;

            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                    sb.Append(c);
                }
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
