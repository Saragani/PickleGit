using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PickleGit.Services.Hosting
{
    public sealed class GitLabProvider : IHostingProvider
    {
        public string ProviderName => "GitLab";
        public string Domain { get; }
        public string Owner { get; }
        public string Slug { get; }

        public GitLabProvider(string domain, string owner, string slug)
        {
            Domain = domain; Owner = owner; Slug = slug;
        }

        public bool IsConfigured => HostingCredentials.Load(Domain).secret != null;

        private string ProjectPath => Uri.EscapeDataString($"{Owner}/{Slug}");

        public async Task<List<PullRequestInfo>> GetPullRequestsAsync()
        {
            var (_, token) = HostingCredentials.Load(Domain);

            using (var http = new HttpClient())
            {
                if (token != null)
                    http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);

                var url = $"https://{Domain}/api/v4/projects/{ProjectPath}/merge_requests?state=opened";
                var response = await http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new List<PullRequestInfo>();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var arr = JArray.Parse(json);

                var list = new List<PullRequestInfo>();
                foreach (var item in arr)
                {
                    list.Add(new PullRequestInfo
                    {
                        Id = item["iid"]?.ToString(),
                        Title = item["title"]?.ToString(),
                        Author = item["author"]?["username"]?.ToString(),
                        SourceBranch = item["source_branch"]?.ToString(),
                        TargetBranch = item["target_branch"]?.ToString(),
                        WebUrl = item["web_url"]?.ToString(),
                        State = item["state"]?.ToString()
                    });
                }
                return list;
            }
        }

        public async Task<PullRequestInfo> CreatePullRequestAsync(string title, string description, string sourceBranch, string targetBranch, bool draft)
        {
            var (_, token) = HostingCredentials.Load(Domain);
            if (token == null) throw new InvalidOperationException("No GitLab token configured for this domain.");

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);

                // GitLab's merge-request create endpoint has no boolean draft field — the
                // "Draft: " title prefix is what both the GitLab UI and API treat as draft.
                var effectiveTitle = draft && !(title ?? string.Empty).StartsWith("Draft:", StringComparison.OrdinalIgnoreCase)
                    ? "Draft: " + title : title;
                var body = new JObject
                {
                    ["source_branch"] = sourceBranch,
                    ["target_branch"] = targetBranch,
                    ["title"] = effectiveTitle,
                    ["description"] = description ?? string.Empty
                };
                var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var url = $"https://{Domain}/api/v4/projects/{ProjectPath}/merge_requests";
                var response = await http.PostAsync(url, content).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(ExtractGitLabError(json, response.StatusCode));

                var item = JObject.Parse(json);
                return new PullRequestInfo
                {
                    Id = item["iid"]?.ToString(),
                    Title = item["title"]?.ToString(),
                    Author = item["author"]?["username"]?.ToString(),
                    SourceBranch = item["source_branch"]?.ToString(),
                    TargetBranch = item["target_branch"]?.ToString(),
                    WebUrl = item["web_url"]?.ToString(),
                    State = item["state"]?.ToString()
                };
            }
        }

        private static string ExtractGitLabError(string json, System.Net.HttpStatusCode status)
        {
            try
            {
                var obj = JObject.Parse(json);
                var msg = obj["message"];
                return msg != null ? msg.ToString() : $"HTTP {(int)status}: {json}";
            }
            catch { return $"HTTP {(int)status}: {json}"; }
        }

        public string GetCreatePrUrl(string sourceBranch, string targetBranch, string title = null, string description = null)
        {
            var url = $"https://{Domain}/{Owner}/{Slug}/-/merge_requests/new" +
               $"?merge_request%5Bsource_branch%5D={Uri.EscapeDataString(sourceBranch)}" +
               $"&merge_request%5Btarget_branch%5D={Uri.EscapeDataString(targetBranch)}";
            if (!string.IsNullOrEmpty(title)) url += $"&merge_request%5Btitle%5D={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrEmpty(description)) url += $"&merge_request%5Bdescription%5D={Uri.EscapeDataString(description)}";
            return url;
        }

        public string GetRepoWebUrl() => $"https://{Domain}/{Owner}/{Slug}";
    }
}
