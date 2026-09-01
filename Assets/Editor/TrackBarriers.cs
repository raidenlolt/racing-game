using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// gives the track its missing physical boundaries.
///
/// the track pack ships the roadside barriers as meshes only: every collider in a track scene is the
/// road surface itself, sitting between y -0.3 and 1.6. so the blue walls beside the road are
/// scenery, a car that runs wide passes straight through them, and with nothing underneath it falls
/// forever. that got much easier to trigger once nitro and the jump pads went in.
///
/// two separate fixes, because they solve different halves of the problem:
///   barriers  - mesh colliders on the wall meshes, so the car is kept on the track in the first place
///   ground    - a collider on the big grass plane, so anything that still gets past a barrier lands
///               on terrain instead of falling out of the world
///
/// the walls get mesh colliders rather than box colliders on purpose. each barrier mesh is a long
/// angled run whose axis-aligned bounds are 84m by 67m, so a box collider fitted to those bounds
/// would seal off most of the racetrack.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class TrackBarriers
    {
        private static readonly string[] TargetScenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        [MenuItem("Tools/Racing/Add Barrier Colliders")]
        public static void AddBarrierColliders()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Run();
        }

        /// <summary>batch entry point for -executeMethod</summary>
        public static void Run()
        {
            foreach (var scenePath in TargetScenes)
            {
                try
                {
                    Process(scenePath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Barriers] " + scenePath + " failed: " + e);
                }
            }
            Debug.Log("[Barriers] done");
        }

        private static void Process(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var removed = RemoveGrassMeshCollider();
            var walls = AddWallColliders();
            var ground = AddGroundCollider();
            if (removed) Debug.Log("[Barriers] " + scene.name + ": removed the unstable mesh collider from Grass");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Barriers] " + scene.name + ": " + walls + " wall collider(s), ground floor " +
                      (ground ? "added" : "already present or not found"));
        }

        /// <summary>
        /// mesh colliders on everything under a Barriers group. non-convex, which is allowed and cheap
        /// for static geometry and gives an exact fit to the wall shape
        /// </summary>
        private static int AddWallColliders()
        {
            var added = 0;
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!IsUnderGroupNamed(filter.transform, "Barriers")) continue;
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;

                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                added++;
            }
            return added;
        }

        /// <summary>
        /// the safety floor. a barrier can still be cleared by a big enough jump, and without ground
        /// under the scenery that means falling out of the world rather than a scrappy off-road moment.
        ///
        /// this is a plain box rather than a mesh collider on the grass plane. that plane is 2379
        /// metres across and made of a handful of enormous triangles, and PhysX warns that colliding
        /// against triangles longer than 500 units is unstable. a box is an exact fit for flat ground,
        /// costs nothing, and does not care how the visual mesh happens to be tessellated.
        ///
        /// the top face is placed at the height of the grass, so a car that leaves the road appears to
        /// run onto the terrain rather than onto something floating above it.
        /// </summary>
        private static bool AddGroundCollider()
        {
            const string floorName = "Safety Floor";
            if (Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name == floorName))
                return false;

            // size it around everything solid in the scene, so it covers the whole circuit
            var bounds = new Bounds();
            var any = false;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return false;

            var grass = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m.gameObject.name == "Grass");
            var topY = grass != null ? grass.GetComponent<Renderer>().bounds.max.y : bounds.min.y;

            var floor = new GameObject(floorName);
            var box = floor.AddComponent<BoxCollider>();
            const float thickness = 20f;
            // generous margin so a car flung sideways off the circuit still lands on it
            var size = new Vector3(bounds.size.x * 1.5f, thickness, bounds.size.z * 1.5f);
            floor.transform.position = new Vector3(bounds.center.x, topY - thickness * 0.5f, bounds.center.z);
            box.size = size;
            return true;
        }

        /// <summary>
        /// undoes an earlier version of this tool, which put a mesh collider straight onto the grass
        /// plane. PhysX warns that its 500m+ triangles make collision unstable, so the box floor
        /// above replaces it
        /// </summary>
        private static bool RemoveGrassMeshCollider()
        {
            var removed = false;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf.gameObject.name != "Grass") continue;
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null) continue;
                Object.DestroyImmediate(mc);
                removed = true;
            }
            return removed;
        }

        private const string WallRoot = "Perimeter Walls";
        private const float WallHeight = 16f;
        private const float WallThickness = 3f;

        /// <summary>
        /// how far the wall is sunk below the line it is built from.
        ///
        /// the walls are positioned off the AI waypoints, and those sit 1.26m above the road surface.
        /// building straight from them left every wall hovering that far off the ground, and a car
        /// nose or wheel could slide into the gap and wedge there, which is exactly the "stuck on the
        /// track" report. sinking them well under the tarmac closes it for good.
        /// </summary>
        private const float WallSink = 4f;
        /// <summary>how far beyond the measured road edge the wall sits</summary>
        private const float WallMargin = 5f;
        /// <summary>
        /// how far each wall runs past its segment, in metres, so corners do not leave a wedge gap.
        /// this is deliberately an absolute pad and not a multiplier: a 1.35x multiplier turned the
        /// 127m straight into a 172m wall whose 45m of overshoot cut straight across the next corner
        /// and sealed the track shut
        /// </summary>
        private const float WallCornerPad = 3f;

        /// <summary>
        /// longest wall segment. the line is resampled to this so walls follow curves instead of
        /// cutting the chord across them, and so no single wall is long enough to overshoot far.
        /// short segments matter most at the hairpin, where a long straight box cannot follow the
        /// bend and ends up lying across the road
        /// </summary>
        private const float MaxWallSegment = 20f;

        /// <summary>
        /// the closest any wall is ever allowed to sit to the racing line.
        ///
        /// this is a backstop against the offset folding in on itself. offsetting a centreline
        /// inwards by more than the corner radius puts the inner wall past the centre of the turn
        /// and out the other side, which is how a wall ends up lying across the track: measured on
        /// Race_Track_01, two walls sat 0.3m and 0.5m from the racing line and pinched the corridor
        /// down to 0.9m, which is where cars and bots were getting stuck.
        /// </summary>
        private const float MinWallClearance = 10f;

        /// <summary>
        /// builds a continuous invisible wall down both sides of the circuit.
        ///
        /// the blue barriers that ship with the track are scenery, not a boundary: probing outwards
        /// from 29 points around Race_Track_01 found an open side at 21 of them, and four points with
        /// nothing on either side. that, not the jump pads, is how a car leaves the track. the pads
        /// only reach a 1.4m apex against barriers 3.5m tall, so they were never the way out.
        ///
        /// the wall follows the road edge measured per point rather than sitting at a fixed offset,
        /// because the drivable width varies from about 19m to 40m and a fixed offset would cut
        /// across the racing line at the wide corners.
        /// </summary>
        [MenuItem("Tools/Racing/Build Perimeter Walls")]
        public static void BuildPerimeterWallsMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var scenePath in TargetScenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var made = BuildPerimeterWalls();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Walls] " + scene.name + ": " + made + " wall segment(s)");
            }
            Debug.Log("[Walls] done");
        }

        private static int BuildPerimeterWalls()
        {
            var existing = GameObject.Find(WallRoot);
            if (existing != null) Object.DestroyImmediate(existing);

            var line = Resample(OrderedWaypoints());
            if (line.Count < 4) { Debug.LogWarning("[Walls] not enough waypoints to build a perimeter"); return 0; }

            // the road is scenery: the track pack ships no collider on any of the 20 road pieces, so
            // the outward probe below had nothing to hit and every wall silently landed at the 8m
            // seed distance instead of at the measured road edge. lend the road colliders for the
            // duration of the build and take them away again, so gameplay physics is untouched.
            var borrowed = LendRoadColliders();
            // the overlap and raycast probes below read the physics scene, which does not see a
            // collider added this same frame until the transforms are pushed through
            Physics.SyncTransforms();
            wallsDropped = 0;
            int made;
            try
            {
                var root = new GameObject(WallRoot);
                made = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    var a = line[i];
                    var b = line[(i + 1) % line.Count];
                    var mid = (a + b) * 0.5f;
                    var fwd = b - a; fwd.y = 0f;
                    var length = fwd.magnitude;
                    if (length < 0.5f) continue;
                    fwd /= length;
                    var right = Vector3.Cross(Vector3.up, fwd);

                    made += Wall(root.transform, mid, fwd, right, length, +1, line);
                    made += Wall(root.transform, mid, fwd, right, length, -1, line);
                }
            }
            finally
            {
                foreach (var c in borrowed) if (c != null) Object.DestroyImmediate(c);
            }
            Debug.Log("[Walls] measured against " + borrowed.Count + " temporary road collider(s); " +
                      wallsDropped + " segment(s) dropped for sitting on the tarmac");
            return made;
        }

        private static int Wall(Transform parent, Vector3 mid, Vector3 fwd, Vector3 right, float length, int side,
                                List<Vector3> line)
        {
            var offset = RoadEdgeDistance(mid, right * side) + WallMargin;
            var position = mid + right * side * offset;

            // backstop: on a corner tighter than the offset, the offset point crosses the centre of
            // the turn and lands on or past the racing line. push any such wall back out to a
            // guaranteed clearance, keeping it on the side it belongs to.
            var clearance = DistanceToLine(position, line);
            if (clearance < MinWallClearance)
            {
                var away = position - ClosestPointOnLine(position, line);
                away.y = 0f;
                // a wall sitting exactly on the line has no side to preserve, so fall back to normal
                if (away.sqrMagnitude < 0.01f) away = right * side;
                position = ClosestPointOnLine(position, line) + away.normalized * MinWallClearance;
                position.y = mid.y;
            }

            var rotation = Quaternion.LookRotation(fwd, Vector3.up);
            var size = new Vector3(WallThickness, WallHeight, length + WallCornerPad * 2f);

            // never leave a wall standing on the racing surface, whatever the probe above measured
            if (!ClearOfRoad(position, rotation, size, right * side, out position))
            {
                wallsDropped++;
                return 0;
            }

            var go = new GameObject(side > 0 ? "Wall R" : "Wall L");
            go.transform.SetParent(parent, false);
            // bottom sits at mid.y - WallSink, i.e. under the tarmac, so there is no gap to wedge into
            go.transform.position = position + Vector3.up * (WallHeight * 0.5f - WallSink);
            go.transform.rotation = rotation;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            return 1;
        }

        /// <summary>
        /// temporarily gives every road piece a mesh collider, returning the ones it created so the
        /// caller can remove them again. only pieces that have no collider already are touched.
        /// </summary>
        private static List<Collider> LendRoadColliders()
        {
            var made = new List<Collider>();
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!filter.gameObject.name.ToLower().StartsWith("road")) continue;
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;

                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = false;
                made.Add(mc);
            }
            return made;
        }

        private static Vector3 ClosestPointOnLine(Vector3 p, List<Vector3> line)
        {
            var best = line[0];
            var bestSqr = float.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 a = line[i], b = line[(i + 1) % line.Count];
                var ab = b - a;
                var lengthSqr = ab.sqrMagnitude;
                var t = lengthSqr < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
                var q = a + ab * t;
                var d = q - p; d.y = 0f;
                if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; best = q; }
            }
            return best;
        }

        private static float DistanceToLine(Vector3 p, List<Vector3> line)
        {
            var d = ClosestPointOnLine(p, line) - p;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>
        /// splits any span longer than MaxWallSegment into equal parts, so the wall follows the track
        /// shape rather than chording across it and no wall is long enough to overshoot into a corner
        /// </summary>
        private static List<Vector3> Resample(List<Vector3> line)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < line.Count; i++)
            {
                var a = line[i];
                var b = line[(i + 1) % line.Count];
                result.Add(a);

                var span = Vector3.Distance(a, b);
                var parts = Mathf.CeilToInt(span / MaxWallSegment);
                for (int k = 1; k < parts; k++)
                    result.Add(Vector3.Lerp(a, b, (float)k / parts));
            }
            return result;
        }

        /// <summary>seed distance used when a probe finds no road at all in a direction</summary>
        private const float MinRoadEdge = 8f;
        /// <summary>metres of missing road tolerated before the scan calls it the edge, so a seam between two tiles is not mistaken for one</summary>
        private const float GapTolerance = 4f;
        private const float RoadProbeUp = 20f;
        private const float RoadProbeDown = 25f;

        /// <summary>
        /// how far the road actually extends in a direction, by stepping outwards and following the
        /// road surface up and down as it goes.
        ///
        /// the previous version probed at one fixed height -- 15 m above the segment midpoint,
        /// looking 35 m down -- and stopped at the very first step that found nothing. that holds on
        /// a flat circuit and fails on a climbing one: twenty metres to the side of a waypoint the
        /// tarmac on Highland and Canyon can sit well outside that window, so the scan ended metres
        /// early and the wall was then built standing on the racing surface. measured before this
        /// change, 108 of Highland's 242 walls and 74 of Canyon's 180 physically intersected the
        /// road mesh, against 38 of 194 on the flat Coastal circuit.
        ///
        /// it now carries the last known road height outwards with it, keeps a wide window either
        /// side of that, and tolerates a short gap before deciding it has found the edge.
        /// </summary>
        private static float RoadEdgeDistance(Vector3 from, Vector3 direction)
        {
            var step = direction;
            step.y = 0f;
            if (step.sqrMagnitude < 0.0001f) return MinRoadEdge;
            step.Normalize();

            var surfaceY = from.y;
            var last = 0f;
            var gap = 0f;

            for (var d = 1f; d <= 60f; d += 1f)
            {
                var probe = from + step * d;
                probe.y = surfaceY;

                float hitY;
                if (RoadSurfaceHeight(probe, out hitY))
                {
                    surfaceY = hitY;
                    last = d;
                    gap = 0f;
                }
                else
                {
                    gap += 1f;
                    if (gap >= GapTolerance) break;
                }
            }
            return Mathf.Max(last, MinRoadEdge);
        }

        /// <summary>
        /// the height of the road surface under or over a point, within a generous window.
        ///
        /// where a circuit stacks over itself the hit nearest the point's own height is the piece
        /// this probe is walking along, not simply the highest one the ray passes through
        /// </summary>
        private static bool RoadSurfaceHeight(Vector3 p, out float y)
        {
            y = 0f;
            var found = false;
            var nearest = float.MaxValue;

            var hits = Physics.RaycastAll(p + Vector3.up * RoadProbeUp, Vector3.down,
                                          RoadProbeUp + RoadProbeDown, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (!h.collider.gameObject.name.ToLower().StartsWith("road")) continue;
                var gap = Mathf.Abs(h.point.y - p.y);
                if (found && gap >= nearest) continue;
                y = h.point.y;
                nearest = gap;
                found = true;
            }
            return found;
        }

        private static bool IsOverRoad(Vector3 p)
        {
            float y;
            return RoadSurfaceHeight(p, out y);
        }

        /// <summary>how far a wall may be nudged outwards to get off the tarmac before it is abandoned</summary>
        private const float MaxRoadPush = 30f;

        /// <summary>walls dropped in the current build because they could not be got off the road</summary>
        private static int wallsDropped;

        /// <summary>
        /// the last word on wall placement: a wall must never stand on the racing surface.
        ///
        /// the offset above is a measurement and measurements can be wrong, so the finished box is
        /// tested against the road colliders themselves and pushed outwards until it is clear. a
        /// wall that cannot be cleared is dropped rather than shipped -- a gap in the perimeter is
        /// caught by the safety floor and by CarRespawn, whereas an invisible wall across the tarmac
        /// is exactly the "stuck in a random collider" the fix exists to remove.
        /// </summary>
        private static bool ClearOfRoad(Vector3 start, Quaternion rotation, Vector3 size, Vector3 outward,
                                        out Vector3 result)
        {
            result = start;

            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = Vector3.right;
            outward.Normalize();

            var half = size * 0.5f;
            for (var push = 0f; push <= MaxRoadPush; push += 1f)
            {
                var candidate = start + outward * push;
                var centre = candidate + Vector3.up * (WallHeight * 0.5f - WallSink);
                if (TouchesRoad(centre, half, rotation)) continue;

                result = candidate;
                return true;
            }
            return false;
        }

        private static bool TouchesRoad(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            foreach (var c in Physics.OverlapBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (c.gameObject.name.ToLower().StartsWith("road")) return true;
            return false;
        }

        /// <summary>the racing line, taken from the AI waypoints in their numbered order</summary>
        private static List<Vector3> OrderedWaypoints()
        {
            var found = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("AI Waypoint") && t.GetComponent<MeshRenderer>() != null)
                .ToList();
            found.Sort((x, y) => NumberIn(x.name).CompareTo(NumberIn(y.name)));
            return found.Select(t => t.position).ToList();
        }

        private static int NumberIn(string s)
        {
            var digits = new string(s.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? int.Parse(digits) : 0;
        }

        private static bool IsUnderGroupNamed(Transform t, string groupName)
        {
            var p = t.parent;
            var depth = 0;
            while (p != null && depth < 8)
            {
                if (p.name.Trim() == groupName) return true;
                p = p.parent;
                depth++;
            }
            return false;
        }
    }
}
