using System.Collections.Generic;
using UnityEngine;

namespace Gitmos
{
    // Vertical barcode columns. Each column is a stack of slices that scroll downward; an
    // event lights the top slices of its column. After a few seconds the whole stage tips
    // back into receding lanes.
    public class ActivityBarcode : StageBase
    {
        public Shader screenShader;

        public bool HasInk { get; private set; }

        const int Columns = 30;
        const int Slices = 64;
        const float StageHeight = 7.6f;
        const float StageWidth = 12.5f;
        const float ScrollSeconds = 0.075f;
        const float Distance = 9.2f;

        static readonly Color[] ColumnColors =
        {
            new Color(0.3f, 1f, 0.82f),
            new Color(0.3f, 0.86f, 1f),
            new Color(0.38f, 0.55f, 1f),
            new Color(0.55f, 0.42f, 1f),
            new Color(0.75f, 0.4f, 1f),
            new Color(0.95f, 0.42f, 0.95f),
            new Color(1f, 0.4f, 0.62f),
            new Color(1f, 0.38f, 0.42f),
            new Color(1f, 0.6f, 0.32f)
        };

        class Column
        {
            public float X;
            public float Z;
            public float Width;
            public Color Color;
            public float[] Level = new float[Slices];
            public float[] Thickness = new float[Slices];
            public float Pending;
            public float Energy;
        }

        readonly Column[] columns = new Column[Columns];
        readonly List<Vector3> vertices = new List<Vector3>(16000);
        readonly List<Color> colors = new List<Color>(16000);
        readonly List<Vector2> uvs = new List<Vector2>(16000);
        readonly List<int> triangles = new List<int>(24000);
        readonly System.Random rng = new System.Random(7);

        Mesh mesh;
        MeshRenderer meshRenderer;
        Material material;
        Transform pivot;
        float scroll;
        float visibleFor;

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
            string key = !string.IsNullOrEmpty(gitEvent.Category) ? gitEvent.Category
                : !string.IsNullOrEmpty(gitEvent.Repo) ? gitEvent.Repo
                : gitEvent.Type.ToString();
            Column column = columns[Catalog.StableHash(key) % Columns];
            if (column == null)
                return;
            float amount = gitEvent.Weight > 0f ? Mathf.Clamp(gitEvent.Weight, 0.3f, 2.5f) : 1f;
            column.Pending += 0.45f * amount;
            column.Energy = Mathf.Min(1.6f, column.Energy + 0.25f * amount);
            HasInk = true;
        }

        void Build()
        {
            Shader shader = Shader.Find("Gitmos/Additive");
            if (shader == null)
                shader = screenShader;
            material = new Material(shader);
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetFloat("_Boost", 1.6f);

            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);

            mesh = new Mesh { name = "ActivityBars", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var meshObject = new GameObject("Bars");
            meshObject.transform.SetParent(pivot, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int i = 0; i < Columns; i++)
            {
                float u = (i + 0.5f) / Columns;
                var column = new Column
                {
                    X = (u - 0.5f) * StageWidth + Next(-0.12f, 0.12f),
                    Z = Next(-1.2f, 3.2f),
                    Width = Next(0.03f, 0.12f),
                    Color = ColumnColors[(i * 4 + (int)Next(0f, 3f)) % ColumnColors.Length]
                };
                for (int s = 0; s < Slices; s++)
                {
                    column.Level[s] = Ambient();
                    column.Thickness[s] = Next(0.25f, 0.9f);
                }
                columns[i] = column;
            }
        }

        float Next(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)rng.NextDouble());
        }

        float Ambient()
        {
            double roll = rng.NextDouble();
            if (roll < 0.72)
                return 0f;
            return Mathf.Lerp(0.03f, 0.22f, (float)(rng.NextDouble() * rng.NextDouble()));
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Camera cam = Camera.main;
            if (cam != null)
                PlaceInFrontOf(cam, Distance);

            if (Alpha > 0.01f)
                visibleFor += dt;
            else
                visibleFor = 0f;

            // Upright barcode first, then tip back into receding lanes.
            float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 11.5f, visibleFor));
            if (pivot != null)
            {
                pivot.localRotation = Quaternion.Euler(tip * 64f, tip * -16f, tip * 9f);
                pivot.localPosition = new Vector3(0f, tip * -1.4f, tip * 1.6f);
            }

            scroll += dt / ScrollSeconds;
            while (scroll >= 1f)
            {
                scroll -= 1f;
                Advance();
            }

            for (int i = 0; i < Columns; i++)
                columns[i].Energy = Mathf.Max(0f, columns[i].Energy - dt * 0.35f);

            if (material != null)
                material.SetFloat("_Alpha", Alpha);
            if (meshRenderer != null)
                meshRenderer.enabled = Alpha > 0.01f;
            if (Alpha > 0.01f)
                Draw();
        }

        void Advance()
        {
            for (int c = 0; c < Columns; c++)
            {
                Column column = columns[c];
                for (int s = Slices - 1; s > 0; s--)
                {
                    column.Level[s] = column.Level[s - 1];
                    column.Thickness[s] = column.Thickness[s - 1];
                }
                float lit = Mathf.Min(1.5f, column.Pending);
                column.Pending = Mathf.Max(0f, column.Pending - 0.6f);
                column.Level[0] = lit > 0.05f ? Mathf.Max(lit, Ambient()) : Ambient();
                column.Thickness[0] = lit > 0.05f ? Next(0.55f, 0.95f) : Next(0.2f, 0.9f);
            }
        }

        void Draw()
        {
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();

            float sliceHeight = StageHeight / Slices;
            float top = StageHeight * 0.5f;
            for (int c = 0; c < Columns; c++)
            {
                Column column = columns[c];
                float depthFade = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(-1.2f, 3.2f, column.Z));

                // Faint full-height body so quiet columns still read as columns.
                Color body = column.Color * (0.012f + column.Energy * 0.03f) * depthFade;
                body.a = 1f;
                AddQuad(column.X, column.Z, -top, top, column.Width * 0.5f, body);

                for (int s = 0; s < Slices; s++)
                {
                    float level = column.Level[s];
                    if (level < 0.02f)
                        continue;
                    // Slices fade as they fall toward the bottom of the stage.
                    float age = (s + scroll) / Slices;
                    float fall = Mathf.Lerp(1f, 0.25f, age * age);
                    float y = top - (s + scroll) * sliceHeight;
                    float half = sliceHeight * 0.5f * column.Thickness[s];
                    float intensity = level * fall * depthFade * (level > 0.5f ? 2.4f : 1f);
                    Color color = Color.Lerp(column.Color, Color.white, Mathf.Clamp01(level - 0.7f) * 1.4f) * intensity;
                    color.a = 1f;
                    AddQuad(column.X, column.Z, y - half, y + half, column.Width, color);
                    if (level > 0.6f)
                    {
                        Color glow = column.Color * (intensity * 0.22f);
                        glow.a = 1f;
                        AddQuad(column.X, column.Z, y - sliceHeight * 0.9f, y + sliceHeight * 0.9f, column.Width * 3.2f, glow);
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void AddQuad(float x, float z, float y0, float y1, float halfWidth, Color color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x - halfWidth, y0, z));
            vertices.Add(new Vector3(x - halfWidth, y1, z));
            vertices.Add(new Vector3(x + halfWidth, y1, z));
            vertices.Add(new Vector3(x + halfWidth, y0, z));
            for (int k = 0; k < 4; k++)
                colors.Add(color);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
