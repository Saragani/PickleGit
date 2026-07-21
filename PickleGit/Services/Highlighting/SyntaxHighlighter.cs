using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PickleGit.Models;

namespace PickleGit.Services.Highlighting
{
    /// <summary>
    /// Lightweight per-line regex-free lexer used to color diff text. Deliberately not a full
    /// language grammar — it recognizes comments, strings, numbers, keywords and tag/attribute-like
    /// words well enough to make diffs easier to scan, per file extension.
    ///
    /// Lexer state (currently just "inside a block comment") carries across lines in the order
    /// they appear in the diff hunks, not the true full-file order — context lines omitted between
    /// hunks are invisible to the lexer, so a block comment opened far above a hunk won't be known
    /// about. This mirrors the limitation every hunk-based diff syntax highlighter has.
    /// </summary>
    public static class SyntaxHighlighter
    {
        private enum Language { CSharpLike, Python, PowerShell, Css, Xml, Json, Markdown }

        private static readonly Dictionary<string, Language> ExtensionMap = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = Language.CSharpLike, [".java"] = Language.CSharpLike, [".c"] = Language.CSharpLike,
            [".cpp"] = Language.CSharpLike, [".cc"] = Language.CSharpLike, [".h"] = Language.CSharpLike,
            [".hpp"] = Language.CSharpLike, [".js"] = Language.CSharpLike, [".jsx"] = Language.CSharpLike,
            [".ts"] = Language.CSharpLike, [".tsx"] = Language.CSharpLike,
            [".py"] = Language.Python,
            [".ps1"] = Language.PowerShell, [".psm1"] = Language.PowerShell, [".psd1"] = Language.PowerShell,
            [".css"] = Language.Css, [".scss"] = Language.Css, [".less"] = Language.Css,
            [".xml"] = Language.Xml, [".xaml"] = Language.Xml, [".html"] = Language.Xml,
            [".htm"] = Language.Xml, [".csproj"] = Language.Xml, [".config"] = Language.Xml,
            [".json"] = Language.Json,
            [".md"] = Language.Markdown, [".markdown"] = Language.Markdown,
        };

        private static readonly HashSet<string> EmptyKeywords = new HashSet<string>();

        private static readonly HashSet<string> CLikeKeywords = new HashSet<string>(new[]
        {
            "public","private","protected","internal","static","void","class","struct","interface","enum",
            "namespace","using","return","if","else","for","foreach","while","do","switch","case","break",
            "continue","new","this","base","null","true","false","var","const","readonly","virtual","override",
            "abstract","sealed","async","await","try","catch","finally","throw","import","export","function",
            "let","default","extends","implements","package","from","as","typeof","instanceof","in","of",
            "int","long","short","byte","bool","boolean","string","double","float","char","object","dynamic","get","set"
        }, StringComparer.Ordinal);

        private static readonly HashSet<string> PythonKeywords = new HashSet<string>(new[]
        {
            "def","class","import","from","as","return","if","elif","else","for","while","break","continue",
            "pass","try","except","finally","raise","with","lambda","yield","None","True","False","and","or",
            "not","in","is","global","nonlocal","assert","del","self"
        }, StringComparer.Ordinal);

        private static readonly HashSet<string> PowerShellKeywords = new HashSet<string>(new[]
        {
            "function","param","if","elseif","else","foreach","for","while","do","switch","break","continue",
            "return","try","catch","finally","throw","begin","process","end","class","enum","using","in",
            "$true","$false","$null"
        }, StringComparer.OrdinalIgnoreCase);

        public static bool IsSupported(string filePath) =>
            !string.IsNullOrEmpty(filePath) && ExtensionMap.ContainsKey(Path.GetExtension(filePath));

        /// <summary>Walks a file's diff hunks in order and fills in each DiffLine's SyntaxSpans in place.
        /// No-op when the file's extension isn't recognized.</summary>
        public static void Apply(List<DiffHunk> hunks, string filePath)
        {
            if (hunks == null || hunks.Count == 0) return;
            if (!ExtensionMap.TryGetValue(Path.GetExtension(filePath ?? string.Empty), out var lang)) return;

            bool inBlockComment = false;
            foreach (var hunk in hunks)
            {
                foreach (var line in hunk.Lines)
                {
                    if (string.IsNullOrEmpty(line.Content) || line.Content.Length <= 1) continue;
                    var code = line.Content.Substring(1); // strip the leading +/-/space diff marker
                    var spans = TokenizeLine(code, lang, ref inBlockComment);
                    if (spans.Count > 0)
                        line.SyntaxSpans = spans.Select(s => new SyntaxSpan(s.Start + 1, s.Length, s.Kind)).ToList();
                }
            }
        }

