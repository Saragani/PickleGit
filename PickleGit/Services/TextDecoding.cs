using System;
using System.Collections.Generic;
using System.Text;

namespace PickleGit.Services
{
    /// <summary>A file's bytes decoded to text lines, remembering exactly how to write them back:
    /// the encoding, the BOM/preamble bytes, and each line's own terminator.</summary>
    public sealed class DecodedText
    {
        public Encoding Encoding { get; set; }

        /// <summary>The exact bytes this was decoded from (empty for a missing side) — lets an in-app
        /// edit verify its target still holds this content before overwriting it.</summary>
        public byte[] SourceBytes { get; set; } = new byte[0];

        /// <summary>The BOM bytes the file started with (empty when it had none).</summary>
        public byte[] Preamble { get; set; } = new byte[0];

        /// <summary>Lines without their terminators.</summary>
        public List<string> Lines { get; } = new List<string>();

        /// <summary>Each line's terminator ("\r\n", "\n", "\r"), or "" for a final line with no
        /// newline. Same length as <see cref="Lines"/>.</summary>
        public List<string> Terminators { get; } = new List<string>();

        /// <summary>True when the last line ends without a newline.</summary>
        public bool MissingFinalNewline => Terminators.Count > 0 && Terminators[Terminators.Count - 1].Length == 0;

        /// <summary>Same encoding and same BOM — a partial edit can be re-encoded without mixing encodings.</summary>
        public bool SameEncodingAs(DecodedText other) =>
            other != null && Encoding.CodePage == other.Encoding.CodePage && Preamble.Length == other.Preamble.Length;

        public byte[] Encode(IList<string> lines, IList<string> terminators)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
                sb.Append(lines[i]).Append(terminators[i]);
            var body = Encoding.GetBytes(sb.ToString());
            var result = new byte[Preamble.Length + body.Length];
            Buffer.BlockCopy(Preamble, 0, result, 0, Preamble.Length);
            Buffer.BlockCopy(body, 0, result, Preamble.Length, body.Length);
            return result;
        }
    }

    /// <summary>
    /// Decodes file content that libgit2 reports as binary because it contains NUL bytes — UTF-16
    /// (LE/BE, with or without BOM) and UTF-32 with BOM — plus UTF-8 with/without BOM, so the diff
    /// view can show it as text. Returns false for genuinely binary content.
    /// </summary>
    public static class TextDecoding
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private const int SniffBytes = 8000;

        public static bool TryDecode(byte[] bytes, out DecodedText decoded)
        {
            decoded = null;
            if (bytes == null) bytes = new byte[0];

            Encoding enc;
            int bomLen;
            if (!DetectEncoding(bytes, out enc, out bomLen)) return false;

            string text;
            try
            {
                text = enc.GetString(bytes, bomLen, bytes.Length - bomLen);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (!LooksLikeText(text)) return false;

            decoded = new DecodedText { Encoding = enc, Preamble = Slice(bytes, 0, bomLen), SourceBytes = bytes };
            SplitLines(text, decoded.Lines, decoded.Terminators);
            return true;
        }

        /// <summary>Cheap pre-check on the first bytes of a (possibly huge) file: would
        /// <see cref="TryDecode"/> likely accept the whole thing? Lets callers skip reading a large
        /// genuinely-binary file in full just to have it rejected. Decodes leniently, since the prefix
        /// may end mid-character; <paramref name="isWholeContent"/> says whether this is all of it
        /// (an odd length is only meaningful then).</summary>
        public static bool IsProbablyText(byte[] prefix, bool isWholeContent)
        {
            if (prefix == null || prefix.Length == 0) return true;
            var sample = prefix;
            if (!isWholeContent && sample.Length % 2 != 0)
            {
                // Keep UTF-16 parity checks meaningful on a truncated prefix.
                sample = new byte[prefix.Length - 1];
                Buffer.BlockCopy(prefix, 0, sample, 0, sample.Length);
            }
            if (!DetectEncoding(sample, out var enc, out var bomLen)) return false;
            var lenient = Encoding.GetEncoding(enc.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            return LooksLikeText(lenient.GetString(sample, bomLen, sample.Length - bomLen));
        }

        /// <summary>How many leading bytes <see cref="IsProbablyText"/> needs.</summary>
        public const int SniffLength = SniffBytes;

        private static bool DetectEncoding(byte[] b, out Encoding enc, out int bomLen)
        {
            enc = null;
            bomLen = 0;
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
            { enc = new UTF32Encoding(false, false, true); bomLen = 4; return true; }
            if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
            { enc = new UTF32Encoding(true, false, true); bomLen = 4; return true; }
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            { enc = StrictUtf8; bomLen = 3; return true; }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            { enc = new UnicodeEncoding(false, false, true); bomLen = 2; return true; }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            { enc = new UnicodeEncoding(true, false, true); bomLen = 2; return true; }

            int n = Math.Min(b.Length, SniffBytes);
            int zerosEven = 0, zerosOdd = 0;
            for (int i = 0; i < n; i++)
            {
                if (b[i] != 0) continue;
                if ((i & 1) == 0) zerosEven++; else zerosOdd++;
            }
            if (zerosEven + zerosOdd == 0)
            {
                // No NULs: UTF-8 when valid, otherwise the system ANSI code page (what an editor would show).
                try { StrictUtf8.GetString(b); enc = StrictUtf8; }
                catch (DecoderFallbackException) { enc = Encoding.Default; }
                return true;
            }

            // BOM-less UTF-16: mostly-ASCII text puts a NUL in every other byte, all on one parity.
            if (b.Length % 2 != 0) return false;
            int pairs = n / 2;
            if (pairs == 0) return false;
            if (zerosOdd >= pairs * 0.3 && zerosEven <= pairs * 0.02)
            { enc = new UnicodeEncoding(false, false, true); return true; }
            if (zerosEven >= pairs * 0.3 && zerosOdd <= pairs * 0.02)
            { enc = new UnicodeEncoding(true, false, true); return true; }
            return false;
        }

        /// <summary>Rejects decoded content that is still binary-looking: any NUL char, or more than
        /// 1% control characters other than tab/CR/LF/FF/ESC/backspace.</summary>
        private static bool LooksLikeText(string text)
        {
            int n = Math.Min(text.Length, SniffBytes);
            int control = 0;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                if (c == '\0') return false;
                if (c < 0x20 && c != '\t' && c != '\r' && c != '\n' && c != '\f' && c != '\x1b' && c != '\b')
                    control++;
            }
            return n == 0 || control * 100 <= n;
        }

        private static void SplitLines(string text, List<string> lines, List<string> terminators)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\n' && c != '\r') continue;
                string term;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') term = "\r\n";
                else term = c.ToString();
                lines.Add(text.Substring(start, i - start));
                terminators.Add(term);
                i += term.Length - 1;
                start = i + 1;
            }
            if (start < text.Length)
            {
                lines.Add(text.Substring(start));
                terminators.Add(string.Empty);
            }
        }

        private static byte[] Slice(byte[] b, int offset, int count)
        {
            var r = new byte[count];
            Buffer.BlockCopy(b, offset, r, 0, count);
            return r;
        }
    }
}
