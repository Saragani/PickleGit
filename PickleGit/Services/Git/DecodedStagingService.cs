using System.Collections.Generic;
using System.Linq;
using PickleGit.Models;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// Hunk/line stage, unstage and discard for diffs computed in-app from decoded text (UTF-16 etc.,
    /// see GitService.DecodedTextFallback). `git apply` can't be used for these: it splits patches on
    /// the byte 0x0A, which breaks UTF-16's "0A 00", and a patch built from decoded text never matches
    /// the raw blob bytes. Instead the selection is applied to the decoded lines here, and the caller
    /// re-encodes the result (same encoding, BOM and per-line terminators) and writes it to the index
    /// (stage/unstage) or the working file (discard).
    ///
    /// Direction rules match PatchBuilder.BuildLinePatch:
    ///  - Stage (forward, target = old side, the index): selected deletions are removed, selected
    ///    additions inserted; unselected deletions stay, unselected additions are left out.
    ///  - Unstage/Discard (reverse, target = new side — the index for Unstage, the working file for
    ///    Discard): selected additions are removed, selected deletions put back; unselected additions
    ///    stay, unselected deletions are left out.
    /// Only the given hunk is affected; everything outside it is copied from the target unchanged.
    /// </summary>
    public static class DecodedStagingService
    {
        /// <summary>Can hunk/line operations run on this decoded diff? Both sides must share an
        /// encoding and BOM — splicing UTF-8 lines into a UTF-16 file would corrupt it.</summary>
        public static bool CanApplyPartially(FileDiffResult diff) =>
            diff != null && diff.IsDecodedText && diff.OldText != null && diff.NewText != null
            && diff.OldText.SameEncodingAs(diff.NewText);

        /// <summary>Returns the encoded bytes of the target side after applying <paramref name="selected"/>
        /// (or the whole hunk when it's empty) in the direction implied by <paramref name="op"/>.</summary>
        public static byte[] Apply(FileDiffResult diff, DiffHunk hunk, ISet<DiffLine> selected, StagingPatchOp op)
        {
            bool forward = op == StagingPatchOp.Stage;
            var target = forward ? diff.OldText : diff.NewText;
            var other = forward ? diff.NewText : diff.OldText;

            bool IsSelected(DiffLine l) => selected == null || selected.Count == 0 || selected.Contains(l);

            var lines = new List<string>();
            var terms = new List<string>();

            // Where the hunk starts in the target, 0-based: the first target-side line number in it, or
            // (pure insertion/deletion with no target lines) the header start, which git reports as the
            // line *before* an empty range.
            int? firstTargetLine = hunk.Lines
                .Select(l => forward ? l.OldLineNumber : l.NewLineNumber)
                .FirstOrDefault(n => n.HasValue);
            int start = firstTargetLine.HasValue ? firstTargetLine.Value - 1 : (forward ? hunk.OldStart : hunk.NewStart);
            if (start < 0) start = 0;
            if (start > target.Lines.Count) start = target.Lines.Count;

            for (int i = 0; i < start; i++) { lines.Add(target.Lines[i]); terms.Add(target.Terminators[i]); }

            int consumed = 0;
            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Context:
                        AddFrom(target, (forward ? line.OldLineNumber : line.NewLineNumber).Value - 1, lines, terms);
                        consumed++;
                        break;
                    case DiffLineKind.Deleted:
                        if (forward)
                        {
                            // Line exists in the target (old side): keep it unless its deletion is selected.
                            if (!IsSelected(line)) AddFrom(target, line.OldLineNumber.Value - 1, lines, terms);
                            consumed++;
                        }
                        else if (IsSelected(line))
                            AddFrom(other, line.OldLineNumber.Value - 1, lines, terms); // put the deleted line back
                        break;
                    case DiffLineKind.Added:
                        if (!forward)
                        {
                            // Line exists in the target (new side): keep it unless its addition is selected.
                            if (!IsSelected(line)) AddFrom(target, line.NewLineNumber.Value - 1, lines, terms);
                            consumed++;
                        }
                        else if (IsSelected(line))
                            AddFrom(other, line.NewLineNumber.Value - 1, lines, terms); // bring the added line in
                        break;
                    // Header ("\ No newline at end of file") carries no content of its own.
                }
            }

            for (int i = start + consumed; i < target.Lines.Count; i++) { lines.Add(target.Lines[i]); terms.Add(target.Terminators[i]); }

            // A line that had no terminator (it was the last line of its side) but is no longer last
            // needs one, or it would run into the next line.
            var defaultTerm = DominantTerminator(target) ?? DominantTerminator(other) ?? "\n";
            for (int i = 0; i < lines.Count - 1; i++)
                if (terms[i].Length == 0) terms[i] = defaultTerm;

            return target.Encode(lines, terms);
        }

        private static void AddFrom(DecodedText side, int index, List<string> lines, List<string> terms)
        {
            lines.Add(side.Lines[index]);
            terms.Add(side.Terminators[index]);
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
