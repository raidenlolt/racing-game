using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// puts a car back on the track when it leaves it, the way Asphalt does: stray off the circuit
    /// and after a moment you are simply placed back on the racing line, pointing the right way.
    ///
    /// the track pack ships no boundary colliders at all: the blue barriers beside the road are
    /// meshes with nothing solid on them, and the only thing any car actually rests on is a single
    /// flat safety floor spanning the whole level. tarmac and grass are the same collider, so being
    /// on the road cannot be tested by looking down -- there is no road down there to find.
    ///
    /// what does exist is the AI waypoint loop, which traces the racing line. so off-track is
    /// measured as distance from that line. measured against the scene: a car on the road sits
    /// within 2.4 m of it, and the barrier walls lining the road sit between 6.4 m and 13.0 m from
    /// it, so the threshold below clears the widest part of the circuit with room to spare.
    ///
    /// distance is to the line SEGMENTS rather than to the waypoints themselves. waypoints here are
    /// 39-136 m apart, so a car perfectly on the racing line can still be 68 m from the nearest
    /// waypoint; point distance would either fire constantly or need a threshold so large it never
    /// fires at all.
    ///
    /// this runs on the bots too, since an AI that falls off is gone for the whole race and would
    /// otherwise sit at the bottom of the world dragging its rubber-band partner around.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarRespawn : MonoBehaviour
    {
        public AIWaypointSet aiWaypointSet;
        public RaceManagerItem raceManager;

        [Header("Triggers")]
        [Tooltip("Falling below this height counts as off the map. The road surface sits near y=0.")]
        public float fallHeight = -15f;
        [Tooltip("Name prefix identifying a drivable surface. The track kit names every road piece road2x / road90.")]
        public string roadNamePrefix = "road";
        [Tooltip("Fallback only, for tracks whose road has no collider: metres from the racing line that count as off the circuit. Measured on Race_Track_01, legitimate tarmac reaches 24.4 m from the line on the wide corners, so this must stay well clear of that or a car running the outside of a corner is reset while still on the road.")]
        public float maxDistanceFromLine = 32f;
        [Tooltip("Seconds spent off the circuit before the car is put back. Long enough to survive a clip over a barrier or a moment in the air, short enough that you cannot drive a shortcut across the grass.")]
        public float secondsOffRoadBeforeReset = 2.5f;

        [Header("Wedged against a barrier")]
        [Tooltip("Below this speed in mph the car counts as not moving.")]
        public float stuckSpeed = 8f;
        [Tooltip("Seconds wedged against a wall before the car is put back on the line. Long enough that reversing out under your own power is never interrupted.")]
        public float secondsStuckBeforeReset = 4f;

        // the reported case: the car beached on the verge just outside the trackside barrier, stopped
        // dead, and never came back. the road kit's collider is one slab that runs under the verge
        // and the barrier, so the surface test still said "road", and nothing solid was touching the
        // car so the wedged test never fired. two signals that do not depend on the geometry:
        [Header("Going nowhere")]
        [Tooltip("Seconds of holding the throttle without moving before the car is put back. The player is asking to go and cannot; nothing else needs to be known.")]
        public float secondsThrottledStuckBeforeReset = 3f;
        [Tooltip("Seconds stopped, throttle or not, before a car this far from the racing line is put back. Idling in the driving lane is left alone.")]
        public float secondsIdleOffLineBeforeReset = 6f;
        [Tooltip("Metres from the racing line beyond which a stopped car counts as off the lane rather than idling")]
        public float idleResetDistanceFromLine = 9f;

        [Header("Placement")]
        [Tooltip("How far above the road the car is placed, so it drops onto the surface instead of through it")]
        public float respawnHeight = 2f;
        [Tooltip("Minimum seconds between respawns, so a bad landing cannot loop")]
        public float cooldown = 2f;

        public int RespawnCount { get; private set; }

        private Rigidbody body;
        private CarController car;
        private float lastRespawnTime = -999f;
        /// <summary>when the car first stood still with the throttle held, or -1</summary>
        private float throttledStuckSince = -1f;
        /// <summary>when the car was last on the circuit, as wall-clock time</summary>
        private float lastOnTrackTime;
        /// <summary>whether this scene's road is solid enough to test against directly</summary>
        private bool roadIsSolid;
        /// <summary>when the car was last moving under its own power, as wall-clock time</summary>
        private float lastMovingTime;
        /// <summary>bots run AIStuckRecovery, which tries reversing out before resorting to a reset</summary>
        private bool hasOwnStuckRecovery;
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];
        private static readonly Collider[] Nearby = new Collider[24];

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            car = GetComponent<CarController>();
            lastOnTrackTime = Time.time;
            lastMovingTime = Time.time;
            roadIsSolid = SceneHasRoadColliders();
            hasOwnStuckRecovery = GetComponent<AIStuckRecovery>() != null;
        }

        /// <summary>
        /// whether the road in this scene has colliders at all.
        ///
        /// this decides which off-track test is used, and it has to be asked rather than assumed:
        /// Race_Track_01 has a collider on all 20 of its road pieces, Race_Track_03 has none and its
        /// cars rest on the flat safety floor instead. running the surface test on a track with no
        /// road collider reports every car as off-road on every frame and resets the entire field on
        /// a loop, which is exactly what happened the first time this was tried.
        /// </summary>
        private bool SceneHasRoadColliders()
        {
            foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (!c.isTrigger && c.gameObject.name.StartsWith(roadNamePrefix,
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void FixedUpdate()
        {
            // outside a running race, and during the cooldown after a reset, the car is left alone
            // and the off-track clock is held at zero rather than quietly running in the background
            if (raceManager == null || raceManager.Item == null || !raceManager.Item.IsRaceInProgress()
                || Time.time - lastRespawnTime < cooldown)
            {
                lastOnTrackTime = Time.time;
                lastMovingTime = Time.time;
                throttledStuckSince = -1f;
                return;
            }
            if (aiWaypointSet == null || aiWaypointSet.Items.Count == 0)
                return;

            Vector3 onLine, along;
            var fromLine = DistanceFromRacingLine(transform.position, out onLine, out along);
            if (IsOnTrack(fromLine, onLine))
                lastOnTrackTime = Time.time;

            var mph = body.linearVelocity.magnitude * 2.23693629f;
            if (mph > stuckSpeed) lastMovingTime = Time.time;

            var throttled = car != null && car.AccelInput > 0.5f;
            if (throttled && mph < stuckSpeed)
            {
                if (throttledStuckSince < 0f) throttledStuckSince = Time.time;
            }
            else
            {
                throttledStuckSince = -1f;
            }

            if (transform.position.y < fallHeight ||                              // fell out of the world
                Time.time - lastOnTrackTime >= secondsOffRoadBeforeReset ||       // off the circuit
                IsWedgedAgainstBarrier() ||                                       // nose into a wall
                IsGoingNowhere(fromLine))                                         // beached, throttle or not
            {
                PlaceOnLine(onLine, along);
            }
        }

        /// <summary>
        /// stopped and not getting anywhere, without needing to know what stopped the car.
        /// bots are excluded: AIStuckRecovery reverses them out first and calls ForceRespawn itself
        /// </summary>
        private bool IsGoingNowhere(float distanceFromLine)
        {
            if (hasOwnStuckRecovery) return false;
            if (throttledStuckSince >= 0f && Time.time - throttledStuckSince >= secondsThrottledStuckBeforeReset)
                return true;
            return Time.time - lastMovingTime >= secondsIdleOffLineBeforeReset
                   && distanceFromLine > idleResetDistanceFromLine;
        }

        /// <summary>
        /// whether the car has come to rest against a wall and is not getting off it.
        ///
        /// hitting a barrier head on leaves the car stopped and pointing into it, and unlike a bot
        /// the player has no recovery driver to reverse them out -- the reported case was driving
        /// straight into a corner barrier and simply staying there. it is deliberately not enough to
        /// be stationary: a car stopped in the middle of the road is idling, not stuck, and teleporting
        /// someone who has just paused would be worse than leaving them. contact with a wall is what
        /// separates the two.
        ///
        /// bots are excluded because AIStuckRecovery already handles them, and it tries reversing
        /// first, which costs them far less track position than a reset.
        /// </summary>
        private bool IsWedgedAgainstBarrier()
        {
            if (hasOwnStuckRecovery) return false;
            if (Time.time - lastMovingTime < secondsStuckBeforeReset) return false;

            // only asked once the car has already been still for several seconds, so the cost of the
            // overlap test never lands on a normal frame
            var half = new Vector3(4f, 3f, 7f);
            var count = Physics.OverlapBoxNonAlloc(transform.position, half, Nearby, transform.rotation,
                                                   ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = Nearby[i];
                if (c == null || c.transform.root == transform.root) continue;
                if (IsBarrier(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// the two things a car can be pinned against: the invisible perimeter walls built along the
        /// road edge, and the track kit's own roadside barrier meshes
        /// </summary>
        private static bool IsBarrier(Collider c)
        {
            if (c.gameObject.name.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase))
                return true;

            var parent = c.transform.parent;
            for (int depth = 0; parent != null && depth < 4; depth++, parent = parent.parent)
                if (parent.name.Trim() == "Barriers") return true;

            return false;
        }

        /// <summary>
        /// whether the car counts as still on the circuit.
        ///
        /// where the road is solid this asks the road itself, which is exact and needs no threshold.
        /// where it is not, distance from the racing line is the only signal available, and the
        /// threshold has to clear the widest legitimate tarmac -- 24.4 m on Race_Track_01 -- rather
        /// than the road's nominal width.
        /// </summary>
        private bool IsOnTrack(float distanceFromLine, Vector3 onLine)
        {
            // outside the perimeter walls is off the circuit whatever surface is underneath. a car
            // that has been launched over a wall lands on the safety floor, which the road test
            // already rejects, but on some tracks the road slab itself continues past the wall
            if (IsBeyondPerimeterWall(onLine)) return false;

            if (!roadIsSolid)
                return distanceFromLine <= maxDistanceFromLine;

            // the ray starts above the roof so it still finds the road while the car is airborne
            var origin = transform.position + Vector3.up * 4f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, 40f, ~0,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.root == transform.root) continue;   // our own bodywork
                if (hit.collider.gameObject.name.StartsWith(roadNamePrefix,
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// whether one of the invisible perimeter walls stands between the racing line and the car.
        /// the walls are the only colliders named Wall*, and they run along both edges of the road
        /// on every track that has them, so a wall between the line and the car means the car is on
        /// the far side of the road edge
        /// </summary>
        private bool IsBeyondPerimeterWall(Vector3 onLine)
        {
            var from = onLine + Vector3.up * 1.5f;
            var to = transform.position + Vector3.up * 1.5f;
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance < 1f) return false;

            var count = Physics.RaycastNonAlloc(from, delta / distance, GroundHits, distance, ~0,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.gameObject.name.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// shortest distance from a point to the racing line, ignoring height, along with the point
        /// on the line it maps to and the direction the line runs there.
        ///
        /// height is dropped from the comparison because a car in the air over the track is still on
        /// the track, and the waypoints sit 1.6 m up regardless.
        /// </summary>
        /// <summary>
        /// public face of the racing-line query for other components on the car. WallSlide uses it to
        /// learn which way the wall runs, since every wall and railing follows the road
        /// </summary>
        public bool TryGetRacingLine(Vector3 position, out Vector3 closest, out Vector3 forward)
        {
            closest = position;
            forward = transform.forward;
            if (aiWaypointSet == null || aiWaypointSet.Items.Count < 2) return false;
            DistanceFromRacingLine(position, out closest, out forward);
            return true;
        }

        private float DistanceFromRacingLine(Vector3 position, out Vector3 closest, out Vector3 forward)
        {
            var items = aiWaypointSet.Items;
            var best = float.MaxValue;
            closest = position;
            forward = transform.forward;

            for (int i = 0; i < items.Count; i++)
            {
                var aT = items[i].aiWaypointTransform;
                var bT = items[(i + 1) % items.Count].aiWaypointTransform;
                if (aT == null || bT == null) continue;

                Vector3 a = aT.position, b = bT.position;
                var ab = b - a;
                var lengthSqr = ab.sqrMagnitude;
                // project onto the segment and clamp, so corners are measured to the bend itself
                // rather than to whichever waypoint happens to be nearer
                var t = lengthSqr < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(position - a, ab) / lengthSqr);
                var point = a + ab * t;

                var flat = point - position;
                flat.y = 0f;
                var distance = flat.magnitude;
                if (distance < best)
                {
                    best = distance;
                    closest = point;
                    forward = ab.sqrMagnitude > 0.001f ? ab : forward;
                }
            }
            return best;
        }

        /// <summary>
        /// puts the car back on the racing line on demand. AIStuckRecovery calls this as its last
        /// resort, once reversing has failed to free a bot that is wedged against something
        /// </summary>
        public void ForceRespawn()
        {
            Vector3 onLine, along;
            DistanceFromRacingLine(transform.position, out onLine, out along);
            PlaceOnLine(onLine, along);
        }

        /// <summary>
        /// drops the car onto the racing line, facing the way the track runs.
        ///
        /// placement is at the projected point rather than at a waypoint, so you rejoin roughly
        /// where you went off instead of being thrown up to 68 m up or down the circuit.
        /// </summary>
        private void PlaceOnLine(Vector3 onLine, Vector3 along)
        {
            along.y = 0f;
            if (along.sqrMagnitude < 0.01f) along = Vector3.forward;

            body.position = onLine + Vector3.up * respawnHeight;
            body.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            // the transform is set as well as the rigidbody, so the camera and any LateUpdate reading
            // transform.position this frame do not see the old position for a frame
            transform.SetPositionAndRotation(body.position, body.rotation);

            lastRespawnTime = Time.time;
            lastOnTrackTime = Time.time;
            lastMovingTime = Time.time;
            throttledStuckSince = -1f;
            RespawnCount++;

            var slide = GetComponent<WallSlide>();
            if (slide != null) slide.Release();
        }
    }
}
