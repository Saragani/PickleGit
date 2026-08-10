using System;
using System.Collections.Generic;

namespace PickleGit.Services
{
    /// <summary>
    /// Small, independent longest-common-subsequence alignment helper for the merge-conflict
    /// editor (ViewModels/MergeConflictEditorViewModel.cs) — pairs a conflict block's Ours lines
    /// against its Theirs lines for side-by-side display.
    ///
    /// Deliberately NOT sharing code with GitService's existing private word-diff
    /// (ComputeWordDiff/ComputeWordDiffsForHunk): that method is tightly coupled to DiffLine's
    /// leading +/- marker character and is part of the normal file-diff pipeline, which CLAUDE.md
    /// documents as having caused a real silent-blank-pane bug before. Refactoring it purely to
    /// share ~30 lines of DP would risk that well-tested path for no functional benefit — this
    /// class's inputs (plain line strings, no marker prefix) are different anyway.
    /// </summary>
    public static class LcsAligner
    {
        /// <summary>One row of an alignment: at least one of LeftIndex/RightIndex is &gt;= 0.
        /// Matched is true only when both are present and the underlying tokens are equal.</summary>
        public struct AlignedPair
        {
            public int LeftIndex;
            public int RightIndex;
            public bool Matched;
        }

        /// <summary>Lines/tokens beyond this count skip the O(n*m) DP entirely (guards against a
        /// pathological huge conflict block) and fall back to "all left, then all right",
        /// unmatched — still correct to render, just without alignment.</summary>
        private const int MaxAlignTokens = 2000;

        public static List<AlignedPair> Align(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            int n = left.Count, m = right.Count;
            var pairs = new List<AlignedPair>();

            if (n > MaxAlignTokens || m > MaxAlignTokens)
            {
                for (int i = 0; i < n; i++) pairs.Add(new AlignedPair { LeftIndex = i, RightIndex = -1 });
                for (int j = 0; j < m; j++) pairs.Add(new AlignedPair { LeftIndex = -1, RightIndex = j });
                return pairs;
            }

            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = left[i] == right[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            int oi = 0, ni = 0;
            while (oi < n && ni < m)
            {
                if (left[oi] == right[ni])
                {
                    pairs.Add(new AlignedPair { LeftIndex = oi, RightIndex = ni, Matched = true });
                    oi++; ni++;
                }
                else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
                {
                    pairs.Add(new AlignedPair { LeftIndex = oi, RightIndex = -1 });
                    oi++;
                }
                else
                {
                    pairs.Add(new AlignedPair { LeftIndex = -1, RightIndex = ni });
                    ni++;
                }
            }
            while (oi < n) { pairs.Add(new AlignedPair { LeftIndex = oi, RightIndex = -1 }); oi++; }
            while (ni < m) { pairs.Add(new AlignedPair { LeftIndex = -1, RightIndex = ni }); ni++; }
            return pairs;
        }
    }
}
