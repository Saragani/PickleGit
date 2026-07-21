using System;

namespace PickleGit.Models
{
    /// <summary>One line of a file annotated with the commit that last touched it.</summary>
    public class BlameLine
    {
        public int LineNumber { get; set; }
        public string Content { get; set; }
        public string Sha { get; set; }
        public string ShortSha => string.IsNullOrEmpty(Sha) ? string.Empty : Sha.Substring(0, Math.Min(7, Sha.Length));
        public string AuthorName { get; set; }
        public DateTimeOffset AuthorDate { get; set; }
        public string MessageShort { get; set; }

        /// <summary>Alternates each time the owning commit changes, so the UI can band consecutive same-commit lines.</summary>
        public bool IsBandAlt { get; set; }
    }
}
