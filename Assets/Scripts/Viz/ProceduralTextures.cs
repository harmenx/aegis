using UnityEngine;

namespace Gitmos
{
    public static class ProceduralTextures
    {
        public static Texture2D SoftCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SoftCircle"
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float glow = Mathf.Exp(-d * d * 3.2f);
                    byte value = (byte)Mathf.Clamp(glow * 255f, 0f, 255f);
                    pixels[y * size + x] = new Color32(value, value, value, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static Texture2D LandMask(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "LandMask"
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float lat = (v - 0.5f) * 180f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float lon = (u - 0.5f) * 360f;
                    byte value = (byte)Mathf.Clamp(LandWeight(lat, lon) * 255f, 0f, 255f);
                    pixels[y * width + x] = new Color32(value, value, value, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static float LandWeight(float latitude, float longitude)
        {
            float weight = 0f;
            for (int i = 0; i < LandBlobs.Length; i++)
            {
                Vector4 blob = LandBlobs[i];
                float dLat = latitude - blob.x;
                float dLon = Mathf.DeltaAngle(longitude, blob.y) * Mathf.Cos(latitude * Mathf.Deg2Rad);
                float distance = Mathf.Sqrt(dLat * dLat + dLon * dLon);
                float presence = Mathf.SmoothStep(blob.z, blob.z * 0.25f, distance) * blob.w;
                weight = Mathf.Max(weight, presence);
            }
            return weight;
        }

        public static Texture2D FiberStrip(int size)
        {
            var texture = new Texture2D(4, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "FiberStrip"
            };
            var pixels = new Color32[4 * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                float edge = Mathf.Abs(v - 0.5f) * 2f;
                float glow = Mathf.Exp(-edge * edge * 5.5f);
                byte value = (byte)Mathf.Clamp(glow * 255f, 0f, 255f);
                var pixel = new Color32(value, value, value, 255);
                for (int x = 0; x < 4; x++)
                    pixels[y * 4 + x] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static readonly Vector4[] LandBlobs =
        {
            new Vector4(45f, -100f, 32f, 1f),
            new Vector4(58f, -155f, 14f, 0.7f),
            new Vector4(15f, -90f, 12f, 0.65f),
            new Vector4(-15f, -60f, 24f, 1f),
            new Vector4(50f, 10f, 18f, 0.85f),
            new Vector4(64f, 18f, 12f, 0.45f),
            new Vector4(8f, 20f, 26f, 1f),
            new Vector4(25f, 45f, 14f, 0.55f),
            new Vector4(48f, 85f, 34f, 1f),
            new Vector4(22f, 78f, 16f, 0.7f),
            new Vector4(35f, 105f, 18f, 0.75f),
            new Vector4(60f, 95f, 20f, 0.4f),
            new Vector4(-25f, 134f, 16f, 0.85f),
            new Vector4(-6f, 108f, 14f, 0.45f),
            new Vector4(62f, -40f, 14f, 0.35f)
        };
    }
}
