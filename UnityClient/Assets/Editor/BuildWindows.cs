using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    public static class BuildWindows
    {
        [MenuItem("FPS AI Coach/Build Windows MVP")]
        public static void Perform()
        {
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x);

            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "Windows"));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "FPS-AI-Coach-Live.exe");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows build failed: {report.summary.result}");

            Debug.Log($"FPS AI Coach build created: {outputPath}");
        }
    }
}
