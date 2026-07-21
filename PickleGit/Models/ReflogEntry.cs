using System;

namespace PickleGit.Models
{
    /// <summary>One line of .git/logs/HEAD — the safety net behind Undo (see Services/GitService.GetReflog).</summary>
    public class ReflogEntry
    {
        public int Index { get; set; }
        public string OldSha { get; set; }
        public string NewSha { get; set; }
        public string Message { get; set; }
        public DateTimeOffset Timestamp { get; set; }

        public string ShortNewSha => !string.IsNullOrEmpty(NewSha) ? NewSha.Substring(0, Math.Min(7, NewSha.Length)) : string.Empty;
        public string Selector => $"HEAD@{{{Index}}}";
    }
}
