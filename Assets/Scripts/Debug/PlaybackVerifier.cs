using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gitmos
{
    public class PlaybackVerifier : MonoBehaviour
    {
        float elapsed;
        bool running;
        bool finished;
        bool sawEvents;
        bool sawArcs;
        bool sawInk;
        bool sawStream;
        bool jumped;
        bool capturedGlobe;
        bool shotQueued;
        float densityAtStart = -1f;
        bool densityGrew;
        bool awaitingOverlay;
        string pendingPath;
        Color32[] scenePixels;
        RenderTexture uiTexture;
        PanelSettings pendingSettings;
        RenderTexture previousPanelTarget;
        int lastBright;
        int lastOverlayPixels;

        const int CaptureWidth = 1280;
        const int CaptureHeight = 720;

        void Start()
        {
#if UNITY_EDITOR
            running = UnityEditor.EditorPrefs.GetBool("Gitmos.Verify", false);
#else
            running = false;
#endif
            if (!running)
            {
                enabled = false;
                return;
            }
            Application.runInBackground = true;
        }

        void Update()
        {
            if (!running || finished)
                return;

            if (awaitingOverlay)
            {
                lastBright = CompleteOverlay();
                awaitingOverlay = false;
            }

            elapsed += Time.deltaTime;
            var globe = FindFirstObjectByType<ConnectionGlobe>();
            var barcode = FindFirstObjectByType<ActivityBarcode>();
            var stability = FindFirstObjectByType<StabilityMesh>();
            var dashboard = FindFirstObjectByType<DashboardController>();
            var metronome = FindFirstObjectByType<Metronome>();

            if (EventBus.Published >= 20)
                sawEvents = true;
            if (globe != null && globe.ActiveArcs > 0)
                sawArcs = true;
            if (barcode != null && barcode.HasInk)
                sawInk = true;
            if (dashboard != null && dashboard.StreamLines > 0)
                sawStream = true;
            if (stability != null && densityAtStart < 0f && elapsed > 0.3f)
                densityAtStart = stability.Density;
            if (stability != null && densityAtStart >= 0f && stability.Density > densityAtStart + 0.08f)
                densityGrew = true;

            if (!capturedGlobe && elapsed > 1.15f && sawArcs && !awaitingOverlay)
            {
                BeginCapture(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playback-globe.png")), dashboard);
                capturedGlobe = true;
                return;
            }

            if (!jumped && elapsed > 1.8f && sawArcs && metronome != null)
            {
                metronome.Jump(3);
                jumped = true;
            }

            if (elapsed < 5.4f || !capturedGlobe || awaitingOverlay)
                return;

            if (!shotQueued)
            {
                BeginCapture(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playback-shot.png")), dashboard);
                shotQueued = true;
                return;
            }

            Finish(globe, barcode, stability, dashboard, metronome);
        }

        void Finish(
            ConnectionGlobe globe,
            ActivityBarcode barcode,
            StabilityMesh stability,
            DashboardController dashboard,
            Metronome metronome)
        {
            finished = true;
            bool stage = metronome != null && metronome.VisibleIndex == 3;
            bool title = dashboard != null && dashboard.StageTitle.Contains("Resolved");
            bool forks = dashboard != null && dashboard.ForkedProjects >= 1248;
            bool pass = sawEvents && sawArcs && sawInk && sawStream && densityGrew && stage && title && forks;

            if (lastBright < 150)
                pass = false;

            var report = new StringBuilder();
            report.AppendLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
            report.AppendLine("events=" + EventBus.Published + " sawEvents=" + sawEvents);
            report.AppendLine("arcs=" + (globe != null ? globe.ActiveArcs : 0) + " sawArcs=" + sawArcs);
            report.AppendLine("ink=" + (barcode != null && barcode.HasInk) + " sawInk=" + sawInk);
            report.AppendLine("stream=" + (dashboard != null ? dashboard.StreamLines : 0) + " sawStream=" + sawStream);
            report.AppendLine("forks=" + (dashboard != null ? dashboard.ForkedProjects : 0));
            report.AppendLine("title=" + (dashboard != null ? dashboard.StageTitle : ""));
            report.AppendLine("stage=" + (metronome != null ? metronome.VisibleIndex : -1));
            report.AppendLine("density=" + (stability != null ? stability.Density : 0f) + " start=" + densityAtStart + " grew=" + densityGrew);
            report.AppendLine("brightPixels=" + lastBright + " overlay=" + lastOverlayPixels);
            report.AppendLine("shot=" + Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playback-shot.png")));

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playback-report.txt"));
            File.WriteAllText(path, report.ToString());
            Debug.Log(report.ToString());

#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetBool("Gitmos.Verify", false);
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void BeginCapture(string path, DashboardController dashboard)
        {
            pendingPath = path;
            Camera cam = Camera.main;
            if (cam == null)
                return;

            var scene = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = scene;
            cam.Render();
            cam.targetTexture = previous;

            RenderTexture.active = scene;
            var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            image.Apply();
            scenePixels = image.GetPixels32();
            RenderTexture.active = null;
            Destroy(image);
            scene.Release();
            Destroy(scene);

            UIDocument document = dashboard != null ? dashboard.GetComponent<UIDocument>() : null;
            if (document == null || document.panelSettings == null)
            {
                lastBright = WriteComposite(path, scenePixels, null);
                return;
            }

            if (uiTexture != null)
            {
                uiTexture.Release();
                Destroy(uiTexture);
            }
            uiTexture = new RenderTexture(CaptureWidth, CaptureHeight, 0, RenderTextureFormat.ARGB32);
            uiTexture.Create();
            RenderTexture.active = uiTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            pendingSettings = document.panelSettings;
            previousPanelTarget = pendingSettings.targetTexture;
            pendingSettings.targetTexture = uiTexture;
            document.rootVisualElement?.MarkDirtyRepaint();
            awaitingOverlay = true;
        }

        int CompleteOverlay()
        {
            Color32[] uiPixels = null;
            if (uiTexture != null)
            {
                RenderTexture.active = uiTexture;
                var overlay = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
                overlay.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                overlay.Apply();
                uiPixels = overlay.GetPixels32();
                RenderTexture.active = null;
                Destroy(overlay);
                uiTexture.Release();
                Destroy(uiTexture);
                uiTexture = null;
            }

            if (pendingSettings != null)
                pendingSettings.targetTexture = previousPanelTarget;
            pendingSettings = null;

            int bright = WriteComposite(pendingPath, scenePixels, uiPixels);
            scenePixels = null;
            return bright;
        }

        int WriteComposite(string path, Color32[] scene, Color32[] ui)
        {
            if (scene == null)
                return 0;
            lastOverlayPixels = 0;
            if (ui != null)
            {
                int count = scene.Length < ui.Length ? scene.Length : ui.Length;
                for (int i = 0; i < count; i++)
                {
                    byte a = ui[i].a;
                    if (a == 0)
                        continue;
                    lastOverlayPixels++;
                    float alpha = a / 255f;
                    scene[i] = new Color32(
                        (byte)(ui[i].r + scene[i].r * (1f - alpha)),
                        (byte)(ui[i].g + scene[i].g * (1f - alpha)),
                        (byte)(ui[i].b + scene[i].b * (1f - alpha)),
                        255);
                }
            }

            var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            image.SetPixels32(scene);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            int bright = 0;
            for (int i = 0; i < scene.Length; i++)
            {
                if (scene[i].r + scene[i].g + scene[i].b > 90)
                    bright++;
            }
            Destroy(image);
            Debug.Log("Gitmos capture overlay=" + lastOverlayPixels + " path=" + path);
            return bright;
        }
    }
}
