using System;

namespace PickleGit.Services.Hosting
{
    public static class GitRemoteUrlParser
    {
        /// <summary>Splits a remote URL (https, ssh://, or SCP-like git@host:path) into (domain, path-without-.git).</summary>
        public static (string domain, string path) Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return (null, null);
            string domain, path;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return (null, null);
                domain = uri.Host;
                path = uri.AbsolutePath.Trim('/');
            }
            else
            {
                // SCP-like: git@host:owner/repo.git
                var atIdx = url.IndexOf('@');
                var colonIdx = url.IndexOf(':', atIdx + 1);
                if (colonIdx < 0) return (null, null);
                domain = url.Substring(atIdx + 1, colonIdx - atIdx - 1);
                path = url.Substring(colonIdx + 1).Trim('/');
            }

            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 4);

            return (domain, path);
        }
    }
}
