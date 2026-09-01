using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// builds the car and track pickers into the existing menu.
///
/// the two new panels are cloned from the Laps container rather than authored from scratch, so they
/// inherit its background, fonts, button art and outlines for free and cannot drift out of style with
/// the rest of the menu. they go in a second row because the existing row is 1300 wide and already
/// holds three 340-wide panels, so a fourth and fifth would overflow it.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class CarMapSelectionSetup
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string SO = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";

        /// <summary>
        /// distinct driving characters for the cars, which shipped identical at 140/2500/20.
        /// the spread is centred on those baselines so the field stays roughly as fast overall, and
        /// each car trades something for what it gains rather than one being strictly best.
        ///
        /// the later three sit on the same trade line the first four established, roughly
        /// torque = 2950 - (topSpeed - 132) * 20 and steer = 25 - (topSpeed - 132) * 0.27, so a car
        /// that gains top end pays for it in launch and turn-in. APEX is the extreme of that line
        /// (fastest, worst off the line, worst through a corner) and RUMBLE the other end
        /// </summary>
        private class Profile
        {
            public string Prefab, Name;
            public float TopSpeed, Torque, Steer;
        }

        private static readonly Profile[] Profiles =
        {
            new Profile { Prefab = "Player Car 1", Name = "VECTOR",  TopSpeed = 140f, Torque = 2600f, Steer = 21f },
            new Profile { Prefab = "Player Car 2", Name = "METEOR",  TopSpeed = 162f, Torque = 2350f, Steer = 17f },
            new Profile { Prefab = "Player Car 3", Name = "KATANA",  TopSpeed = 132f, Torque = 2950f, Steer = 25f },
            new Profile { Prefab = "Player Car 4", Name = "TITAN",   TopSpeed = 150f, Torque = 2700f, Steer = 19f },
            new Profile { Prefab = "Player Car 5", Name = "APEX",    TopSpeed = 170f, Torque = 2200f, Steer = 15f },
            new Profile { Prefab = "Player Car 6", Name = "RUMBLE",  TopSpeed = 136f, Torque = 2880f, Steer = 24f },
            new Profile { Prefab = "Player Car 7", Name = "MAMMOTH", TopSpeed = 142f, Torque = 2900f, Steer = 18f },
        };

        private static readonly string[][] Maps =
        {
            new[] { "Race_Track_01", "COASTAL" },
            new[] { "Race_Track_02", "HIGHLAND" },
            new[] { "Race_Track_03", "CANYON" },
            new[] { "Race_Track_04", "SPEEDWAY" },
        };

        [MenuItem("Tools/Racing/Set Up Car And Map Selection")]
        public static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ApplyCarProfiles();
            var carCatalogue = BuildCarCatalogue();
            var mapCatalogue = BuildMapCatalogue();
            BuildMenuUi(carCatalogue, mapCatalogue);
            WireSpawners(carCatalogue);

            AssetDatabase.SaveAssets();
            Debug.Log("[Selection] done");
        }

        /// <summary>
        /// gives each car its own numbers. only the base prefabs are touched: the AI variants are
        /// prefab variants of them and inherit these values, so a bot in a given car drives like the
        /// player would in that car
        /// </summary>
        private static void ApplyCarProfiles()
        {
            foreach (var p in Profiles)
            {
                var path = Cars + p.Prefab + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Debug.LogWarning("[Selection] missing " + path); continue; }
                try
                {
                    var car = root.GetComponent<CarController>();
                    if (car == null) { Debug.LogWarning("[Selection] no CarController on " + p.Prefab); continue; }
                    car.m_Topspeed = p.TopSpeed;
                    car.m_FullTorqueOverAllWheels = p.Torque;
                    car.m_MaximumSteerAngle = p.Steer;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("[Selection] " + p.Name + " (" + p.Prefab + "): " + p.TopSpeed + "mph, torque " +
                              p.Torque + ", steer " + p.Steer);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        private static CarCatalogue BuildCarCatalogue()
        {
            var path = SO + "Car Catalogue.asset";
            var cat = AssetDatabase.LoadAssetAtPath<CarCatalogue>(path);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<CarCatalogue>();
                AssetDatabase.CreateAsset(cat, path);
            }
            cat.cars = new List<CarEntry>();
            foreach (var p in Profiles)
            {
                cat.cars.Add(new CarEntry
                {
                    displayName = p.Name,
                    playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Cars + p.Prefab + ".prefab"),
                    aiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Cars + p.Prefab + " (AI Variant).prefab"),
                });
            }
            EditorUtility.SetDirty(cat);
            Debug.Log("[Selection] car catalogue: " + cat.cars.Count + " cars");
            return cat;
        }

        private static MapCatalogue BuildMapCatalogue()
        {
            var path = SO + "Map Catalogue.asset";
            var cat = AssetDatabase.LoadAssetAtPath<MapCatalogue>(path);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<MapCatalogue>();
                AssetDatabase.CreateAsset(cat, path);
            }
            cat.maps = new List<MapEntry>();
            foreach (var m in Maps)
            {
                // only offer tracks that are actually in Build Settings, since LoadScene needs them
                var inBuild = EditorBuildSettings.scenes.Any(s => s.enabled && s.path.EndsWith("/" + m[0] + ".unity"));
                if (!inBuild)
                {
                    Debug.LogWarning("[Selection] " + m[0] + " is not an enabled build scene, leaving it out of the map list");
                    continue;
                }
                cat.maps.Add(new MapEntry { displayName = m[1], sceneName = m[0] });
            }
            EditorUtility.SetDirty(cat);
            Debug.Log("[Selection] map catalogue: " + cat.maps.Count + " maps");
            return cat;
        }

        private static void BuildMenuUi(CarCatalogue carCatalogue, MapCatalogue mapCatalogue)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            if (root == null) { Debug.LogError("[Selection] missing " + GuiPrefab); return; }

            try
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var mainPanels = all.FirstOrDefault(t => t.name == "Main Panels");
                var lapsContainer = all.FirstOrDefault(t => t.name == "Laps Container");
                var scripts = all.FirstOrDefault(t => t.name == "GUI Scripts");
                var menuGui = root.GetComponentInChildren<MenuGUI>(true);
                if (mainPanels == null || lapsContainer == null || scripts == null || menuGui == null)
                {
                    Debug.LogError("[Selection] menu structure not as expected, aborting UI build");
                    return;
                }

                if (all.Any(t => t.name == "Selection Panels"))
                {
                    Debug.Log("[Selection] selection row already present, leaving the UI alone");
                    return;
                }

                // a second row, placed just above the existing one inside MenuUI's vertical layout
                var row = new GameObject("Selection Panels", typeof(RectTransform));
                row.transform.SetParent(mainPanels.parent, false);
                row.transform.SetSiblingIndex(mainPanels.GetSiblingIndex());
                ((RectTransform)row.transform).sizeDelta = new Vector2(1300f, 250f);
                var layout = row.AddComponent<HorizontalLayoutGroup>();
                var source = mainPanels.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = source.spacing;
                layout.childAlignment = source.childAlignment;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                var carContainer = CloneContainer(lapsContainer, row.transform, "Car Container", "CAR", "Car");
                var preview = BuildPreviewPanel(row.transform);
                var mapContainer = CloneContainer(lapsContainer, row.transform, "Map Container", "TRACK", "Map");

                var carSelector = new GameObject("Car Selector GUI").AddComponent<CarSelectorGUI>();
                carSelector.transform.SetParent(scripts, false);
                carSelector.carCatalogue = carCatalogue;
                Bind(carSelector, carContainer, "Car");

                var carPreview = preview.gameObject.AddComponent<CarPreview>();
                carPreview.carCatalogue = carCatalogue;
                carPreview.targetImage = preview;
                carSelector.preview = carPreview;

                var mapSelector = new GameObject("Map Selector GUI").AddComponent<MapSelectorGUI>();
                mapSelector.transform.SetParent(scripts, false);
                mapSelector.mapCatalogue = mapCatalogue;
                Bind(mapSelector, mapContainer, "Map");

                menuGui.mapCatalogue = mapCatalogue;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[Selection] built the car and track pickers into the menu");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// duplicates the laps panel and renames its parts. cloning keeps the background, font,
        /// button art and outline identical to the panels beside it without copying any styling code
        /// </summary>
        private static Transform CloneContainer(Transform source, Transform parent, string name,
                                                string title, string prefix)
        {
            var clone = Object.Instantiate(source.gameObject, parent).transform;
            clone.name = name;

            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("Select Laps")) { t.name = "Select " + prefix + " TMP"; SetText(t, title); }
                else if (t.name.Contains("Laps Qty")) { t.name = prefix + " Value TMP"; SetText(t, "-"); }
                else if (t.name.Contains("Laps Down")) t.name = prefix + " Down BTN";
                else if (t.name.Contains("Laps Up")) t.name = prefix + " Up BTN";
            }
            return clone;
        }

        private static void SetText(Transform t, string value)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = value;
        }

        private static void Bind(SelectorGUI selector, Transform container, string prefix)
        {
            var all = container.GetComponentsInChildren<Transform>(true);
            var down = all.FirstOrDefault(t => t.name == prefix + " Down BTN");
            var up = all.FirstOrDefault(t => t.name == prefix + " Up BTN");
            var value = all.FirstOrDefault(t => t.name == prefix + " Value TMP");

            if (down != null) selector.buttonDown = down.GetComponent<Button>();
            if (up != null) selector.buttonUp = up.GetComponent<Button>();
            if (value != null) selector.quantityTMP = value.GetComponent<TMP_Text>();
            selector.defaultQuantity = 0;
        }

        private static RawImage BuildPreviewPanel(Transform parent)
        {
            var go = new GameObject("Car Preview", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(340f, 230f);
            var img = go.AddComponent<RawImage>();
            img.color = Color.white;
            return img;
        }

        /// <summary>
        /// points every track's spawner at the roster, so the car chosen in the menu is the car that
        /// actually spawns rather than whatever that scene happened to have wired
        /// </summary>
        private static void WireSpawners(CarCatalogue carCatalogue)
        {
            foreach (var m in Maps)
            {
                var path = "Assets/Racing_Track_Pack/Scenes/" + m[0] + ".unity";
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var spawner = Object.FindFirstObjectByType<PlayersSpawner>();
                if (spawner == null) { Debug.LogWarning("[Selection] no spawner in " + m[0]); continue; }
                spawner.carCatalogue = carCatalogue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(spawner);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Selection] wired the roster into " + m[0]);
            }
        }
    }
}
