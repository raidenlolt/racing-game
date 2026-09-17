using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// the WebGL build the THRYL platform hosts. one entry point for the menu and for batch mode:
///
///   Unity.exe -batchmode -quit -nographics -buildTarget WebGL -projectPath . -logFile out.log
///             -executeMethod SpinMotion.EditorTools.WebGLBuild.Run [-out Builds/WebGL]
///
/// settings it pins before building (everything else comes from Player Settings):
///   gzip + decompression fallback   loads on any static host, no server header configuration
///                                   needed; brotli without fallback only loads when the host
///                                   sends the right Content-Encoding over https, which is not
///                                   something we control on the platform's side
///   scenes                          the enabled scenes in Build Settings (menu + three tracks)
/// writes Library/webgl_build_report.txt with the result so a headless run can be checked.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class WebGLBuild
    {
        public const string DefaultOutput = "Builds/WebGL";
        public const string ReportPath = "Library/webgl_build_report.txt";

        [MenuItem("Tools/Racing/Build WebGL")]
        public static void BuildFromMenu()
        {
            var report = Build(DefaultOutput);
            if (report != null && report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.Combine(DefaultOutput, "index.html"));
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-out");
            var output = index >= 0 && index + 1 < args.Length ? args[index + 1] : DefaultOutput;
            var report = Build(output);
            if (Application.isBatchMode)
                EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>the settings a WebGL build of this game always uses; safe to call before switching platform</summary>
        public static void ApplySettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        }

        public static BuildReport Build(string output)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[WebGL] no enabled scenes in Build Settings");
                WriteReport("FAIL no scenes");
                return null;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[WebGL] switching active build target to WebGL");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[WebGL] could not switch to the WebGL build target (module installed?)");
                    WriteReport("FAIL platform switch");
                    return null;
                }
            }

            ApplySettings();
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            Debug.Log("[WebGL] building " + scenes.Length + " scenes to " + output);
            var started = DateTime.Now;
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            var line = summary.result + " " + output + " " + (summary.totalSize / (1024 * 1024)) + " MB, "
                       + summary.totalErrors + " errors, " + summary.totalWarnings + " warnings, "
                       + (DateTime.Now - started).TotalMinutes.ToString("F1") + " min";
            Debug.Log("[WebGL] " + line);
            WriteReport((summary.result == BuildResult.Succeeded ? "PASS " : "FAIL ") + line);
            return report;
        }

        private static void WriteReport(string text)
        {
            try { File.WriteAllText(ReportPath, text + Environment.NewLine); } catch { }
        }
    }
}
