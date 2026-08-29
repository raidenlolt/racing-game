using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// adds the speed readout to the race HUD.
///
/// this edits the GUI prefab rather than a scene copy, so every track picks it up and the change
/// survives the scene being reloaded. the prefab is opened with LoadPrefabContents and saved back,
/// which is the only way to edit a prefab asset without going through an instance and its overrides.
///
/// placement is bottom centre. that is the one part of the race HUD with nothing in it: the joystick
/// occupies the bottom left out to x=410, the brake and throttle pads the bottom right from x=1185,
/// and the nitro gauge sits above them. the readout sits in the gap between, where it is visible
/// without competing with a thumb.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class SpeedGuiSetup
    {
        private const string PrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string PanelName = "Speed HUD";

        [MenuItem("Tools/Racing/Add Speed Readout")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var raceUi = FindChild(root.transform, "RaceUI");
                if (raceUi == null) { Debug.LogError("[Speed] RaceUI not found in the GUI prefab"); return; }

                // rebuild from scratch so re-running cannot leave a stale copy behind
                var existing = FindChild(root.transform, PanelName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var style = GatherStyle(root.transform);
                var panel = BuildPanel(raceUi, style);

                var gui = root.GetComponent<SpeedGUI>();
                if (gui == null) gui = root.AddComponent<SpeedGUI>();
                gui.gameEvents = FindGameEvents(root.transform);
                gui.raceManager = FindRaceManager(root.transform);
                gui.speedTMP = panel.value;
                gui.unitTMP = panel.unit;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Speed] readout added to the GUI prefab: value + unit wired, gameEvents=" +
                          (gui.gameEvents != null) + " raceManager=" + (gui.raceManager != null));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private struct Style
        {
            public Sprite panelSprite;
            public Color panelColor;
            public TMP_FontAsset font;
            public Color textColor;
        }

        /// <summary>
        /// copies the look of the panels already on the HUD, so the readout does not arrive in a
        /// different font and a different grey to everything around it
        /// </summary>
        private static Style GatherStyle(Transform root)
        {
            var style = new Style
            {
                panelColor = new Color(0f, 0f, 0f, 0.588f),
                textColor = Color.white
            };

            var reference = FindChild(root, "Race Position Panel");
            if (reference != null)
            {
                var img = reference.GetComponent<Image>();
                if (img != null) { style.panelSprite = img.sprite; style.panelColor = img.color; }
            }

            var text = FindChild(root, "Position TMP");
            if (text != null)
            {
                var tmp = text.GetComponent<TMP_Text>();
                if (tmp != null) { style.font = tmp.font; style.textColor = tmp.color; }
            }
            return style;
        }

        private struct Built { public TMP_Text value; public TMP_Text unit; }

        private static Built BuildPanel(Transform parent, Style style)
        {
            var panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            // anchored to the bottom edge centre so it holds position on any aspect ratio
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 36f);
            rect.sizeDelta = new Vector2(300f, 132f);

            var bg = panel.GetComponent<Image>();
            bg.sprite = style.panelSprite;
            bg.color = style.panelColor;
            bg.type = Image.Type.Simple;
            bg.raycastTarget = false;   // never steal a touch from the controls beside it

            var value = MakeText(rect, "Speed Value TMP", style, 76f,
                new Vector2(0f, 22f), new Vector2(280f, 84f));
            // the number swings between one and three digits, so let it shrink rather than clip
            value.enableAutoSizing = true;
            value.fontSizeMin = 40f;
            value.fontSizeMax = 76f;

            var unit = MakeText(rect, "Speed Unit TMP", style, 26f,
                new Vector2(0f, -44f), new Vector2(280f, 34f));
            unit.text = "MPH";

            return new Built { value = value, unit = unit };
        }

        private static TMP_Text MakeText(RectTransform parent, string name, Style style, float size,
                                         Vector2 position, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (style.font != null) tmp.font = style.font;
            tmp.color = style.textColor;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.text = "0";
            return tmp;
        }

        /// <summary>
        /// the GUI prefab's other scripts already reference the shared event bus and race manager
        /// assets; borrowing from one of them avoids hard coding an asset path that could move
        /// </summary>
        private static GameEvents FindGameEvents(Transform root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var field = behaviour.GetType().GetField("gameEvents");
                if (field == null || field.FieldType != typeof(GameEvents)) continue;
                var value = field.GetValue(behaviour) as GameEvents;
                if (value != null) return value;
            }
            return AssetDatabase.FindAssets("t:GameEvents")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameEvents>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(a => a != null);
        }

        private static RaceManagerItem FindRaceManager(Transform root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var field = behaviour.GetType().GetField("raceManager");
                if (field == null || field.FieldType != typeof(RaceManagerItem)) continue;
                var value = field.GetValue(behaviour) as RaceManagerItem;
                if (value != null) return value;
            }
            return AssetDatabase.FindAssets("t:RaceManagerItem")
                .Select(g => AssetDatabase.LoadAssetAtPath<RaceManagerItem>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(a => a != null);
        }

        private static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindChild(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
