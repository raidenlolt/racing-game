using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// spreads the menu out to the edges of the screen: the car steppers become big arrows pinned to the
/// left and right, the car's name sits under it at the bottom, and everything else gets out of the
/// middle so the car has the screen to itself.
///
/// the pieces are re-parented rather than rebuilt, because CarSelectorGUI already holds references to
/// those exact buttons and that exact label. moving a transform keeps the references intact; making
/// new objects would have meant re-wiring them and losing the styling already on them.
///
/// they move into a "Free Layout" child which is marked ignoreLayout. MenuUI is a vertical layout
/// group, so anything parented directly under it gets stacked and its anchors overwritten; the flag
/// is what buys the freedom to pin things to screen edges while staying inside MenuUI, which matters
/// because MenuUI is what gets switched off when the race starts.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class MenuLayoutSides
    {
        private const string PrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string FreeLayoutName = "Free Layout";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        private static readonly Color Accent = new Color(0.16f, 0.85f, 1f, 1f);

        [MenuItem("Tools/Racing/Spread Menu To Edges")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var menu = Find(root.transform, "MenuUI");
                if (menu == null) { Debug.LogError("[Layout] MenuUI not found"); return; }

                var free = EnsureFreeLayout(menu);

                PinSideButton(root, free, "Car Down BTN", true);
                PinSideButton(root, free, "Car Up BTN", false);
                PlaceCarName(root, free);
                PlaceCorner(root, free, "Laps Container", 40f);
                PlaceCorner(root, free, "Map Container", 40f + 230f + 20f);

                // the row that held them is empty now, and an empty row still reserves its height,
                // which would push PLAY up the screen for no reason
                Remove(root, "Main Panels");
                Remove(root, "Car Container");

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Layout] menu spread to the edges");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            ClearStaleSceneOverrides();
        }

        /// <summary>
        /// drops the scene copies' own idea of where these pieces sit.
        ///
        /// every track holds an INSTANCE of the GUI prefab, and earlier tools recorded property
        /// modifications on it. an instance override beats the prefab, so Laps Container carried
        /// m_AnchoredPosition 0,0 from a previous layout and ignored the 40,40 written above -- the
        /// panel sat jammed into the very corner of the screen no matter what the prefab said.
        /// reverting the whole RectTransform hands those objects back to the prefab's layout.
        /// </summary>
        private static void ClearStaleSceneOverrides()
        {
            var moved = new[] { "Laps Container", "Map Container", "Car Value TMP", "Car Down BTN", "Car Up BTN", "Free Layout" };

            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var gui = GameObject.Find("GUI");
                if (gui == null) { Debug.LogWarning("[Layout] " + scene.name + ": no GUI in scene"); continue; }

                var reverted = 0;
                foreach (var name in moved)
                {
                    // the menu's panels live under MenuUI; the race HUD has its own object with the
                    // same name, and reverting that one would move the in-race lap counter
                    var menu = Find(gui.transform, "MenuUI");
                    if (menu == null) continue;

                    var target = Find(menu, name);
                    if (target == null) continue;
                    if (!PrefabUtility.IsPartOfPrefabInstance(target)) continue;

                    PrefabUtility.RevertObjectOverride(target.GetComponent<RectTransform>(),
                                                      InteractionMode.AutomatedAction);
                    reverted++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Layout] " + scene.name + ": reverted " + reverted + " stale layout override(s)");
            }
        }

        private static RectTransform EnsureFreeLayout(Transform menu)
        {
            var existing = menu.Find(FreeLayoutName) as RectTransform;
            if (existing != null) return existing;

            var go = new GameObject(FreeLayoutName, typeof(RectTransform));
            go.transform.SetParent(menu, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // without this the vertical group would stack it like everything else
            var element = go.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            return rect;
        }

        /// <summary>a big arrow pinned to one side, vertically centred, where a thumb naturally sits</summary>
        private static void PinSideButton(GameObject root, RectTransform free, string name, bool left)
        {
            var button = Find(root.transform, name);
            if (button == null) { Debug.LogWarning("[Layout] " + name + " not found"); return; }

            button.SetParent(free, false);
            var rect = (RectTransform)button;
            rect.anchorMin = new Vector2(left ? 0f : 1f, 0.5f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            rect.anchoredPosition = new Vector2(left ? 78f : -78f, -10f);
            rect.sizeDelta = new Vector2(150f, 150f);

            // out on the open road these sit over bright tarmac rather than inside a dark panel, so
            // the greyed-out state has to stay readable. at 0.25 alpha the disabled arrow all but
            // vanished and the control looked broken rather than unavailable.
            var control = rect.GetComponent<Button>();
            if (control != null)
            {
                var colours = control.colors;
                colours.disabledColor = new Color(colours.normalColor.r, colours.normalColor.g,
                                                  colours.normalColor.b, 0.5f);
                control.colors = colours;
            }

            Debug.Log("[Layout] " + name + " pinned to the " + (left ? "left" : "right") + " edge");
        }

        /// <summary>the car's name, centred under it and above PLAY</summary>
        private static void PlaceCarName(GameObject root, RectTransform free)
        {
            var label = Find(root.transform, "Car Value TMP");
            if (label == null) { Debug.LogWarning("[Layout] Car Value TMP not found"); return; }

            label.SetParent(free, false);
            var rect = (RectTransform)label;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 236f);
            rect.sizeDelta = new Vector2(1000f, 110f);

            var tmp = label.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Accent;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 44f;
                tmp.fontSizeMax = 96f;
            }
            Debug.Log("[Layout] car name moved to the bottom centre");
        }

        /// <summary>tucks a panel into the bottom left, clear of the centred name and PLAY</summary>
        private static void PlaceCorner(GameObject root, RectTransform free, string name, float fromBottom)
        {
            var panel = Find(root.transform, name);
            if (panel == null) return;

            panel.SetParent(free, false);
            var rect = (RectTransform)panel;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(40f, fromBottom);
            Debug.Log("[Layout] " + name + " moved to the bottom left");
        }

        private static void Remove(GameObject root, string name)
        {
            var t = Find(root.transform, name);
            if (t == null) return;
            Object.DestroyImmediate(t.gameObject);
            Debug.Log("[Layout] removed the empty " + name);
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
