using UnityEngine;

namespace Gitmos
{
    public class StabilityMesh : StageBase
    {
        public Shader stabilityShader;
        public Shader additiveShader;

        public float Density { get; private set; } = 6f;

        const int Columns = 80;
        const int Rows = 36;

        Mesh body;
        Mesh threads;
        Material bodyMaterial;
        Material threadMaterial;
        MeshRenderer bodyRenderer;
        MeshRenderer threadRenderer;
        Vector3[] bodyVertices;
        Vector2[] bodyUvs;
        Color[] bodyColors;
        Vector3[] threadVertices;
        Color[] threadColors;

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
            if (body != null)
                Destroy(body);
            if (threads != null)
                Destroy(threads);
            if (bodyMaterial != null)
                Destroy(bodyMaterial);
            if (threadMaterial != null)
                Destroy(threadMaterial);
        }

        void HandleEvent(GitEvent gitEvent)
        {
            if (gitEvent.Type == GitEventType.Merge || gitEvent.Type == GitEventType.Review || gitEvent.Type == GitEventType.PullRequest)
                Density = Mathf.Min(26f, Density + 0.12f);
            else if (!string.IsNullOrEmpty(gitEvent.Category))
                Density = Mathf.Min(26f, Density + 0.05f);
        }

        void Update()
        {
            Density = Mathf.Min(26f, Density + Time.deltaTime * 0.045f);
            transform.localRotation = Quaternion.Euler(12f, Mathf.Sin(Time.time * 0.25f) * 10f, 0f);
        }

        public override void SetAlpha(float alpha)
        {
            base.SetAlpha(alpha);
            if (bodyMaterial != null)
                bodyMaterial.SetFloat("_Alpha", Alpha);
            if (threadMaterial != null)
                threadMaterial.SetFloat("_Alpha", Alpha);
            if (bodyRenderer != null)
                bodyRenderer.enabled = Alpha > 0.01f;
            if (threadRenderer != null)
                threadRenderer.enabled = Alpha > 0.01f;
        }

        void LateUpdate()
        {
            if (bodyMaterial != null)
            {
                bodyMaterial.SetFloat("_LinesY", Mathf.Lerp(8f, 22f, Density / 26f));
                bodyMaterial.SetFloat("_Alpha", Alpha);
            }
            if (threadMaterial != null)
                threadMaterial.SetFloat("_Alpha", Alpha);
            if (Alpha <= 0.01f)
                return;
            Deform();
        }

        void Build()
        {
            if (stabilityShader == null)
                stabilityShader = Shader.Find("Gitmos/Stability");
            if (additiveShader == null)
                additiveShader = Shader.Find("Gitmos/Additive");

            body = new Mesh { name = "StabilityBody", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            int vertCount = Columns * Rows;
            bodyVertices = new Vector3[vertCount];
            bodyUvs = new Vector2[vertCount];
            bodyColors = new Color[vertCount];
            var triangles = new int[(Columns - 1) * (Rows - 1) * 6];
            int t = 0;
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    int index = y * Columns + x;
                    bodyUvs[index] = new Vector2(x / (Columns - 1f), y / (Rows - 1f));
                    bodyColors[index] = Color.white;
                    if (x < Columns - 1 && y < Rows - 1)
                    {
                        int a = index;
                        int b = index + 1;
                        int c = index + Columns;
                        int d = c + 1;
                        triangles[t++] = a;
                        triangles[t++] = c;
                        triangles[t++] = b;
                        triangles[t++] = b;
                        triangles[t++] = c;
                        triangles[t++] = d;
                    }
                }
            }
            body.vertices = bodyVertices;
            body.uv = bodyUvs;
            body.colors = bodyColors;
            body.triangles = triangles;

