using System.Collections.Generic;

namespace PickleGit.Models
{
    /// <summary>Per-commit bisect marker for the graph badge — populated during the same history
    /// walk that sets CommitInfo.IsHead, cross-referenced against the current BisectState.</summary>
    public enum BisectMark { None, Good, Bad, Skip, Current }

    /// <summary>Snapshot of an in-progress `git bisect` session, reconstructed from `.git/BISECT_*`
    /// files (LibGit2Sharp exposes none of this) — see GitService.GetBisectState. Mirrors
    /// ConflictState's read-only-reconstruction convention so the banner survives an app restart.</summary>
    public class BisectState
    {
        public bool InProgress { get; set; }

        /// <summary>The original bad commit bisect was started with.</summary>
        public string BadSha { get; set; }

        public List<string> GoodShas { get; set; } = new List<string>();
        public List<string> SkippedShas { get; set; } = new List<string>();

        /// <summary>HEAD while bisecting — the commit currently under test.</summary>
        public string CurrentSha { get; set; }

        /// <summary>Parsed from the last step's stdout ("N revisions left to test after this
        /// (roughly M steps)"). -1 when unknown — e.g. right after an app restart, since git only
        /// prints this on the triggering command's own stdout, not into any state file.</summary>
        public int RevisionsLeft { get; set; } = -1;
        public int StepsRemaining { get; set; } = -1;

        /// <summary>Set transiently when a step's stdout contains "<sha> is the first bad commit" —
        /// not reconstructed on a cold restart (see GetBisectState's remarks).</summary>
        public bool Found { get; set; }
        public string FirstBadSha { get; set; }
        public string FirstBadSummary { get; set; }

        public bool HasBisect => InProgress;
    }
}
