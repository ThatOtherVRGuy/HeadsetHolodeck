using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Holodeck.Editor
{
    public static class CommandLineAndroidBuild
    {
        const string DefaultOutputPath = "Builds/Holodeck.apk";

        public static void BuildApk()
        {
            string outputPath = GetArgument("-outputPath", DefaultOutputPath);
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = EditorUserBuildSettings.development
                    ? BuildOptions.Development
                    : BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CommandLineAndroidBuild] Result={summary.result}, Output={summary.outputPath}, Size={summary.totalSize} bytes, Time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {summary.result}");
        }

        static string GetArgument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }

            return fallback;
        }
    }
}
