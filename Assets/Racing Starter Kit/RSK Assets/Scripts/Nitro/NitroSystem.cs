using UnityEngine;

namespace SpinMotion
{
    public enum NitroLevel
    {
        None,
        One,
        Two,
        Three
    }

    /// <summary>
    /// the Asphalt-style boost economy for one car. tapping once engages level one, tapping again
    /// while already boosting steps up to two and then three: each level is more powerful but burns
    /// the gauge faster, so holding a long level one run and spending a short level three are both
    /// valid. tapping while the gauge sits in the red zone gives a perfect nitro, which is level
    /// three power at level two drain, so there is a reason to wait rather than fire on sight.
    ///
    /// the same component runs on the player and on the AI cars. only the player raises GameEvents,
    /// so the HUD and camera do not have to filter out fourteen other cars doing the same thing.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class NitroSystem : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;

        [Tooltip("Only the local player's car should have this set. Drives the HUD and camera FX.")]
        public bool isPlayer;

        [Header("Gauge")]
        [Range(0f, 1f)] public float charge;
        [Tooltip("Charge the car starts each race with")]
        [Range(0f, 1f)] public float startingCharge = 0.25f;
        [Tooltip("Minimum charge needed before nitro can be engaged at all")]
        [Range(0f, 1f)] public float minimumChargeToFire = 0.08f;

        [Header("Perfect Nitro")]
        [Tooltip("Gauge window that counts as the red zone. Firing inside it grants level three power at level two drain.")]
        public Vector2 redZone = new Vector2(0.30f, 0.45f);

        [Header("Per level: speed multiplier, torque multiplier, drain per second")]
        public Vector3 levelOne = new Vector3(1.25f, 1.6f, 0.28f);
        public Vector3 levelTwo = new Vector3(1.40f, 2.0f, 0.42f);
        public Vector3 levelThree = new Vector3(1.60f, 2.6f, 0.62f);

        [Header("Feel")]
        [Tooltip("Instant shove along the car's forward axis when a level is engaged, so the boost has a kick rather than only a higher ceiling")]
        public float engageImpulse = 900f;
        [Tooltip("How fast the multipliers ease in and out. Snapping them causes a visible physics jolt.")]
        public float multiplierLerpSpeed = 8f;

        public NitroLevel Level { get; private set; }
        public bool IsActive { get { return Level != NitroLevel.None; } }
        public bool IsInRedZone { get { return charge >= redZone.x && charge <= redZone.y; } }
        /// <summary>true while the current run was started with a perfect nitro tap</summary>
        public bool IsPerfect { get; private set; }

        private CarController car;
        private Rigidbody body;
        private float targetSpeedMultiplier = 1f;
        private float targetTorqueMultiplier = 1f;
        private float lastReportedCharge = -1f;
        private bool lastReportedRedZone;

        private void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();

            charge = startingCharge;

            if (gameEvents != null)
            {
                gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
                gameEvents.RestartRaceEvent.AddListener(OnRaceStarted);
                gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);
                if (isPlayer)
                    gameEvents.OnClickFireNitroEvent.AddListener(Fire);
            }
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.RaceStartedEvent.RemoveListener(OnRaceStarted);
            gameEvents.RestartRaceEvent.RemoveListener(OnRaceStarted);
            gameEvents.RaceFinishedEvent.RemoveListener(OnRaceFinished);
            if (isPlayer)
                gameEvents.OnClickFireNitroEvent.RemoveListener(Fire);
        }

        private void OnRaceStarted()
        {
            charge = startingCharge;
            Disengage();
        }

        private void OnRaceFinished(RaceFinishType raceFinishType)
        {
            Disengage();
        }

        /// <summary>adds to the gauge. called by NitroCharger for drifting, air time and spins</summary>
        public void AddCharge(float amount)
        {
            if (amount <= 0f) return;
            charge = Mathf.Clamp01(charge + amount);
        }

        /// <summary>
        /// one tap. engages the next level up, or a perfect nitro if the gauge is in the red zone
        /// </summary>
        public void Fire()
        {
            if (raceManager != null && raceManager.Item != null && !raceManager.Item.IsRaceInProgress())
                return;
            if (charge < minimumChargeToFire)
                return;
            if (Level == NitroLevel.Three)
                return; // already at the top, further taps do nothing

            // the red zone check has to happen before the level steps up, otherwise the charge the
            // player timed their tap against is not the charge we are judging
            var perfect = !IsActive && IsInRedZone;

            switch (Level)
            {
                case NitroLevel.None: Level = perfect ? NitroLevel.Three : NitroLevel.One; break;
                case NitroLevel.One: Level = NitroLevel.Two; break;
                case NitroLevel.Two: Level = NitroLevel.Three; break;
            }

            if (perfect) IsPerfect = true;

            if (body != null && engageImpulse > 0f)
                body.AddForce(transform.forward * engageImpulse, ForceMode.Impulse);

            if (isPlayer && gameEvents != null)
                gameEvents.PlayerNitroFiredEvent.Invoke(Level, perfect);
        }

        private void Update()
        {
            if (IsActive)
            {
                charge -= CurrentDrainPerSecond() * Time.deltaTime;
                if (charge <= 0f)
                {
                    charge = 0f;
                    Disengage();
                }
            }

            var settings = SettingsFor(Level);
            targetSpeedMultiplier = IsActive ? settings.x : 1f;
            targetTorqueMultiplier = IsActive ? settings.y : 1f;

            // ease rather than snap: dropping top speed instantly at burnout makes CapSpeed yank the
            // rigidbody velocity down in a single frame, which reads as hitting a wall
            car.boostSpeedMultiplier =
                Mathf.Lerp(car.boostSpeedMultiplier, targetSpeedMultiplier, multiplierLerpSpeed * Time.deltaTime);
            car.boostTorqueMultiplier =
                Mathf.Lerp(car.boostTorqueMultiplier, targetTorqueMultiplier, multiplierLerpSpeed * Time.deltaTime);

            ReportChargeIfChanged();
        }

        /// <summary>
        /// a perfect run keeps level three's power but only pays level two's drain, which is the
        /// whole reason to hold the tap until the gauge reaches the red zone
        /// </summary>
        private float CurrentDrainPerSecond()
        {
            if (IsPerfect) return levelTwo.z;
            return SettingsFor(Level).z;
        }

        private Vector3 SettingsFor(NitroLevel level)
        {
            switch (level)
            {
                case NitroLevel.One: return levelOne;
                case NitroLevel.Two: return levelTwo;
                case NitroLevel.Three: return levelThree;
                default: return new Vector3(1f, 1f, 0f);
            }
        }

        private void Disengage()
        {
            var wasActive = IsActive;
            Level = NitroLevel.None;
            IsPerfect = false;
            if (wasActive && isPlayer && gameEvents != null)
                gameEvents.PlayerNitroEndedEvent.Invoke();
        }

        private void ReportChargeIfChanged()
        {
            if (!isPlayer || gameEvents == null) return;
            // the gauge is redrawn from this event, so only fire it on a visible change rather than
            // every frame for every listener
            if (Mathf.Abs(charge - lastReportedCharge) < 0.002f && IsInRedZone == lastReportedRedZone)
                return;
            lastReportedCharge = charge;
            lastReportedRedZone = IsInRedZone;
            gameEvents.PlayerNitroChangedEvent.Invoke(charge, IsInRedZone);
        }
    }
}
