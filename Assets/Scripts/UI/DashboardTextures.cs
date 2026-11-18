using UnityEngine;

namespace Aegis
{
    // Small textures the dashboard draws in code instead of shipping as assets.
    public static class DashboardTextures
    {
        public static void Destroy(Texture2D texture)
        {
            if (texture != null)
                Object.Destroy(texture);
        }

        public static Texture2D Vignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Vignette"
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float d = Mathf.Sqrt(dx * dx * 0.85f + dy * dy);
                    float alpha = Mathf.SmoothStep(0.78f, 1.4f, d) * 0.28f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        public static Texture2D Grid(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                name = "Grid"
            };
            var pixels = new Color32[size * size];
            var line = new Color32(160, 180, 220, 4);
            var cross = new Color32(170, 190, 230, 12);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x == 0 || y == 0;
                    bool onCross = (x == 0 && y < 3) || (y == 0 && x < 3) || (x == 0 && y > size - 3) || (y == 0 && x > size - 3);
                    pixels[y * size + x] = onCross ? cross : onLine ? line : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        // A 5x5 mirrored identicon, so every name gets a distinct, stable face.
        public static Texture2D Identicon(string name, Color color)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Identicon"
            };
            var pixels = new Color32[size * size];
            int hash = Catalog.StableHash(name);
            Color disc = Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.85f);
            disc = Color.Lerp(disc, color, 0.35f);
            Color mark = Color.Lerp(disc, Color.white, 0.55f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = Mathf.Clamp((x - 6) / 7, 0, 4);
                    int cy = Mathf.Clamp((y - 6) / 7, 0, 4);
                    int mirrored = cx > 2 ? 4 - cx : cx;
                    bool inside = x >= 6 && x < 41 && y >= 6 && y < 41;
                    bool on = inside && ((hash >> (mirrored * 5 + cy)) & 1) == 1;
                    pixels[y * size + x] = on ? mark : disc;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
