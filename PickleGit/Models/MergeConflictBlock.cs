using System.Collections.Generic;

namespace PickleGit.Models
{
    public enum ConflictResolution { Unresolved, Ours, Theirs, Both, BothReverse, Base }

    /// <summary>One &lt;&lt;&lt;&lt;&lt;&lt;&lt; / ======= / &gt;&gt;&gt;&gt;&gt;&gt;&gt; region in a conflicted file.</summary>
    public class MergeConflictBlock
    {
        public int Index { get; set; }
        public string OursLabel { get; set; }
        public string TheirsLabel { get; set; }
        public string OursText { get; set; }
        public string TheirsText { get; set; }

        /// <summary>diff3-style common-ancestor text, or null when not present.</summary>
        public string BaseText { get; set; }

        /// <summary>Exact original marker span (including &lt;&lt;&lt;&lt;&lt;&lt;&lt;/=======/&gt;&gt;&gt;&gt;&gt;&gt;&gt;), used to locate and replace this block in the live result text.</summary>
        public string RawText { get; set; }

        public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    }

    /// <summary>Result of parsing a conflicted file's content (see Services/MergeConflictParser.cs).</summary>
    public class MergeConflictDocument
    {
        /// <summary>Full "ours" reconstruction: context lines + each block's ours side.</summary>
        public string OursText { get; set; }

        /// <summary>Full "theirs" reconstruction: context lines + each block's theirs side.</summary>
        public string TheirsText { get; set; }

        public List<MergeConflictBlock> Blocks { get; set; } = new List<MergeConflictBlock>();

        /// <summary>Detected line-ending style ("\r\n" or "\n") of the source file.</summary>
        public string Newline { get; set; } = "\n";
    }
}
