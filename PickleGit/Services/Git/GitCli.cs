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
            // Git LFS detects isatty on its own stdout and silently suppresses its "Downloading/
            // Uploading LFS objects: NN%" progress meter entirely (not just switching format) when
            // it isn't connected to a real terminal — which our redirected pipe never is. Force it
            // on unconditionally; the variable is a no-op for any invocation that never shells out
            // to git-lfs (plain git commands, or repos with no LFS filters configured).
            psi.EnvironmentVariables["GIT_LFS_FORCE_PROGRESS"] = "1";
            if (opts?.Env != null)
            {
                foreach (var kv in opts.Env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sw = Stopwatch.StartNew();
            PickleGit.Services.AppLog.Info($"git {args} (in {workDir})");

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            using (proc)
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start git.exe.");

                // Process.OutputDataReceived/ErrorDataReceived only splits on '\n' — git and
                // git-lfs write their live progress meters ("Filtering content: 45% (…)") using a
                // bare '\r' so a terminal can overwrite the line in place, with no '\n' until the
                // whole operation finishes. The built-in event-based reader buffers all of those
                // \r-separated updates and delivers them as one giant blob only at the very end
                // (or not at all), so the progress bar never moves. Pumping the streams manually
                // and treating '\r' as a line terminator too delivers each update as soon as it
                // arrives.
                // Plain git writes its own progress meter ("Receiving objects: 45%…") to stderr,
                // but git-lfs writes its transfer meter ("Downloading LFS objects: 45%…") to
                // stdout — so progress has to be reported from both streams, not just stderr.
                var stdoutTask = PumpStreamAsync(proc.StandardOutput, line =>
                {
                    // Keep the buffered copy byte-for-byte (callers like GetBranchesViaCli parse
                    // tab-delimited output and a trailing empty field is significant) — only trim
                    // the copy handed to the status bar, since git pads progress lines with
                    // trailing spaces to blank out a previous longer line when overwriting via '\r'.
                    lock (stdout) stdout.AppendLine(line);
                    opts?.Progress?.Report(line.TrimEnd());
                });
                var stderrTask = PumpStreamAsync(proc.StandardError, line =>
                {
                    lock (stderr) stderr.AppendLine(line);
                    opts?.Progress?.Report(line.TrimEnd());
                });

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
                    // Ensure both pumps have drained (EOF) before reading stdout/stderr below.
                    try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); }
                    catch { /* pump can fault on Kill() tearing down the pipe mid-read */ }
                    ct.ThrowIfCancellationRequested();
                    PickleGit.Services.AppLog.Info($"git {args} exit={exitCode} in {sw.ElapsedMilliseconds}ms (in {workDir})");
                    return new GitCliResult
                    {
                        ExitCode = exitCode,
                        StdOut = stdout.ToString(),
                        StdErr = stderr.ToString()
                    };
                }
            }
        }

        /// <summary>Reads <paramref name="reader"/> to EOF, invoking <paramref name="onLine"/> for each
        /// chunk terminated by '\r', '\n', or '\r\n' (unlike <see cref="Process.BeginErrorReadLine"/>,
        /// which only recognizes '\n' and so never fires mid-operation for a bare-'\r' progress meter).</summary>
        private static async Task PumpStreamAsync(StreamReader reader, Action<string> onLine)
        {
            var buffer = new char[4096];
            var line = new StringBuilder();
            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        var c = buffer[i];
                        if (c == '\r' || c == '\n')
                        {
                            if (line.Length > 0)
                            {
                                onLine(line.ToString());
                                line.Clear();
                            }
                        }
                        else
                        {
                            line.Append(c);
                        }
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            if (line.Length > 0)
                onLine(line.ToString());
        }
    }
}
