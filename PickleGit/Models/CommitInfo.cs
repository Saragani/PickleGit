using System;
using System.Collections.Generic;

namespace PickleGit.Models
{
    public class CommitInfo
    {
        public string Sha { get; set; }
        public string ShortSha => (!IsUncommitted && Sha != null) ? Sha.Substring(0, Math.Min(7, Sha.Length)) : string.Empty;
        public string Message { get; set; }
        public string MessageShort
        {
            get
            {
                if (Message == null) return string.Empty;
                var idx = Message.IndexOf('\n');
                return idx >= 0 ? Message.Substring(0, idx).Trim() : Message.Trim();
            }
        }
        public string AuthorName { get; set; }
        public string AuthorEmail { get; set; }
        public DateTimeOffset AuthorDate { get; set; }
        public string CommitterName { get; set; }
        public string CommitterEmail { get; set; }
        public DateTimeOffset CommitterDate { get; set; }
        public List<string> ParentShas { get; set; } = new List<string>();
        public List<string> Refs { get; set; } = new List<string>();
        public bool IsHead { get; set; }
        public bool IsStash { get; set; }
        public bool IsUncommitted { get; set; }

        /// <summary>Bisect good/bad/skip/current badge — set during the same history walk that
        /// computes IsHead/RefMask, cross-referenced against the active BisectState. None when no
        /// bisect is in progress.</summary>
        public BisectMark BisectMark { get; set; } = BisectMark.None;

        /// <summary>
        /// Branch-membership bit mask computed during the history walk.
        /// Bit 0 = reachable from the current branch (HEAD); bits 1..63 map to
        /// other branch tips (see CommitHistory.BranchMasks). 0 = unknown.
        /// </summary>
        public ulong RefMask { get; set; }

        public string AuthorDateRelative
        {
            get
            {
                var span = DateTimeOffset.Now - AuthorDate;
                if (span.TotalSeconds < 60) return "just now";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
                if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
                if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
                return $"{(int)(span.TotalDays / 365)}y ago";
            }
        }
    }

    /// <summary>Result of a single-walk history query (see GitService.GetHistory).</summary>
    public class CommitHistory
    {
        public List<CommitInfo> Commits { get; set; } = new List<CommitInfo>();

        /// <summary>Mask bit for each branch (by friendly name). The current branch is always bit 0 (1UL).</summary>
        public Dictionary<string, ulong> BranchMasks { get; set; } = new Dictionary<string, ulong>();

        /// <summary>True when the walk stopped at the commit ceiling — more history exists.</summary>
        public bool ReachedLimit { get; set; }

        /// <summary>Mask matching commits reachable from the current branch / HEAD.</summary>
        public const ulong CurrentBranchMask = 1UL;
    }
}
