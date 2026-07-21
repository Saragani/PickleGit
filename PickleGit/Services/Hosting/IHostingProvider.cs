using System.Collections.Generic;
using System.Threading.Tasks;

namespace PickleGit.Services.Hosting
{
    /// <summary>
    /// One hosting service (GitHub / GitLab / Bitbucket Cloud) for a single repo, identified by
    /// owner/slug parsed from the remote URL. Auth token comes from CredentialStore, keyed by domain
    /// (see HostingProviderFactory) — never store or log the token itself.
    /// </summary>
    public interface IHostingProvider
    {
        string ProviderName { get; }
        string Domain { get; }
        string Owner { get; }
        string Slug { get; }

        /// <summary>True when a token/app-password is stored for this provider's domain.</summary>
        bool IsConfigured { get; }

        Task<List<PullRequestInfo>> GetPullRequestsAsync();

        /// <summary>Creates the pull/merge request via the provider's REST API. Requires IsConfigured.</summary>
        Task<PullRequestInfo> CreatePullRequestAsync(string title, string description, string sourceBranch, string targetBranch, bool draft);

        /// <summary>Browser-based "compose PR" URL — no API call needed, works even when not configured.</summary>
        string GetCreatePrUrl(string sourceBranch, string targetBranch, string title = null, string description = null);

        /// <summary>Web URL for browsing the repo itself (used for "Open in browser").</summary>
        string GetRepoWebUrl();
    }
}
