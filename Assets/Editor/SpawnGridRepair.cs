using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// makes sure every track has one spawn point per car and that the points sit on the road.
///
/// a race is the player plus RaceData.AiBotsSelected bots. Race_Track_02 and Race_Track_03 were left
/// with six points for seven cars, and PlayersSpawner used to clamp the index, so the seventh car
/// was instantiated inside the sixth and physics threw the pair apart at the start. the spawner now
/// refuses to double-book a slot; this puts the missing slots back so it does not have to.
///
/// missing points are taken from the Spawning prefab's spare children and placed by extending the
/// existing grid: same column as the point two places earlier, one row further back. every point is
/// then dropped onto whatever collider is beneath it, so a hand-dragged point that ended up inside
/// the tarmac stops being a launch ramp.
///
/// no slot may sit on the start line. checkpoint 1 is the start line and its trigger is 1.4 m thick;
/// a car whose slot is inside it never "enters" it at the flag, so its first crossing is never
/// counted and a 1 lap race needs 2 laps (Race_Track_01's pole slot was 0.4 m past the line). any
/// slot closer than MinLineClearance to the line is moved to a new row at the back of the grid.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class SpawnGridRepair
    {
        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_04.unity",
        };

        /// <summary>row pitch of the grid TrackWiring builds</summary>
        private const float RowSpacing = 9f;
        /// <summary>height above the surface, matching TrackWiring's WaypointHeightAboveRoad</summary>
        private const float HeightAboveRoad = 0.6f;
        private const float ProbeHeight = 10f;
        /// <summary>two points closer than this hold overlapping cars</summary>
        private const float MinSpacing = 5f;
        /// <summary>metres a slot must be short of the start line: a car length plus the trigger</summary>
        private const float MinLineClearance = 6f;

        [MenuItem("Tools/Racing/Repair Spawn Grids")]
        public static void RunMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        /// <summary>
        /// batch entry point:
        ///   Unity.exe -batchmode -quit -projectPath . -executeMethod SpinMotion.EditorTools.SpawnGridRepair.Run
        /// </summary>
        public static void Run()
        {
            var needed = RaceData.AiBotsSelected + 1;
            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var spawner = Object.FindFirstObjectByType<PlayersSpawner>();
                if (spawner == null)
                {
                    Debug.LogWarning("[SpawnGrid] " + scene.name + ": no PlayersSpawner, skipped");
                    continue;
                }

                Repair(spawner, needed, scene.name);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[SpawnGrid] done");
        }

        private static void Repair(PlayersSpawner spawner, int needed, string sceneName)
        {
            var list = spawner.spawnPoints.Where(t => t != null).ToList();
            var before = list.Count;

            var spare = spawner.transform.root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("SpawnPoint") && !list.Contains(t))
                .OrderBy(t => NumericSuffix(t.name))
                .ToList();

            while (list.Count < needed)
            {
                Transform point;
                if (spare.Count > 0)
                {
                    point = spare[0];
                    spare.RemoveAt(0);
                }
                else
                {
                    // Race_Track_02 and 03 deleted the prefab's extra points from their instance, so
                    // there is nothing to reuse. a plain transform is all the spawner reads; it goes in
                    // beside the existing points as an added child of the instance
                    var parent = list.Count > 0 ? list[0].parent : spawner.transform;
                    var next = list.Count > 0 ? list.Max(t => NumericSuffix(t.name)) + 1 : 1;
                    point = new GameObject("SpawnPoint " + next).transform;
                    point.SetParent(parent, true);
                    Undo.RegisterCreatedObjectUndo(point.gameObject, "spawn point");
                }

                var i = list.Count;
                Vector3 pos;
                Quaternion rot;
                if (i >= 2)
                {
                    // same column as the point two back, one row further down the grid. the column
                    // direction is measured from the grid itself rather than assumed, so a grid that
                    // was dragged about by hand extends the way it actually runs
                    var same = list[i - 2];
                    var back = i >= 4 ? (same.position - list[i - 4].position).normalized
                                      : -(same.rotation * Vector3.forward);
                    back.y = 0f;
                    if (back.sqrMagnitude < 0.01f) back = -(same.rotation * Vector3.forward);
                    pos = same.position + back.normalized * RowSpacing;
                    rot = same.rotation;
                }
                else if (i == 1)
                {
                    var first = list[0];
                    pos = first.position + first.rotation * new Vector3(14f, 0f, -4.5f);
                    rot = first.rotation;
                }
                else
                {
                    pos = point.position;
                    rot = point.rotation;
                }

                point.position = pos;
                point.rotation = rot;
                list.Add(point);
                Debug.Log("[SpawnGrid] " + sceneName + ": added " + point.name + " at " + pos.ToString("F1"));
            }

            if (list.Count < needed)
                Debug.LogError("[SpawnGrid] " + sceneName + ": only " + list.Count + " of " + needed
                               + " spawn points and no spare points left in the Spawning prefab");

            KeepBehindStartLine(list, sceneName);

            // everything onto the surface
            foreach (var point in list)
            {
                var snapped = SnapToGround(point.position, out var hitName);
                if (snapped.HasValue)
                {
                    point.position = snapped.Value;
                }
                else
                {
                    Debug.LogWarning("[SpawnGrid] " + sceneName + ": nothing under " + point.name
                                     + " at " + point.position.ToString("F1") + ", left where it was");
                }
                point.rotation = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
                RecordOverrides(point);
            }

            for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    var d = Vector3.Distance(list[a].position, list[b].position);
                    if (d < MinSpacing)
                        Debug.LogError("[SpawnGrid] " + sceneName + ": " + list[a].name + " and " + list[b].name
                                       + " are " + d.ToString("F1") + " m apart, cars will overlap");
                }

            spawner.spawnPoints = list;
            RecordOverrides(spawner);
            Debug.Log("[SpawnGrid] " + sceneName + ": " + before + " -> " + list.Count + " spawn points (need " + needed + ")");
        }

        /// <summary>
        /// the point HeightAboveRoad over the first non-trigger collider below the position, or null
        /// when there is nothing under it at all
        /// </summary>
        /// <summary>
        /// moves any slot that is on or past the start line to a fresh row behind the last one, in
        /// the same column it had. the line is checkpoint 1; travel direction is the grid's own facing
        /// </summary>
        private static void KeepBehindStartLine(List<Transform> list, string sceneName)
        {
            var checkpoints = Object.FindFirstObjectByType<Checkpoints>(FindObjectsInactive.Include);
            var line = checkpoints != null ? checkpoints.GetComponentsInChildren<Checkpoint>(true).FirstOrDefault() : null;
            var trigger = line != null ? line.GetComponent<Collider>() : null;
            if (trigger == null || list.Count == 0)
            {
                Debug.LogWarning("[SpawnGrid] " + sceneName + ": no start line checkpoint found, line clearance not checked");
                return;
            }

            var forward = Vector3.zero;
            foreach (var p in list) forward += p.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            var lineCentre = trigger.bounds.center;
            var right = Vector3.Cross(Vector3.up, forward);

            foreach (var point in list)
            {
                var along = Vector3.Dot(point.position - lineCentre, forward);
                if (along < -MinLineClearance) continue;

                // the row behind the rearmost of the other slots, keeping this slot's column
                var others = list.Where(p => p != point).ToList();
                var rearmost = others.Min(p => Vector3.Dot(p.position - lineCentre, forward));
                var lateral = Vector3.Dot(point.position - lineCentre, right);
                var target = lineCentre + forward * (rearmost - RowSpacing) + right * lateral;
                target.y = point.position.y;
                Debug.LogWarning("[SpawnGrid] " + sceneName + ": " + point.name + " was " + along.ToString("F1")
                                 + " m from the start line, moved to the back row at " + target.ToString("F1"));
                point.position = target;
            }
        }

        private static Vector3? SnapToGround(Vector3 at, out string hitName)
        {
            hitName = null;
            var origin = at + Vector3.up * ProbeHeight;
            var hits = Physics.RaycastAll(origin, Vector3.down, ProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider != null && h.collider.attachedRigidbody == null)
                .OrderBy(h => h.distance)
                .ToList();
            if (hits.Count == 0) return null;
            hitName = hits[0].collider.name;
            return hits[0].point + Vector3.up * HeightAboveRoad;
        }

        private static int NumericSuffix(string name)
        {
            var digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }

        /// <summary>
        /// the tracks hold the Spawning prefab as an instance, so edits to its children have to be
        /// recorded as overrides or the next prefab reimport drops them
        /// </summary>
        private static void RecordOverrides(Object target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(target);
        }
    }
}
