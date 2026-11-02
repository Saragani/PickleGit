using System;
using System.Collections.Generic;
using System.Text;
using PickleGit.Models;

namespace PickleGit.Services
{
    /// <summary>
    /// In-app line diff that produces the same <see cref="DiffHunk"/>/<see cref="DiffLine"/> shape as
    /// GitService.ParsePatch (header "@@ -a,b +c,d @@", "+"/"-"/" " prefixed Content, old/new line
    /// numbers, "\ No newline at end of file" markers as Header lines).
    ///
    /// Used for content libgit2 reports as binary but <see cref="TextDecoding"/> can decode (UTF-16
    /// and friends). Linear-space Myers (bisect / middle-snake recursion, as in diff-match-patch)
    /// so there is no line-count cap: memory is O(N+M), time O((N+M)·D).
    /// </summary>
    public static class LineDiff
    {
        private enum Op : byte { Equal, Delete, Insert }

        public static List<DiffHunk> Compute(DecodedText oldText, DecodedText newText, int contextLines, bool ignoreWhitespace)
        {
            var a = oldText?.Lines ?? new List<string>();
            var b = newText?.Lines ?? new List<string>();
            bool aNoEol = oldText != null && oldText.MissingFinalNewline;
            bool bNoEol = newText != null && newText.MissingFinalNewline;

            // Map each line to an int id so comparisons are cheap. The final line of a side without a
            // trailing newline gets a distinct key, so "x" and "x\n" compare as different (as in git).
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var ka = ToKeys(a, aNoEol, ignoreWhitespace, ids);
            var kb = ToKeys(b, bNoEol, ignoreWhitespace, ids);

            var ops = new List<Op>(a.Count + b.Count);
            new Differ(ka, kb, ops).Run();
            GroupDeletesFirst(ops);
            return BuildHunks(ops, a, b, aNoEol, bNoEol, Math.Max(0, contextLines));
        }

        /// <summary>The recursive split can interleave deletes and inserts inside one contiguous change
        /// block (e.g. "+x, -y, +z"). Reorder every such block to all deletes then all inserts, the way
        /// git presents it — the same edits, just in conventional order.</summary>
        private static void GroupDeletesFirst(List<Op> ops)
        {
            int i = 0;
            while (i < ops.Count)
            {
                if (ops[i] == Op.Equal) { i++; continue; }
                int start = i, deletes = 0;
                while (i < ops.Count && ops[i] != Op.Equal) { if (ops[i] == Op.Delete) deletes++; i++; }
                for (int j = start; j < i; j++)
                    ops[j] = j - start < deletes ? Op.Delete : Op.Insert;
            }
        }

        /// <summary>Line alignment between <paramref name="a"/> and <paramref name="b"/> (exact content
        /// match, same Myers search as <see cref="Compute"/>): for each index in a, the matched index
        /// in b, or -1 when that line of a was deleted. Matched indices are strictly increasing.
        /// Used by <see cref="ThreeWayMerge"/>.</summary>
        public static int[] MatchLines(IList<string> a, IList<string> b)
        {
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            int[] Keys(IList<string> lines)
            {
                var k = new int[lines.Count];
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!ids.TryGetValue(lines[i], out var id)) { id = ids.Count; ids[lines[i]] = id; }
                    k[i] = id;
                }
                return k;
            }
            var ka = Keys(a);
            var kb = Keys(b);
            var ops = new List<Op>(a.Count + b.Count);
            new Differ(ka, kb, ops).Run();

