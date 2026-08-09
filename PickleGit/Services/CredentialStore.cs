using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PickleGit.Services
{
    /// <summary>
    /// Stores credentials securely using the Windows Credential Manager API.
    /// </summary>
    public static class CredentialStore
    {
        private const string AppPrefix = "PickleGit:";

        public static void Save(string host, string username, string secret)
        {
            var target = AppPrefix + host + ":" + username;
            var secretBytes = Encoding.Unicode.GetBytes(secret);
            var cred = new NativeMethods.CREDENTIAL
            {
                Type = 1,       // CRED_TYPE_GENERIC
                TargetName = target,
                UserName = username,
                CredentialBlob = Marshal.AllocHGlobal(secretBytes.Length),
                CredentialBlobSize = secretBytes.Length,
                Persist = 2,    // CRED_PERSIST_LOCAL_MACHINE
                Comment = "PickleGit credential"
            };
            try
            {
                Marshal.Copy(secretBytes, 0, cred.CredentialBlob, secretBytes.Length);
                NativeMethods.CredWrite(ref cred, 0);
            }
            finally
            {
                ZeroUnmanaged(cred.CredentialBlob, secretBytes.Length);
                Marshal.FreeHGlobal(cred.CredentialBlob);
                Array.Clear(secretBytes, 0, secretBytes.Length);
            }
        }

        public static string Load(string host, string username)
        {
            var target = AppPrefix + host + ":" + username;
            if (!NativeMethods.CredRead(target, 1, 0, out IntPtr credPtr))
                return null;
            byte[] bytes = null;
            try
            {
                var cred = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                if (cred.CredentialBlobSize == 0) return null;
                bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                ZeroUnmanaged(cred.CredentialBlob, cred.CredentialBlobSize);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                NativeMethods.CredFree(credPtr);
                if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            }
        }

        /// <summary>Best-effort wipe of a secret before its unmanaged buffer is freed/returned,
        /// so a plaintext PAT/password doesn't linger in freed heap memory recoverable from a
        /// process/crash dump.</summary>
        private static void ZeroUnmanaged(IntPtr ptr, int length)
        {
            for (int i = 0; i < length; i++)
                Marshal.WriteByte(ptr, i, 0);
        }

        public static void Delete(string host, string username)
        {
            var target = AppPrefix + host + ":" + username;
            NativeMethods.CredDelete(target, 1, 0);
        }

        /// <summary>
        /// Looks up credentials stored by Git Credential Manager (key format "git:https://host").
        /// This lets the app reuse credentials that were entered via git CLI.
        /// </summary>
        public static (string username, string password) LoadFromGitCredentialManager(string remoteUrl)
        {
            if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
                return default;

            // GCM Core uses "git:<scheme>://<host>"; legacy GCM uses "<scheme>://<host>"
            var candidates = new[]
            {
                $"git:{uri.Scheme}://{uri.Host}",
                $"{uri.Scheme}://{uri.Host}",
            };

            foreach (var target in candidates)
            {
                if (!NativeMethods.CredRead(target, 1, 0, out IntPtr credPtr))
                    continue;
                byte[] bytes = null;
                try
                {
                    var cred = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                    if (string.IsNullOrEmpty(cred.UserName) || cred.CredentialBlobSize == 0)
                        continue;
                    bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                    ZeroUnmanaged(cred.CredentialBlob, cred.CredentialBlobSize);
                    var password = Encoding.Unicode.GetString(bytes);
                    if (!string.IsNullOrEmpty(password))
                        return (cred.UserName, password);
                }
                finally
                {
                    NativeMethods.CredFree(credPtr);
                    if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                }
            }
            return default;
        }

        /// <summary>
        /// Asks git's configured credential helper for credentials (same path git CLI uses).
        /// Works regardless of which helper is configured — GCM, wincred, store, etc.
        ///
        /// <paramref name="timeoutMs"/> matters a lot more than it looks: when nothing is cached yet,
        /// a helper like Git Credential Manager doesn't just fail — it runs its own interactive
        /// sign-in (for Bitbucket/GitHub, opening a real browser tab for OAuth), exactly like it does
        /// when plain `git push` from a terminal needs a credential. That flow takes as long as the
        /// human needs to complete it. The original hardcoded 5-second timeout was fine for the
        /// silent, already-cached-or-nothing lookups this method is also used for (see
        /// TryAutoResolveCredential's default-argument call), but killed the process long before an
        /// interactive OAuth login could ever finish — so the "ask git's own helper" step always
        /// failed for a brand-new sign-in and silently fell through to PickleGit's own username/
        /// password dialog instead, even though GCM would have handled it via browser exactly like
        /// the command line does. EnsureCredentialsAsync's primary lookup now passes a long timeout
        /// (plus <paramref name="ct"/> so the app's own Cancel button can still abort a stuck one) to
        /// give that real sign-in a chance; nothing here forces the dialog anymore for a host GCM can
        /// authenticate on its own.
        /// </summary>
        public static (string username, string password) LoadViaGitCredentialHelper(
            string remoteUrl, int timeoutMs = 5000, System.Threading.CancellationToken ct = default)
        {
            if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
                return default;

            var input = $"protocol={uri.Scheme}\nhost={uri.Host}\n\n";
            var gitPath = Git.GitCli.ResolveGitPath();
            if (gitPath == null) return default;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = "credential fill",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null) return default;
                    using (ct.Register(() => { try { proc.Kill(); } catch { } }))
                    {
                        proc.StandardInput.Write(input);
                        proc.StandardInput.Close();
                        // Collect stdout and drain stderr asynchronously to prevent pipe-buffer deadlock
                        var stdoutLines = new List<string>();
                        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutLines.Add(e.Data); };
                        proc.ErrorDataReceived += (_, __) => { };
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        if (!proc.WaitForExit(timeoutMs)) { try { proc.Kill(); } catch { } return default; }
                        proc.WaitForExit(); // flush any remaining async-read callbacks
                        if (proc.ExitCode != 0) return default;
                        var output = string.Join("\n", stdoutLines);

                        string username = null, password = null;
                        foreach (var line in output.Split('\n'))
                        {
                            var eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            var key = line.Substring(0, eq).Trim();
                            var val = line.Substring(eq + 1).Trim();
                            if (key == "username") username = val;
                            else if (key == "password") password = val;
                        }
                        return (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                            ? (username, password) : default;
                    }
                }
            }
            catch { return default; }
        }

        /// <summary>
        /// Tells git's configured credential helper (GCM, wincred, store, etc.) that a credential it
        /// supplied was rejected by the server — the "reject" step of the git-credential protocol
        /// (see git-credential(1)). Without this, a helper like GCM that cached a since-expired/revoked
        /// OAuth token or app password has no way to know it's stale: `credential fill` is read-only
        /// and just keeps returning the same cached value forever, so every retry (even after the user
        /// closes PickleGit's own re-entry dialog expecting the "saved Windows credentials" to work)
        /// fails identically and indefinitely. Real git.exe calls this automatically as part of its own
        /// HTTPS push/fetch failure handling; PickleGit's manual "fill credentials, then hand them to
        /// LibGit2Sharp" path bypassed that protocol entirely until this was added. Best-effort — a
        /// helper that no-ops or isn't configured must not surface an error for this.
        /// </summary>
        public static void RejectViaGitCredentialHelper(string remoteUrl, string username, string password)
        {
            if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri)) return;
            var gitPath = Git.GitCli.ResolveGitPath();
            if (gitPath == null) return;
            var input = $"protocol={uri.Scheme}\nhost={uri.Host}\n" +
                        (!string.IsNullOrEmpty(username) ? $"username={username}\n" : "") +
                        (!string.IsNullOrEmpty(password) ? $"password={password}\n" : "") +
                        "\n";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = "credential reject",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null) return;
                    proc.StandardInput.Write(input);
                    proc.StandardInput.Close();
                    proc.OutputDataReceived += (_, __) => { };
                    proc.ErrorDataReceived += (_, __) => { };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } }
                }
            }
            catch { }
        }

        public static List<(string host, string username)> ListAll()
        {
            var results = new List<(string, string)>();
            if (!NativeMethods.CredEnumerate(AppPrefix + "*", 0, out int count, out IntPtr pCredentials))
                return results;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var ptr = Marshal.ReadIntPtr(pCredentials, i * IntPtr.Size);
                    var cred = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(ptr);
                    if (cred.TargetName != null && cred.TargetName.StartsWith(AppPrefix))
                    {
                        // Target format is "<prefix><host>:<username>". Hosts may themselves
                        // contain ':' (host:port), so split on the *last* colon — preferring the
                        // stored UserName when it matches the tail exactly.
                        var rest = cred.TargetName.Substring(AppPrefix.Length);
                        var user = cred.UserName;
                        if (!string.IsNullOrEmpty(user) && rest.EndsWith(":" + user, StringComparison.Ordinal))
                        {
                            results.Add((rest.Substring(0, rest.Length - user.Length - 1), user));
                        }
                        else
                        {
                            var lastColon = rest.LastIndexOf(':');
                            if (lastColon > 0 && lastColon < rest.Length - 1)
                                results.Add((rest.Substring(0, lastColon), rest.Substring(lastColon + 1)));
                        }
                    }
                }
            }
            finally
            {
                NativeMethods.CredFree(pCredentials);
            }
            return results;
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct CREDENTIAL
            {
                public int Flags;
                public int Type;
                public string TargetName;
                public string Comment;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
                public int CredentialBlobSize;
                public IntPtr CredentialBlob;
                public int Persist;
                public int AttributeCount;
                public IntPtr Attributes;
                public string TargetAlias;
                public string UserName;
            }

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredDelete(string target, int type, int flags);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr pCredentials);

            [DllImport("advapi32.dll")]
            public static extern void CredFree([In] IntPtr buffer);
        }
    }
}
