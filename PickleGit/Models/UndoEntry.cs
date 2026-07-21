namespace PickleGit.Models
{
    public enum UndoKind
    {
        /// <summary>Commit or amend — inverse is a soft reset (keeps the changes staged).</summary>
        Commit,
        /// <summary>Merge, cherry-pick, revert, reset — inverse is a hard reset (dirty-tree guarded).</summary>
        HeadHardMove,
        /// <summary>Branch/commit/tag checkout — inverse re-checks-out the previous ref.</summary>
        Checkout,
        /// <summary>Branch deletion — inverse recreates the branch at its old tip.</summary>
        BranchDelete
    }

    /// <summary>Enough state to reverse one mutating operation. See Services/UndoService.cs.</summary>
    public class UndoEntry
    {
        public UndoKind Kind { get; set; }
        public string Description { get; set; }

        public string PreHeadSha { get; set; }
        public string PostHeadSha { get; set; }

        /// <summary>Checkout: the branch that was active before the checkout (null if it was detached).</summary>
        /// <remarks>BranchDelete: the name of the deleted branch, to recreate.</remarks>
        public string BranchName { get; set; }

        /// <summary>BranchDelete: the branch's tip sha, to recreate it at the right commit.</summary>
        public string Sha { get; set; }
    }
}
