using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PickleGit.Services.Hosting
{
    public sealed class GitHubProvider : IHostingProvider
    {
        public string ProviderName => "GitHub";
        public string Domain { get; }
        public string Owner { get; }
        public string Slug { get; }

        public GitHubProvider(string domain, string owner, string slug)
        {
            Domain = domain; Owner = owner; Slug = slug;
            // GitHub Enterprise Server hosts its REST API at https://<host>/api/v3
            ApiBase = domain.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? "https://api.github.com"
                : $"https://{domain}/api/v3";
        }

        private string ApiBase { get; }

        public bool IsConfigured => HostingCredentials.Load(Domain).secret != null;

        public async Task<List<PullRequestInfo>> GetPullRequestsAsync()
        {
            var (_, token) = HostingCredentials.Load(Domain);

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PickleGit", "1.0"));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                // Public repos work unauthenticated (at GitHub's lower rate limit); attach a token
                // when configured for private repos and higher limits.
                if (token != null)
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

                var url = $"{ApiBase}/repos/{Owner}/{Slug}/pulls?state=open";
                var response = await http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new List<PullRequestInfo>();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var arr = JArray.Parse(json);

                var list = new List<PullRequestInfo>();
                foreach (var item in arr)
                {
                    list.Add(new PullRequestInfo
                    {
                        Id = item["number"]?.ToString(),
                        Title = item["title"]?.ToString(),
                        Author = item["user"]?["login"]?.ToString(),
                        SourceBranch = item["head"]?["ref"]?.ToString(),
                        TargetBranch = item["base"]?["ref"]?.ToString(),
                        WebUrl = item["html_url"]?.ToString(),
                        State = item["state"]?.ToString()
                    });
                }
                return list;
            }
        }

        public async Task<PullRequestInfo> CreatePullRequestAsync(string title, string description, string sourceBranch, string targetBranch, bool draft)
        {
            var (_, token) = HostingCredentials.Load(Domain);
            if (token == null) throw new InvalidOperationException("No GitHub token configured for this domain.");

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PickleGit", "1.0"));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

                var body = new JObject
                {
                    ["title"] = title,
                    ["head"] = sourceBranch,
                    ["base"] = targetBranch,
                    ["body"] = description ?? string.Empty,
                    ["draft"] = draft
                };
                var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var url = $"{ApiBase}/repos/{Owner}/{Slug}/pulls";
                var response = await http.PostAsync(url, content).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(ExtractGitHubError(json, response.StatusCode));

                var item = JObject.Parse(json);
                return new PullRequestInfo
                {
                    Id = item["number"]?.ToString(),
                    Title = item["title"]?.ToString(),
                    Author = item["user"]?["login"]?.ToString(),
                    SourceBranch = item["head"]?["ref"]?.ToString(),
                    TargetBranch = item["base"]?["ref"]?.ToString(),
                    WebUrl = item["html_url"]?.ToString(),
                    State = item["state"]?.ToString()
                };
            }
        }

        private static string ExtractGitHubError(string json, System.Net.HttpStatusCode status)
        {
            try
            {
                var obj = JObject.Parse(json);
                var msg = obj["message"]?.ToString();
                var errors = obj["errors"] as JArray;
                if (errors != null && errors.Count > 0)
                {
                    var details = string.Join("; ", errors.Select(e => e["message"]?.ToString() ?? e.ToString()));
                    return string.IsNullOrEmpty(msg) ? details : $"{msg}: {details}";
                }
                return string.IsNullOrEmpty(msg) ? $"HTTP {(int)status}" : msg;
            }
            catch { return $"HTTP {(int)status}: {json}"; }
        }

        public string GetCreatePrUrl(string sourceBranch, string targetBranch, string title = null, string description = null)
        {
            var url = $"https://{Domain}/{Owner}/{Slug}/compare/{Uri.EscapeDataString(targetBranch)}...{Uri.EscapeDataString(sourceBranch)}?expand=1";
            if (!string.IsNullOrEmpty(title)) url += $"&title={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrEmpty(description)) url += $"&body={Uri.EscapeDataString(description)}";
            return url;
        }

        public string GetRepoWebUrl() => $"https://{Domain}/{Owner}/{Slug}";
    }
}
