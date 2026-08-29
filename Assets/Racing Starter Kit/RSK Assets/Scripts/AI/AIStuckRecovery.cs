using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// gets a bot moving again when it has hung up on something.
    ///
    /// the kit already had an unstick inside AICarAvoidanceBehaviour, but it only fires below
    /// 0.2 m/s, which is 0.4 mph. a car grinding along a wall still rolls at five to ten mph, so it
    /// sat far above that threshold and the unstick never triggered. this watches a realistic speed
    /// over a sustained window instead of waiting for a dead stop.
    ///
    /// recovery escalates rather than repeating one trick:
    ///   nudge    - reverse while steering towards whichever side has more open space
    ///   respawn  - if that has not worked after several seconds, put the car back on the racing line
    ///
    /// steering away matters. the original unstick locked steering to zero and reversed in a straight
    /// line, which backs a wedged car straight into whatever it was already touching.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AIStuckRecovery : MonoBehaviour
    {
        public RaceManagerItem raceManager;

        [Header("Detection")]
        [Tooltip("Below this speed (mph) the car counts as not making progress")]
        public float stuckSpeed = 12f;
        [Tooltip("Seconds of no progress before the first recovery attempt")]
        public float secondsBeforeNudge = 2.5f;
        [Tooltip("Seconds of no progress before giving up and respawning on the racing line")]
        public float secondsBeforeRespawn = 7f;

        [Header("Nudge")]
        [Tooltip("How long each reverse-and-turn attempt lasts")]
        public float nudgeDuration = 1.2f;
        [Tooltip("How far to probe each side when deciding which way to turn out")]
        public float probeDistance = 25f;

        public bool IsRecovering { get; private set; }
        public int NudgeCount { get; private set; }
        public int RespawnCount { get; private set; }

        private CarController car;
        private CarRespawn respawn;
        /// <summary>
        /// when the car first dropped below the speed threshold, as wall-clock time.
        ///
        /// this is a timestamp rather than an accumulator on purpose. accumulating only while not
        /// nudging meant the counter froze during every nudge and, because it was never reset after
        /// one, each nudge immediately qualified for the next. a car observed doing this ran 17
        /// nudges in 16 seconds and never once reached the respawn threshold.
        /// </summary>
        private float stuckSince = -1f;
        private float nudgeUntil;
        private int nudgeDirection = 1;
        private float normalTorque;
        private float normalSteer;
        private bool borrowed;

        private void Awake()
        {
            car = GetComponent<CarController>();
            respawn = GetComponent<CarRespawn>();
            normalTorque = car.m_FullTorqueOverAllWheels;
            normalSteer = car.m_MaximumSteerAngle;
        }

        private void OnDisable()
        {
            Restore();
        }

        private void Update()
        {
            if (raceManager == null || raceManager.Item == null || !raceManager.Item.IsRaceInProgress())
            {
                stuckSince = -1f;
                Restore();
                return;
            }

            // moving again: clear everything, including any nudge still in flight
            if (car.CurrentSpeed >= stuckSpeed)
            {
                stuckSince = -1f;
                IsRecovering = false;
                if (borrowed) Restore();
                return;
            }

            if (stuckSince < 0f) stuckSince = Time.time;
            var stuckDuration = Time.time - stuckSince;

            // the escalation is checked BEFORE the nudge-in-flight guard, so a car that keeps failing
            // to free itself still reaches the respawn instead of nudging forever
            if (stuckDuration >= secondsBeforeRespawn)
            {
                Restore();
                if (respawn != null)
                {
                    respawn.ForceRespawn();
                    RespawnCount++;
                }
                stuckSince = -1f;
                IsRecovering = false;
                return;
            }

            if (Time.time < nudgeUntil) return;   // current nudge still running
            if (borrowed) Restore();              // it just ended, hand the car back

            if (stuckDuration < secondsBeforeNudge) return;

            IsRecovering = true;
            BeginNudge();
        }

        /// <summary>reverse out, turning towards whichever side has more room</summary>
        private void BeginNudge()
        {
            nudgeDirection = MoreOpenSide();

            // borrow the same two fields AICarAvoidanceBehaviour borrows, and restore them the moment
            // the nudge ends, so the two systems cannot leave the car permanently altered
            normalTorque = Mathf.Abs(car.m_FullTorqueOverAllWheels);
            normalSteer = Mathf.Abs(car.m_MaximumSteerAngle) > 0.01f ? Mathf.Abs(car.m_MaximumSteerAngle) : normalSteer;

            car.m_FullTorqueOverAllWheels = -normalTorque;   // reverse
            car.forceSteering = nudgeDirection;              // and turn out while doing it
            car.forceSteeringFactor = 0.9f;
            borrowed = true;

            nudgeUntil = Time.time + nudgeDuration;
            NudgeCount++;
        }

        private void Restore()
        {
            if (!borrowed) return;
            car.m_FullTorqueOverAllWheels = normalTorque;
            car.m_MaximumSteerAngle = normalSteer;
            car.forceSteering = 0;
            borrowed = false;
        }

        /// <summary>which way out has more space, +1 for the car's right, -1 for its left</summary>
        private int MoreOpenSide()
        {
            var origin = transform.position + Vector3.up * 1.2f;
            var left = Clearance(origin, -transform.right);
            var right = Clearance(origin, transform.right);
            return right >= left ? 1 : -1;
        }

        private float Clearance(Vector3 origin, Vector3 direction)
        {
            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, probeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                // ignore our own bodywork
                if (hit.collider.transform.root == transform.root)
                    return probeDistance;
                return hit.distance;
            }
            return probeDistance;
        }
    }
}
