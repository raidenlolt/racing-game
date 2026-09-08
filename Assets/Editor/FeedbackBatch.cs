using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// runs any number of the Tools › Racing setup passes from one headless launch, so a change that
/// touches prefabs and scenes needs a single editor start instead of one per pass:
///
///   Unity.exe -batchmode -quit -nographics -projectPath . -logFile out.log
///             -executeMethod SpinMotion.EditorTools.FeedbackBatch.Run
///             -steps SpawnGridRepair,CarSpeedTune
///
/// each name is a static class in this namespace with a public static Run() method. steps run in
/// the order given and the batch stops at the first one that throws, so a broken prefab edit
/// cannot be followed by a scene save built on top of it.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class FeedbackBatch
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-steps");
            if (index < 0 || index + 1 >= args.Length)
            {
                Debug.LogError("[Batch] no -steps argument");
                EditorApplication.Exit(2);
                return;
            }

            var steps = args[index + 1].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            foreach (var step in steps)
            {
                var type = typeof(FeedbackBatch).Assembly.GetType("SpinMotion.EditorTools." + step);
                var method = type?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    Debug.LogError("[Batch] no SpinMotion.EditorTools." + step + ".Run()");
                    EditorApplication.Exit(3);
                    return;
                }

                Debug.Log("[Batch] === " + step + " ===");
                try
                {
                    method.Invoke(null, null);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Batch] " + step + " failed: " + (e.InnerException ?? e));
                    EditorApplication.Exit(4);
                    return;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Batch] all " + steps.Count + " step(s) finished");
        }
    }
}
