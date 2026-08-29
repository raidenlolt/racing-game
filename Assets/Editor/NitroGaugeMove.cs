using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// moves the nitro gauge out from beside the fire button and puts it under the speedometer.
///
/// the button stays where it is. it is a control and belongs under a thumb; the gauge is a readout
/// and belongs where the eyes already are, which during a race is the speed. splitting them means
/// the bar is read without looking away from the middle of the screen.
///
/// the speedometer shifts up to make room, so the bar is not jammed against the bottom edge of the
/// screen where a phone's gesture bar lives.
///
/// the gauge is re-parented rather than rebuilt, so NitroGUI's reference to the fill image survives.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class NitroGaugeMove
    {
        private const string PrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        /// <summary>speedometer moves up from 36 to clear the bar beneath it</summary>
        private const float SpeedPanelY = 86f;
        /// <summary>
        /// sits directly under the speed panel rather than down at the screen edge. at 34 the bar was
        /// 44 pixels from the bottom of the phone, which is where the system gesture bar lives.
        /// </summary>
        private const float GaugeY = 58f;
        private const float GaugeWidth = 320f;
        private const float GaugeHeight = 26f;

        [MenuItem("Tools/Racing/Move Nitro Gauge Under Speedo")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var raceUi = Find(root.transform, "RaceUI");
                var gauge = Find(root.transform, "Gauge Track");
                var speed = Find(root.transform, "Speed HUD");
                if (raceUi == null || gauge == null) { Debug.LogError("[Gauge] RaceUI or Gauge Track missing"); return; }

                // lift the speedometer so the bar has somewhere to sit
                if (speed != null)
                {
                    var speedRect = (RectTransform)speed;
                    speedRect.anchoredPosition = new Vector2(0f, SpeedPanelY);
                    Debug.Log("[Gauge] speedometer raised to y " + SpeedPanelY);
                }

                gauge.SetParent(raceUi, false);
                var rect = (RectTransform)gauge;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, GaugeY);
                rect.sizeDelta = new Vector2(GaugeWidth, GaugeHeight);

                // the empty part of the bar has to be visible, or all the player sees is a short blue
                // stub floating in the dark with no sense of how much of the gauge that is
                var track = gauge.GetComponent<Image>();
                if (track != null) track.color = new Color(0.02f, 0.03f, 0.04f, 0.8f);

                // the fill is a child and is stretched to its parent, so it follows without help
                var fill = Find(gauge, "Gauge Fill");
                if (fill != null)
                {
                    var fillRect = (RectTransform)fill;
                    fillRect.sizeDelta = new Vector2(GaugeWidth - 8f, GaugeHeight - 8f);
                }

                var button = Find(root.transform, "Fire Nitro Button");
                Debug.Log("[Gauge] gauge moved under the speedometer; fire button left at "
                          + (button != null ? ((RectTransform)button).anchoredPosition.ToString("F0") : "?"));

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            ClearStaleOverrides();
            Debug.Log("[Gauge] done");
        }

        /// <summary>
        /// the tracks each hold an instance of the GUI prefab, and instance overrides beat the
        /// prefab. an old recorded position on these objects would pin them where they used to be,
        /// which is exactly what happened the last time something on this HUD was moved.
        /// </summary>
        private static void ClearStaleOverrides()
        {
            var moved = new[] { "Gauge Track", "Gauge Fill", "Speed HUD" };

            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var gui = GameObject.Find("GUI");
                if (gui == null) continue;

                var raceUi = Find(gui.transform, "RaceUI");
                if (raceUi == null) continue;

                var reverted = 0;
                foreach (var name in moved)
                {
                    var target = Find(raceUi, name);
                    if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target)) continue;
                    PrefabUtility.RevertObjectOverride(target.GetComponent<RectTransform>(),
                                                      InteractionMode.AutomatedAction);
                    reverted++;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Gauge] " + scene.name + ": reverted " + reverted + " stale override(s)");
            }
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
