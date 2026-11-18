using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis
{
    // Splits a growing UTF-8 byte stream into lines. Keeps decoder state between reads,
    // so a multi-byte character split across two reads still decodes correctly.
    public sealed class JsonLinesReader
    {
        readonly Decoder decoder = new UTF8Encoding(false, false).GetDecoder();
        readonly StringBuilder pending = new StringBuilder();
        readonly int maxLineChars;
        char[] chars = new char[4096];
        bool overflowing;

        public long Offset { get; private set; }
        public int DroppedLines { get; private set; }

        public JsonLinesReader(int maxLineChars = 1 << 20)
        {
            this.maxLineChars = Math.Max(16, maxLineChars);
        }

        public void Reset()
        {
            decoder.Reset();
            pending.Length = 0;
            overflowing = false;
            Offset = 0;
        }

        // Feeds bytes and appends every complete, non-empty line to lines.
        public void Feed(byte[] buffer, int count, List<string> lines)
        {
            Offset += count;
            int needed = decoder.GetCharCount(buffer, 0, count, false);
            if (chars.Length < needed)
                chars = new char[Math.Max(needed, chars.Length * 2)];
            int decoded = decoder.GetChars(buffer, 0, count, chars, 0, false);

            for (int i = 0; i < decoded; i++)
            {
                char character = chars[i];
                if (character == '\n')
                {
                    if (!overflowing)
                    {
                        string line = pending.ToString().Trim();
                        if (line.Length > 0)
                            lines.Add(line);
                    }
                    pending.Length = 0;
                    overflowing = false;
                    continue;
                }
                if (character == '\r' || overflowing)
                    continue;
                if (pending.Length >= maxLineChars)
                {
                    // Drop the rest of an oversized line instead of buffering without bound.
                    overflowing = true;
                    pending.Length = 0;
                    DroppedLines++;
                    continue;
                }
                pending.Append(character);
            }
        }
    }
}
