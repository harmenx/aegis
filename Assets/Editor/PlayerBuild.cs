using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Aegis.Editor
{
    // Command-line and menu entry point for player builds:
    //   Unity -batchmode -quit -projectPath . -buildTarget StandaloneOSX -executeMethod Aegis.Editor.PlayerBuild.Build
    // Output goes to Builds/<target>/. The process exits non-zero when the build fails.
    public static class PlayerBuild
    {
        const string ScenePath = "Assets/Scenes/Lobby.unity";

        [MenuItem("Aegis/Build Player")]
        public static void Build()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string folder = Path.Combine("Builds", target.ToString());
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = target,
                locationPathName = Path.Combine(folder, ExecutableName(target)),
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Log.Info("Built " + target + " to " + summary.outputPath + " (" + (summary.totalSize >> 20) + " MB).");
                return;
            }

            Log.Error("Build for " + target + " " + summary.result + " with " + summary.totalErrors + " error(s).");
            if (Environment.GetCommandLineArgs() is string[] args && Array.IndexOf(args, "-batchmode") >= 0)
                EditorApplication.Exit(1);
        }

        static string ExecutableName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX: return "Aegis.app";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "Aegis.exe";
                case BuildTarget.WebGL: return "Aegis";
                default: return "Aegis";
            }
        }
    }
}
