using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gitmos
{
    public class GifRecorder : MonoBehaviour
    {
        const int Width = 960;
        const int Height = 540;
        const int FrameCount = 36;
        const float Interval = 1f / 12f;

        float elapsed;
        float next;
        int written;
        bool recording;
        bool awaitingOverlay;
        string directory;
        Color32[] scenePixels;
        RenderTexture uiTexture;
        PanelSettings pendingSettings;
        RenderTexture previousPanelTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "gif-record.flag"));
            if (!File.Exists(flag))
                return;
            File.Delete(flag);
            var host = new GameObject("GifRecorder");
            host.AddComponent<GifRecorder>();
        }

        void Start()
        {
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "gif-frames"));
            Directory.CreateDirectory(directory);
            foreach (string existing in Directory.GetFiles(directory, "*.png"))
                File.Delete(existing);
            recording = true;
            next = 1.8f;
        }

        void Update()
        {
            if (!recording)
                return;

            if (awaitingOverlay)
            {
                CompleteOverlay();
                awaitingOverlay = false;
                if (written >= FrameCount)
                    Finish();
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed < next || awaitingOverlay)
                return;

            next += Interval;
            var dashboard = FindAnyObjectByType<DashboardController>();
            BeginCapture(dashboard);
        }

        void BeginCapture(DashboardController dashboard)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            var scene = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = scene;
            cam.Render();
            cam.targetTexture = previous;

            RenderTexture.active = scene;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            scenePixels = image.GetPixels32();
            RenderTexture.active = null;
            Destroy(image);
            scene.Release();
            Destroy(scene);

            UIDocument document = dashboard != null ? dashboard.GetComponent<UIDocument>() : null;
            if (document == null || document.panelSettings == null)
            {
                WriteFrame(null);
                if (written >= FrameCount)
                    Finish();
                return;
            }

            if (uiTexture != null)
            {
                uiTexture.Release();
                Destroy(uiTexture);
            }
            uiTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            uiTexture.Create();
            pendingSettings = document.panelSettings;
            previousPanelTarget = pendingSettings.targetTexture;
            pendingSettings.targetTexture = uiTexture;
            document.rootVisualElement?.MarkDirtyRepaint();
            awaitingOverlay = true;
        }

        void CompleteOverlay()
        {
            Color32[] uiPixels = null;
            if (uiTexture != null)
            {
                RenderTexture.active = uiTexture;
                var overlay = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                overlay.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
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
            WriteFrame(uiPixels);
        }

        void WriteFrame(Color32[] ui)
        {
            if (scenePixels == null)
                return;
            if (ui != null)
            {
                int count = scenePixels.Length < ui.Length ? scenePixels.Length : ui.Length;
                for (int i = 0; i < count; i++)
                {
                    byte a = ui[i].a;
                    if (a == 0)
                        continue;
                    float alpha = a / 255f;
                    scenePixels[i] = new Color32(
                        (byte)(ui[i].r + scenePixels[i].r * (1f - alpha)),
                        (byte)(ui[i].g + scenePixels[i].g * (1f - alpha)),
                        (byte)(ui[i].b + scenePixels[i].b * (1f - alpha)),
                        255);
                }
            }

            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.SetPixels32(scenePixels);
            image.Apply();
            string path = Path.Combine(directory, "f" + written.ToString("000") + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Destroy(image);
            scenePixels = null;
            written++;
        }

        void Finish()
        {
            recording = false;
            string done = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "gif-record.done"));
            File.WriteAllText(done, written.ToString());
            Debug.Log("Aegis gif wrote " + written + " frames");
#if UNITY_EDITOR
            // When the playback verifier is also running, let it finish the session.
            if (!UnityEditor.EditorPrefs.GetBool("Gitmos.Verify", false))
                UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
