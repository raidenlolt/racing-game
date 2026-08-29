using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// keeps the AI pack around the player so races stay close, following the standard rubber-banding
    /// curve: a dead zone either side of the player where nothing is applied, a linear ramp out from
    /// there, and a clamp at the extremes.
    ///
    /// the important part is the order the two knobs are used in. the speed band is applied first: it
    /// only changes the speed the AI aims for and is capped at, so a banded car is still the same car
    /// driving harder or softer, which is invisible to the player. only once that knob is at its limit
    /// does the torque band open up and actually hand the car more power, which is the part a player
    /// can notice and resent. keeping the dead zone wide enough that neither is active in a
    /// wheel-to-wheel fight is what stops the whole thing feeling like a cheat.
    ///
    /// banding is suppressed for the first seconds of a race: pulling the field back towards the
    /// player before turn one bunches everyone into a pile-up instead of stringing them out.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AIRubberBanding : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;
        public RealTimeRacePositionsItem realTimeRacePositions;

        [Header("Curve")]
        [Tooltip("No banding at all within this distance of the player, in metres")]
        public float deadZone = 45f;
        [Tooltip("Distance at which banding reaches its maximum, in metres")]
        public float maxDistance = 260f;

        // the hold-back side is deliberately much gentler than the catch-up side. throttling a leader
        // hard is what made the front runner feel rubbery and slow: measured mid-race, every bot ahead
        // was pinned at 88% speed and 80% torque, so nobody could ever build a lead worth chasing.
        // barely slowing leaders while still helping stragglers keeps the pack close without making
        // the car in front look like it is waiting for you
        [Header("Speed band (applied first, effectively driver effort)")]
        [Tooltip("Multiplier when maximally ahead of the player. Keep close to 1 so a leader can actually pull away.")]
        public float speedBandMin = 0.97f;
        [Tooltip("Multiplier when maximally behind the player, i.e. pushing on")]
        public float speedBandMax = 1.14f;

        [Header("Torque band (only opens once the speed band is maxed out)")]
        public float torqueBandMin = 0.94f;
        public float torqueBandMax = 1.35f;
        [Tooltip("Share of the ramp spent on the speed band before torque starts moving. 1 = never use torque.")]
        [Range(0.1f, 1f)] public float speedBandShare = 0.6f;

        [Header("Safeguards")]
        [Tooltip("Seconds after the start during which banding is suppressed so the field can string out")]
        public float suppressAfterStartSeconds = 6f;
        [Tooltip("Never reduce power below this speed (mph), or a banded-back car cannot climb hills or pull away")]
        public float minimumSpeedForNegativeBand = 18f;
        public float bandLerpSpeed = 1.5f;

        public float CurrentSignedDistance { get; private set; }
        public float CurrentSpeedBand { get; private set; }
        public float CurrentTorqueBand { get; private set; }

        private CarController car;
        private CheckpointTracker tracker;
        private float raceStartedTime = -999f;

        private void Awake()
        {
            car = GetComponent<CarController>();
            tracker = GetComponentInChildren<CheckpointTracker>();
            CurrentSpeedBand = 1f;
            CurrentTorqueBand = 1f;

            if (gameEvents != null)
            {
                gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
                gameEvents.RestartRaceEvent.AddListener(OnRaceStarted);
            }
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.RaceStartedEvent.RemoveListener(OnRaceStarted);
            gameEvents.RestartRaceEvent.RemoveListener(OnRaceStarted);
        }

        private void OnRaceStarted()
        {
            raceStartedTime = Time.time;
        }

        private void Update()
        {
            var targetSpeedBand = 1f;
            var targetTorqueBand = 1f;

            if (ShouldBand())
            {
                CurrentSignedDistance = SignedDistanceFromPlayer();
                ComputeBands(CurrentSignedDistance, out targetSpeedBand, out targetTorqueBand);
            }

            CurrentSpeedBand = Mathf.Lerp(CurrentSpeedBand, targetSpeedBand, bandLerpSpeed * Time.deltaTime);
            CurrentTorqueBand = Mathf.Lerp(CurrentTorqueBand, targetTorqueBand, bandLerpSpeed * Time.deltaTime);

            car.bandingSpeedMultiplier = CurrentSpeedBand;
            car.bandingTorqueMultiplier = CurrentTorqueBand;
        }

        private bool ShouldBand()
        {
            if (raceManager == null || raceManager.Item == null || !raceManager.Item.IsRaceInProgress())
                return false;
            if (Time.time - raceStartedTime < suppressAfterStartSeconds)
                return false;
            if (realTimeRacePositions == null || realTimeRacePositions.Item == null)
                return false;
            if (tracker == null)
                return false;

            var scores = realTimeRacePositions.Item.RacePositionTotalScores;
            var index = tracker.GetCarRacePositionIndex();
            // index 0 is the human player, who is the reference and never banded
            return index > 0 && index < scores.Count && scores.Count > 0;
        }

        /// <summary>
        /// positive when this car is ahead of the player, negative when behind. the sign comes from
        /// the race score, which already accounts for laps and checkpoints, while the magnitude is
        /// plain world distance because that is what the banding curve is tuned against
        /// </summary>
        private float SignedDistanceFromPlayer()
        {
            var positions = realTimeRacePositions.Item;
            var index = tracker.GetCarRacePositionIndex();

            var playerTracker = positions.CarCheckpointTrackers.Count > 0
                ? positions.CarCheckpointTrackers[0]
                : null;
            if (playerTracker == null) return 0f;

            var distance = Vector3.Distance(transform.position, playerTracker.transform.position);
            var ahead = positions.RacePositionTotalScores[index] > positions.RacePositionTotalScores[0];
            return ahead ? distance : -distance;
        }

        private void ComputeBands(float signedDistance, out float speedBand, out float torqueBand)
        {
            speedBand = 1f;
            torqueBand = 1f;

            var magnitude = Mathf.Abs(signedDistance);
            if (magnitude <= deadZone) return;

            // 0 at the edge of the dead zone, 1 at max distance and beyond
            var t = Mathf.Clamp01((magnitude - deadZone) / Mathf.Max(1f, maxDistance - deadZone));
            var ahead = signedDistance > 0f;

            // the first share of the ramp moves the speed band; the remainder moves torque, and only
            // after speed has bottomed or topped out
            var speedT = Mathf.Clamp01(t / speedBandShare);
            var torqueT = speedBandShare >= 1f
                ? 0f
                : Mathf.Clamp01((t - speedBandShare) / (1f - speedBandShare));

            if (ahead)
            {
                speedBand = Mathf.Lerp(1f, speedBandMin, speedT);
                torqueBand = Mathf.Lerp(1f, torqueBandMin, torqueT);

                // a car that has been powered down and is now crawling cannot recover, so stop
                // taking power away once it is already slow
                if (car.CurrentSpeed < minimumSpeedForNegativeBand)
                {
                    speedBand = 1f;
                    torqueBand = 1f;
                }
            }
            else
            {
                speedBand = Mathf.Lerp(1f, speedBandMax, speedT);
                torqueBand = Mathf.Lerp(1f, torqueBandMax, torqueT);
            }
        }
    }
}
