using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PickleGit.Models;

namespace PickleGit.Services
{
    /// <summary>
    /// Small, independent longest-common-subsequence alignment helper for the merge-conflict
    /// editor (ViewModels/MergeConflictEditorViewModel.cs). Used for two unrelated purposes that
    /// share the same O(n*m) DP shape: pairing a conflict block's Ours lines against its Theirs
    /// lines for side-by-side display, and — for one matched-but-different line pair — aligning
    /// word tokens to compute intra-line highlight spans.
    ///
    /// Deliberately NOT sharing code with GitService's existing private word-diff
    /// (ComputeWordDiff/ComputeWordDiffsForHunk): that method is tightly coupled to DiffLine's
    /// leading +/- marker character and is part of the normal file-diff pipeline, which CLAUDE.md
    /// documents as having caused a real silent-blank-pane bug before. Refactoring it purely to
    /// share ~30 lines of DP would risk that well-tested path for no functional benefit — this
    /// class's inputs (plain line/word strings, no marker prefix) are different anyway.
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

        private const int MaxWordDiffTokens = 300;

        private static readonly Regex WordTokenRegex = new Regex(@"\w+|\s+|[^\w\s]", RegexOptions.Compiled);

        public static List<string> TokenizeWords(string s)
        {
            var result = new List<string>();
            foreach (Match m in WordTokenRegex.Matches(s ?? string.Empty))
                result.Add(m.Value);
            return result;
        }

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

        /// <summary>Word-level intra-line diff between two arbitrary strings — no marker-char
        /// coupling, unlike GitService.ComputeWordDiff. Returns (null, null) when either side
        /// tokenizes past MaxWordDiffTokens (matches GitService's own cap for the same reason:
        /// bounding the O(n*m) DP).</summary>
        public static (List<DiffHighlightSpan> Left, List<DiffHighlightSpan> Right) DiffWords(string leftText, string rightText)
        {
            var leftTokens = TokenizeWords(leftText);
            var rightTokens = TokenizeWords(rightText);
            if (leftTokens.Count > MaxWordDiffTokens || rightTokens.Count > MaxWordDiffTokens)
                return (null, null);

            var pairs = Align(leftTokens, rightTokens);
            return (BuildSpans(leftTokens, pairs, isLeft: true), BuildSpans(rightTokens, pairs, isLeft: false));
        }

        private static List<DiffHighlightSpan> BuildSpans(List<string> tokens, List<AlignedPair> pairs, bool isLeft)
        {
            var spans = new List<DiffHighlightSpan>();
            int pos = 0, unmatchedStart = -1;
            foreach (var p in pairs)
            {
                int idx = isLeft ? p.LeftIndex : p.RightIndex;
                if (idx < 0) continue;
                if (p.Matched)
                {
                    if (unmatchedStart >= 0) { spans.Add(new DiffHighlightSpan(unmatchedStart, pos - unmatchedStart)); unmatchedStart = -1; }
                }
                else if (unmatchedStart < 0)
                {
                    unmatchedStart = pos;
                }
                pos += tokens[idx].Length;
            }
            if (unmatchedStart >= 0) spans.Add(new DiffHighlightSpan(unmatchedStart, pos - unmatchedStart));
            return spans;
        }
    }
}
