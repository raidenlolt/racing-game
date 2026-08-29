using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// stages the menu as a showroom on the track: drops the camera to a low hero angle beside the start
/// straight, builds the car carousel in front of it, and moves the menu panels down out of the way.
///
/// the camera is aimed ACROSS the direction the cars travel, so a car moving down the straight reads
/// as moving horizontally across the screen. that is what lets one car leave to the left while the
/// next arrives from the right.
///
/// the old render-texture preview panel goes with it. the car is a real car on real tarmac now, so a
/// thumbnail of one sitting above it would only compete.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class MenuShowcaseSetup
    {
        private const string GuiPrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string ShowcaseRootName = "Menu Car Showcase";
        private const string AnchorName = "Showcase Stage";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        /// <summary>how far down the straight from the front grid slot the stage sits</summary>
        private const float StageAlongTrack = 34f;
        /// <summary>camera stand-off from the car, across the direction of travel</summary>
        private const float CameraBack = 13f;
        private const float CameraHeight = 3.2f;
        /// <summary>
        /// slides the camera along the travel axis, which swings the car from side-on to three
        /// quarter. negative puts the camera on the side the car is facing, so we see its nose.
        /// at -7.5 against a 15.5 stand-off the car was 26 degrees off pure profile, which read as a
        /// flat side elevation; -11 against 16.5 gives 34 degrees and a proper hero angle.
        /// </summary>
        private const float CameraAlong = -9.5f;
        /// <summary>
        /// aimed slightly below the car. the bottom of the screen still carries the car's name and
        /// the PLAY button, so the aim point is dropped to lift the car clear of them -- but not as
        /// far as before, now that the selector panels have moved out to the edges and the middle of
        /// the screen is free.
        /// </summary>
        private const float CameraLookHeight = -0.55f;
        private const float CameraFov = 38f;

        [MenuItem("Tools/Racing/Build Menu Showroom")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            StripPreviewPanel();
            AnchorMenuToBottom();

            foreach (var path in Scenes) BuildInScene(path);

            AssetDatabase.SaveAssets();
            Debug.Log("[Showroom] done");
        }

        private static void BuildInScene(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var spawner = Object.FindFirstObjectByType<PlayersSpawner>(FindObjectsInactive.Include);
            if (spawner == null || spawner.spawnPoints.Count == 0)
            {
                Debug.LogWarning("[Showroom] " + scene.name + ": no spawn points, skipped");
                return;
            }

            // the front of the grid gives a point on the track and the direction it runs
            var grid = spawner.spawnPoints[0];
            var along = grid.forward;
            var stagePosition = grid.position + along * StageAlongTrack;

            // cars travel back UP the straight, so they face the camera's left as they arrive
            var travel = -along;

            var existing = GameObject.Find(ShowcaseRootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(ShowcaseRootName);
            var anchor = new GameObject(AnchorName);
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = stagePosition;
            anchor.transform.rotation = Quaternion.LookRotation(travel, Vector3.up);

            var showcase = root.AddComponent<MenuCarShowcase>();
            showcase.stageAnchor = anchor.transform;
            showcase.carCatalogue = FindAsset<CarCatalogue>();
            showcase.gameEvents = FindAsset<GameEvents>();

            PlaceCamera(stagePosition, along);
            WireSelector(showcase);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Showroom] " + scene.name + ": stage at " + stagePosition.ToString("F0")
                      + ", cars travel " + travel.ToString("F2"));
        }

        /// <summary>
        /// drops the start menu camera from its 46 degree overhead view to a low three quarter angle
        /// beside the track, looking across the direction the cars drive
        /// </summary>
        private static void PlaceCamera(Vector3 stage, Vector3 along)
        {
            var camera = Object.FindObjectsByType<StartMenuCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (camera.Length == 0) { Debug.LogWarning("[Showroom] no StartMenuCamera found"); return; }

            var t = camera[0].transform;
            // across the track, from the side the cars present their flank to
            var across = Vector3.Cross(Vector3.up, along).normalized;

            t.position = stage + across * CameraBack + Vector3.up * CameraHeight + along * CameraAlong;
            t.rotation = Quaternion.LookRotation((stage + Vector3.up * CameraLookHeight - t.position).normalized, Vector3.up);

            var cam = camera[0].GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = CameraFov;

            Debug.Log("[Showroom] menu camera -> " + t.position.ToString("F1") + " looking at the stage, fov " + CameraFov);
        }

        private static void WireSelector(MenuCarShowcase showcase)
        {
            var selector = Object.FindFirstObjectByType<CarSelectorGUI>(FindObjectsInactive.Include);
            if (selector == null) { Debug.LogWarning("[Showroom] no CarSelectorGUI in scene"); return; }

            selector.showcase = showcase;
            EditorUtility.SetDirty(selector);
            PrefabUtility.RecordPrefabInstancePropertyModifications(selector);
        }

        /// <summary>the render-texture preview is redundant now the real car is on stage</summary>
        private static void StripPreviewPanel()
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefabPath);
            try
            {
                var holder = Find(root.transform, "Selection Panels");
                if (holder != null)
                {
                    Object.DestroyImmediate(holder.gameObject);
                    Debug.Log("[Showroom] removed the render-texture preview panel");
                }
                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// pushes the menu controls to the bottom of the screen and lightens the scrim.
        ///
        /// both exist because the background stopped being wallpaper: the car is the subject now, so
        /// the panels have to sit under it rather than through the middle of it, and a heavy scrim
        /// would only mute the thing the screen is meant to show off.
        /// </summary>
        private static void AnchorMenuToBottom()
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefabPath);
            try
            {
                var menu = Find(root.transform, "MenuUI");
                if (menu == null) { Debug.LogWarning("[Showroom] MenuUI not found"); return; }

                var layout = menu.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.childAlignment = TextAnchor.LowerCenter;
                    layout.padding = new RectOffset(0, 0, 0, 48);
                    layout.spacing = 18f;
                }

                var scrim = menu.GetComponent<Image>();
                if (scrim != null) scrim.color = new Color(0f, 0f, 0f, 0.22f);

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefabPath);
                Debug.Log("[Showroom] menu controls anchored to the bottom, scrim lightened");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static T FindAsset<T>() where T : ScriptableObject
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
