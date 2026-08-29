using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// builds the race setup into a track scene that only has art in it: managers, cameras, the AI
/// waypoint loop, the checkpoint gates and the starting grid, all laid out along the circuit that
/// the scene's own modular road pieces describe.
/// mirrors how Race_Track_01 was authored by hand, so a wired scene stays editable afterwards
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class TrackWiring
    {
        private const string Prefabs = "Assets/Racing Starter Kit/RSK Assets/Prefabs/";
        private const string TrackBased = Prefabs + "Track Based/";
        private const string Managers = Prefabs + "Managers and Systems/";
        private const string Cars = Prefabs + "Player Cars/";

        // the road kit names every drivable piece with one of these prefixes
        private static readonly string[] RoadPrefixes = { "road2x", "road90" };

        // heights are measured off the road surface, matching the values Race_Track_01 uses
        private const float WaypointHeightAboveRoad = 0.6f;
        private const float CheckpointHeightAboveRoad = 4.8f;
        private const float CheckpointScale = 5.5f;

        // starting grid: two staggered columns, cars queued back from the start line
        private const float GridColumnOffset = 7f;
        private const float GridRowSpacing = 9f;
        private const float GridStagger = 4.5f;
        private const float GridSetback = 18f;

        private static readonly string[] TargetScenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        [MenuItem("Tools/Racing/Wire Up Unwired Tracks")]
        public static void WireUpMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            WireAll();
        }

        /// <summary>batch entry point: Unity.exe -executeMethod SpinMotion.EditorTools.TrackWiring.WireAll</summary>
        public static void WireAll()
        {
            foreach (var scenePath in TargetScenes)
            {
                try
                {
                    Wire(scenePath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[TrackWiring] " + scenePath + " failed: " + e);
                }
            }

            RegisterScenesInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[TrackWiring] done");
        }

        private static void Wire(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log("[TrackWiring] wiring " + scenePath);

            if (Object.FindFirstObjectByType<RaceManager>() != null)
            {
                Debug.LogWarning("[TrackWiring] " + scene.name +
                                 " already has a RaceManager, skipping so hand edits are not clobbered");
                return;
            }

            var line = BuildRacingLine();
            if (line.Count < 8)
                throw new System.InvalidOperationException(
                    "only found " + line.Count + " road pieces, cannot derive a racing line");

            Debug.Log("[TrackWiring] " + scene.name + ": racing line has " + line.Count +
                      " points, length " + LineLength(line).ToString("F0") + "m");

            // plain managers, no placement needed
            foreach (var p in new[] { "GameManager", "RaceManager", "Real Time Race Positions", "GUI", "SFX" })
                InstantiatePrefabAt(Managers + p + ".prefab");

            PlaceMenuCamera(line);
            InstantiatePrefabAt(TrackBased + "Finish Race Camera.prefab");

            PlaceWaypoints(line);
            var startIndex = PlaceCheckpoints(line);
            PlaceSpawning(line, startIndex);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- racing line ----------

        /// <summary>
        /// the modular road pieces are laid end to end around a closed circuit, so walking them
        /// nearest-neighbour from one extreme recovers the loop in order
        /// </summary>
        private static List<Vector3> BuildRacingLine()
        {
            var pieces = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => RoadPrefixes.Any(p => t.name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)))
                .Select(t => t.position)
                .ToList();

            if (pieces.Count == 0) return pieces;

            var remaining = new List<Vector3>(pieces);
            var start = remaining.OrderBy(p => p.x).ThenBy(p => p.z).First();
            remaining.Remove(start);

            var ordered = new List<Vector3> { start };
            while (remaining.Count > 0)
            {
                var current = ordered[ordered.Count - 1];
                var next = remaining.OrderBy(p => SqrXZ(p, current)).First();
                remaining.Remove(next);
                ordered.Add(next);
            }
            return ordered;
        }

        private static float SqrXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float LineLength(List<Vector3> line)
        {
            float total = 0f;
            for (int i = 0; i < line.Count; i++)
                total += Vector3.Distance(line[i], line[(i + 1) % line.Count]);
            return total;
        }

        /// <summary>direction of travel at a point, taken from the next point on the loop</summary>
        private static Vector3 Tangent(List<Vector3> line, int i)
        {
            var a = line[i];
            var b = line[(i + 1) % line.Count];
            var dir = b - a;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
        }

        // ---------- placement ----------

        private static void PlaceWaypoints(List<Vector3> line)
        {
            var root = InstantiatePrefabAt(TrackBased + "AI Waypoints.prefab");
            var existing = ChildrenOf(root);
            var waypointPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "AI Waypoint.prefab");

            // one waypoint per road piece keeps the AI lookahead tight through corners.
            // the container prefab ships with a few, so top it up to match this circuit
            for (int i = existing.Count; i < line.Count; i++)
            {
                var extra = (GameObject)PrefabUtility.InstantiatePrefab(waypointPrefab);
                extra.transform.SetParent(root.transform, false);
                existing.Add(extra.transform);
            }
            for (int i = line.Count; i < existing.Count; i++)
                Object.DestroyImmediate(existing[i].gameObject);
            existing = existing.Take(line.Count).ToList();

            for (int i = 0; i < line.Count; i++)
            {
                var t = existing[i];
                t.position = line[i] + Vector3.up * WaypointHeightAboveRoad;
                t.rotation = Quaternion.LookRotation(Tangent(line, i), Vector3.up);
                t.name = i == 0 ? "AI Waypoint" : "AI Waypoint (" + i + ")";
                // AIWaypoints reads children in sibling order, so loop order must equal hierarchy
                // order. it already does: `existing` is the prefab's own children in sibling order
                // followed by the ones appended above, which is exactly the hierarchy order. calling
                // SetSiblingIndex here would only risk Unity refusing to reorder prefab children
                RecordOverrides(t);
                RecordOverrides(t.gameObject);
            }
        }

        /// <summary>
        /// spreads the checkpoint gates evenly around the loop. returns the racing line index the
        /// start/finish gate sits on, which is where the grid gets built
        /// </summary>
        private static int PlaceCheckpoints(List<Vector3> line)
        {
            var root = InstantiatePrefabAt(TrackBased + "Checkpoints.prefab");
            var gates = ChildrenOf(root);
            if (gates.Count == 0)
                throw new System.InvalidOperationException("Checkpoints prefab has no gates");

            for (int i = 0; i < gates.Count; i++)
            {
                // Checkpoints.Start numbers gates by sibling order, so gate i must sit at the i-th
                // fraction of the loop or lap detection counts them out of sequence
                int lineIndex = Mathf.RoundToInt((float)i / gates.Count * line.Count) % line.Count;
                var t = gates[i];
                t.position = line[lineIndex] + Vector3.up * CheckpointHeightAboveRoad;
                t.rotation = Quaternion.LookRotation(Tangent(line, lineIndex), Vector3.up);
                t.localScale = Vector3.one * CheckpointScale;
                RecordOverrides(t);
            }
            return 0; // gate 0 is the start/finish line
        }

        private static void PlaceSpawning(List<Vector3> line, int startIndex)
        {
            var root = InstantiatePrefabAt(TrackBased + "Spawning.prefab");
            var spawner = root.GetComponentInChildren<PlayersSpawner>();
            if (spawner == null)
                throw new System.InvalidOperationException("Spawning prefab has no PlayersSpawner");

            var points = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("SpawnPoint"))
                .OrderBy(t => NumericSuffix(t.name))
                .ToList();

            var forward = Tangent(line, startIndex);
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var origin = line[startIndex];

            // two staggered columns queued back from the start line, like a real grid
            for (int i = 0; i < points.Count; i++)
            {
                int row = i / 2, col = i % 2;
                var pos = origin
                          - forward * (GridSetback + row * GridRowSpacing + col * GridStagger)
                          + right * (col == 0 ? -GridColumnOffset : GridColumnOffset);
                points[i].position = pos + Vector3.up * WaypointHeightAboveRoad;
                points[i].rotation = Quaternion.LookRotation(forward, Vector3.up);
                RecordOverrides(points[i]);
            }

            // the shipped prefab still carries the pre-rename single aiCarPrefab field, so its
            // aiCarPrefabs list deserialises empty and no bots would spawn. populate it explicitly
            spawner.spawnPoints = points.ToList();
            spawner.aiCarPrefabs = new List<GameObject>
            {
                LoadCar("Player Car 1 (AI Variant)"),
                LoadCar("Player Car 2 (AI Variant)"),
                LoadCar("Player Car 3 (AI Variant)"),
                LoadCar("Player Car 4 (AI Variant)"),
            }.Where(c => c != null).ToList();

            if (spawner.playerPrefab == null)
                spawner.playerPrefab = LoadCar("Player Car 1");
            if (spawner.aiWaypointTrackerPrefab == null)
                spawner.aiWaypointTrackerPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "AI Car Waypoint Tracker.prefab");

            RecordOverrides(spawner);
        }

        private static void PlaceMenuCamera(List<Vector3> line)
        {
            var cam = InstantiatePrefabAt(TrackBased + "Start Menu Camera.prefab");
            // park it just off the start line looking back down the grid
            var forward = Tangent(line, 0);
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            cam.transform.position = line[0] + Vector3.up * 12f - forward * 40f + right * 22f;
            cam.transform.rotation =
                Quaternion.LookRotation((line[0] - cam.transform.position).normalized, Vector3.up);
            RecordOverrides(cam.transform);
        }

        // ---------- jump pads ----------

        private const string JumpPrefabPath = Prefabs + "Jump.prefab";
        private const float CarMass = 600f;
        private const float Gravity = 9.81f;
        /// <summary>140 mph top speed expressed in m/s, the worst case for overshooting a landing</summary>
        private const float TopSpeedMetresPerSecond = 62.6f;
        /// <summary>land this far inside the end of the straight rather than exactly on it</summary>
        private const float LandingSafetyFactor = 0.85f;
        private const float MinStraightLength = 35f;
        private const int MaxPadsPerTrack = 3;

        private static readonly string[] PadScenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        [MenuItem("Tools/Racing/Place Jump Pads On Straights")]
        public static void PlaceJumpPadsMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            PlaceJumpPads();
        }

        /// <summary>
        /// drops jump pads onto the longest straights of each track.
        ///
        /// the road kit runs its track diagonally across a grid, so the direction from one piece
        /// centre to the next swings back and forth even along a visually straight section. what
        /// actually identifies a straight is a run of consecutive road2x pieces sharing a yaw, which
        /// is what this looks for.
        ///
        /// each pad's launch force is then derived from the runway in front of it rather than being a
        /// fixed value, because the longest straight in the kit is only 78m and a car at top speed
        /// crosses that in 1.25s. a pad tuned for feel alone throws the car into the next corner.
        /// </summary>
        public static void PlaceJumpPads()
        {
            var padPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JumpPrefabPath);
            if (padPrefab == null)
            {
                Debug.LogError("[TrackWiring] cannot find " + JumpPrefabPath);
                return;
            }

            foreach (var scenePath in PadScenes)
            {
                try
                {
                    PlaceJumpPadsIn(scenePath, padPrefab);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[TrackWiring] pads for " + scenePath + " failed: " + e);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[TrackWiring] jump pads done");
        }

        private static void PlaceJumpPadsIn(string scenePath, GameObject padPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // replace a previous run's pads rather than refusing to touch the scene, so a fix to the
            // placement rules can actually be applied. only the container this tool creates is
            // cleared: anything hand-placed elsewhere in the scene is left alone and reported
            var previous = GameObject.Find("Jump Pads");
            if (previous != null)
            {
                Object.DestroyImmediate(previous);
                Debug.Log("[TrackWiring] " + scene.name + ": removed the previous Jump Pads container");
            }

            var strays = Object.FindObjectsByType<JumpBoostPad>(FindObjectsSortMode.None).Length;
            if (strays > 0)
            {
                Debug.LogWarning("[TrackWiring] " + scene.name + " has " + strays +
                                 " jump pad(s) outside the Jump Pads container, leaving those in place");
            }

            var ordered = OrderedRoadPieces();
            if (ordered.Count < 4)
            {
                Debug.LogWarning("[TrackWiring] " + scene.name + " has no usable road pieces");
                return;
            }

            var runs = FindStraightRuns(ordered);
            var usable = runs
                .Select(r => new { Run = r, Length = RunLength(ordered, r) })
                .Where(r => r.Length >= MinStraightLength)
                .OrderByDescending(r => r.Length)
                .Take(MaxPadsPerTrack)
                .ToList();

            if (usable.Count == 0)
            {
                Debug.LogWarning("[TrackWiring] " + scene.name + " has no straight at least " +
                                 MinStraightLength + "m long, no pads placed");
                return;
            }

            var root = new GameObject("Jump Pads");
            foreach (var entry in usable)
            {
                var run = entry.Run;
                var start = ordered[run[0]];
                var end = ordered[run[run.Count - 1]];

                var direction = end.position - start.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f) direction = start.forward;
                direction.Normalize();

                var pad = (GameObject)PrefabUtility.InstantiatePrefab(padPrefab);
                pad.transform.SetParent(root.transform, false);
                // sit the pad at the mouth of the straight so the whole run is available as runway
                pad.transform.position = start.position + Vector3.up * 0.1f;
                pad.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                var force = LaunchForceForRunway(entry.Length);
                var padComponent = pad.GetComponent<JumpBoostPad>();
                if (padComponent != null)
                {
                    // m_JumpForce is [SerializeField] private, so it has to be reached this way
                    var so = new SerializedObject(padComponent);
                    var jump = so.FindProperty("m_JumpForce");
                    if (jump != null)
                    {
                        jump.floatValue = force;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(padComponent);
                }
                RecordOverrides(pad.transform);

                var hang = 2f * (force / CarMass) / Gravity;
                Debug.Log(string.Format(
                    "[TrackWiring] {0}: pad on a {1:F0}m straight, force {2:F0}N ({3:F2}s air, {4:F0}m flight at top speed)",
                    scene.name, entry.Length, force, hang, hang * TopSpeedMetresPerSecond));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// solves for the impulse that lands the car inside the runway at top speed.
        /// flight distance d = v_horizontal * 2 * v_up / g, so v_up = d * g / (2 * v_horizontal)
        /// </summary>
        private static float LaunchForceForRunway(float runway)
        {
            var usable = runway * LandingSafetyFactor;
            var launchSpeed = usable * Gravity / (2f * TopSpeedMetresPerSecond);
            return CarMass * launchSpeed;
        }

        private static List<Transform> OrderedRoadPieces()
        {
            var pieces = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => RoadPrefixes.Any(p => t.name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (pieces.Count == 0) return pieces;

            var remaining = new List<Transform>(pieces);
            var start = remaining.OrderBy(t => t.position.x).ThenBy(t => t.position.z).First();
            remaining.Remove(start);

            var ordered = new List<Transform> { start };
            while (remaining.Count > 0)
            {
                var current = ordered[ordered.Count - 1].position;
                var next = remaining.OrderBy(t => SqrXZ(t.position, current)).First();
                remaining.Remove(next);
                ordered.Add(next);
            }
            return ordered;
        }

        private static bool IsStraightPiece(Transform t)
        {
            return t.name.StartsWith("road2x", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameYaw(Transform a, Transform b)
        {
            return Mathf.Abs(Mathf.DeltaAngle(a.eulerAngles.y, b.eulerAngles.y)) < 5f;
        }

        /// <summary>largest gap between two pieces that can still be the same straight, in metres</summary>
        private const float MaxStraightStep = 60f;

        /// <summary>
        /// whether `candidate` genuinely extends the straight built so far.
        ///
        /// sharing a yaw and being neighbours in the ordered chain is not enough. the chain is built
        /// nearest-neighbour, so it can seat two spatially separate straights that happen to share a
        /// heading right next to each other, and grouping on yaw alone then welds them into one long
        /// phantom straight spanning the gap. that is what produced a 117m "straight" on
        /// Race_Track_03 whose pad threw the car 40m clear of the road.
        ///
        /// the test is deliberately convention-free: it compares this step against the previous step
        /// in the same run rather than against a piece's local axes. the road kit models its pieces
        /// with the road along local X, not Z, so anything keyed off transform.forward silently
        /// rejects every real straight.
        /// </summary>
        private static bool ContinuesStraight(List<Transform> ordered, List<int> run, Transform candidate)
        {
            var last = ordered[run[run.Count - 1]];
            if (!SameYaw(last, candidate)) return false;

            var step = candidate.position - last.position;
            step.y = 0f;
            var distance = step.magnitude;
            if (distance < 0.1f || distance > MaxStraightStep) return false;

            // two pieces always define a line, so there is nothing to compare against yet
            if (run.Count < 2) return true;

            var previousStep = last.position - ordered[run[run.Count - 2]].position;
            previousStep.y = 0f;
            if (previousStep.sqrMagnitude < 0.01f) return false;

            return Vector3.Dot(step.normalized, previousStep.normalized) > 0.98f;
        }

        /// <summary>
        /// final guard: every piece in the run must sit on the line from its first to its last, so a
        /// run that slipped through the incremental checks still cannot become a jump ramp
        /// </summary>
        private static bool IsCollinear(List<Transform> ordered, List<int> run)
        {
            if (run.Count < 3) return true;

            var a = ordered[run[0]].position;
            var b = ordered[run[run.Count - 1]].position;
            var axis = b - a;
            axis.y = 0f;
            var length = axis.magnitude;
            if (length < 0.01f) return false;
            axis /= length;

            for (int i = 1; i < run.Count - 1; i++)
            {
                var offset = ordered[run[i]].position - a;
                offset.y = 0f;
                var along = Vector3.Dot(offset, axis);
                var deviation = (offset - axis * along).magnitude;
                if (deviation > 2f) return false;
            }
            return true;
        }

        /// <summary>
        /// groups consecutive straight pieces that share a yaw. the scan deliberately begins at a
        /// corner piece so that a straight spanning the end of the list is not cut in half by the
        /// array boundary
        /// </summary>
        private static List<List<int>> FindStraightRuns(List<Transform> ordered)
        {
            var n = ordered.Count;
            var runs = new List<List<int>>();

            var scanStart = 0;
            for (int i = 0; i < n; i++)
            {
                if (!IsStraightPiece(ordered[i])) { scanStart = i; break; }
            }

            var current = new List<int>();
            for (int step = 0; step < n; step++)
            {
                var index = (scanStart + step) % n;
                var piece = ordered[index];

                if (IsStraightPiece(piece) &&
                    (current.Count == 0 || ContinuesStraight(ordered, current, piece)))
                {
                    current.Add(index);
                }
                else
                {
                    if (current.Count > 0) runs.Add(current);
                    current = IsStraightPiece(piece) ? new List<int> { index } : new List<int>();
                }
            }
            if (current.Count > 0) runs.Add(current);

            return runs.Where(r => IsCollinear(ordered, r)).ToList();
        }

        private static float RunLength(List<Transform> ordered, List<int> run)
        {
            var total = 0f;
            for (int i = 0; i < run.Count - 1; i++)
                total += Vector3.Distance(ordered[run[i]].position, ordered[run[i + 1]].position);
            return total;
        }

        // ---------- helpers ----------

        private static GameObject LoadCar(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Cars + name + ".prefab");
            if (go == null) Debug.LogWarning("[TrackWiring] missing car prefab: " + name);
            return go;
        }

        private static GameObject InstantiatePrefabAt(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new System.InvalidOperationException("prefab not found: " + path);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        private static List<Transform> ChildrenOf(GameObject root)
        {
            var list = new List<Transform>();
            foreach (Transform c in root.transform) list.Add(c);
            return list;
        }

        private static int NumericSuffix(string name)
        {
            var digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }

        /// <summary>
        /// records edits against the object that actually owns them. this has to be the exact object
        /// whose serialised properties changed: passing a GameObject would only capture m_Name, and
        /// the component field edits on PlayersSpawner would be dropped on the next prefab reimport
        /// </summary>
        private static void RecordOverrides(Object target)
        {
            if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static void RegisterScenesInBuildSettings()
        {
            var wanted = new[]
            {
                "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
                "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
                "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
            };
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in wanted)
            {
                var existing = scenes.FirstOrDefault(s => s.path == path);
                if (existing == null)
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                else
                    existing.enabled = true;
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[TrackWiring] build settings now lists " +
                      scenes.Count(s => s.enabled) + " enabled scene(s)");
        }
    }
}
