using System;

namespace PickleGit.Services.Hosting
{
    public static class HostingProviderFactory
    {
        /// <summary>Detects the hosting provider from a remote URL, or null if unrecognized.</summary>
        public static IHostingProvider Create(string remoteUrl)
        {
            var (domain, path) = GitRemoteUrlParser.Parse(remoteUrl);
            if (domain == null || string.IsNullOrEmpty(path)) return null;

            var segments = path.Split('/');
            if (segments.Length < 2) return null;
            var slug = segments[segments.Length - 1];
            var owner = string.Join("/", segments, 0, segments.Length - 1);
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(owner)) return null;

            if (domain.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return new GitHubProvider(domain, owner, slug);
            if (domain.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
                return new GitLabProvider(domain, owner, slug);
            if (domain.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase))
                return new BitbucketCloudProvider(domain, owner, slug);

            // Self-hosted: the user maps custom domains to a provider kind in
            // Settings → Integrations (GitHub Enterprise / self-managed GitLab).
            var kind = AppSettings.LoadHostingDomainKind(domain);
            if (string.Equals(kind, "github", StringComparison.OrdinalIgnoreCase))
                return new GitHubProvider(domain, owner, slug);
            if (string.Equals(kind, "gitlab", StringComparison.OrdinalIgnoreCase))
                return new GitLabProvider(domain, owner, slug);
            return null;
        }
    }
}
