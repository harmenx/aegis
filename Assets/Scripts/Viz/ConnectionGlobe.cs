using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gitmos
{
    public class ConnectionGlobe : StageBase
    {
        public Shader fresnelShader;
        public Shader additiveShader;
        public Shader haloShader;

        public int ActiveArcs { get; private set; }
        public int ActiveSpikes => spikes.Count;

        const float Radius = 2.05f;
        const float ShellScale = 1.2f;
        const int MaxArcs = 90;
        const int MaxSpikes = 1400;
        const int ArcSegments = 72;
        const int MaxCallouts = 4;

        static readonly Color[] FiberPalette =
        {
            new Color(0.32f, 0.92f, 1f),
            new Color(0.62f, 0.4f, 1f),
            new Color(0.86f, 0.9f, 1f),
            new Color(0.45f, 0.3f, 1f),
            new Color(0.4f, 1f, 0.78f),
            new Color(0.95f, 0.45f, 1f)
        };

        class Arc
        {
            public Vector3 From;
            public Vector3 To;
            public Color Color;
            public float Age;
            public float Travel;
            public float Hold;
            public float Height;
        }

        class Spike
        {
            public Vector3 Base;
            public Vector3 Direction;
            public float Length;
            public Color Color;
            public float Age;
            public float Life;
        }

        class Burst
        {
            public Vector3 Direction;
            public Color Color;
            public float Age;
            public float Life;
        }

        class Callout
        {
            public string Name;
            public string Detail;
            public Vector3 Direction;
            public float Age;
        }

        class CalloutView
        {
            public VisualElement Root;
            public VisualElement Leader;
            public VisualElement Dot;
            public Label Title;
            public Label Detail;
        }

        readonly List<Arc> arcs = new List<Arc>(MaxArcs);
        readonly List<Spike> spikes = new List<Spike>(MaxSpikes);
        readonly List<Burst> bursts = new List<Burst>(64);
        readonly List<Callout> callouts = new List<Callout>(MaxCallouts);
        readonly List<CalloutView> calloutViews = new List<CalloutView>(MaxCallouts);
        readonly float[] heat = new float[64];
        readonly Vector3[] arcPoints = new Vector3[ArcSegments + 1];
        readonly float[] arcAlphas = new float[ArcSegments + 1];

        MeshRenderer sphereRenderer;
        MeshRenderer haloRenderer;
        MeshRenderer pointRenderer;
        MeshRenderer lineRenderer;
        Mesh pointMesh;
        Mesh lineMesh;
        Material pointMaterial;
        Material lineMaterial;
        Material sphereMaterial;
        Material haloMaterial;
        readonly List<Material> ringMaterials = new List<Material>();
        readonly List<Vector3> pointVerts = new List<Vector3>(4000);
        readonly List<Vector2> pointUvs = new List<Vector2>(4000);
        readonly List<Color> pointColors = new List<Color>(4000);
        readonly List<int> pointTris = new List<int>(6000);
        readonly List<Vector3> lineVerts = new List<Vector3>(40000);
        readonly List<Vector2> lineUvs = new List<Vector2>(40000);
        readonly List<Color> lineColors = new List<Color>(40000);
        readonly List<int> lineTris = new List<int>(60000);
        Vector3[] starDirections;
        float[] starSizes;
        Vector3 cameraLocal;
        VisualElement calloutLayer;
        VisualElement calloutRoot;

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
            DisposeObject(pointMesh);
            DisposeObject(lineMesh);
            DisposeObject(pointMaterial);
            DisposeObject(lineMaterial);
            DisposeObject(sphereMaterial);
            DisposeObject(haloMaterial);
            foreach (Material material in ringMaterials)
                DisposeObject(material);
        }

        void HandleEvent(GitEvent gitEvent)
        {
            if (gitEvent.SkipGlobe)
                return;

            int index = Mathf.Clamp(gitEvent.CityIndex, 0, Catalog.Cities.Length - 1);
            if (index < heat.Length)
                heat[index] = Mathf.Min(3f, heat[index] + 0.45f);

            Vector3 origin = Catalog.Direction(gitEvent.Latitude, gitEvent.Longitude);
            Color ink = Palette.For(gitEvent);
            bursts.Add(new Burst { Direction = origin, Color = ink, Age = 0f, Life = 1.1f });
            if (bursts.Count > 60)
                bursts.RemoveAt(0);

            string place = string.IsNullOrEmpty(gitEvent.City) ? gitEvent.Actor : gitEvent.City;
            string detail = string.IsNullOrEmpty(gitEvent.Repo) ? gitEvent.Actor : gitEvent.Repo;
            RememberCallout(place, detail, origin);

            float weight = gitEvent.Weight > 0f ? Mathf.Clamp(gitEvent.Weight, 0.3f, 3f) : 1f;
            SpawnSpikes(origin, ink, Mathf.RoundToInt(Random.Range(4, 9) * weight));

            if (gitEvent.Type == GitEventType.Delete)
                return;

            Vector3 destination;
            if (gitEvent.HasTarget)
            {
                destination = Catalog.Direction(gitEvent.TargetLatitude, gitEvent.TargetLongitude);
                if (!string.IsNullOrEmpty(gitEvent.TargetPlace))
                    RememberCallout(gitEvent.TargetPlace, detail, destination);
            }
            else if (!string.IsNullOrEmpty(gitEvent.Category))
            {
                return;
            }
            else
            {
                if (Random.value > 0.55f)
                    return;
                int other = Random.Range(0, Catalog.Cities.Length);
                if (other == index)
                    other = (other + 7) % Catalog.Cities.Length;
                City city = Catalog.Cities[other];
                destination = Catalog.Direction(city.Latitude, city.Longitude);
            }

            if (arcs.Count >= MaxArcs)
                arcs.RemoveAt(0);

            float angle = Vector3.Angle(origin, destination);
            Color tint = FiberPalette[Random.Range(0, FiberPalette.Length)];
            arcs.Add(new Arc
            {
                From = origin,
                To = destination,
                Color = Color.Lerp(ink, tint, 0.72f),
                Age = 0f,
                Travel = Mathf.Lerp(0.9f, 2.4f, angle / 180f),
                Hold = Random.Range(0.6f, 2.2f),
                Height = Radius * Mathf.Lerp(0.06f, 0.75f, Mathf.Pow(angle / 180f, 0.8f)) * Random.Range(0.8f, 1.2f)
            });
        }

        void SpawnSpikes(Vector3 origin, Color ink, int count)
        {
            Vector3 tangent = Vector3.Cross(origin, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(origin, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(origin, tangent).normalized;

            // Every place leans its hair the same way, so bundles read as one tuft.
            int seed = Mathf.Abs(Mathf.RoundToInt(origin.x * 1000f + origin.y * 7919f + origin.z * 104729f));
            float leanAngle = (seed % 628) / 100f;
            Vector3 lean = tangent * Mathf.Cos(leanAngle) + bitangent * Mathf.Sin(leanAngle);

            for (int i = 0; i < count; i++)
            {
                if (spikes.Count >= MaxSpikes)
                    spikes.RemoveAt(0);
                float spread = 0.028f;
                Vector3 jitter = tangent * Gaussian() * spread + bitangent * Gaussian() * spread;
                Vector3 baseDir = (origin + jitter).normalized;
                Vector3 dir = (baseDir + lean * Random.Range(0.12f, 0.42f) + Random.insideUnitSphere * 0.12f).normalized;
                float roll = Random.value;
                float length = roll > 0.9f ? Random.Range(0.9f, 1.7f) : Random.Range(0.12f, 0.75f);
                Color tint = FiberPalette[Random.Range(0, FiberPalette.Length)];
                spikes.Add(new Spike
                {
                    Base = baseDir,
                    Direction = dir,
                    Length = length,
                    Color = Color.Lerp(ink, tint, 0.65f),
                    Age = 0f,
                    Life = Random.Range(3.5f, 8.5f)
                });
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Catalog.Cities.Length && i < heat.Length; i++)
                heat[i] = Mathf.Max(0f, heat[i] - dt * 0.35f * Mathf.Max(0.4f, heat[i]));

            for (int i = arcs.Count - 1; i >= 0; i--)
            {
                Arc arc = arcs[i];
                arc.Age += dt;
                if (arc.Age >= arc.Travel * 2f + arc.Hold)
                    arcs.RemoveAt(i);
            }
            for (int i = spikes.Count - 1; i >= 0; i--)
            {
                spikes[i].Age += dt;
                if (spikes[i].Age >= spikes[i].Life)
                    spikes.RemoveAt(i);
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                bursts[i].Age += dt;
                if (bursts[i].Age >= bursts[i].Life)
                    bursts.RemoveAt(i);
            }
            for (int i = callouts.Count - 1; i >= 0; i--)
            {
                callouts[i].Age += dt;
                if (callouts[i].Age > 7f)
                    callouts.RemoveAt(i);
            }
            ActiveArcs = arcs.Count;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            float alpha = Alpha;
            if (sphereMaterial != null)
            {
                sphereMaterial.SetFloat("_Alpha", alpha);
                // Only occlude while fully shown; mid-fade it must not punch holes in other stages.
                sphereMaterial.SetFloat("_ZWrite", alpha > 0.98f ? 1f : 0f);
            }
            if (haloMaterial != null)
                haloMaterial.SetFloat("_Alpha", alpha);
            if (pointMaterial != null)
                pointMaterial.SetFloat("_Alpha", alpha);
            if (lineMaterial != null)
                lineMaterial.SetFloat("_Alpha", alpha);
            foreach (Material ring in ringMaterials)
                ring.SetFloat("_Alpha", alpha);

            bool show = alpha > 0.01f && cam != null;
            if (sphereRenderer != null)
                sphereRenderer.enabled = show;
            if (haloRenderer != null)
                haloRenderer.enabled = show;
            if (pointRenderer != null)
                pointRenderer.enabled = show;
            if (lineRenderer != null)
                lineRenderer.enabled = show;
            if (!show)
            {
                HideCallouts();
                return;
            }

            cameraLocal = transform.InverseTransformPoint(cam.transform.position);
            if (sphereMaterial != null)
            {
                // Key light sits up and to the left of the viewer, so the globe keeps a lit
                // face and a night limb from any orbit angle.
                Transform camTransform = cam.transform;
                Vector3 light = -camTransform.right * 0.75f + camTransform.up * 0.55f - camTransform.forward * 0.45f;
                sphereMaterial.SetVector("_LightDir", light.normalized);
            }

            DrawPoints(cam);
            DrawLines();
            DrawCallouts(cam);
        }

        void Build()
        {
            if (fresnelShader == null)
                fresnelShader = Shader.Find("Gitmos/Fresnel");
            if (additiveShader == null)
                additiveShader = Shader.Find("Gitmos/Additive");
            if (haloShader == null)
                haloShader = Shader.Find("Gitmos/Halo");

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sphere";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (Radius * 2f);
            DisposeObject(sphere.GetComponent<Collider>());
            sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphere.GetComponent<MeshFilter>().sharedMesh = SphereMesh(96, 64);
            sphereMaterial = new Material(fresnelShader);
            sphereMaterial.SetTexture("_LandMap", LoadEarth());
            sphereMaterial.SetColor("_BaseColor", new Color(0.012f, 0.011f, 0.022f));
            sphereMaterial.SetColor("_LandColor", new Color(0.36f, 0.32f, 0.5f));
            sphereMaterial.SetColor("_CityColor", new Color(1f, 0.78f, 0.6f));
            sphereMaterial.SetColor("_RimColor", new Color(0.2f, 0.2f, 0.42f));
            sphereMaterial.SetFloat("_RimPower", 3.4f);
            sphereMaterial.SetFloat("_CityBoost", 2.6f);
            sphereMaterial.SetFloat("_Ambient", 0.32f);
            sphereRenderer.sharedMaterial = sphereMaterial;
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;

            if (haloShader != null)
            {
                var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                halo.name = "Halo";
                halo.transform.SetParent(transform, false);
                halo.transform.localScale = Vector3.one * (Radius * 2f * ShellScale);
                DisposeObject(halo.GetComponent<Collider>());
                haloRenderer = halo.GetComponent<MeshRenderer>();
                haloMaterial = new Material(haloShader);
                haloMaterial.SetColor("_Color", new Color(0.1f, 0.11f, 0.28f));
                haloMaterial.SetFloat("_Inner", 1f / ShellScale);
                haloMaterial.SetFloat("_Falloff", 4.2f);
                haloMaterial.SetFloat("_InnerGlow", 0.18f);
                haloRenderer.sharedMaterial = haloMaterial;
                haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            pointMaterial = new Material(additiveShader);
            pointMaterial.SetTexture("_BaseMap", ProceduralTextures.SoftCircle(64));
            pointMaterial.SetFloat("_Boost", 2.2f);
            lineMaterial = new Material(additiveShader);
            lineMaterial.SetTexture("_BaseMap", ProceduralTextures.FiberStrip(64));
            lineMaterial.SetFloat("_Boost", 1.7f);

            pointMesh = new Mesh { name = "GlobePoints", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            lineMesh = new Mesh { name = "GlobeLines", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            pointRenderer = MeshChild("Points", pointMesh, pointMaterial);
            lineRenderer = MeshChild("Lines", lineMesh, lineMaterial);

            int stars = 320;
            starDirections = new Vector3[stars];
            starSizes = new float[stars];
            var rng = new System.Random(11);
            for (int i = 0; i < stars; i++)
            {
                starDirections[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                starSizes[i] = Mathf.Lerp(0.012f, 0.04f, Mathf.Pow((float)rng.NextDouble(), 3f));
            }

            BuildRings();
            SeedSpikes();
        }

        MeshRenderer MeshChild(string childName, Mesh mesh, Material material)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        void SeedSpikes()
        {
            // A warm start so the first frame already has tufts over the busiest places.
            int[] busy = { 1, 0, 2, 3, 5, 8, 19, 24 };
            foreach (int cityIndex in busy)
            {
                if (cityIndex >= Catalog.Cities.Length)
                    continue;
                City city = Catalog.Cities[cityIndex];
                Vector3 dir = Catalog.Direction(city.Latitude, city.Longitude);
                int before = spikes.Count;
                SpawnSpikes(dir, FiberPalette[cityIndex % FiberPalette.Length], 16);
                for (int i = before; i < spikes.Count; i++)
                    spikes[i].Age = Random.Range(0f, spikes[i].Life * 0.7f);
            }
        }

        void BuildRings()
        {
            float[] pitch = { 72f, 28f, -38f };
            float[] yaw = { 12f, 96f, 200f };
            float[] roll = { 8f, -14f, 22f };
            float[] scale = { 1.32f, 1.6f, 1.95f };
            float[] phase = { 0.2f, 1.9f, 3.4f };
            for (int ring = 0; ring < pitch.Length; ring++)
            {
                var ringObject = new GameObject("Ring" + ring);
                ringObject.transform.SetParent(transform, false);
                ringObject.transform.localRotation = Quaternion.Euler(pitch[ring], yaw[ring], roll[ring]);
                var filter = ringObject.AddComponent<MeshFilter>();
                var renderer = ringObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = RingMesh(Radius * scale[ring], 0.0035f, 160, phase[ring]);
                var material = new Material(additiveShader);
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                material.SetFloat("_Boost", 0.42f);
                ringMaterials.Add(material);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var spinner = ringObject.AddComponent<RingSpin>();
                spinner.degreesPerSecond = ring % 2 == 0 ? 5f : -3.5f;
            }
        }

        static Mesh RingMesh(float radius, float width, int segments, float phase)
        {
            var mesh = new Mesh { name = "Ring" };
            var vertices = new Vector3[segments * 2];
            var colors = new Color[segments * 2];
            var uvs = new Vector2[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * (radius + width);
                vertices[i * 2 + 1] = dir * (radius - width);
                float fade = Mathf.SmoothStep(-0.2f, 0.95f, Mathf.Sin(angle + phase));
                var color = new Color(0.55f, 0.62f, 1f, Mathf.Lerp(0.0f, 0.6f, fade));
                colors[i * 2] = color;
                colors[i * 2 + 1] = color;
                uvs[i * 2] = new Vector2(0f, 0f);
                uvs[i * 2 + 1] = new Vector2(1f, 0f);
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh SphereMesh(int longitudeSegments, int latitudeSegments)
        {
            // Unity's primitive sphere is coarse enough that the silhouette shows facets at
            // close range. This UV sphere is radius 0.5 to match the primitive's scale.
            int columns = longitudeSegments + 1;
            int rows = latitudeSegments + 1;
            var vertices = new Vector3[columns * rows];
            var normals = new Vector3[columns * rows];
            var triangles = new int[longitudeSegments * latitudeSegments * 6];
            for (int y = 0; y < rows; y++)
            {
                float lat = Mathf.Lerp(-90f, 90f, y / (float)latitudeSegments);
                for (int x = 0; x < columns; x++)
                {
                    float lon = Mathf.Lerp(-180f, 180f, x / (float)longitudeSegments);
                    Vector3 dir = Catalog.Direction(lat, lon);
                    vertices[y * columns + x] = dir * 0.5f;
                    normals[y * columns + x] = dir;
                }
            }
            int t = 0;
            for (int y = 0; y < latitudeSegments; y++)
            {
                for (int x = 0; x < longitudeSegments; x++)
                {
                    int a = y * columns + x;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = d;
                    triangles[t++] = c;
                }
            }
            var mesh = new Mesh { name = "GlobeSphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        void DrawPoints(Camera cam)
        {
            pointVerts.Clear();
            pointUvs.Clear();
            pointColors.Clear();
            pointTris.Clear();

            Vector3 right = transform.InverseTransformDirection(cam.transform.right);
            Vector3 up = transform.InverseTransformDirection(cam.transform.up);

            for (int i = 0; i < Catalog.Cities.Length && i < heat.Length; i++)
            {
                float h = heat[i];
                if (h < 0.02f)
                    continue;
                City city = Catalog.Cities[i];
                Vector3 dir = Catalog.Direction(city.Latitude, city.Longitude);
                float glow = Mathf.Clamp01(h / 1.5f);
                Color hot = Color.Lerp(new Color(0.55f, 0.6f, 1f), new Color(0.85f, 0.92f, 1f), glow);
                hot.a = 0.25f + glow * 0.75f;
                AddBillboard(dir * (Radius * 1.004f), right, up, 0.035f + glow * 0.09f, hot);
            }

            for (int i = 0; i < bursts.Count; i++)
            {
                Burst burst = bursts[i];
                float u = burst.Age / burst.Life;
                Color color = burst.Color;
                color.a = (1f - u) * (1f - u) * 1.4f;
                AddBillboard(burst.Direction * (Radius * 1.006f), right, up, Mathf.Lerp(0.015f, 0.07f, u), color);
            }

            for (int i = 0; i < arcs.Count; i++)
            {
                Arc arc = arcs[i];
                float head = ArcHead(arc);
                float tail = ArcTail(arc);
                if (head <= tail || head >= 0.999f && arc.Age > arc.Travel + 0.4f)
                    continue;
                Color color = arc.Color;
                color.a = 1.3f;
                AddBillboard(ArcPoint(arc, head), right, up, 0.03f, color);
            }

            for (int i = 0; i < starDirections.Length; i++)
            {
                float radius = 18f + (i % 7) * 1.6f;
                float twinkle = 0.12f + 0.12f * Mathf.Sin(Time.time * (0.4f + (i % 5) * 0.2f) + i);
                AddBillboard(starDirections[i] * radius, right, up, starSizes[i], new Color(0.7f, 0.76f, 1f, twinkle));
            }

            pointMesh.Clear();
            pointMesh.SetVertices(pointVerts);
            pointMesh.SetUVs(0, pointUvs);
            pointMesh.SetColors(pointColors);
            pointMesh.SetTriangles(pointTris, 0);
            pointMesh.RecalculateBounds();
        }

        void AddBillboard(Vector3 center, Vector3 right, Vector3 up, float size, Color color)
        {
            int start = pointVerts.Count;
            pointVerts.Add(center - right * size - up * size);
            pointVerts.Add(center - right * size + up * size);
            pointVerts.Add(center + right * size + up * size);
            pointVerts.Add(center + right * size - up * size);
            pointUvs.Add(new Vector2(0f, 0f));
            pointUvs.Add(new Vector2(0f, 1f));
            pointUvs.Add(new Vector2(1f, 1f));
            pointUvs.Add(new Vector2(1f, 0f));
            for (int k = 0; k < 4; k++)
                pointColors.Add(color);
            pointTris.Add(start);
            pointTris.Add(start + 1);
            pointTris.Add(start + 2);
            pointTris.Add(start);
            pointTris.Add(start + 2);
            pointTris.Add(start + 3);
        }

        static float ArcHead(Arc arc)
        {
            float t = Mathf.Clamp01(arc.Age / arc.Travel);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        static float ArcTail(Arc arc)
        {
            float t = Mathf.Clamp01((arc.Age - arc.Travel - arc.Hold) / arc.Travel);
            return t * t * (3f - 2f * t);
        }

        static Vector3 ArcPoint(Arc arc, float t)
        {
            Vector3 dir = Vector3.Slerp(arc.From, arc.To, t).normalized;
            return dir * (Radius * 1.003f + arc.Height * Mathf.Sin(Mathf.PI * t));
        }

        void DrawLines()
        {
            lineVerts.Clear();
            lineUvs.Clear();
            lineColors.Clear();
            lineTris.Clear();

            for (int i = 0; i < spikes.Count; i++)
            {
                Spike spike = spikes[i];
                float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(spike.Age / 0.6f), 3f);
                float fade = Mathf.Clamp01((spike.Life - spike.Age) / 1.6f);
                if (fade <= 0.01f)
                    continue;
                Vector3 a = spike.Base * (Radius * 1.002f);
                Vector3 b = a + spike.Direction * (spike.Length * grow);
                Color root = spike.Color;
                root.a = 0.75f * fade;
                Color tip = spike.Color;
                tip.a = 0.06f * fade;
                AppendLine(a, b, 0.0042f, root, tip);
            }

            for (int i = 0; i < arcs.Count; i++)
            {
                Arc arc = arcs[i];
                float head = ArcHead(arc);
                float tail = ArcTail(arc);
                if (head - tail < 0.002f)
                    continue;
                int count = Mathf.Clamp(Mathf.CeilToInt(ArcSegments * (head - tail)), 3, ArcSegments);
                for (int k = 0; k <= count; k++)
                {
                    float t = Mathf.Lerp(tail, head, k / (float)count);
                    arcPoints[k] = ArcPoint(arc, t);
                    float nearHead = Mathf.Clamp01(1f - (head - t) * 3.5f);
                    arcAlphas[k] = 0.32f + 0.68f * nearHead * nearHead;
                }
                Color glow = arc.Color;
                glow.a = 0.16f;
                AppendStrip(count + 1, 0.022f, glow);
                Color core = Color.Lerp(arc.Color, Color.white, 0.35f);
                core.a = 0.95f;
                AppendStrip(count + 1, 0.0045f, core);
            }

            lineMesh.Clear();
            if (lineVerts.Count == 0)
                return;
            lineMesh.SetVertices(lineVerts);
            lineMesh.SetUVs(0, lineUvs);
            lineMesh.SetColors(lineColors);
            lineMesh.SetTriangles(lineTris, 0);
            lineMesh.RecalculateBounds();
        }

        Vector3 FacingSide(Vector3 point, Vector3 forward)
        {
            Vector3 toCamera = cameraLocal - point;
            Vector3 side = Vector3.Cross(forward, toCamera);
            if (side.sqrMagnitude < 0.000001f)
                side = Vector3.Cross(forward, Vector3.up);
            return side.normalized;
        }

        void AppendLine(Vector3 a, Vector3 b, float width, Color colorA, Color colorB)
        {
            Vector3 forward = b - a;
            if (forward.sqrMagnitude < 0.0000001f)
                return;
            Vector3 side = FacingSide(a, forward.normalized) * width;
            int start = lineVerts.Count;
            lineVerts.Add(a + side);
            lineVerts.Add(a - side);
            lineVerts.Add(b + side);
            lineVerts.Add(b - side);
            lineColors.Add(colorA);
            lineColors.Add(colorA);
            lineColors.Add(colorB);
            lineColors.Add(colorB);
            lineUvs.Add(new Vector2(0f, 1f));
            lineUvs.Add(new Vector2(0f, 0f));
            lineUvs.Add(new Vector2(1f, 1f));
            lineUvs.Add(new Vector2(1f, 0f));
            lineTris.Add(start);
            lineTris.Add(start + 2);
            lineTris.Add(start + 1);
            lineTris.Add(start + 1);
            lineTris.Add(start + 2);
            lineTris.Add(start + 3);
        }

        void AppendStrip(int count, float width, Color color)
        {
            int start = lineVerts.Count;
            for (int k = 0; k < count; k++)
            {
                Vector3 point = arcPoints[k];
                Vector3 forward = k < count - 1 ? arcPoints[k + 1] - point : point - arcPoints[k - 1];
                if (forward.sqrMagnitude < 0.0000001f)
                    forward = Vector3.up;
                Vector3 side = FacingSide(point, forward.normalized) * width;
                Color sample = color;
                sample.a *= arcAlphas[k];
                lineVerts.Add(point + side);
                lineVerts.Add(point - side);
                lineColors.Add(sample);
                lineColors.Add(sample);
                float u = k / (float)(count - 1);
                lineUvs.Add(new Vector2(u, 1f));
                lineUvs.Add(new Vector2(u, 0f));
                if (k == 0)
                    continue;
                int a = start + (k - 1) * 2;
                lineTris.Add(a);
                lineTris.Add(a + 2);
                lineTris.Add(a + 1);
                lineTris.Add(a + 1);
                lineTris.Add(a + 2);
                lineTris.Add(a + 3);
            }
        }

        void RememberCallout(string name, string detail, Vector3 direction)
        {
            if (string.IsNullOrEmpty(name))
                return;
            string label = name.ToLowerInvariant();
            for (int i = 0; i < callouts.Count; i++)
            {
                if (callouts[i].Name != label)
                    continue;
                callouts[i].Direction = direction;
                callouts[i].Detail = detail;
                if (callouts[i].Age > 5.4f)
                    callouts[i].Age = 5.4f;
                return;
            }
            if (callouts.Count >= MaxCallouts)
            {
                int oldest = 0;
                for (int i = 1; i < callouts.Count; i++)
                {
                    if (callouts[i].Age > callouts[oldest].Age)
                        oldest = i;
                }
                if (callouts[oldest].Age < 2.6f)
                    return;
                callouts.RemoveAt(oldest);
            }
            callouts.Insert(0, new Callout { Name = label, Detail = detail, Direction = direction, Age = 0f });
        }

        bool EnsureCalloutLayer()
        {
            if (calloutLayer != null && calloutLayer.panel != null)
                return true;
            DashboardController dashboard = FindAnyObjectByType<DashboardController>();
            if (dashboard == null)
                return false;
            UIDocument document = dashboard.GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                return false;
            calloutRoot = document.rootVisualElement.Q<VisualElement>("root") ?? document.rootVisualElement;
            calloutLayer = calloutRoot.Q<VisualElement>("city-callouts");
            if (calloutLayer == null)
            {
                calloutLayer = new VisualElement { name = "city-callouts", pickingMode = PickingMode.Ignore };
                calloutLayer.AddToClassList("callout-layer");
                calloutRoot.Insert(0, calloutLayer);
            }
            calloutViews.Clear();
            calloutLayer.Clear();
            return true;
        }

        CalloutView MakeCalloutView()
        {
            var view = new CalloutView { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
            view.Root.AddToClassList("callout");
            view.Leader = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Leader.AddToClassList("callout-leader");
            view.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Dot.AddToClassList("callout-dot");
            view.Title = new Label { pickingMode = PickingMode.Ignore };
            view.Title.AddToClassList("callout-title");
            view.Detail = new Label { pickingMode = PickingMode.Ignore };
            view.Detail.AddToClassList("callout-detail");
            view.Root.Add(view.Leader);
            view.Root.Add(view.Dot);
            view.Root.Add(view.Title);
            view.Root.Add(view.Detail);
            calloutLayer.Add(view.Root);
            calloutViews.Add(view);
            return view;
        }

        void HideCallouts()
        {
            for (int i = 0; i < calloutViews.Count; i++)
                calloutViews[i].Root.style.display = DisplayStyle.None;
        }

        void DrawCallouts(Camera cam)
        {
            if (!EnsureCalloutLayer())
                return;
            if (Alpha < 0.4f || callouts.Count == 0)
            {
                HideCallouts();
                return;
            }

            int shown = 0;
            Vector3 towardCamera = (cam.transform.position - transform.position).normalized;
            // Viewport space keeps labels pinned however the panel is scaled or rendered.
            Vector3 centreViewport = cam.WorldToViewportPoint(transform.position);
            Rect panelRect = calloutRoot.panel.visualTree.layout;
            Vector2 panelSize = new Vector2(panelRect.width, panelRect.height);
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0f)
                return;
            for (int i = 0; i < callouts.Count; i++)
            {
                Callout callout = callouts[i];
                Vector3 anchorWorld = transform.TransformPoint(callout.Direction * Radius);
                Vector3 viewport = cam.WorldToViewportPoint(anchorWorld);
                float facing = Vector3.Dot(transform.TransformDirection(callout.Direction), towardCamera);
                float onFace = Mathf.SmoothStep(0.05f, 0.4f, facing);
                float fade = Mathf.Clamp01(callout.Age / 0.45f) * Mathf.Clamp01((7f - callout.Age) / 1.4f) * onFace;
                if (viewport.z < 0.1f || fade < 0.04f)
                    continue;

                CalloutView view = shown < calloutViews.Count ? calloutViews[shown] : MakeCalloutView();
                Vector2 anchor = new Vector2(viewport.x * panelSize.x, (1f - viewport.y) * panelSize.y);
                Vector2 centre = new Vector2(centreViewport.x * panelSize.x, (1f - centreViewport.y) * panelSize.y);
                // Labels lean away from the globe's centre so they sit off the busy disc.
                bool leftSide = anchor.x < centre.x;
                float rise = 34f + (i % 2) * 22f;

                view.Root.style.display = DisplayStyle.Flex;
                view.Root.style.left = anchor.x;
                view.Root.style.top = anchor.y - rise;
                view.Root.style.opacity = fade;
                view.Leader.style.height = rise;
                view.Dot.style.top = rise - 2f;
                view.Title.text = callout.Name;
                view.Detail.text = Trim(callout.Detail, 30);
                float textOffset = leftSide ? -6f : 6f;
                view.Title.style.translate = new Translate(new Length(leftSide ? -100 : 0, LengthUnit.Percent), 0);
                view.Detail.style.translate = new Translate(new Length(leftSide ? -100 : 0, LengthUnit.Percent), 0);
                view.Title.style.left = textOffset;
                view.Detail.style.left = textOffset;
                shown++;
            }
            for (int i = shown; i < calloutViews.Count; i++)
                calloutViews[i].Root.style.display = DisplayStyle.None;
        }

        static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? "";
            return value.Substring(0, max - 1) + "…";
        }

        static Texture2D LoadEarth()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "earth-night.jpg");
            if (!File.Exists(path))
                path = Path.Combine(Application.dataPath, "Textures/earth-night.jpg");
            if (!File.Exists(path))
                return ProceduralTextures.LandMask(256, 128);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = "EarthNight"
            };
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        static float Gaussian()
        {
            float u = Mathf.Max(0.0001f, Random.value);
            float v = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(Mathf.PI * 2f * v);
        }

        static void DisposeObject(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }

    public class RingSpin : MonoBehaviour
    {
        public float degreesPerSecond = 4f;

        void Update()
        {
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
