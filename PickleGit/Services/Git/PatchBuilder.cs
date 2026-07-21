using System.Collections.Generic;
using System.Linq;
using System.Text;
using PickleGit.Models;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// Builds minimal unified-diff text for hunk/line staging, fed to
    /// `git apply --cached [--reverse] -` via stdin. LibGit2Sharp has no apply API,
    /// so partial staging goes through the CLI (see CLAUDE.md "Hybrid git backend").
    /// </summary>
    public static class PatchBuilder
    {
        /// <summary>Whole-hunk(s) patch — used for Stage Hunk / Discard Hunk / Unstage Hunk.</summary>
        public static string BuildPatch(string relativePath, IEnumerable<DiffHunk> hunks, FileChangeKind kind)
        {
            var path = relativePath.Replace('\\', '/');
            var sb = new StringBuilder();
            AppendFileHeader(sb, path, kind);
            foreach (var hunk in hunks)
            {
                sb.Append(hunk.Header).Append('\n');
                foreach (var line in hunk.Lines)
                    sb.Append(line.Content).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Line-level patch across one or more hunks.
        /// <paramref name="reverseTarget"/> selects which sibling changes in the same hunk need to be
        /// represented as context vs dropped: for a forward Stage (reverseTarget=false), the target
        /// (index) doesn't have ANY of this hunk's changes yet, so unselected added lines simply
        /// don't exist there — drop them; unselected deleted lines are still present — keep as context.
        /// For Unstage/Discard (reverseTarget=true, both apply with --reverse), the target (index or
        /// working tree, respectively) already reflects the FULL hunk, so it's the opposite: unselected
        /// added lines already exist there — keep as context; unselected deleted lines are already
        /// absent — drop them. Getting this backwards produces a hunk whose declared post-image
        /// doesn't match the real target content, and `git apply` rejects it with "patch does not
        /// apply" (verified by reproducing the failure directly).
        /// </summary>
        public static string BuildLinePatch(string relativePath,
            IEnumerable<(DiffHunk Hunk, HashSet<DiffLine> Selected)> groups, FileChangeKind kind, bool reverseTarget)
        {
            var path = relativePath.Replace('\\', '/');
            var sb = new StringBuilder();
            AppendFileHeader(sb, path, kind);
            foreach (var group in groups)
                AppendLineHunkBody(sb, group.Hunk, group.Selected, reverseTarget);
            return sb.ToString();
        }

        /// <summary>
        /// `git apply` only recognizes a hunk as creating or deleting a whole file when the patch
        /// marks one side as /dev/null (plus a "new file mode"/"deleted file mode" line) — a plain
        /// "--- a/x / +++ b/x" pair means "patch this existing file's content" and fails with
        /// "patch does not apply" against a file that's already gone (Deleted) or doesn't exist yet
        /// (Added/Untracked), which is exactly the shape of a fully add/delete diff's only hunk.
        /// </summary>
        private static void AppendFileHeader(StringBuilder sb, string path, FileChangeKind kind)
        {
            sb.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
            switch (kind)
            {
                case FileChangeKind.Deleted:
                    sb.Append("deleted file mode 100644\n");
                    sb.Append("--- a/").Append(path).Append('\n');
                    sb.Append("+++ /dev/null\n");
                    break;
                case FileChangeKind.Added:
                case FileChangeKind.Untracked:
                    sb.Append("new file mode 100644\n");
                    sb.Append("--- /dev/null\n");
                    sb.Append("+++ b/").Append(path).Append('\n');
                    break;
                default:
                    sb.Append("--- a/").Append(path).Append('\n');
                    sb.Append("+++ b/").Append(path).Append('\n');
                    break;
            }
        }

        private static void AppendLineHunkBody(StringBuilder sb, DiffHunk hunk, HashSet<DiffLine> selected, bool reverseTarget)
        {
            var body = new List<string>();
            int oldCount = 0, newCount = 0;
            // Tracks whether the line immediately before the one currently being processed made it
            // into the output body, so a following "\ No newline at end of file" marker (which always
            // refers to the line right before it) is only kept when that line is actually present.
            bool lastLineIncluded = false;
            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Context:
                        body.Add(line.Content);
                        oldCount++; newCount++;
                        lastLineIncluded = true;
                        break;
                    case DiffLineKind.Added:
                        if (selected.Contains(line))
                        {
                            body.Add(line.Content);
                            newCount++;
                            lastLineIncluded = true;
                        }
                        else if (reverseTarget)
                        {
                            // Already present in the target (index/working tree already has the
                            // full hunk applied) — keep as context so it isn't disturbed.
                            body.Add(" " + line.Content.Substring(1));
                            oldCount++; newCount++;
                            lastLineIncluded = true;
                        }
                        else
                        {
                            lastLineIncluded = false;
                        }
                        break;
                    case DiffLineKind.Deleted:
                        if (selected.Contains(line))
                        {
                            body.Add(line.Content);
                            oldCount++;
                            lastLineIncluded = true;
                        }
                        else if (!reverseTarget)
                        {
                            // Not staging this deletion — keep the line as unchanged context
                            body.Add(" " + line.Content.Substring(1));
                            oldCount++; newCount++;
                            lastLineIncluded = true;
                        }
                        else
                        {
                            // already absent from the target — omit entirely.
                            lastLineIncluded = false;
                        }
                        break;
                    case DiffLineKind.Header:
                        if (lastLineIncluded) body.Add(line.Content);
                        break;
                }
            }
            sb.Append($"@@ -{hunk.OldStart},{oldCount} +{hunk.NewStart},{newCount} @@").Append('\n');
            foreach (var l in body) sb.Append(l).Append('\n');
        }
    }
}
