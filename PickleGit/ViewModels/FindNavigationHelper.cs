namespace PickleGit.ViewModels
{
    /// <summary>Shared wraparound-index math and status-string formatting for a "Find" feature
    /// navigating a list of matches (RepositoryViewModel.Diff.cs's "Find in diff" and
    /// MergeConflictSessionViewModel's Find bar both had their own copy of this — identical
    /// formula, identical wording — before this was extracted, so a future fix to either no
    /// longer risks only landing in one of the two.</summary>
    public static class FindNavigationHelper
    {
        /// <summary>Advances <paramref name="currentPos"/> by <paramref name="direction"/> (+1/-1),
        /// wrapping around <paramref name="count"/> in either direction. Caller must ensure
        /// <paramref name="count"/> is greater than zero.</summary>
        public static int Advance(int currentPos, int direction, int count) =>
            ((currentPos + direction) % count + count) % count;

        /// <summary>"N of M" status for the current match position (1-based for display).</summary>
        public static string PositionStatus(int pos, int count) => $"{pos + 1} of {count}";

        /// <summary>Status shown next to the search box: null while empty (hides the label
        /// entirely), "0 matches" / "N matches" once a term is typed but before navigating to
        /// the first result.</summary>
        public static string MatchCountStatus(string searchTerm, int matchCount) =>
            string.IsNullOrEmpty(searchTerm) ? null
                : matchCount == 0 ? "0 matches" : $"{matchCount} matches";
    }
}
