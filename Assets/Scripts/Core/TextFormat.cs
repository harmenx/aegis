using System;
using System.Globalization;
using System.Text;

namespace Aegis
{
    public static class TextFormat
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // 950 -> "950", 25,099 -> "25,099", 533,951,000 -> "533.9M", 1.2e9 -> "1.20B".
        public static string Compact(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "—";
            double magnitude = Math.Abs(value);
            string sign = value < 0 ? "−" : "";
            if (magnitude >= 1e9)
                return sign + Truncate(magnitude / 1e9, 2).ToString("0.00", Invariant) + "B";
            if (magnitude >= 1e6)
                return sign + Truncate(magnitude / 1e6, 1).ToString("0.0", Invariant) + "M";
            if (magnitude >= 1e5)
                return sign + Truncate(magnitude / 1e3, 1).ToString("0.0", Invariant) + "K";
            return sign + Math.Round(magnitude).ToString("N0", Invariant);
        }

        // Truncate rather than round, so a counter never shows "1.00M" before it reaches a million.
        static double Truncate(double value, int decimals)
        {
            double scale = Math.Pow(10, decimals);
            return Math.Floor(value * scale) / scale;
        }

        public static string Count(int value)
        {
            return value.ToString("N0", Invariant);
        }

        public static string Ellipsize(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (max < 1 || value.Length <= max)
                return value;
            return value.Substring(0, max - 1) + "…";
        }

        public static string ShortRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo))
                return "";
            int slash = repo.LastIndexOf('/');
            return slash >= 0 ? repo.Substring(slash + 1) : repo;
        }

        // Makes outside text safe to place inside a rich-text label: tags in it are shown, not run.
        public static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.IndexOf('<') < 0)
                return value;
            var text = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                // A lookalike angle bracket keeps the text readable without opening a tag.
                text.Append(character == '<' ? '‹' : character);
            }
            return text.ToString();
        }

        // Trims, drops control characters, and caps length for text from outside the app.
        public static string Clean(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            var text = new StringBuilder(Math.Min(value.Length, max));
            foreach (char character in value)
            {
                if (char.IsControl(character))
                    continue;
                text.Append(character);
            }
            string cleaned = text.ToString().Trim();
            return cleaned.Length > max ? cleaned.Substring(0, max) : cleaned;
        }
    }
}