        private static List<SyntaxSpan> TokenizeLine(string code, Language lang, ref bool inBlockComment)
        {
            switch (lang)
            {
                case Language.CSharpLike:
                    return TokenizeGeneric(code, CLikeKeywords, "//", "/*", "*/", hashPreprocessor: true, ref inBlockComment);
                case Language.Python:
                    return TokenizeGeneric(code, PythonKeywords, "#", null, null, hashPreprocessor: false, ref inBlockComment);
                case Language.PowerShell:
                    return TokenizeGeneric(code, PowerShellKeywords, "#", "<#", "#>", hashPreprocessor: false, ref inBlockComment);
                case Language.Css:
                    return TokenizeGeneric(code, EmptyKeywords, null, "/*", "*/", hashPreprocessor: false, ref inBlockComment);
                case Language.Xml:
                    return TokenizeXml(code, ref inBlockComment);
                case Language.Json:
                    return TokenizeJson(code);
                case Language.Markdown:
                    return TokenizeMarkdown(code);
                default:
                    return new List<SyntaxSpan>();
            }
        }

        private static bool StartsWithAt(string s, int pos, string needle)
        {
            if (needle == null || pos + needle.Length > s.Length) return false;
            return string.CompareOrdinal(s, pos, needle, 0, needle.Length) == 0;
        }

