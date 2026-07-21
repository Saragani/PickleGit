namespace PickleGit.Models
{
    public enum TokenKind { Keyword, String, Comment, Number, Type, Preprocessor }

    /// <summary>A syntax-highlighted span within a DiffLine's Content (same coordinate space as HighlightSpans).</summary>
    public struct SyntaxSpan
    {
        public int Start;
        public int Length;
        public TokenKind Kind;
        public SyntaxSpan(int start, int length, TokenKind kind) { Start = start; Length = length; Kind = kind; }
    }
}
