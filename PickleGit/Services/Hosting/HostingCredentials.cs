using System;
using System.Collections.Generic;
using System.Linq;

namespace PickleGit.Services.Hosting
{
    /// <summary>
    /// Thin convention layer over CredentialStore for hosting-provider tokens/app-passwords.
    /// Stored as CredentialStore.Save(domain, "prtoken~&lt;username&gt;", secret) — the "prtoken~"
    /// marker lives in the *username* slot (never the host) so CredentialStore.ListAll()'s naive
    /// single-colon split on "PickleGit:&lt;host&gt;:&lt;username&gt;" still works; domain names never
    /// contain colons, but an extra colon in the host segment would silently corrupt that split.
    /// </summary>
    public static class HostingCredentials
    {
        private const string Marker = "prtoken~";

        public static (string username, string secret) Load(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return (null, null);
            var match = CredentialStore.ListAll()
                .FirstOrDefault(t => string.Equals(t.host, domain, StringComparison.OrdinalIgnoreCase)
                                      && t.username != null && t.username.StartsWith(Marker, StringComparison.Ordinal));
            if (match.username == null) return (null, null);
            var secret = CredentialStore.Load(domain, match.username);
            var realUsername = match.username.Substring(Marker.Length);
            return (string.IsNullOrEmpty(realUsername) ? null : realUsername, secret);
        }

        public static void Save(string domain, string username, string secret)
        {
            CredentialStore.Save(domain, Marker + (username ?? string.Empty), secret);
        }

        public static void Delete(string domain, string username)
        {
            CredentialStore.Delete(domain, Marker + (username ?? string.Empty));
        }

        public static List<(string domain, string username)> ListConfigured()
        {
            return CredentialStore.ListAll()
                .Where(t => t.username != null && t.username.StartsWith(Marker, StringComparison.Ordinal))
                .Select(t => (t.host, t.username.Substring(Marker.Length)))
                .ToList();
        }
    }
}
