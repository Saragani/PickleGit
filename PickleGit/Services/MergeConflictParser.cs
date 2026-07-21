using System.Collections.Generic;
using System.Text;
using PickleGit.Models;

namespace PickleGit.Services
{
    /// <summary>
    /// Parses a working-tree file containing git conflict markers into a document with
    /// separate "ours"/"theirs" reconstructions plus the individual conflict blocks and their
    /// original interleaved order (Items), for MergeConflictFileViewModel. Supports plain and
    /// diff3-style (with a ||||||| base section) markers.
    /// </summary>
    public static class MergeConflictParser
    {
        public static MergeConflictDocument Parse(string content)
        {
            var newline = content.IndexOf("\r\n", System.StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var normalized = content.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');

            var doc = new MergeConflictDocument { Newline = newline };
            var oursSb = new StringBuilder();
            var theirsSb = new StringBuilder();
            var context = new List<string>();

            int state = 0; // 0=context, 1=ours, 2=base, 3=theirs
            List<string> oursLines = null, theirsLines = null, baseLines = null, rawLines = null;
            string oursLabel = null;
            int blockIndex = 0;
            // Derived from each block's own opening marker rather than hardcoded to 7 — git's
            // conflict-marker-size gitattribute can widen markers (e.g. for nested conflicts),
            // and a file using a non-default size would otherwise never match "<<<<<<< " and
            // silently parse as zero conflict blocks.
            int markerLen = 7;

            void FlushContext()
            {
                if (context.Count > 0)
                    doc.Items.Add(new ConflictDocItem
                    {
                        Kind = ConflictDocItemKind.Context,
                        ContextText = string.Join("\n", context)
                    });
                foreach (var l in context)
                {
                    oursSb.Append(l).Append('\n');
                    theirsSb.Append(l).Append('\n');
                }
                context.Clear();
            }

            // A block opened but never closed before EOF (hand-edited or truncated file) would
            // otherwise have its accumulated ours/theirs/base lines silently dropped — only the
            // context buffer got flushed at the end. Folding the raw marker + body back into
            // context at least preserves the content in both reconstructions instead of losing it.
            void FlushUnterminatedBlock()
            {
                if (rawLines == null) return;
                context.AddRange(rawLines);
                rawLines = null;
            }

            bool IsMarkerLine(string line, char c, out int len)
            {
                len = 0;
                while (len < line.Length && line[len] == c) len++;
                return len >= 4 && (len == line.Length || line[len] == ' ');
            }

            string LabelAfterMarker(string line, int len) => line.Length > len + 1 ? line.Substring(len + 1).Trim() : null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (state == 0 && IsMarkerLine(line, '<', out var openLen))
                {
                    FlushContext();
                    state = 1;
                    markerLen = openLen;
                    oursLines = new List<string>();
                    theirsLines = new List<string>();
                    baseLines = new List<string>();
                    rawLines = new List<string> { line };
                    oursLabel = LabelAfterMarker(line, markerLen);
                    continue;
                }
                if (state == 1 && IsMarkerLine(line, '|', out var baseLen) && baseLen == markerLen)
                {
                    state = 2;
                    rawLines.Add(line);
                    continue;
                }
                if ((state == 1 || state == 2) && line == new string('=', markerLen))
                {
                    state = 3;
                    rawLines.Add(line);
                    continue;
                }
                if (state == 3 && IsMarkerLine(line, '>', out var closeLen) && closeLen == markerLen)
                {
                    rawLines.Add(line);
                    var theirsLabel = LabelAfterMarker(line, markerLen);
                    var block = new MergeConflictBlock
                    {
                        Index = blockIndex++,
                        OursLabel = string.IsNullOrEmpty(oursLabel) ? "Ours (HEAD)" : oursLabel,
                        TheirsLabel = string.IsNullOrEmpty(theirsLabel) ? "Theirs" : theirsLabel,
                        OursText = string.Join("\n", oursLines),
                        TheirsText = string.Join("\n", theirsLines),
                        BaseText = baseLines.Count > 0 ? string.Join("\n", baseLines) : null,
                        RawText = string.Join("\n", rawLines).Replace("\n", newline)
                    };
                    doc.Blocks.Add(block);
                    doc.Items.Add(new ConflictDocItem { Kind = ConflictDocItemKind.Block, Block = block });
                    if (oursLines.Count > 0) oursSb.Append(block.OursText).Append('\n');
                    if (theirsLines.Count > 0) theirsSb.Append(block.TheirsText).Append('\n');
                    state = 0;
                    rawLines = null;
                    continue;
                }

                switch (state)
                {
                    case 0: context.Add(line); break;
                    case 1: oursLines.Add(line); rawLines.Add(line); break;
                    case 2: baseLines.Add(line); rawLines.Add(line); break;
                    case 3: theirsLines.Add(line); rawLines.Add(line); break;
                }
            }
            FlushUnterminatedBlock();
            FlushContext();

            doc.OursText = TrimTrailingNewline(oursSb.ToString()).Replace("\n", newline);
            doc.TheirsText = TrimTrailingNewline(theirsSb.ToString()).Replace("\n", newline);
            return doc;
        }

        private static string TrimTrailingNewline(string s)
            => s.EndsWith("\n") ? s.Substring(0, s.Length - 1) : s;
    }
}
