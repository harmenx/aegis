using System.Collections.Generic;
using UnityEngine;

namespace Gitmos
{
    public class ActivityBarcode : StageBase
    {
        public Shader screenShader;

        public bool HasInk { get; private set; }

        const int Columns = 8;
        const int Depth = 7;

        readonly float[] energy = new float[Columns];
        readonly List<Vector3> vertices = new List<Vector3>(2000);
        readonly List<Color> colors = new List<Color>(2000);
        readonly List<int> triangles = new List<int>(3000);

        Mesh mesh;
        MeshRenderer meshRenderer;
        Material material;

        static readonly Color[] ColumnColors =
        {
            new Color(0.25f, 0.95f, 1f),
            new Color(0.35f, 0.55f, 1f),
            new Color(0.55f, 0.35f, 1f),
            new Color(0.78f, 0.35f, 1f),
            new Color(1f, 0.35f, 0.72f),
            new Color(1f, 0.42f, 0.38f),
            new Color(1f, 0.55f, 0.25f),
            new Color(1f, 0.72f, 0.35f)
        };

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
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
        }

        void HandleEvent(GitEvent gitEvent)
        {
            int lane = string.IsNullOrEmpty(gitEvent.Category)
                ? Palette.Lane(gitEvent.Type)
                : Catalog.StableHash(gitEvent.Category) % Columns;
            if (lane < 0)
                lane = 1;
            int column = Mathf.Clamp(lane, 0, Columns - 1);
            float amount = gitEvent.Weight > 0f ? Mathf.Clamp(gitEvent.Weight, 0.2f, 2.5f) : 1f;
            energy[column] = Mathf.Clamp01(energy[column] + 0.28f * amount);
            HasInk = true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < energy.Length; i++)
                energy[i] = Mathf.Max(0f, energy[i] - dt * 0.18f);

            if (material != null)
                material.SetFloat("_Alpha", Alpha);
            if (meshRenderer != null)
                meshRenderer.enabled = Alpha > 0.01f;
            if (Alpha > 0.01f)
                DrawBars();
        }

        void Build()
        {
            Shader shader = Shader.Find("Gitmos/Additive");
            if (shader == null)
                shader = screenShader;
            material = new Material(shader);
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetFloat("_Boost", 1.8f);

            mesh = new Mesh { name = "ActivityBars" };
            var meshObject = new GameObject("Bars");
            meshObject.transform.SetParent(transform, false);
            var filter = meshObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void DrawBars()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            float time = Time.time;
            for (int column = 0; column < Columns; column++)
            {
                Color color = ColumnColors[column];
                for (int row = 0; row < Depth; row++)
                {
                    float x = Mathf.Lerp(-6.4f, 6.4f, column / (float)(Columns - 1));
                    x += Mathf.Sin(time * 0.15f + row) * 0.08f;
                    float z = Mathf.Lerp(5.2f, -4.6f, row / (float)(Depth - 1));
                    float noise = Mathf.PerlinNoise(column * 0.37f, row * 0.41f + time * 0.05f);
                    float height = Mathf.Lerp(0.45f, 3.6f, noise);
                    height *= 0.55f + energy[column] * 1.5f;
                    if (column == 1 && row == 2)
                        height = Mathf.Max(height, 4.2f);
                    float width = 0.045f + (row == 0 ? 0.02f : 0f);
                    float glow = 0.35f + energy[column] * 0.9f;
                    Color sample = color * glow;
                    sample.a = 0.55f + energy[column] * 0.4f;
                    AddBeam(new Vector3(x, -1.15f, z), height, width, sample);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void AddBeam(Vector3 basePoint, float height, float width, Color color)
        {
            Vector3 top = basePoint + Vector3.up * height;
            AddQuad(basePoint, top, Vector3.right, width, color);
            AddQuad(basePoint, top, Vector3.forward, width * 0.65f, color);
        }

        void AddQuad(Vector3 bottom, Vector3 top, Vector3 sideAxis, float width, Color color)
        {
            Vector3 side = sideAxis * width;
            int start = vertices.Count;
            vertices.Add(bottom - side);
            vertices.Add(bottom + side);
            vertices.Add(top + side);
            vertices.Add(top - side);
            colors.Add(color);
            colors.Add(color);
            Color hot = color;
            hot.r = Mathf.Min(1.6f, hot.r + 0.35f);
            hot.g = Mathf.Min(1.6f, hot.g + 0.35f);
            hot.b = Mathf.Min(1.6f, hot.b + 0.35f);
            colors.Add(hot);
            colors.Add(hot);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }
    }
}
