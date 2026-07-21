using System;
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
