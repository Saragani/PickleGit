using System.Collections.Generic;

namespace PickleGit.Models
{
    public enum ConflictOperation { None, Merge, CherryPick, Revert, Rebase }

    /// <summary>
    /// Snapshot of an in-progress merge/cherry-pick/revert/rebase, detected from
    /// .git state files (MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, rebase-merge/
    /// rebase-apply) plus the index's conflict entries. See GitService.GetConflictState.
    /// </summary>
    public class ConflictState
    {
        public ConflictOperation Operation { get; set; } = ConflictOperation.None;

        /// <summary>Human-readable source: branch name / commit being merged, cherry-picked, etc.</summary>
        public string SourceDescription { get; set; }

        /// <summary>Rebase step counters (1-based); 0 when not rebasing.</summary>
        public int RebaseStepCurrent { get; set; }
        public int RebaseStepTotal { get; set; }

        public List<string> ConflictedFiles { get; set; } = new List<string>();

        public bool HasConflicts => Operation != ConflictOperation.None;

        public string OperationLabel
        {
            get
            {
                switch (Operation)
                {
                    case ConflictOperation.Merge: return "MERGE IN PROGRESS";
                    case ConflictOperation.CherryPick: return "CHERRY-PICK IN PROGRESS";
                    case ConflictOperation.Revert: return "REVERT IN PROGRESS";
                    case ConflictOperation.Rebase: return "REBASE IN PROGRESS";
                    default: return string.Empty;
                }
            }
        }
    }
}
