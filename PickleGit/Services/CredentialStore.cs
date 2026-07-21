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
        /// </summary>
        public static (string username, string password) LoadViaGitCredentialHelper(string remoteUrl)
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
                    proc.StandardInput.Write(input);
                    proc.StandardInput.Close();
                    // Collect stdout and drain stderr asynchronously to prevent pipe-buffer deadlock
                    var stdoutLines = new List<string>();
                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutLines.Add(e.Data); };
                    proc.ErrorDataReceived += (_, __) => { };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } return default; }
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
            catch { return default; }
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
