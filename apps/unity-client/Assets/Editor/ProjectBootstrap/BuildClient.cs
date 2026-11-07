using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Orbis.Editor
{
    public static class BuildClient
    {
        const string WorldScene = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Orbis/Build/Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            PrepareEmbeddedWorldFallback();
            RequiredAssetBuilder.Ensure();
            ConfigureDesktopPlayer(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Build(
                BuildTarget.StandaloneWindows64,
                ResolveOutput("Build/Windows/OrbisTerrae.exe"),
                BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        [MenuItem("Orbis/Build/macOS Development")]
        public static void BuildMacDevelopment()
        {
            PrepareEmbeddedWorldFallback();
            RequiredAssetBuilder.Ensure();
            ConfigureDesktopPlayer(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            Build(
                BuildTarget.StandaloneOSX,
                ResolveOutput("Build/macOS/OrbisTerrae.app"),
                BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        static void ConfigureDesktopPlayer(BuildTargetGroup group, BuildTarget target)
        {
            PlayerSettings.companyName = "Orbis Terrae";
            PlayerSettings.productName = "Orbis Terrae";
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            Debug.Log($"[Orbis] Configured {group}/{target} development player.");
        }

        static void PrepareEmbeddedWorldFallback()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root was not found.");
            var repositoryRoot = Directory.GetParent(Directory.GetParent(projectRoot)?.FullName ?? string.Empty)?.FullName
                ?? throw new InvalidOperationException("Repository root was not found.");
            var source = Path.Combine(repositoryRoot, "apps", "client", "src", "assets", "world-relief-v2.png");
            if (!File.Exists(source)) throw new FileNotFoundException("Global relief fallback was not found.", source);
            var destinationDirectory = Path.Combine(Application.streamingAssetsPath, "world-v2");
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(source, Path.Combine(destinationDirectory, "global-relief.png"), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void Build(BuildTarget target, string outputPath, BuildOptions options)
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { WorldScene },
                locationPathName = outputPath,
                target = target,
                options = options,
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Orbis build failed with {summary.totalErrors} errors and {summary.totalWarnings} warnings.");
            }

            Debug.Log($"[Orbis] Build succeeded: {summary.outputPath}, {summary.totalSize} bytes, {summary.totalTime}.");
        }

        static string ResolveOutput(string fallback)
        {
            const string prefix = "--build-output=";
            var overridePath = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var value = string.IsNullOrWhiteSpace(overridePath) ? fallback : overridePath.Substring(prefix.Length);
            return Path.GetFullPath(value);
        }
    }
}
