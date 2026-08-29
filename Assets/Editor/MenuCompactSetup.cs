using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// two related fixes.
///
/// the bot count selector is removed and the field size is fixed in RaceData instead, which frees a
/// slot in the menu's selector row.
///
/// that slot matters, because the menu had grown taller than the screen. the vertical stack came to
/// 1030 units inside a 900 unit canvas, so the Play button sat 130 units below the bottom edge and
/// was unreachable on a phone. moving the car and track pickers up into the freed row and shrinking
/// the preview to a single short strip brings the stack back inside the canvas.
///
/// the row is also kept narrow enough for a 16:9 screen: four 340-wide panels with 50 spacing is
/// 1510, and the canvas is 1600 wide at its narrowest, so the row does not clip on tall phones
/// either.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class MenuCompactSetup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string SpawnPointPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/SpawnPoint.prefab";

        // sized for headroom, not just to fit. merely fitting left the Play button 10 units from the
        // bottom edge, which is about 12 pixels on a 1080p phone and lands underneath the Android
        // gesture navigation bar. these values leave roughly 35 units of margin top and bottom
        private const float PreviewRowHeight = 110f;
        private const float MenuSpacing = 10f;

        [MenuItem("Tools/Racing/Compact Menu And Fix Bot Count")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RebuildMenu();
            AddSpawnPointsToTrack01();
            AssetDatabase.SaveAssets();
            Debug.Log("[Menu] done");
        }

        private static void RebuildMenu()
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            if (root == null) { Debug.LogError("[Menu] missing " + GuiPrefab); return; }

            try
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var menuUi = all.FirstOrDefault(t => t.name == "MenuUI");
                var mainPanels = all.FirstOrDefault(t => t.name == "Main Panels");
                var selectionRow = all.FirstOrDefault(t => t.name == "Selection Panels");
                var botContainer = all.FirstOrDefault(t => t.name == "AI Bots Container");
                var botScript = root.GetComponentInChildren<BotSelectorGUI>(true);
                var carContainer = all.FirstOrDefault(t => t.name == "Car Container");
                var mapContainer = all.FirstOrDefault(t => t.name == "Map Container");
                var preview = all.FirstOrDefault(t => t.name == "Car Preview");

                if (menuUi == null || mainPanels == null)
                {
                    Debug.LogError("[Menu] menu structure not as expected, aborting");
                    return;
                }

                // 1. drop the bot count selector, panel and script both
                if (botContainer != null) { Object.DestroyImmediate(botContainer.gameObject); Debug.Log("[Menu] removed AI Bots Container"); }
                if (botScript != null) { Object.DestroyImmediate(botScript.gameObject); Debug.Log("[Menu] removed Bot Selector GUI script object"); }

                // 2. promote the car and track pickers into the freed row
                if (carContainer != null) carContainer.SetParent(mainPanels, false);
                if (mapContainer != null) mapContainer.SetParent(mainPanels, false);

                // 3. the old row becomes a single short strip holding only the preview
                if (selectionRow != null && preview != null)
                {
                    preview.SetParent(selectionRow, false);
                    ((RectTransform)selectionRow).sizeDelta = new Vector2(1300f, PreviewRowHeight);
                    var previewRect = (RectTransform)preview;
                    // 16:9 so the render texture is not squashed, and short enough to fit the stack
                    previewRect.sizeDelta = new Vector2(PreviewRowHeight * 16f / 9f, PreviewRowHeight);
                }

                // 4. tighten the vertical rhythm to claw back the last of the overflow
                var vlg = menuUi.GetComponent<VerticalLayoutGroup>();
                if (vlg != null) vlg.spacing = MenuSpacing;

                // 5. make the word labels actually readable
                FitWordLabel(carContainer, "Car");
                FitWordLabel(mapContainer, "Map");

                ReportHeight(menuUi, vlg);

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// these panels were cloned from the laps selector, whose value label only ever shows one
        /// digit: 160 units wide, font size 84, overflow set to Truncate. a car name does not fit in
        /// that, so "VECTOR" rendered as "VECT" and "COASTAL" as "COAS".
        ///
        /// the buttons sit at plus and minus 115 and are 70 wide, leaving exactly 160 between them,
        /// so widening alone cannot solve it. auto-sizing is what actually fixes it: long names shrink
        /// to fit while a short one still renders large. the buttons are also nudged outwards to buy
        /// the label a little more room, staying inside the 340 wide panel.
        /// </summary>
        private static void FitWordLabel(Transform container, string prefix)
        {
            if (container == null) return;
            var all = container.GetComponentsInChildren<RectTransform>(true);

            var value = all.FirstOrDefault(t => t.name == prefix + " Value TMP");
            if (value != null)
            {
                value.sizeDelta = new Vector2(190f, value.sizeDelta.y);
                var tmp = value.GetComponent<TMPro.TMP_Text>();
                if (tmp != null)
                {
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMax = 84f;
                    tmp.fontSizeMin = 24f;
                    tmp.enableWordWrapping = false;
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                }
            }

            foreach (var side in new[] { "Down", "Up" })
            {
                var btn = all.FirstOrDefault(t => t.name == prefix + " " + side + " BTN");
                if (btn == null) continue;
                var x = side == "Down" ? -128f : 128f;
                btn.anchoredPosition = new Vector2(x, btn.anchoredPosition.y);
            }
            Debug.Log("[Menu] " + prefix + " label set to auto-size 24-84 in a 190 wide slot");
        }

        private static void ReportHeight(Transform menuUi, VerticalLayoutGroup vlg)
        {
            float total = 0f;
            var count = 0;
            foreach (RectTransform c in menuUi)
            {
                if (!c.gameObject.activeSelf) continue;
                total += c.sizeDelta.y;
                count++;
            }
            if (vlg != null) total += vlg.spacing * Mathf.Max(0, count - 1) + vlg.padding.top + vlg.padding.bottom;
            Debug.Log("[Menu] stack height now " + total + " of 900 available" +
                      (total <= 900f ? "  (fits)" : "  (STILL OVERFLOWS by " + (total - 900f) + ")"));
        }

        /// <summary>
        /// Race_Track_01 was authored with a six car grid, which seats the player plus five bots. the
        /// fixed field is six bots, so it needs at least seven places. this extends the existing two
        /// column grid backwards by one more row, keeping the same spacing and rotation
        /// </summary>
        private static void AddSpawnPointsToTrack01()
        {
            const string path = "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var spawner = Object.FindFirstObjectByType<PlayersSpawner>();
            if (spawner == null) { Debug.LogWarning("[Menu] no spawner in Race_Track_01"); return; }

            var needed = RaceData.AiBotsSelected + 1;
            var existing = spawner.spawnPoints.Where(p => p != null).ToList();
            if (existing.Count >= needed)
            {
                Debug.Log("[Menu] Race_Track_01 already has " + existing.Count + " spawn points for " + needed + " cars");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnPointPrefab);
            if (prefab == null) { Debug.LogError("[Menu] missing " + SpawnPointPrefab); return; }

            // the grid runs in two columns of decreasing x, so a new row goes further back in +x.
            // derive the step from the rows that are already there rather than hardcoding it
            var ordered = existing.OrderByDescending(p => p.position.x).ToList();
            var backRow = ordered.Take(2).ToList();
            var step = Mathf.Abs(ordered[0].position.x - ordered[2].position.x);
            if (step < 1f) step = 17f;

            var parent = backRow[0].parent;
            var added = 0;
            while (existing.Count < needed)
            {
                foreach (var reference in backRow)
                {
                    if (existing.Count >= needed) break;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    go.name = "SpawnPoint " + (existing.Count + 1);
                    go.transform.position = reference.position + new Vector3(step * (added / 2 + 1), 0f, 0f);
                    go.transform.rotation = reference.rotation;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                    spawner.spawnPoints.Add(go.transform);
                    existing.Add(go.transform);
                    added++;
                    Debug.Log("[Menu] added " + go.name + " at " + go.transform.position);
                }
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(spawner);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Menu] Race_Track_01 now has " + spawner.spawnPoints.Count(p => p != null) +
                      " spawn points for " + needed + " cars");
        }
    }
}
