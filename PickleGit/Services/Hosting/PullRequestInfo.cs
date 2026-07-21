namespace PickleGit.Services.Hosting
{
    public class PullRequestInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string SourceBranch { get; set; }
        public string TargetBranch { get; set; }
        public string WebUrl { get; set; }
        public string State { get; set; }

        public string DisplayTitle => $"#{Id} {Title}";
        public string DisplayRoute => $"{SourceBranch} → {TargetBranch}";
    }
}
