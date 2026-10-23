using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Gitmos.Editor
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Lobby.unity";

        static ProjectSetup()
        {
            string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "autoplay.flag"));
            if (!File.Exists(flag))
                return;
            File.Delete(flag);
            EditorApplication.update += EnterPlayWhenReady;
        }

        [MenuItem("Gitmos/Build Lobby Scene")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/Settings");
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();

            var pipeline = EnsurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EnsureGlobalSettings();

            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/IBMPlexMono-Regular.ttf");
            if (font == null)
                throw new InvalidOperationException("IBM Plex Mono failed to import.");

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/GitmosTheme.tss");
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Dashboard.uxml");
            if (tree == null)
                throw new InvalidOperationException("Dashboard UXML failed to import.");

            Shader fresnel = RequireShader("Gitmos/Fresnel");
            Shader additive = RequireShader("Gitmos/Additive");
            Shader screen = RequireShader("Gitmos/Screen");
            Shader stability = RequireShader("Gitmos/Stability");

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.themeStyleSheet = theme;
            ReplaceAsset("Assets/Settings/GitmosPanel.asset", panel);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("Systems");
            systems.AddComponent<EventSimulator>();
            systems.AddComponent<GitHubEventsClient>();
            systems.AddComponent<PointsFeed>();
            var metronome = systems.AddComponent<Metronome>();
            systems.AddComponent<PlaybackVerifier>();

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.008f, 0.01f, 0.016f, 1f);
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 80f;
            camera.allowHDR = true;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            var rig = cameraObject.AddComponent<CameraRig>();

            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreateVolumeProfile();

            var stageRoot = new GameObject("Stages");
            var globe = CreateStage<ConnectionGlobe>("Connection", stageRoot.transform);
            globe.fresnelShader = fresnel;
            globe.additiveShader = additive;
            var barcode = CreateStage<ActivityBarcode>("Barcode", stageRoot.transform);
            barcode.screenShader = screen;
            var scan = CreateStage<ScanBurst>("Scan", stageRoot.transform);
            scan.screenShader = screen;
            scan.additiveShader = additive;
            var mesh = CreateStage<StabilityMesh>("Stability", stageRoot.transform);
            mesh.stabilityShader = stability;
            mesh.additiveShader = additive;

            metronome.stages = new StageBase[] { globe, barcode, scan, mesh };
            metronome.holdSeconds = 18f;
            metronome.fadeSeconds = 1.5f;

            var uiObject = new GameObject("Dashboard");
            var document = uiObject.AddComponent<UIDocument>();
            document.visualTreeAsset = tree;
            var panelProperty = new SerializedObject(document).FindProperty("m_PanelSettings");
            panelProperty.objectReferenceValue = panel;
            panelProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            document.sortingOrder = 10;
            var dashboard = uiObject.AddComponent<DashboardController>();
            dashboard.monoFont = font;
            dashboard.panelSettings = panel;

            metronome.dashboard = dashboard;
            rig.dashboard = dashboard;
            rig.target = stageRoot.transform;
            rig.distance = 11.5f;
            rig.maxDistance = 14f;

            PlayerSettings.productName = "Aegis";
            PlayerSettings.companyName = "Aegis";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Gitmos lobby scene built.");
        }

        [MenuItem("Gitmos/Play")]
        public static void Play()
        {
            EditorApplication.update -= EnterPlayWhenReady;
            EditorApplication.update += EnterPlayWhenReady;
        }

        static void EnterPlayWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= EnterPlayWhenReady;
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);
            FocusGameView();
            Debug.Log("Gitmos entering play.");
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        static void FocusGameView()
        {
            var gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameView == null)
                return;
            var window = EditorWindow.GetWindow(gameView, false, "Game", true);
            window.Show();
            window.Focus();
        }

        public static void BuildAndVerify()
        {
            Build();
            EditorPrefs.SetBool("Gitmos.Verify", true);
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            pumpStarted = EditorApplication.timeSinceStartup;
            enterQueued = false;
        }

        static bool enterQueued;
        static double pumpStarted;
        static bool playHooked;

        static void Pump()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (EditorApplication.timeSinceStartup - pumpStarted > 150)
            {
                Debug.LogError("Gitmos verification timed out.");
                EditorApplication.update -= Pump;
                EditorApplication.Exit(1);
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (!enterQueued)
                {
                    enterQueued = true;
                    EditorSceneManager.OpenScene(ScenePath);
                    if (!playHooked)
                    {
                        EditorApplication.playModeStateChanged += OnPlayMode;
                        playHooked = true;
                    }
                    EditorApplication.EnterPlaymode();
                }
                return;
            }

            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= OnPlayMode;
            playHooked = false;
            EditorApplication.update -= Pump;

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "playback-report.txt"));
            if (!File.Exists(path))
            {
                Debug.LogError("Gitmos verification did not write a report.");
                EditorApplication.Exit(1);
                return;
            }

            string report = File.ReadAllText(path);
            Debug.Log(report);
            EditorApplication.Exit(report.Contains("RESULT: PASS") ? 0 : 1);
        }

        static T CreateStage<T>(string name, Transform parent) where T : Component
        {
            var stage = new GameObject(name);
            stage.transform.SetParent(parent, false);
            return stage.AddComponent<T>();
        }

        static Shader RequireShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Missing shader " + shaderName);
            return shader;
        }

        static UniversalRenderPipelineAsset EnsurePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/GitmosURP.asset");
            if (existing != null)
                return existing;

            var post = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (post == null)
                throw new InvalidOperationException("URP post-process data was not imported.");

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "GitmosRenderer";
            renderer.postProcessData = post;
            ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            ReplaceAsset("Assets/Settings/GitmosRenderer.asset", renderer);

            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.name = "GitmosURP";
            ResourceReloader.ReloadAllNullIn(pipeline, UniversalRenderPipelineAsset.packagePath);
            ReplaceAsset("Assets/Settings/GitmosURP.asset", pipeline);
            return pipeline;
        }

        static VolumeProfile CreateVolumeProfile()
        {
            ReplaceAsset("Assets/Settings/GitmosVolume.asset", null);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/GitmosVolume.asset");

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.28f);
            bloom.intensity.Override(2.35f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.82f, 0.9f, 1f));
            bloom.highQualityFiltering.Override(true);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.42f);
            vignette.color.Override(Color.black);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void ReplaceAsset(string path, UnityEngine.Object asset)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
            if (asset != null)
                AssetDatabase.CreateAsset(asset, path);
        }

        static void EnsureGlobalSettings()
        {
            try
            {
                Type type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                    "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
                MethodInfo ensure = type?.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (ensure == null)
                    return;
                if (ensure.GetParameters().Length == 1)
                    ensure.Invoke(null, new object[] { true });
                else
                    ensure.Invoke(null, null);
            }
            catch (Exception error)
            {
                Debug.LogWarning("URP global settings were not created explicitly. " + error.Message);
            }
        }
    }
}
