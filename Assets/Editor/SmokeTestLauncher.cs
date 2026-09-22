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

        /// <summary>the same test on whatever scene is open, watched in the editor; leaves play mode when done</summary>
        [MenuItem("Tools/Racing/Run Smoke Test In Editor")]
        public static void RunHere()
        {
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            File.WriteAllText(FlagPath, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>lap-count check on the open track: a 1 lap race must end after 1 lap</summary>
        [MenuItem("Tools/Racing/Run Lap Count Test In Editor")]
        public static void RunLapTestHere()
        {
            if (File.Exists(LapCountTest.ReportPath)) File.Delete(LapCountTest.ReportPath);
            File.WriteAllText(LapCountTest.FlagPath, "1");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// batch entry for the lap count test:
        ///   -executeMethod SpinMotion.EditorTools.SmokeTestLauncher.RunLapTest -scene <path> [-laps N]
        /// </summary>
        public static void RunLapTest()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-scene");
            var scene = index >= 0 && index + 1 < args.Length ? args[index + 1]
                                                                : "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity";
            var lapsIndex = Array.IndexOf(args, "-laps");
            var laps = lapsIndex >= 0 && lapsIndex + 1 < args.Length ? args[lapsIndex + 1] : "1";

            if (File.Exists(LapCountTest.ReportPath)) File.Delete(LapCountTest.ReportPath);
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            File.WriteAllText(LapCountTest.FlagPath, laps);
            Debug.Log("[LapTest] entering play mode on " + scene + " with " + laps + " lap(s)");
            EditorApplication.EnterPlaymode();
        }

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
            if (!Application.isBatchMode || !(File.Exists(FlagPath) || File.Exists(LapCountTest.FlagPath))) return;
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
