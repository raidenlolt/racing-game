using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// puts the mini map on the race HUD. the GUI prefab is instanced in every track scene, so this one
/// edit reaches all maps; the map itself is traced at runtime from each track's AI waypoint loop.
///
/// it sits on the left under the race position panel, clear of the steering stick at the bottom
/// left and the position and lap panels along the top. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class MiniMapSetup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string SO = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/";

        private static readonly Vector2 PanelPosition = new Vector2(10f, -200f);
        private static readonly Vector2 PanelSize = new Vector2(230f, 210f);

        [MenuItem("Tools/Racing/Set Up Mini Map")]
        public static void Run()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(SO + "GameEvents.asset");
            var positions = AssetDatabase.LoadAssetAtPath<RealTimeRacePositionsItem>(SO + "Real Time Race Positions Item.asset");
            var waypoints = AssetDatabase.LoadAssetAtPath<AIWaypointSet>(SO + "AI Waypoint Set.asset");
            if (events == null || positions == null || waypoints == null)
            {
                Debug.LogError("[MiniMap] ScriptableObjects not found");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var raceUi = Find(root.transform, "RaceUI");
                if (raceUi == null) { Debug.LogError("[MiniMap] RaceUI not found"); return; }

                var panel = Find(raceUi, "Mini Map");
                if (panel == null)
                {
                    panel = new GameObject("Mini Map", typeof(RectTransform), typeof(Image)).transform;
                    panel.SetParent(raceUi, false);
                    // after the overlays, before the readouts, so it never covers a panel
                    var lines = Find(raceUi, "Speed Lines");
                    panel.SetSiblingIndex(lines != null ? lines.GetSiblingIndex() + 1 : 2);
                }
                var panelRect = (RectTransform)panel;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
                panelRect.pivot = new Vector2(0f, 1f);
                panelRect.anchoredPosition = PanelPosition;
                panelRect.sizeDelta = PanelSize;

                var background = panel.GetComponent<Image>();
                background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                background.type = Image.Type.Sliced;
                background.color = new Color(0.02f, 0.03f, 0.05f, 0.55f);
                background.raycastTarget = false;

                var trackObject = Find(panel, "Track");
                if (trackObject == null)
                {
                    trackObject = new GameObject("Track", typeof(RectTransform), typeof(MiniMapTrackGraphic)).transform;
                    trackObject.SetParent(panel, false);
                }
                var trackRect = (RectTransform)trackObject;
                trackRect.anchorMin = Vector2.zero;
                trackRect.anchorMax = Vector2.one;
                trackRect.pivot = new Vector2(0.5f, 0.5f);
                trackRect.anchoredPosition = Vector2.zero;
                trackRect.sizeDelta = Vector2.zero;
                var track = trackObject.GetComponent<MiniMapTrackGraphic>();
                track.raycastTarget = false;

                var map = panel.GetComponent<MiniMapGUI>();
                if (map == null) map = panel.gameObject.AddComponent<MiniMapGUI>();
                map.gameEvents = events;
                map.realTimeRacePositions = positions;
                map.aiWaypointSet = waypoints;
                map.track = track;
                map.mapArea = trackRect;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[MiniMap] mini map added to RaceUI at " + PanelPosition + " size " + PanelSize);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            Debug.Log("[MiniMap] done");
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
