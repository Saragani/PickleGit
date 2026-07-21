using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PickleGit.Services.Git
{
    public sealed class GitCliResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public bool Success => ExitCode == 0;

        /// <summary>Best human-readable error text (stderr, falling back to stdout).</summary>
        public string ErrorText =>
            !string.IsNullOrWhiteSpace(StdErr) ? StdErr.Trim() :
            !string.IsNullOrWhiteSpace(StdOut) ? StdOut.Trim() : $"git exited with code {ExitCode}";
    }

    public sealed class GitCliOptions
    {
        public string StdIn { get; set; }
        public IDictionary<string, string> Env { get; set; }
        public IProgress<string> Progress { get; set; }
    }

    /// <summary>
    /// Low-level runner for the system git.exe. Used for operations LibGit2Sharp
    /// cannot do (rebase, interactive rebase, hunk staging via apply, SSH, GPG…).
    /// Features that depend on it should check <see cref="IsGitAvailable"/> and
    /// degrade gracefully when git.exe is not installed.
    /// </summary>
    public static class GitCli
    {
        private static string _gitPath;
        private static bool _resolved;
        private static readonly object _lock = new object();

        /// <summary>Optional user override (from settings) checked before auto-discovery.</summary>
        public static string GitPathOverride { get; set; }

        public static bool IsGitAvailable => ResolveGitPath() != null;

        private static readonly System.Text.RegularExpressions.Regex ScpLikeUrl =
            new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9_.\-]+@[A-Za-z0-9_.\-]+:(?!//)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// True for ssh:// URLs and the SCP-like "user@host:path" syntax. SSH auth is handled
        /// by the system's OpenSSH client/agent, which LibGit2Sharp 0.27 cannot drive — these
        /// remotes are routed through git.exe instead (see CLAUDE.md "Hybrid git backend").
        /// </summary>
        public static bool IsSshUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) || ScpLikeUrl.IsMatch(url);
        }

        public static string ResolveGitPath()
        {
            lock (_lock)
            {
                if (_resolved) return _gitPath;
                _gitPath = DiscoverGitPath();
                _resolved = true;
                return _gitPath;
            }
        }

        /// <summary>Re-run discovery (e.g. after the user changes the override in settings).</summary>
        public static void InvalidateDiscovery()
        {
            lock (_lock) { _resolved = false; _gitPath = null; }
        }

        private static string DiscoverGitPath()
        {
            if (!string.IsNullOrEmpty(GitPathOverride) && File.Exists(GitPathOverride))
                return GitPathOverride;

            // 1. PATH lookup
            try
            {
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        var candidate = Path.Combine(dir.Trim(), "git.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Standard install locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Git\cmd\git.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe"),
            };
            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Runs git with the given arguments in <paramref name="workDir"/>.
        /// Never throws for a non-zero exit code — inspect the result.
        /// Throws <see cref="InvalidOperationException"/> when git.exe cannot be found.
        /// </summary>
        public static async Task<GitCliResult> RunAsync(
            string workDir, string args,
            GitCliOptions opts = null,
            CancellationToken ct = default(CancellationToken))
        {
            var gitPath = ResolveGitPath()
                ?? throw new InvalidOperationException(
                    "git.exe was not found. Install Git for Windows to enable this feature.");

            var psi = new ProcessStartInfo
            {
                FileName = gitPath,
                Arguments = "--no-optional-locks " + args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = opts?.StdIn != null,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            // Never let git spawn an interactive editor from inside the app
            if (!psi.EnvironmentVariables.ContainsKey("GIT_EDITOR"))
                psi.EnvironmentVariables["GIT_EDITOR"] = "true";
            if (opts?.Env != null)
            {
                foreach (var kv in opts.Env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (stdout) stdout.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (stderr) stderr.AppendLine(e.Data);
                opts?.Progress?.Report(e.Data); // git writes progress to stderr
            };
            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            using (proc)
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start git.exe.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (opts?.StdIn != null)
                {
                    using (var stdin = proc.StandardInput)
                        await stdin.WriteAsync(opts.StdIn).ConfigureAwait(false);
                }

                using (ct.Register(() =>
                {
                    try { if (!proc.HasExited) proc.Kill(); }
                    catch { }
                }))
                {
                    var exitCode = await tcs.Task.ConfigureAwait(false);
                    // Ensure async output readers have flushed
                    proc.WaitForExit();
                    ct.ThrowIfCancellationRequested();
                    return new GitCliResult
                    {
                        ExitCode = exitCode,
                        StdOut = stdout.ToString(),
                        StdErr = stderr.ToString()
                    };
                }
            }
        }
    }
}
