using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// wires the THRYL single player integration:
///   config    a ThrylConfig asset in Resources, staging by default, so ThrylClient can boot itself
///             from any scene
///   menu      a player-name label in the car menu, top right
///   results   a best-score and save-status line under the score on the results panel
/// the client itself needs no scene object: it is created on first load from the config.
/// safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ThrylSetup
    {
        private const string ConfigPath = "Assets/Resources/ThrylConfig.asset";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string EventsPath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset";

        [MenuItem("Tools/Racing/Set Up THRYL Integration")]
        public static void Run()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(EventsPath);
            if (events == null) { Debug.LogError("[THRYL] GameEvents asset not found"); return; }

            EnsureConfig();
            WireGui(events);

            AssetDatabase.SaveAssets();
            Debug.Log("[THRYL] done");
        }

        private static void EnsureConfig()
        {
            VfxMaterials.EnsureFolder("Assets/Resources");
            var config = AssetDatabase.LoadAssetAtPath<ThrylConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ThrylConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log("[THRYL] created " + ConfigPath + " (staging)");
            }
            else
            {
                Debug.Log("[THRYL] config present, environment " + config.environment);
            }
        }

        private static void WireGui(GameEvents events)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var free = Find(root.transform, "Free Layout");
                var panel = Find(root.transform, "Race Finish Panel ");
                var finishText = Find(root.transform, "Finish TMP");
                var referenceLabel = Find(root.transform, "Car Value TMP")?.GetComponent<TMP_Text>();
                if (free == null || panel == null) { Debug.LogError("[THRYL] Free Layout or Race Finish Panel missing"); return; }

                // menu: the player's name, top right, opposite the TRACKS button
                var name = TextObject(free, "Player Name TMP", referenceLabel, 34f, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
                var nameRect = (RectTransform)name.transform;
                nameRect.anchorMin = nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.pivot = new Vector2(1f, 1f);
                nameRect.anchoredPosition = new Vector2(-40f, -52f);
                nameRect.sizeDelta = new Vector2(520f, 70f);
                name.color = new Color(1f, 1f, 1f, 0.85f);

                // results: under the place and score
                var status = TextObject(panel, "Platform Status TMP", finishText != null ? finishText.GetComponent<TMP_Text>() : referenceLabel, 22f, FontStyles.Normal, TextAlignmentOptions.Center);
                var statusRect = (RectTransform)status.transform;
                statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
                statusRect.pivot = new Vector2(0.5f, 0.5f);
                statusRect.anchoredPosition = new Vector2(0f, -6f);
                statusRect.sizeDelta = new Vector2(380f, 30f);
                status.color = new Color(1f, 1f, 1f, 0.8f);
                status.text = "";

                var gui = root.GetComponent<ThrylStatusGUI>();
                if (gui == null) gui = root.AddComponent<ThrylStatusGUI>();
                gui.gameEvents = events;
                gui.playerNameTMP = name;
                gui.resultsStatusTMP = status;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[THRYL] GUI wired: player name on the menu, status on the results panel");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static TMP_Text TextObject(Transform parent, string name, TMP_Text reference, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            var t = Find(parent, name);
            if (t == null)
            {
                t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).transform;
                t.SetParent(parent, false);
            }
            var text = t.GetComponent<TMP_Text>();
            if (reference != null && reference.font != null) text.font = reference.font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
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
