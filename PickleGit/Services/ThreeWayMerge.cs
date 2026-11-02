using System.Collections.Generic;
using System.Text;

namespace PickleGit.Services
{
    /// <summary>Raw bytes of a conflicted file's index stages (GitService.GetConflictStageBytes); a
    /// stage is null when that side has no version. Only produced for conflicts git treated as binary.</summary>
    public sealed class ConflictStages
    {
        public byte[] Base { get; set; }
        public byte[] Ours { get; set; }
        public byte[] Theirs { get; set; }

        /// <summary>A stage exceeded GitService.MaxRebuildBlobBytes, so no bytes were loaded.</summary>
        public bool TooLarge { get; set; }

        /// <summary>A stage's first bytes show it isn't decodable text (a genuine binary asset), so
        /// no bytes were loaded.</summary>
        public bool NotText { get; set; }
    }

    /// <summary>
    /// In-app line-based three-way merge (diff3), for conflicts git refuses to text-merge because it
    /// treats the file as binary (any NUL byte — so every UTF-16 file). git leaves only "ours" in the
    /// working file with no conflict markers; this rebuilds the conflict from the three index stages
    /// and emits the same marker text git would have written (diff3 style, with the base section), so
    /// the merge editor's marker-driven parser (MergeConflictParser) can show and resolve it as usual.
    ///
    /// Chunking follows the classic diff3 algorithm: base lines matched in BOTH ours and theirs are
    /// stable; everything between two stable lines is one chunk. A chunk changed on only one side,
    /// or changed identically on both, merges cleanly; otherwise it becomes a conflict block.
    /// </summary>
    public static class ThreeWayMerge
    {
        public sealed class Result
        {
            /// <summary>Merged text with conflict markers, using ours' line terminator and final-newline state.</summary>
            public string MarkedText { get; set; }
            public int ConflictCount { get; set; }
        }

        /// <param name="includeBase">true = diff3 style (with the "|||||||" base section);
        /// false = git's default "merge" style (ours/theirs only).</param>
        public static Result Merge(DecodedText baseText, DecodedText ours, DecodedText theirs,
            string oursLabel, string baseLabel, string theirsLabel, bool includeBase = true)
        {
            var o = baseText?.Lines ?? new List<string>();
            var a = ours?.Lines ?? new List<string>();
            var b = theirs?.Lines ?? new List<string>();
            var mapA = LineDiff.MatchLines(o, a);
            var mapB = LineDiff.MatchLines(o, b);

            var nl = DominantTerminator(ours) ?? DominantTerminator(theirs) ?? DominantTerminator(baseText) ?? "\n";
            var output = new List<string>();
            int conflicts = 0;

            int i = 0, j = 0, k = 0;
            while (true)
            {
                // Next base line that both sides kept.
                int l = i;
                while (l < o.Count && !(mapA[l] >= 0 && mapB[l] >= 0)) l++;

                if (l < o.Count && l == i && mapA[l] == j && mapB[l] == k)
                {
                    output.Add(a[j]); // stable: identical in all three
                    i++; j++; k++;
                    continue;
                }

                int aEnd = l < o.Count ? mapA[l] : a.Count;
                int bEnd = l < o.Count ? mapB[l] : b.Count;
                var oc = Slice(o, i, l);
                var ac = Slice(a, j, aEnd);
                var bc = Slice(b, k, bEnd);

                if (SameLines(ac, oc)) output.AddRange(bc);            // only theirs changed
                else if (SameLines(bc, oc)) output.AddRange(ac);       // only ours changed
                else if (SameLines(ac, bc)) output.AddRange(ac);       // both made the same change
                else
                {
                    conflicts++;
                    output.Add("<<<<<<< " + oursLabel);
                    output.AddRange(ac);
                    if (includeBase)
                    {
                        output.Add("||||||| " + baseLabel);
                        output.AddRange(oc);
                    }
                    output.Add("=======");
                    output.AddRange(bc);
                    output.Add(">>>>>>> " + theirsLabel);
                }

                i = l; j = aEnd; k = bEnd;
                if (l >= o.Count) break;
            }

            var sb = new StringBuilder();
            for (int n = 0; n < output.Count; n++)
            {
                sb.Append(output[n]);
                bool last = n == output.Count - 1;
                if (!last || ours == null || !ours.MissingFinalNewline) sb.Append(nl);
            }
            return new Result { MarkedText = sb.ToString(), ConflictCount = conflicts };
        }

        private static List<string> Slice(List<string> src, int from, int to)
        {
            var r = new List<string>(to > from ? to - from : 0);
            for (int x = from; x < to; x++) r.Add(src[x]);
            return r;
        }

        private static bool SameLines(List<string> x, List<string> y)
        {
            if (x.Count != y.Count) return false;
            for (int n = 0; n < x.Count; n++) if (x[n] != y[n]) return false;
            return true;
        }

        private static string DominantTerminator(DecodedText text)
        {
            if (text == null) return null;
            int crlf = 0, lf = 0, cr = 0;
            foreach (var t in text.Terminators)
            {
                if (t == "\r\n") crlf++;
                else if (t == "\n") lf++;
                else if (t == "\r") cr++;
            }
            if (crlf + lf + cr == 0) return null;
            return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
        }
    }
}
