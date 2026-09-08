#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// headless play-mode check of the race flow. opens a track, drops a flag file that the runtime
/// SmokeTest picks up after the play-mode domain reload, and enters play mode; the runtime side
/// drives a race, writes a report and exits the editor with a pass/fail code.
///
///   Unity.exe -batchmode -nographics -projectPath . -logFile out.log
///             -executeMethod SpinMotion.EditorTools.SmokeTestLauncher.Run
///             -scene Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity
///
/// no -quit: the runtime side quits when it is done (or the launcher's own timeout does).
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class SmokeTestLauncher
    {
        public const string FlagPath = "Library/smoke_test.flag";
        public const string ReportPath = "Library/smoke_report.txt";

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-scene");
            var scene = index >= 0 && index + 1 < args.Length ? args[index + 1]
                                                                : "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity";

            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            File.WriteAllText(FlagPath, scene);

            Debug.Log("[Smoke] entering play mode on " + scene);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// once play mode is up, arm a wall-clock timeout so a hang cannot leave a headless editor
        /// running forever
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ArmTimeout()
        {
            if (!Application.isBatchMode || !File.Exists(FlagPath)) return;
            var deadline = EditorApplication.timeSinceStartup + 240.0;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup > deadline)
                {
                    Debug.LogError("[Smoke] timeout");
                    File.AppendAllText(ReportPath, "FAIL timeout\n");
                    EditorApplication.Exit(9);
                }
            };
        }
    }
}
#endif
