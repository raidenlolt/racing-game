using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// takes a car out of the game: the menu roster, the AI opponent pool, the spawner fallback, and
/// finally the prefabs themselves.
///
/// the order matters. every reference has to be repointed BEFORE the prefabs are deleted, or the
/// scenes are left holding missing references that only show up as a car failing to spawn at
/// runtime. VECTOR was referenced in four places, only one of which was the catalogue:
///
///   Car Catalogue          the menu entry
///   Spawning.prefab        playerPrefab, the fallback used by tracks with no catalogue wired
///   Race_Track_02 and 03   both playerPrefab AND the AI opponent roster
///
/// Race_Track_01 already pointed elsewhere, which is why VECTOR never appeared as an opponent there.
///
/// the AI variant is a prefab variant of the base car, so the two are deleted together; removing the
/// base alone would leave the variant broken.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class RemoveCar
    {
        private const string CarFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string CataloguePath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/Car Catalogue.asset";
        private const string SpawningPrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Track Based/Spawning.prefab";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        /// <summary>the car being removed, by prefab name</summary>
        private const string DoomedCar = "Player Car 1";

        [MenuItem("Tools/Racing/Remove VECTOR")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var basePath = CarFolder + DoomedCar + ".prefab";
            var aiPath = CarFolder + DoomedCar + " (AI Variant).prefab";
            var doomedBase = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            var doomedAi = AssetDatabase.LoadAssetAtPath<GameObject>(aiPath);

            if (doomedBase == null) { Debug.LogWarning("[RemoveCar] " + basePath + " is already gone"); }

            var replacement = ChooseReplacement(doomedBase);
            if (replacement == null) { Debug.LogError("[RemoveCar] no other car to fall back to, aborting"); return; }
            Debug.Log("[RemoveCar] fallback player car will be " + replacement.name);

            RemoveFromCatalogue(doomedBase);
            FixSpawningPrefab(doomedBase, doomedAi, replacement);
            foreach (var scene in Scenes) FixScene(scene, doomedBase, doomedAi, replacement);

            // only now that nothing points at them
            DeleteAsset(basePath);
            DeleteAsset(aiPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RemoveCar] done");
        }

        /// <summary>the first remaining car in the catalogue, used wherever the removed one was referenced</summary>
        private static GameObject ChooseReplacement(GameObject doomed)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<CarCatalogue>(CataloguePath);
            if (catalogue == null) return null;
            foreach (var entry in catalogue.cars)
                if (entry != null && entry.playerPrefab != null && entry.playerPrefab != doomed)
                    return entry.playerPrefab;
            return null;
        }

        private static void RemoveFromCatalogue(GameObject doomed)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<CarCatalogue>(CataloguePath);
            if (catalogue == null) { Debug.LogError("[RemoveCar] catalogue not found"); return; }

            var removed = catalogue.cars.RemoveAll(c => c == null || c.playerPrefab == doomed);
            EditorUtility.SetDirty(catalogue);
            AssetDatabase.SaveAssets();
            Debug.Log("[RemoveCar] catalogue: removed " + removed + " entry(s), " + catalogue.cars.Count + " car(s) left: "
                      + string.Join(", ", catalogue.cars.Select(c => c.displayName)));
        }

        private static void FixSpawningPrefab(GameObject doomedBase, GameObject doomedAi, GameObject replacement)
        {
            var root = PrefabUtility.LoadPrefabContents(SpawningPrefabPath);
            try
            {
                var spawner = root.GetComponentInChildren<PlayersSpawner>(true);
                if (spawner == null) { Debug.LogWarning("[RemoveCar] no spawner in Spawning.prefab"); return; }

                var changed = Retarget(spawner, doomedBase, doomedAi, replacement, "Spawning.prefab");
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, SpawningPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void FixScene(string scenePath, GameObject doomedBase, GameObject doomedAi, GameObject replacement)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var spawner = Object.FindFirstObjectByType<PlayersSpawner>(FindObjectsInactive.Include);
            if (spawner == null) { Debug.LogWarning("[RemoveCar] no spawner in " + scene.name); return; }

            if (Retarget(spawner, doomedBase, doomedAi, replacement, scene.name))
            {
                // the spawner is a prefab instance, so the change has to be recorded as an override
                PrefabUtility.RecordPrefabInstancePropertyModifications(spawner);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        /// <summary>
        /// points a spawner away from the doomed car: its fallback becomes the replacement, and the
        /// doomed AI variant leaves the opponent pool
        /// </summary>
        private static bool Retarget(PlayersSpawner spawner, GameObject doomedBase, GameObject doomedAi,
                                     GameObject replacement, string where)
        {
            var changed = false;

            if (spawner.playerPrefab == doomedBase)
            {
                spawner.playerPrefab = replacement;
                changed = true;
                Debug.Log("[RemoveCar] " + where + ": playerPrefab -> " + replacement.name);
            }

            var removed = spawner.aiCarPrefabs.RemoveAll(p => p == null || p == doomedAi || p == doomedBase);
            if (removed > 0)
            {
                changed = true;
                Debug.Log("[RemoveCar] " + where + ": removed " + removed + " from the AI roster, "
                          + spawner.aiCarPrefabs.Count + " opponent car(s) left");
            }

            if (changed) EditorUtility.SetDirty(spawner);
            return changed;
        }

        private static void DeleteAsset(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) == null) return;
            if (AssetDatabase.DeleteAsset(path)) Debug.Log("[RemoveCar] deleted " + path);
            else Debug.LogError("[RemoveCar] could not delete " + path);
        }
    }
}
