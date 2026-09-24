using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// puts AIFinishBehaviour on every car prefab, so a bot that has completed its laps brakes, stops
/// colliding with the other cars and is hidden until the restart. the component switches itself
/// off on the player's car.
///
/// the AI cars are prefab variants of the player cars, so the base prefabs are done first and a
/// variant that already inherits the component is left alone; a variant that was given its own
/// copy before its base had one (an earlier pass did exactly that, and each bot ended up with two)
/// has the extra removed. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class AiFinishSetup
    {
        private const string CarsFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars";

        [MenuItem("Tools/Racing/Set Up AI Finish")]
        public static void Run()
        {
            var added = 0; var removed = 0; var seen = 0;
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { CarsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0)
                .ThenBy(p => p)
                .ToList();
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var controller = root.GetComponentInChildren<CarController>(true);
                    if (controller == null) continue;
                    seen++;
                    var dirty = false;

                    var copies = controller.GetComponents<AIFinishBehaviour>();
                    if (copies.Length > 1)
                    {
                        // keep the inherited one (or the first), drop the rest
                        foreach (var extra in copies.Skip(1))
                        {
                            Object.DestroyImmediate(extra);
                            removed++;
                        }
                        dirty = true;
                    }
                    else if (copies.Length == 0)
                    {
                        controller.gameObject.AddComponent<AIFinishBehaviour>();
                        added++;
                        dirty = true;
                    }

                    if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[AIFinish] " + seen + " car prefabs: " + added + " given AIFinishBehaviour, " + removed + " duplicate(s) removed");
        }
    }
}
