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

        public int ActiveArcs { get; private set; }

        const int Segments = 28;
        const int MaxArcs = 56;
        const float Radius = 2.05f;

        static readonly Color[] FiberPalette =
        {
            new Color(0.35f, 0.95f, 1f),
            new Color(0.78f, 0.38f, 1f),
            new Color(0.92f, 0.94f, 1f),
            new Color(0.48f, 0.28f, 1f),
            new Color(0.55f, 0.82f, 1f),
            new Color(1f, 0.5f, 0.92f)
        };

        class Arc
        {
            public Vector3 From;
            public Vector3 To;
            public Color Color;
            public float Age;
            public float Life;
            public float Lift;
        }

        class SprayFiber
        {
            public float LatOffset;
            public float LonOffset;
            public float LatReach;
            public float LonReach;
            public float Lift;
            public Color Color;
            public float Phase;
            public float Speed;
        }

        class Fan
        {
            public Vector3 Origin;
            public Color Color;
            public float Age;
            public float Life;
            public float Seed;
        }

        class Callout
        {
            public string Name;
            public Vector3 Direction;
            public float Age;
        }

        class Burst
        {
            public Vector3 Direction;
            public Color Color;
            public float Age;
            public float Life;
        }

        readonly List<Arc> arcs = new List<Arc>(MaxArcs);
        readonly List<Burst> bursts = new List<Burst>(64);
        readonly List<SprayFiber> spray = new List<SprayFiber>(28);
        readonly List<Fan> fans = new List<Fan>(12);
        readonly List<Callout> callouts = new List<Callout>(6);
        readonly float[] flash = new float[64];
        Vector3[] cityLights = System.Array.Empty<Vector3>();
        float[] cityLightSizes = System.Array.Empty<float>();
        float sprayLongitude = 120f;

        MeshRenderer sphereRenderer;
        MeshRenderer atmosphereRenderer;
        MeshRenderer pointRenderer;
        MeshRenderer arcRenderer;
        Mesh pointMesh;
        Mesh arcMesh;
        Material pointMaterial;
        Material arcMaterial;
        Material sphereMaterial;
        Material atmosphereMaterial;
        readonly List<Vector3> pointVerts = new List<Vector3>(4000);
        readonly List<Vector2> pointUvs = new List<Vector2>(4000);
        readonly List<Color> pointColors = new List<Color>(4000);
        readonly List<int> pointTris = new List<int>(6000);
        readonly List<Vector3> arcVerts = new List<Vector3>(16000);
        readonly List<Vector2> arcUvs = new List<Vector2>(16000);
        readonly List<Color> arcColors = new List<Color>(16000);
        readonly List<int> arcTris = new List<int>(24000);
        Vector3[] starDirections;
        float[] starSizes;

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
            DestroyObject(pointMesh);
            DestroyObject(arcMesh);
            DestroyObject(pointMaterial);
            DestroyObject(arcMaterial);
            DestroyObject(sphereMaterial);
            DestroyObject(atmosphereMaterial);
        }

        void HandleEvent(GitEvent gitEvent)
        {
            if (gitEvent.SkipGlobe)
                return;

            int index = Mathf.Clamp(gitEvent.CityIndex, 0, Catalog.Cities.Length - 1);
            if (index < flash.Length)
                flash[index] = 1f;

            Vector3 origin = Catalog.Direction(gitEvent.Latitude, gitEvent.Longitude);
            Color ink = Palette.For(gitEvent);
            bursts.Add(new Burst
            {
                Direction = origin,
                Color = ink,
                Age = 0f,
                Life = 0.9f
            });
            string place = string.IsNullOrEmpty(gitEvent.City) ? gitEvent.Actor : gitEvent.City;
            RememberCallout(place, origin);
            if (gitEvent.Type != GitEventType.Delete)
            {
                if (fans.Count >= 8)
                    fans.RemoveAt(0);
                Color fanTint = FiberPalette[Random.Range(0, FiberPalette.Length)];
                fans.Add(new Fan
                {
                    Origin = origin,
                    Color = Color.Lerp(ink, fanTint, 0.7f),
                    Age = 0f,
                    Life = Random.Range(1.8f, 3.2f),
                    Seed = Random.Range(0f, 6.2f)
                });
            }

            if (gitEvent.Type == GitEventType.Delete)
                return;

            Vector3 destination;
            if (gitEvent.HasTarget)
            {
                destination = Catalog.Direction(gitEvent.TargetLatitude, gitEvent.TargetLongitude);
                if (!string.IsNullOrEmpty(gitEvent.TargetPlace))
                    RememberCallout(gitEvent.TargetPlace, destination);
            }
            else if (!string.IsNullOrEmpty(gitEvent.Category))
            {
                return;
            }
            else
            {
                int other = Random.Range(0, Catalog.Cities.Length);
                if (other == index)
                    other = (other + 7) % Catalog.Cities.Length;
                City city = Catalog.Cities[other];
                destination = Catalog.Direction(city.Latitude, city.Longitude);
            }

            if (arcs.Count >= MaxArcs)
                arcs.RemoveAt(0);

            Color tint = FiberPalette[Random.Range(0, FiberPalette.Length)];
            arcs.Add(new Arc
            {
                From = origin,
                To = destination,
                Color = Color.Lerp(ink, tint, 0.78f),
                Age = 0f,
                Life = Random.Range(5.5f, 8.5f),
                Lift = Random.Range(0.35f, 0.7f)
            });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Catalog.Cities.Length && i < flash.Length; i++)
                flash[i] = Mathf.Max(0f, flash[i] - dt * 0.85f);

            for (int i = arcs.Count - 1; i >= 0; i--)
            {
                arcs[i].Age += dt;
                if (arcs[i].Age >= arcs[i].Life)
                    arcs.RemoveAt(i);
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                bursts[i].Age += dt;
                if (bursts[i].Age >= bursts[i].Life)
                    bursts.RemoveAt(i);
            }
            for (int i = fans.Count - 1; i >= 0; i--)
            {
                fans[i].Age += dt;
                if (fans[i].Age >= fans[i].Life)
                    fans.RemoveAt(i);
            }
            for (int i = callouts.Count - 1; i >= 0; i--)
            {
                callouts[i].Age += dt;
                if (callouts[i].Age > 7f)
                    callouts.RemoveAt(i);
            }
            ActiveArcs = arcs.Count;
            sprayLongitude += dt * 4.5f;
            for (int i = 0; i < spray.Count; i++)
                spray[i].Phase += dt * spray[i].Speed;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            float alpha = Alpha;
            if (sphereMaterial != null)
                sphereMaterial.SetFloat("_Alpha", alpha);
            if (atmosphereMaterial != null)
                atmosphereMaterial.SetFloat("_Alpha", alpha * 0.42f);
            if (pointMaterial != null)
                pointMaterial.SetFloat("_Alpha", alpha);
            if (arcMaterial != null)
                arcMaterial.SetFloat("_Alpha", alpha);

            bool show = alpha > 0.01f && cam != null;
            if (sphereRenderer != null)
                sphereRenderer.enabled = show;
            if (atmosphereRenderer != null)
                atmosphereRenderer.enabled = show;
            if (pointRenderer != null)
                pointRenderer.enabled = show;
            if (arcRenderer != null)
                arcRenderer.enabled = show;
            if (!show)
                return;

            DrawPoints(cam);
            DrawArcs();
            DrawCallouts(cam);
        }

        void Build()
        {
            if (fresnelShader == null)
                fresnelShader = Shader.Find("Gitmos/Fresnel");
            if (additiveShader == null)
                additiveShader = Shader.Find("Gitmos/Additive");

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sphere";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * (Radius * 2f);
            DestroyObject(sphere.GetComponent<Collider>());
            sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphereMaterial = new Material(fresnelShader);
            sphereMaterial.SetTexture("_LandMap", LoadEarth());
            sphereMaterial.SetColor("_BaseColor", new Color(0.003f, 0.004f, 0.008f));
            sphereMaterial.SetColor("_LandColor", new Color(0.07f, 0.09f, 0.13f));
            sphereMaterial.SetColor("_RimColor", new Color(0.07f, 0.16f, 0.38f));
            sphereMaterial.SetFloat("_RimPower", 3.1f);
            sphereMaterial.SetFloat("_Coverage", 1f);
            sphereRenderer.sharedMaterial = sphereMaterial;
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;

            var atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            atmosphere.name = "Atmosphere";
            atmosphere.transform.SetParent(transform, false);
            atmosphere.transform.localScale = Vector3.one * (Radius * 2.18f);
            DestroyObject(atmosphere.GetComponent<Collider>());
            atmosphereRenderer = atmosphere.GetComponent<MeshRenderer>();
            atmosphereMaterial = new Material(fresnelShader);
            atmosphereMaterial.SetTexture("_LandMap", Texture2D.blackTexture);
            atmosphereMaterial.SetColor("_BaseColor", Color.black);
            atmosphereMaterial.SetColor("_LandColor", Color.black);
            atmosphereMaterial.SetColor("_RimColor", new Color(0.1f, 0.24f, 0.5f));
            atmosphereMaterial.SetFloat("_RimPower", 2.7f);
            atmosphereMaterial.SetFloat("_Coverage", 0f);
            atmosphereRenderer.sharedMaterial = atmosphereMaterial;
            atmosphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Texture2D circle = ProceduralTextures.SoftCircle(64);
            pointMaterial = new Material(additiveShader);
            pointMaterial.SetTexture("_BaseMap", circle);
            pointMaterial.SetFloat("_Boost", 2.4f);
            arcMaterial = new Material(additiveShader);
            arcMaterial.SetTexture("_BaseMap", ProceduralTextures.FiberStrip(64));
            arcMaterial.SetFloat("_Boost", 1.65f);

            pointMesh = new Mesh { name = "GlobePoints", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            arcMesh = new Mesh { name = "GlobeArcs", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            int stars = 280;
            starDirections = new Vector3[stars];
            starSizes = new float[stars];
            for (int i = 0; i < stars; i++)
            {
                starDirections[i] = Random.onUnitSphere;
                starSizes[i] = Random.Range(0.015f, 0.045f);
            }

            var points = new GameObject("Points");
            points.transform.SetParent(transform, false);
            var filter = points.AddComponent<MeshFilter>();
            filter.sharedMesh = pointMesh;
            pointRenderer = points.AddComponent<MeshRenderer>();
            pointRenderer.sharedMaterial = pointMaterial;
            pointRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var arcObject = new GameObject("Arcs");
            arcObject.transform.SetParent(transform, false);
            var arcFilter = arcObject.AddComponent<MeshFilter>();
            arcFilter.sharedMesh = arcMesh;
            arcRenderer = arcObject.AddComponent<MeshRenderer>();
            arcRenderer.sharedMaterial = arcMaterial;
            arcRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            BuildCityLights();
            SeedSpray();
            BuildRings();
        }

        void BuildCityLights()
        {
            var directions = new List<Vector3>(360);
            var sizes = new List<float>(360);
            var rng = new System.Random(19);
            int attempts = 0;
            while (directions.Count < 320 && attempts < 9000)
            {
                attempts++;
                float lat = Mathf.Lerp(-56f, 70f, (float)rng.NextDouble());
                float lon = Mathf.Lerp(-180f, 180f, (float)rng.NextDouble());
                if (ProceduralTextures.LandWeight(lat, lon) < 0.42f)
                    continue;
                directions.Add(Catalog.Direction(lat, lon));
                float n = (float)rng.NextDouble();
                sizes.Add(Mathf.Lerp(0.007f, n > 0.92f ? 0.026f : 0.016f, n));
            }
            cityLights = directions.ToArray();
            cityLightSizes = sizes.ToArray();
        }

        void SeedSpray()
        {
            var rng = new System.Random(3);
            for (int i = 0; i < 34; i++)
            {
                bool longArc = rng.NextDouble() > 0.62;
                float reach = longArc ? Mathf.Lerp(28f, 74f, (float)rng.NextDouble()) : Mathf.Lerp(8f, 24f, (float)rng.NextDouble());
                spray.Add(new SprayFiber
                {
                    LatOffset = Mathf.Lerp(-16f, 20f, (float)rng.NextDouble()),
                    LonOffset = Mathf.Lerp(-14f, 18f, (float)rng.NextDouble()),
                    LatReach = reach * (rng.NextDouble() > 0.45 ? 1f : -0.35f),
                    LonReach = reach * (rng.NextDouble() > 0.5f ? 1f : -1f),
                    Lift = longArc ? Mathf.Lerp(0.9f, 1.7f, (float)rng.NextDouble()) : Mathf.Lerp(0.4f, 1.05f, (float)rng.NextDouble()),
                    Color = FiberPalette[i % FiberPalette.Length],
                    Phase = (float)rng.NextDouble(),
                    Speed = Mathf.Lerp(0.18f, 0.55f, (float)rng.NextDouble())
                });
            }
        }

        void BuildRings()
        {
            float[] pitch = { 68f, 46f, 18f, -22f, 98f, -54f };
            float[] yaw = { 8f, 36f, 74f, 118f, 156f, 208f };
            float[] roll = { 6f, -12f, 20f, 4f, -16f, 28f };
            float[] scale = { 1.14f, 1.26f, 1.4f, 1.55f, 1.72f, 1.9f };
            float[] phase = { 0.2f, 1.4f, 2.6f, 0.8f, 3.4f, 2.1f };
            for (int ring = 0; ring < pitch.Length; ring++)
            {
                var ringObject = new GameObject("Ring" + ring);
                ringObject.transform.SetParent(transform, false);
                ringObject.transform.localRotation = Quaternion.Euler(pitch[ring], yaw[ring], roll[ring]);
                var filter = ringObject.AddComponent<MeshFilter>();
                var renderer = ringObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = RingMesh(Radius * scale[ring], 0.0042f, 128, phase[ring]);
                var material = new Material(additiveShader);
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                material.SetFloat("_Boost", 0.95f);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var spinner = ringObject.AddComponent<RingSpin>();
                spinner.degreesPerSecond = (ring % 2 == 0 ? 6f : -4.5f);
                spinner.material = material;
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
                Vector3 side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = dir * radius + side * width;
                vertices[i * 2 + 1] = dir * radius - side * width;
                float fade = Mathf.SmoothStep(-0.15f, 0.9f, Mathf.Sin(angle + phase));
                var color = new Color(0.62f, 0.92f, 1f, Mathf.Lerp(0.04f, 0.72f, fade));
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

        void DrawPoints(Camera cam)
        {
            pointVerts.Clear();
            pointUvs.Clear();
            pointColors.Clear();
            pointTris.Clear();

            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;
            for (int i = 0; i < Catalog.Cities.Length; i++)
            {
                City city = Catalog.Cities[i];
                Vector3 dir = Catalog.Direction(city.Latitude, city.Longitude);
                float pulse = i < flash.Length ? flash[i] : 0f;
                float size = 0.012f + pulse * 0.018f;
                Color color = Color.Lerp(new Color(0.75f, 0.88f, 1f), new Color(1.2f, 1.35f, 1.5f), pulse);
                color.a = 0.15f + pulse * 1.1f;
                AddBillboard(dir * (Radius * 1.01f), right, up, size, color);
            }

            for (int i = 0; i < bursts.Count; i++)
            {
                Burst burst = bursts[i];
                float u = burst.Age / burst.Life;
                float size = Mathf.Lerp(0.02f, 0.08f, u);
                Color color = burst.Color;
                color.a = (1f - u) * 1.8f;
                AddBillboard(burst.Direction * (Radius * 1.02f), right, up, size, color);
            }

            for (int i = 0; i < starDirections.Length; i++)
            {
                float radius = 16f + (i % 7) * 1.4f;
                Color color = new Color(0.7f, 0.8f, 1f, 0.22f);
                AddBillboard(starDirections[i] * radius, right, up, starSizes[i], color);
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
            pointColors.Add(color);
            pointColors.Add(color);
            pointColors.Add(color);
            pointColors.Add(color);
            pointTris.Add(start);
            pointTris.Add(start + 1);
            pointTris.Add(start + 2);
            pointTris.Add(start);
            pointTris.Add(start + 2);
            pointTris.Add(start + 3);
        }

        void DrawArcs()
        {
            arcVerts.Clear();
            arcUvs.Clear();
            arcColors.Clear();
            arcTris.Clear();

            Vector3 hotspot = Catalog.Direction(36f, sprayLongitude);
            DrawFan(hotspot, new Color(0.45f, 0.95f, 1f), 0.85f, 1.7f, 18);

            for (int i = 0; i < fans.Count; i++)
            {
                Fan fan = fans[i];
                float life = Mathf.Clamp01(fan.Age / fan.Life);
                float fade = Mathf.Clamp01(fan.Age / 0.15f) * Mathf.Clamp01((fan.Life - fan.Age) / 0.7f);
                DrawFan(fan.Origin, fan.Color, fade, fan.Seed, 14);
            }

            for (int i = 0; i < arcs.Count; i++)
            {
                Arc arc = arcs[i];
                float life = Mathf.Clamp01(arc.Age / arc.Life);
                float fadeIn = Mathf.Clamp01(arc.Age / 0.35f);
                float fadeOut = Mathf.Clamp01((arc.Life - arc.Age) / 1.4f);
                float intensity = fadeIn * fadeOut;
                float angle = Vector3.Angle(arc.From, arc.To);
                float lift = Mathf.Lerp(0.18f, 0.72f, Mathf.Clamp01(angle / 140f)) * arc.Lift;
                DrawFiber(arc.From, arc.To, lift, arc.Color, intensity, life);
            }

            arcMesh.Clear();
            if (arcVerts.Count == 0)
                return;
            arcMesh.SetVertices(arcVerts);
            arcMesh.SetUVs(0, arcUvs);
            arcMesh.SetColors(arcColors);
            arcMesh.SetTriangles(arcTris, 0);
            arcMesh.RecalculateBounds();
        }

        void DrawFiber(Vector3 from, Vector3 to, float lift, Color color, float intensity, float head)
        {
            if (intensity <= 0.01f)
                return;
            Color glow = color;
            glow.a = intensity * 0.22f;
            AppendRibbon(from, to, lift, 0.02f, glow, head);

            Color core = color;
            core.r = Mathf.Lerp(core.r, 1.25f, 0.45f);
            core.g = Mathf.Lerp(core.g, 1.2f, 0.4f);
            core.b = Mathf.Lerp(core.b, 1.35f, 0.45f);
            core.a = intensity;
            AppendRibbon(from, to, lift, 0.0035f, core, head);
        }

        void DrawFan(Vector3 origin, Color color, float intensity, float seed, int count)
        {
            if (intensity <= 0.02f)
                return;
            Vector3 tangent = Vector3.Cross(origin, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(origin, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(origin, tangent).normalized;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + seed;
                float lift = 0.2f + (i % 5) * 0.16f;
                Vector3 aim = (origin * lift + tangent * Mathf.Cos(ang) * 0.85f + bitangent * Mathf.Sin(ang) * 0.85f).normalized;
                float length = 0.28f + (i % 4) * 0.32f + (i % 3) * 0.12f;
                Vector3 start = origin * (Radius * 1.01f);
                Color sample = color;
                sample.a = intensity * (0.4f + (i % 3) * 0.22f);
                AppendStraight(start, start + aim * length, 0.0045f, sample);
            }
        }

        void AppendStraight(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 forward = b - a;
            if (forward.sqrMagnitude < 0.000001f)
                return;
            forward.Normalize();
            Vector3 side = Vector3.Cross(forward, a.sqrMagnitude > 0.001f ? a.normalized : Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(forward, Vector3.right);
            side.Normalize();
            int start = arcVerts.Count;
            arcVerts.Add(a + side * width);
            arcVerts.Add(a - side * width);
            arcVerts.Add(b + side * width);
            arcVerts.Add(b - side * width);
            arcColors.Add(color);
            arcColors.Add(color);
            arcColors.Add(color);
            arcColors.Add(color);
            arcUvs.Add(new Vector2(0f, 1f));
            arcUvs.Add(new Vector2(0f, 0f));
            arcUvs.Add(new Vector2(1f, 1f));
            arcUvs.Add(new Vector2(1f, 0f));
            arcTris.Add(start);
            arcTris.Add(start + 2);
            arcTris.Add(start + 1);
            arcTris.Add(start + 1);
            arcTris.Add(start + 2);
            arcTris.Add(start + 3);
        }

        void RememberCallout(string name, Vector3 direction)
        {
            if (string.IsNullOrEmpty(name))
                return;
            for (int i = 0; i < callouts.Count; i++)
            {
                if (callouts[i].Name == name)
                {
                    callouts[i].Age = 0f;
                    callouts[i].Direction = direction;
                    return;
                }
            }
            if (callouts.Count >= 5)
                callouts.RemoveAt(callouts.Count - 1);
            callouts.Insert(0, new Callout { Name = name.ToLowerInvariant(), Direction = direction, Age = 0f });
        }

        void DrawCallouts(Camera cam)
        {
            if (callouts.Count == 0)
                return;
            DashboardController dashboard = FindFirstObjectByType<DashboardController>();
            if (dashboard == null)
                return;
            UIDocument document = dashboard.GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                return;
            VisualElement root = document.rootVisualElement.Q<VisualElement>("root");
            if (root == null)
                root = document.rootVisualElement;
            VisualElement layer = root.Q<VisualElement>("city-callouts");
            if (layer == null)
            {
                layer = new VisualElement { name = "city-callouts", pickingMode = PickingMode.Ignore };
                layer.style.position = Position.Absolute;
                layer.style.left = 0;
                layer.style.right = 0;
                layer.style.top = 0;
                layer.style.bottom = 0;
                root.Add(layer);
            }
            layer.Clear();
            if (Alpha < 0.4f)
                return;
            for (int i = 0; i < callouts.Count; i++)
            {
                Callout callout = callouts[i];
                Vector3 world = transform.TransformPoint(callout.Direction * (Radius * 1.22f));
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z < 0.1f)
                    continue;
                if (Vector3.Dot(callout.Direction, (cam.transform.position - transform.position).normalized) < 0.12f)
                    continue;
                Vector2 panel = RuntimePanelUtils.ScreenToPanel(root.panel, screen);
                float fade = Mathf.Clamp01(callout.Age / 0.25f) * Mathf.Clamp01((7f - callout.Age) / 1.2f);
                var label = new Label(callout.Name);
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.left = panel.x + 6f;
                label.style.top = panel.y - 8f;
                label.style.fontSize = 11;
                label.style.color = new Color(0.78f, 0.86f, 0.94f, fade * Alpha);
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                layer.Add(label);
            }
        }

        static Texture2D LoadEarth()
        {
            string path = Path.Combine(Application.dataPath, "Textures/earth-night.jpg");
            if (!File.Exists(path))
                return ProceduralTextures.LandMask(256, 128);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "EarthNight"
            };
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        void AppendRibbon(Vector3 from, Vector3 to, float lift, float width, Color color, float head)
        {
            Vector3 previous = Bezier(from, to, lift, 0f);
            int segments = Segments;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Bezier(from, to, lift, t);
                Vector3 forward = point - previous;
                if (forward.sqrMagnitude < 0.000001f)
                    forward = to - from;
                forward.Normalize();
                Vector3 side = Vector3.Cross(forward, previous.sqrMagnitude > 0.001f ? previous.normalized : Vector3.up);
                if (side.sqrMagnitude < 0.0001f)
                    side = Vector3.Cross(forward, Vector3.right);
                side.Normalize();

                float along = (i - 1) / (float)segments;
                float headGlow = Mathf.Exp(-Mathf.Pow((along - head) * 8f, 2f));
                Color sample = color;
                sample.a *= 0.75f + headGlow * 1.15f;

                int start = arcVerts.Count;
                if (i == 1)
                {
                    arcVerts.Add(previous + side * width);
                    arcVerts.Add(previous - side * width);
                    arcColors.Add(sample);
                    arcColors.Add(sample);
                    arcUvs.Add(new Vector2(0f, 1f));
                    arcUvs.Add(new Vector2(0f, 0f));
                    start = arcVerts.Count;
                }
                arcVerts.Add(point + side * width);
                arcVerts.Add(point - side * width);
                arcColors.Add(sample);
                arcColors.Add(sample);
                arcUvs.Add(new Vector2(t, 1f));
                arcUvs.Add(new Vector2(t, 0f));
                int a = start - 2;
                int b = start - 1;
                int c = start;
                int d = start + 1;
                arcTris.Add(a);
                arcTris.Add(c);
                arcTris.Add(b);
                arcTris.Add(b);
                arcTris.Add(c);
                arcTris.Add(d);
                previous = point;
            }
        }

        static Vector3 Bezier(Vector3 from, Vector3 to, float lift, float t)
        {
            Vector3 a = from * Radius;
            Vector3 b = to * Radius;
            Vector3 mid = Vector3.Slerp(from, to, 0.5f).normalized * (Radius + lift);
            float u = 1f - t;
            return u * u * a + 2f * u * t * mid + t * t * b;
        }

        static void DestroyObject(Object target)
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
        public Material material;

        void Update()
        {
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
            var globe = GetComponentInParent<ConnectionGlobe>();
            if (material != null && globe != null)
                material.SetFloat("_Alpha", globe.Alpha);
        }
    }
}
