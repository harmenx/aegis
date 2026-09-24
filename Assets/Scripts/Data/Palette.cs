using UnityEngine;

namespace Gitmos
{
    public static class Palette
    {
        public static readonly Color Background = new Color(0.02f, 0.03f, 0.055f, 1f);
        public static readonly Color Cyan = new Color(0.35f, 1f, 0.92f);
        public static readonly Color Blue = new Color(0.32f, 0.55f, 1f);
        public static readonly Color Magenta = new Color(0.82f, 0.42f, 1f);
        public static readonly Color Orange = new Color(1f, 0.54f, 0.28f);
        public static readonly Color Red = new Color(1f, 0.28f, 0.38f);
        public static readonly Color Green = new Color(0.35f, 1f, 0.62f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.45f);

        public static Color For(GitEvent gitEvent)
        {
            if (gitEvent.Tint.a > 0.001f)
                return gitEvent.Tint;
            return For(gitEvent.Type);
        }

        public static Color For(GitEventType type)
        {
            switch (type)
            {
                case GitEventType.Create: return Cyan;
                case GitEventType.Push: return Blue;
                case GitEventType.PullRequest: return Magenta;
                case GitEventType.Fork: return Orange;
                case GitEventType.Delete: return Red;
                case GitEventType.Merge: return Green;
                case GitEventType.Review: return Gold;
                default: return Color.white;
            }
        }

        public static Color FromName(string name)
        {
            float hue = (Catalog.StableHash(name) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.62f, 1f);
        }

        public static int Lane(GitEventType type)
        {
            switch (type)
            {
                case GitEventType.Create: return 0;
                case GitEventType.Push: return 1;
                case GitEventType.Merge: return 1;
                case GitEventType.PullRequest: return 2;
                case GitEventType.Review: return 2;
                case GitEventType.Fork: return 3;
                case GitEventType.Delete: return 4;
                default: return 1;
            }
        }

        public static Color ForLanguage(string language)
        {
            switch (language)
            {
                case "JavaScript": return new Color(0.95f, 0.86f, 0.25f);
                case "TypeScript": return new Color(0.35f, 0.65f, 1f);
                case "Python": return new Color(0.35f, 0.7f, 1f);
                case "Go": return new Color(0.4f, 0.85f, 0.95f);
                case "Rust": return new Color(1f, 0.45f, 0.25f);
                case "C#": return new Color(0.55f, 0.35f, 0.9f);
                case "Java": return new Color(0.95f, 0.45f, 0.25f);
                case "Ruby": return new Color(0.9f, 0.25f, 0.3f);
                case "C++": return new Color(0.45f, 0.55f, 0.95f);
                case "Swift": return new Color(1f, 0.45f, 0.4f);
                case "Kotlin": return new Color(0.55f, 0.4f, 1f);
                case "PHP": return new Color(0.55f, 0.55f, 0.95f);
                default: return new Color(0.6f, 0.75f, 0.7f);
            }
        }
    }
}