        private static List<SyntaxSpan> TokenizeGeneric(string code, HashSet<string> keywords, string lineComment,
            string blockStart, string blockEnd, bool hashPreprocessor, ref bool inBlockComment)
        {
            var spans = new List<SyntaxSpan>();
            int len = code.Length;
            int pos = 0;

            if (inBlockComment)
            {
                int endIdx = blockEnd != null ? code.IndexOf(blockEnd, StringComparison.Ordinal) : -1;
                if (endIdx < 0) { spans.Add(new SyntaxSpan(0, len, TokenKind.Comment)); return spans; }
                spans.Add(new SyntaxSpan(0, endIdx + blockEnd.Length, TokenKind.Comment));
                pos = endIdx + blockEnd.Length;
                inBlockComment = false;
            }

            if (hashPreprocessor)
            {
                int firstNonSpace = pos;
                while (firstNonSpace < len && char.IsWhiteSpace(code[firstNonSpace])) firstNonSpace++;
                if (firstNonSpace < len && code[firstNonSpace] == '#')
                {
                    spans.Add(new SyntaxSpan(pos, len - pos, TokenKind.Preprocessor));
                    return spans;
                }
            }

            while (pos < len)
            {
                if (lineComment != null && StartsWithAt(code, pos, lineComment))
                {
                    spans.Add(new SyntaxSpan(pos, len - pos, TokenKind.Comment));
                    return spans;
                }
                if (blockStart != null && StartsWithAt(code, pos, blockStart))
                {
                    int endIdx = blockEnd != null ? code.IndexOf(blockEnd, pos + blockStart.Length, StringComparison.Ordinal) : -1;
                    if (endIdx < 0)
                    {
                        spans.Add(new SyntaxSpan(pos, len - pos, TokenKind.Comment));
                        inBlockComment = true;
                        return spans;
                    }
                    spans.Add(new SyntaxSpan(pos, endIdx + blockEnd.Length - pos, TokenKind.Comment));
                    pos = endIdx + blockEnd.Length;
                    continue;
                }

                char c = code[pos];
                if (c == '"' || c == '\'')
                {
                    int start = pos;
                    char quote = c;
                    pos++;
                    while (pos < len && code[pos] != quote)
                    {
                        if (code[pos] == '\\' && pos + 1 < len) pos++;
                        pos++;
                    }
                    if (pos < len) pos++;
                    spans.Add(new SyntaxSpan(start, pos - start, TokenKind.String));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int start = pos;
                    while (pos < len && (char.IsDigit(code[pos]) || code[pos] == '.')) pos++;
                    spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Number));
                    continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    int start = pos;
                    // A bare '$' (C# interpolated string prefix "$\"...\"", kept as part of the token
                    // so PowerShell's $true/$false/$null keywords below still match with their $) isn't
                    // itself a letter/digit/underscore, so the loop below never advances past it on its
                    // own — leaving `word` empty and `word[0]` throwing IndexOutOfRangeException. Advance
                    // past it explicitly first.
                    if (c == '$') pos++;
                    while (pos < len && (char.IsLetterOrDigit(code[pos]) || code[pos] == '_')) pos++;
                    var word = code.Substring(start, pos - start);
                    if (keywords.Contains(word))
                        spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Keyword));
                    else if (char.IsUpper(word[0]))
                        spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Type));
                    continue;
                }
                pos++;
            }
            return spans;
        }

        private static List<SyntaxSpan> TokenizeXml(string code, ref bool inBlockComment)
        {
            var spans = new List<SyntaxSpan>();
            int len = code.Length;
            int pos = 0;

            if (inBlockComment)
            {
                int endIdx = code.IndexOf("-->", StringComparison.Ordinal);
                if (endIdx < 0) { spans.Add(new SyntaxSpan(0, len, TokenKind.Comment)); return spans; }
                spans.Add(new SyntaxSpan(0, endIdx + 3, TokenKind.Comment));
                pos = endIdx + 3;
                inBlockComment = false;
            }

            while (pos < len)
            {
                if (StartsWithAt(code, pos, "<!--"))
                {
                    int endIdx = code.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                    if (endIdx < 0)
                    {
                        spans.Add(new SyntaxSpan(pos, len - pos, TokenKind.Comment));
                        inBlockComment = true;
                        return spans;
                    }
                    spans.Add(new SyntaxSpan(pos, endIdx + 3 - pos, TokenKind.Comment));
                    pos = endIdx + 3;
                    continue;
                }
                char c = code[pos];
                if (c == '"' || c == '\'')
                {
                    int start = pos;
                    char quote = c;
                    pos++;
                    while (pos < len && code[pos] != quote) pos++;
                    if (pos < len) pos++;
                    spans.Add(new SyntaxSpan(start, pos - start, TokenKind.String));
                    continue;
                }
                if (c == '<')
                {
                    int start = pos;
                    pos++;
                    if (pos < len && code[pos] == '/') pos++;
                    int nameStart = pos;
                    while (pos < len && (char.IsLetterOrDigit(code[pos]) || "_.:-".IndexOf(code[pos]) >= 0)) pos++;
                    if (pos > nameStart) spans.Add(new SyntaxSpan(nameStart, pos - nameStart, TokenKind.Type));
                    continue;
                }
                if (char.IsLetter(c))
                {
                    int start = pos;
                    while (pos < len && (char.IsLetterOrDigit(code[pos]) || "_.:-".IndexOf(code[pos]) >= 0)) pos++;
                    spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Keyword));
                    continue;
                }
                pos++;
            }
            return spans;
        }

        private static List<SyntaxSpan> TokenizeJson(string code)
        {
            var spans = new List<SyntaxSpan>();
            int len = code.Length;
            int pos = 0;
            while (pos < len)
            {
                char c = code[pos];
                if (c == '"')
                {
                    int start = pos;
                    pos++;
                    while (pos < len && code[pos] != '"')
                    {
                        if (code[pos] == '\\' && pos + 1 < len) pos++;
                        pos++;
                    }
                    if (pos < len) pos++;
                    int look = pos;
                    while (look < len && char.IsWhiteSpace(code[look])) look++;
                    var kind = (look < len && code[look] == ':') ? TokenKind.Keyword : TokenKind.String;
                    spans.Add(new SyntaxSpan(start, pos - start, kind));
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && pos + 1 < len && char.IsDigit(code[pos + 1])))
                {
                    int start = pos;
                    pos++;
                    while (pos < len && (char.IsDigit(code[pos]) || code[pos] == '.')) pos++;
                    spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Number));
                    continue;
                }
                if (char.IsLetter(c))
                {
                    int start = pos;
                    while (pos < len && char.IsLetter(code[pos])) pos++;
                    var word = code.Substring(start, pos - start);
                    if (word == "true" || word == "false" || word == "null")
                        spans.Add(new SyntaxSpan(start, pos - start, TokenKind.Keyword));
                    continue;
                }
                pos++;
            }
            return spans;
        }

        private static List<SyntaxSpan> TokenizeMarkdown(string code)
        {
            var spans = new List<SyntaxSpan>();
            var trimmed = code.TrimStart();
            int indent = code.Length - trimmed.Length;
            if (trimmed.StartsWith("#"))
            {
                spans.Add(new SyntaxSpan(indent, code.Length - indent, TokenKind.Keyword));
                return spans;
            }
            int pos = 0, len = code.Length;
            while (pos < len)
            {
                if (code[pos] == '`')
                {
                    int end = code.IndexOf('`', pos + 1);
                    if (end < 0) break;
                    spans.Add(new SyntaxSpan(pos, end - pos + 1, TokenKind.String));
                    pos = end + 1;
                    continue;
                }
                pos++;
            }
            return spans;
        }
    }
}
