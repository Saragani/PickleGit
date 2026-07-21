using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PickleGit.Services.Hosting
{
    /// <summary>
    /// Bitbucket Cloud (bitbucket.org). Auth is HTTP Basic with the account username and an
    /// app password (Bitbucket has no bearer-token PR API for app passwords), unlike GitHub/GitLab.
    /// </summary>
    public sealed class BitbucketCloudProvider : IHostingProvider
    {
        public string ProviderName => "Bitbucket";
        public string Domain { get; }
        public string Owner { get; }
        public string Slug { get; }

        public BitbucketCloudProvider(string domain, string owner, string slug)
        {
            Domain = domain; Owner = owner; Slug = slug;
        }

        public bool IsConfigured => HostingCredentials.Load(Domain).secret != null;

        public async Task<List<PullRequestInfo>> GetPullRequestsAsync()
        {
            var (username, secret) = HostingCredentials.Load(Domain);

            using (var http = new HttpClient())
            {
                if (secret != null)
                {
                    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}"));
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
                }

                var url = $"https://api.bitbucket.org/2.0/repositories/{Owner}/{Slug}/pullrequests?state=OPEN";
                var response = await http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new List<PullRequestInfo>();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var root = JObject.Parse(json);
                var arr = root["values"] as JArray ?? new JArray();

                var list = new List<PullRequestInfo>();
                foreach (var item in arr)
                {
                    list.Add(new PullRequestInfo
                    {
                        Id = item["id"]?.ToString(),
                        Title = item["title"]?.ToString(),
                        Author = item["author"]?["display_name"]?.ToString(),
                        SourceBranch = item["source"]?["branch"]?["name"]?.ToString(),
                        TargetBranch = item["destination"]?["branch"]?["name"]?.ToString(),
                        WebUrl = item["links"]?["html"]?["href"]?.ToString(),
                        State = item["state"]?.ToString()
                    });
                }
                return list;
            }
        }

        public async Task<PullRequestInfo> CreatePullRequestAsync(string title, string description, string sourceBranch, string targetBranch, bool draft)
        {
            // Bitbucket Cloud has no draft-PR concept — the flag is accepted but has no effect here.
            var (username, secret) = HostingCredentials.Load(Domain);
            if (secret == null) throw new InvalidOperationException("No Bitbucket app-password configured for this domain.");

            using (var http = new HttpClient())
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

                var body = new JObject
                {
                    ["title"] = title,
                    ["description"] = description ?? string.Empty,
                    ["source"] = new JObject { ["branch"] = new JObject { ["name"] = sourceBranch } },
                    ["destination"] = new JObject { ["branch"] = new JObject { ["name"] = targetBranch } }
                };
                var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var url = $"https://api.bitbucket.org/2.0/repositories/{Owner}/{Slug}/pullrequests";
                var response = await http.PostAsync(url, content).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(ExtractBitbucketError(json, response.StatusCode));

                var item = JObject.Parse(json);
                return new PullRequestInfo
                {
                    Id = item["id"]?.ToString(),
                    Title = item["title"]?.ToString(),
                    Author = item["author"]?["display_name"]?.ToString(),
                    SourceBranch = item["source"]?["branch"]?["name"]?.ToString(),
                    TargetBranch = item["destination"]?["branch"]?["name"]?.ToString(),
                    WebUrl = item["links"]?["html"]?["href"]?.ToString(),
                    State = item["state"]?.ToString()
                };
            }
        }

        private static string ExtractBitbucketError(string json, System.Net.HttpStatusCode status)
        {
            try
            {
                var obj = JObject.Parse(json);
                var msg = obj["error"]?["message"]?.ToString();
                return msg ?? $"HTTP {(int)status}: {json}";
            }
            catch { return $"HTTP {(int)status}: {json}"; }
        }

        public string GetCreatePrUrl(string sourceBranch, string targetBranch, string title = null, string description = null)
            => $"https://bitbucket.org/{Owner}/{Slug}/pull-requests/new" +
               $"?source={Uri.EscapeDataString(sourceBranch)}&dest={Uri.EscapeDataString(targetBranch)}";

        public string GetRepoWebUrl() => $"https://bitbucket.org/{Owner}/{Slug}";
    }
}