            bodyMaterial = new Material(stabilityShader);
            bodyMaterial.SetFloat("_LinesX", 56f);
            bodyMaterial.SetFloat("_LinesY", 8f);
            bodyMaterial.SetFloat("_Fill", 0.025f);
            bodyMaterial.SetFloat("_Boost", 1.45f);
            bodyMaterial.SetColor("_ColorA", new Color(0.25f, 0.9f, 1f));
            bodyMaterial.SetColor("_ColorB", new Color(0.75f, 0.35f, 1f));

            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(transform, false);
            var bodyFilter = bodyObject.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = body;
            bodyRenderer = bodyObject.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = bodyMaterial;
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            threads = new Mesh { name = "StabilityThreads" };
            threadMaterial = new Material(additiveShader);
            threadMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            threadMaterial.SetFloat("_Boost", 1.1f);
            var threadObject = new GameObject("Threads");
            threadObject.transform.SetParent(transform, false);
            var threadFilter = threadObject.AddComponent<MeshFilter>();
            threadFilter.sharedMesh = threads;
            threadRenderer = threadObject.AddComponent<MeshRenderer>();
            threadRenderer.sharedMaterial = threadMaterial;
            threadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Deform();
        }

        void Deform()
        {
            float time = Time.time;
            float amp = Mathf.Lerp(0.35f, 1f, Density / 26f);
            for (int y = 0; y < Rows; y++)
            {
                float yn = y / (Rows - 1f);
                for (int x = 0; x < Columns; x++)
                {
                    float xn = x / (Columns - 1f);
                    float signed = xn * 2f - 1f;
                    float half = Mathf.Lerp(0.12f, 1f, Mathf.Pow(1f - Mathf.Abs(signed), 0.75f));
                    float py = Mathf.Lerp(-half, half, yn);
                    float ripple = Mathf.Sin(signed * 7.5f + time * 0.8f) * 0.16f * amp;
                    py += ripple * (1f - signed * signed);
                    float wave = Mathf.Sin(signed * 4.2f + time * 0.8f) * Mathf.Sin(py * 5f - time * 0.45f);
                    float ridge = Mathf.Sin(signed * (3f + Density * 0.18f) + time * 0.6f) * Mathf.Exp(-py * py * 2.4f);
                    float z = (wave * 0.22f + ridge * 0.42f) * amp * (1f - signed * signed);
                    bodyVertices[y * Columns + x] = new Vector3(signed * 3.3f, py * 1.55f, z);
                }
            }
            body.vertices = bodyVertices;
            body.RecalculateNormals();
            body.RecalculateBounds();

            int lines = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(10f, 24f, Density / 26f)), 2, 24);
            int vertCount = lines * 4;
            if (threadVertices == null || threadVertices.Length != vertCount)
            {
                threadVertices = new Vector3[vertCount];
                threadColors = new Color[vertCount];
                var tris = new int[lines * 6];
                for (int i = 0; i < lines; i++)
                {
                    int v = i * 4;
                    int t = i * 6;
                    tris[t] = v;
                    tris[t + 1] = v + 1;
                    tris[t + 2] = v + 2;
                    tris[t + 3] = v;
                    tris[t + 4] = v + 2;
                    tris[t + 5] = v + 3;
                }
                threads.Clear();
                threads.vertices = threadVertices;
                threads.colors = threadColors;
                threads.triangles = tris;
            }

            float thickness = 0.012f;
            for (int i = 0; i < lines; i++)
            {
                float yn = i / (lines - 1f);
                float y = Mathf.Lerp(-1.15f, 1.15f, yn);
                float wobble = Mathf.Sin(time * 0.9f + i * 0.7f) * 0.08f;
                float reach = Mathf.Lerp(3.4f, 4.6f, Mathf.PerlinNoise(i * 0.17f, Density * 0.05f));
                Vector3 left = new Vector3(-reach, y + wobble, 0.05f);
                Vector3 right = new Vector3(reach, y + wobble, 0.05f);
                Vector3 up = Vector3.up * thickness;
                int v = i * 4;
                threadVertices[v] = left - up;
                threadVertices[v + 1] = left + up;
                threadVertices[v + 2] = right + up;
                threadVertices[v + 3] = right - up;
                Color color = Color.Lerp(new Color(0.3f, 0.9f, 1f, 0.45f), new Color(0.8f, 0.4f, 1f, 0.45f), yn);
                for (int k = 0; k < 4; k++)
                    threadColors[v + k] = color;
            }
            threads.vertices = threadVertices;
            threads.colors = threadColors;
            threads.RecalculateBounds();
        }
    }
}