            var map = new int[a.Count];
            int ai = 0, bi = 0;
            foreach (var op in ops)
            {
                if (op == Op.Equal) map[ai++] = bi++;
                else if (op == Op.Delete) map[ai++] = -1;
                else bi++;
            }
            return map;
        }

        private static int[] ToKeys(List<string> lines, bool noEol, bool ignoreWhitespace, Dictionary<string, int> ids)
        {
            var keys = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                var k = ignoreWhitespace ? StripWhitespace(lines[i]) : lines[i];
                if (noEol && i == lines.Count - 1) k += "\0noeol";
                if (!ids.TryGetValue(k, out var id)) { id = ids.Count; ids[k] = id; }
                keys[i] = id;
            }
            return keys;
        }

        private static string StripWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Myers bisect diff over int arrays, emitting one Op per line in order.</summary>
        private sealed class Differ
        {
            private readonly int[] _a, _b;
            private readonly List<Op> _out;

            // Absolute safety net: past this budget, remaining sub-ranges are emitted as
            // delete-all/insert-all instead of searched — still a correct diff, just not minimal.
            // The cost limit in Bisect normally keeps runs far below this.
            private const long BudgetMs = 3000;
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

            public Differ(int[] a, int[] b, List<Op> output) { _a = a; _b = b; _out = output; }

            public void Run() => Diff(0, _a.Length, 0, _b.Length);

            private void Emit(Op op, int count) { for (int i = 0; i < count; i++) _out.Add(op); }

            private void Diff(int aLo, int aHi, int bLo, int bHi)
            {
                // Common prefix.
                int prefix = 0;
                while (aLo + prefix < aHi && bLo + prefix < bHi && _a[aLo + prefix] == _b[bLo + prefix]) prefix++;
                // Common suffix (not overlapping the prefix).
                int suffix = 0;
                while (aHi - suffix > aLo + prefix && bHi - suffix > bLo + prefix && _a[aHi - suffix - 1] == _b[bHi - suffix - 1]) suffix++;

                Emit(Op.Equal, prefix);
                int a0 = aLo + prefix, a1 = aHi - suffix, b0 = bLo + prefix, b1 = bHi - suffix;
                if (a0 == a1) Emit(Op.Insert, b1 - b0);
                else if (b0 == b1) Emit(Op.Delete, a1 - a0);
                else if (!ShareAnyLine(a0, a1, b0, b1)) { Emit(Op.Delete, a1 - a0); Emit(Op.Insert, b1 - b0); }
                else Bisect(a0, a1, b0, b1);
                Emit(Op.Equal, suffix);
            }

            /// <summary>Cheap pre-check: two ranges with no line in common need no search at all.</summary>
            private bool ShareAnyLine(int aLo, int aHi, int bLo, int bHi)
            {
                var inB = new HashSet<int>();
                for (int j = bLo; j < bHi; j++) inB.Add(_b[j]);
                for (int i = aLo; i < aHi; i++) if (inB.Contains(_a[i])) return true;
                return false;
            }

            private void Bisect(int aLo, int aHi, int bLo, int bHi)
            {
                int n = aHi - aLo, m = bHi - bLo;
                int maxD = (n + m + 1) / 2;
                int vOffset = maxD, vLength = 2 * maxD + 2;
                var v1 = new int[vLength];
                var v2 = new int[vLength];
                for (int i = 0; i < vLength; i++) { v1[i] = -1; v2[i] = -1; }
                v1[vOffset + 1] = 0;
                v2[vOffset + 1] = 0;
                int delta = n - m;
                bool front = (delta & 1) != 0;
                int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;

                // Cost limit (same idea as git's xdiff: sqrt of the range size, at least 256). A
                // sub-problem needing more edit steps than this is split at the furthest point the
                // forward search reached so far, and each half is diffed on its own — the result
                // stays a correct diff but may be slightly non-minimal, and time stays bounded
                // (a 20k-line file with every other line changed otherwise takes seconds).
                int costLimit = Math.Max(256, (int)Math.Sqrt(n + m + 3));
                int bestX = 0, bestY = 0;

                for (int d = 0; d < maxD; d++)
                {
                    if ((d & 63) == 0 && _clock.ElapsedMilliseconds > BudgetMs) break;
                    if (d > costLimit && bestX + bestY > 0) { Split(aLo, aHi, bLo, bHi, bestX, bestY); return; }
                    for (int k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
                    {
                        int k1Off = vOffset + k1;
                        int x1 = (k1 == -d || (k1 != d && v1[k1Off - 1] < v1[k1Off + 1])) ? v1[k1Off + 1] : v1[k1Off - 1] + 1;
                        int y1 = x1 - k1;
                        while (x1 < n && y1 < m && _a[aLo + x1] == _b[bLo + y1]) { x1++; y1++; }
                        v1[k1Off] = x1;
                        if (x1 <= n && y1 <= m && x1 + y1 > bestX + bestY && (x1 < n || y1 < m)) { bestX = x1; bestY = y1; }
                        if (x1 > n) k1End += 2;
                        else if (y1 > m) k1Start += 2;
                        else if (front)
                        {
                            int k2Off = vOffset + delta - k1;
                            if (k2Off >= 0 && k2Off < vLength && v2[k2Off] != -1)
                            {
                                int x2 = n - v2[k2Off];
                                if (x1 >= x2) { Split(aLo, aHi, bLo, bHi, x1, y1); return; }
                            }
                        }
                    }

                    for (int k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
                    {
                        int k2Off = vOffset + k2;
                        int x2 = (k2 == -d || (k2 != d && v2[k2Off - 1] < v2[k2Off + 1])) ? v2[k2Off + 1] : v2[k2Off - 1] + 1;
                        int y2 = x2 - k2;
                        while (x2 < n && y2 < m && _a[aHi - x2 - 1] == _b[bHi - y2 - 1]) { x2++; y2++; }
                        v2[k2Off] = x2;
                        if (x2 > n) k2End += 2;
                        else if (y2 > m) k2Start += 2;
                        else if (!front)
                        {
                            int k1Off = vOffset + delta - k2;
                            if (k1Off >= 0 && k1Off < vLength && v1[k1Off] != -1)
                            {
                                int x1 = v1[k1Off];
                                int y1 = vOffset + x1 - k1Off;
                                if (x1 >= n - x2) { Split(aLo, aHi, bLo, bHi, x1, y1); return; }
                            }
                        }
                    }
                }

                // No common subsequence at all (or the time budget ran out).
                Emit(Op.Delete, n);
                Emit(Op.Insert, m);
            }

            private void Split(int aLo, int aHi, int bLo, int bHi, int x, int y)
            {
                Diff(aLo, aLo + x, bLo, bLo + y);
                Diff(aLo + x, aHi, bLo + y, bHi);
            }
        }

        private static List<DiffHunk> BuildHunks(List<Op> ops, List<string> a, List<string> b,
            bool aNoEol, bool bNoEol, int context)
        {
            var hunks = new List<DiffHunk>();

            // Old/new line index reached after each op (prefix positions), to locate change ranges.
            int count = ops.Count;
            var oldPos = new int[count + 1];
            var newPos = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                oldPos[i + 1] = oldPos[i] + (ops[i] == Op.Insert ? 0 : 1);
                newPos[i + 1] = newPos[i] + (ops[i] == Op.Delete ? 0 : 1);
            }

            int idx = 0;
            while (idx < count)
            {
                while (idx < count && ops[idx] == Op.Equal) idx++;
                if (idx >= count) break;

                int start = Math.Max(0, idx - context);
                // Extend while the next change is within 2*context equal lines.
                int end = idx;
                while (true)
                {
                    while (end < count && ops[end] != Op.Equal) end++;
                    int eq = end;
                    while (eq < count && ops[eq] == Op.Equal && eq - end < 2 * context + 1) eq++;
                    if (eq < count && ops[eq] != Op.Equal && eq - end <= 2 * context) { end = eq; continue; }
                    end = Math.Min(count, end + context);
                    break;
                }

                hunks.Add(MakeHunk(ops, start, end, oldPos, newPos, a, b, aNoEol, bNoEol));
                idx = end;
            }
            return hunks;
        }

        private static DiffHunk MakeHunk(List<Op> ops, int start, int end, int[] oldPos, int[] newPos,
            List<string> a, List<string> b, bool aNoEol, bool bNoEol)
        {
            int oldStart = oldPos[start], newStart = newPos[start];
            int oldCount = oldPos[end] - oldStart, newCount = newPos[end] - newStart;
            var hunk = new DiffHunk
            {
                // git convention: an empty range is reported at the line before it (e.g. -0,0).
                OldStart = oldCount == 0 ? oldStart : oldStart + 1,
                NewStart = newCount == 0 ? newStart : newStart + 1,
            };
            hunk.Header = $"@@ -{Range(hunk.OldStart, oldCount)} +{Range(hunk.NewStart, newCount)} @@";

            const string NoEol = "\\ No newline at end of file";
            for (int i = start; i < end; i++)
            {
                int o = oldPos[i], n = newPos[i];
                switch (ops[i])
                {
                    case Op.Equal:
                        hunk.Lines.Add(new DiffLine { Content = " " + b[n], Kind = DiffLineKind.Context, OldLineNumber = o + 1, NewLineNumber = n + 1 });
                        if ((aNoEol && o == a.Count - 1) || (bNoEol && n == b.Count - 1))
                            hunk.Lines.Add(new DiffLine { Content = NoEol, Kind = DiffLineKind.Header });
                        break;
                    case Op.Delete:
                        hunk.Lines.Add(new DiffLine { Content = "-" + a[o], Kind = DiffLineKind.Deleted, OldLineNumber = o + 1 });
                        if (aNoEol && o == a.Count - 1)
                            hunk.Lines.Add(new DiffLine { Content = NoEol, Kind = DiffLineKind.Header });
                        break;
                    default:
                        hunk.Lines.Add(new DiffLine { Content = "+" + b[n], Kind = DiffLineKind.Added, NewLineNumber = n + 1 });
                        if (bNoEol && n == b.Count - 1)
                            hunk.Lines.Add(new DiffLine { Content = NoEol, Kind = DiffLineKind.Header });
                        break;
                }
            }
            return hunk;
        }

        private static string Range(int start, int count) => count == 1 ? start.ToString() : $"{start},{count}";
    }
}
