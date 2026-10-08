using System.Collections.Generic;
using UnityEngine;

namespace Gitmos
{
    public class ScanBurst : StageBase
    {
        public Shader screenShader;
        public Shader additiveShader;

        struct Streak
        {
            public float X;
            public float Height;
            public float Age;
            public float Life;
            public Color Color;
        }

        const int Width = 420;
        const int Height = 520;
        readonly List<Streak> streaks = new List<Streak>(80);
        Color32[] pixels;
        Texture2D texture;
        Material material;
        Material coreMaterial;
        MeshRenderer meshRenderer;
        Transform core;
        float heat;

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            EventBus.OnEvent += HandleEvent;
            Build();
        }

        void OnDisable()
        {
            EventBus.OnEvent -= HandleEvent;
        }

        void OnDestroy()
        {
            if (texture != null)
                Destroy(texture);
            if (material != null)
                Destroy(material);
            if (coreMaterial != null)
                Destroy(coreMaterial);
        }

        void HandleEvent(GitEvent gitEvent)
        {
            float spread = Mathf.Abs(Gaussian()) * 0.42f;
            float x = 0.5f + (Random.value < 0.5f ? -spread : spread);
            streaks.Add(new Streak
            {
                X = Mathf.Clamp(x, 0.04f, 0.96f),
                Height = Random.Range(0.18f, 1f),
                Age = 0f,
                Life = Random.Range(1.1f, 2.4f),
                Color = Palette.For(gitEvent)
            });
            heat = Mathf.Min(1.5f, heat + 0.18f);
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            PlaceInFrontOf(cam, 6.15f);
            float dt = Time.deltaTime;
            heat = Mathf.Max(0f, heat - dt * 0.45f);

            if (material != null)
                material.SetFloat("_Alpha", Alpha);
            if (coreMaterial != null)
            {
                coreMaterial.SetFloat("_Alpha", Alpha);
                float pulse = 1.4f + heat * 2.2f + Mathf.Sin(Time.time * 2.5f) * 0.15f;
                coreMaterial.SetFloat("_Boost", pulse);
            }
            if (meshRenderer != null)
                meshRenderer.enabled = Alpha > 0.01f;
            if (core != null)
            {
                core.gameObject.SetActive(Alpha > 0.01f);
                float scale = 1.6f + heat * 1.1f;
                core.localScale = new Vector3(scale, scale * 0.72f, 1f);
            }

            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                Streak streak = streaks[i];
                streak.Age += dt;
                if (streak.Age >= streak.Life)
                    streaks.RemoveAt(i);
                else
                    streaks[i] = streak;
            }

            Paint();
        }

        void Build()
        {
            if (screenShader == null)
                screenShader = Shader.Find("Gitmos/Screen");
            if (additiveShader == null)
                additiveShader = Shader.Find("Gitmos/Additive");

            pixels = new Color32[Width * Height];
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Scan"
            };

            material = new Material(screenShader);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Boost", 2.6f);
            material.SetFloat("_Hot", 0.35f);
            material.SetColor("_HotColor", new Color(1f, 0.55f, 0.85f));

            var meshObject = new GameObject("ScanMesh");
            meshObject.transform.SetParent(transform, false);
            var filter = meshObject.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad(7.4f, 4.4f);
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            coreMaterial = new Material(additiveShader);
            coreMaterial.SetTexture("_BaseMap", ProceduralTextures.SoftCircle(128));
            coreMaterial.SetFloat("_Boost", 1.8f);
            var coreObject = new GameObject("Core");
            coreObject.transform.SetParent(transform, false);
            coreObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var coreFilter = coreObject.AddComponent<MeshFilter>();
            coreFilter.sharedMesh = BuildQuad(2.4f, 2.4f);
            var coreRenderer = coreObject.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = coreMaterial;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var coreMesh = coreFilter.sharedMesh;
            var colors = new Color[4];
            Color hdr = new Color(1.6f, 0.55f, 1.15f, 1f);
            for (int i = 0; i < 4; i++)
                colors[i] = hdr;
            coreMesh.colors = colors;
            core = coreObject.transform;
        }

        void Paint()
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 255);

            for (int i = 0; i < streaks.Count; i++)
            {
                Streak streak = streaks[i];
                float life = 1f - streak.Age / streak.Life;
                int x = Mathf.Clamp(Mathf.RoundToInt(streak.X * (Width - 1)), 1, Width - 2);
                int half = Mathf.RoundToInt(streak.Height * Height * 0.5f * life);
                int y0 = Mathf.Clamp(Height / 2 - half, 0, Height - 1);
                int y1 = Mathf.Clamp(Height / 2 + half, 0, Height - 1);
                Color color = streak.Color * life;
                for (int y = y0; y <= y1; y++)
                {
                    float along = half == 0 ? 1f : 1f - Mathf.Abs(y - Height / 2f) / half;
                    byte r = (byte)Mathf.Clamp(color.r * along * 255f, 0, 255);
                    byte g = (byte)Mathf.Clamp(color.g * along * 255f, 0, 255);
                    byte b = (byte)Mathf.Clamp(color.b * along * 255f, 0, 255);
                    int row = y * Width;
                    pixels[row + x] = new Color32(r, g, b, 255);
                    if (along > 0.45f)
                    {
                        byte dimR = (byte)(r / 4);
                        byte dimG = (byte)(g / 4);
                        byte dimB = (byte)(b / 4);
                        pixels[row + x - 1] = new Color32(dimR, dimG, dimB, 255);
                        pixels[row + x + 1] = new Color32(dimR, dimG, dimB, 255);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        static float Gaussian()
        {
            float u = Mathf.Max(0.0001f, Random.value);
            float v = Mathf.Max(0.0001f, Random.value);
            return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(Mathf.PI * 2f * v);
        }
    }
}
