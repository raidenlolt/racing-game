using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// the client's change request of 2026-09-22 (feedback/Car Racing Changes.docx), points 2 and 4:
///   minimap    other cars drawn as cyan dots (MiniMapGUI.botColor in the GUI prefab)
///   touch pads the four driving pads become TouchPad components that track pointer ids, replacing
///              the EventTrigger + MobileButtonHandler stack that zeroed an axis on pointer exit and
///              on any finger lifting, which is what broke steering while gas or brake was held
/// the pads' placement, size and art are left alone: only the input components change.
/// safe to re-run. points 3 and 5 are code changes (WallSlide, RaceScore) and need no setup.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ChangesRound3Setup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string ControlsPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Mobile Controls.prefab";

        private struct Pad { public string axis; public bool positive; public bool overrides; }

        /// <summary>every name a driving pad has gone by, in either prefab</summary>
        private static readonly Dictionary<string, Pad> Pads = new Dictionary<string, Pad>
        {
            { "Throttle Button",      new Pad { axis = "Vertical",   positive = true } },
            { "Brake/Reverse Button", new Pad { axis = "Vertical",   positive = false, overrides = true } },
            { "Brake Button",         new Pad { axis = "Vertical",   positive = false, overrides = true } },
            { "Steer Left Button",    new Pad { axis = "Horizontal", positive = false } },
            { "Steer Right Button",   new Pad { axis = "Horizontal", positive = true } },
            { "Turn Left Button",     new Pad { axis = "Horizontal", positive = false } },
            { "Turn Right Button",    new Pad { axis = "Horizontal", positive = true } },
        };

        [MenuItem("Tools/Racing/Apply Client Changes Round 3")]
        public static void Run()
        {
            ConvertPads(ControlsPrefab);
            ConvertPads(GuiPrefab);
            MiniMapColours(GuiPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[Round3] done");
        }

        private static void ConvertPads(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var converted = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    Pad pad;
                    if (!Pads.TryGetValue(t.name, out pad)) continue;
                    // a pad that lives in a nested prefab instance is converted where it is defined
                    if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) && PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) != root) continue;

                    var trigger = t.GetComponent<EventTrigger>();
                    if (trigger != null) Object.DestroyImmediate(trigger);
                    var handler = t.GetComponent<MobileButtonHandler>();
                    if (handler != null) Object.DestroyImmediate(handler);

                    var touch = t.GetComponent<TouchPad>();
                    if (touch == null) touch = t.gameObject.AddComponent<TouchPad>();
                    touch.axisName = pad.axis;
                    touch.positive = pad.positive;
                    touch.overridesOpposite = pad.overrides;
                    converted++;
                }
                if (converted > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log("[Round3] " + System.IO.Path.GetFileName(prefabPath) + ": " + converted + " pads now use TouchPad");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void MiniMapColours(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var map = root.GetComponentInChildren<MiniMapGUI>(true);
                if (map == null) { Debug.LogWarning("[Round3] no MiniMapGUI in " + prefabPath); return; }
                map.botColor = new Color(0f, 1f, 1f, 1f);
                // the dots were 9 reference pixels on a 5 pixel line: invisible on a phone
                map.botSize = 16f;
                map.playerSize = 22f;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[Round3] minimap: other cars are cyan, " + map.botSize + " px with a dark rim");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
