using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the cheap half of the speed sensation: field of view widens with speed and punches out when
    /// nitro fires, plus a shake that grows with velocity. no post-processing, so this costs nothing
    /// on the Mobile quality tier where shadows and AA are already off.
    ///
    /// runs after PlayerCarCameraController, which writes the camera transform every LateUpdate, so
    /// the shake is layered on top of the finished follow position instead of being overwritten.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public class SpeedCameraFX : MonoBehaviour
    {
        public GameEvents gameEvents;

        [Header("Field of view")]
        public float baseFieldOfView = 60f;
        [Tooltip("Extra FOV at the car's top speed")]
        public float speedFieldOfViewGain = 6f;
        [Tooltip("Extra FOV while nitro is running, on top of the speed gain")]
        public float nitroFieldOfViewGain = 8f;
        [Tooltip("Momentary kick the instant a level is engaged")]
        public float nitroPunch = 4f;
        [Tooltip("Hard ceiling on the final field of view. The gains are additive and the punch stacks on top of them, so without a clamp a perfect level three boost at speed reaches past 100 degrees and the view visibly fisheyes.")]
        public float maxFieldOfView = 94f;
        public float fieldOfViewLerpSpeed = 4f;
        public float punchDecaySpeed = 3f;

        [Header("Shake")]
        [Tooltip("Degrees of rotational shake at top speed")]
        public float maxShakeDegrees = 0.6f;
        [Tooltip("Extra shake multiplier while boosting")]
        public float nitroShakeMultiplier = 2.2f;
        public float shakeFrequency = 22f;

        [Header("Impact")]
        [Tooltip("Degrees of camera kick per m/s of impact speed")]
        public float hitKickPerMetrePerSecond = 0.9f;
        [Tooltip("Ceiling on the kick, so a 20 m/s shunt does not throw the view into the sky")]
        public float maxHitKick = 7f;
        [Tooltip("Extra shake, in degrees, added on a hit and decayed over the burst")]
        public float hitShakePerMetrePerSecond = 0.25f;
        public float hitDecaySpeed = 6f;

        [Header("Post processing (Desktop tier only)")]
        [Tooltip("Object holding the heavier URP Volume. Left disabled on low quality levels.")]
        public GameObject postProcessingRoot;
        [Tooltip("Quality level index at or above which the post-processing root is enabled")]
        public int minimumQualityLevelForPost = 1;

        private Camera cam;
        private PlayerCarCameraController follow;
        private CarController car;
        private NitroSystem nitro;
        private float punch;
        private float shakeSeed;
        private Vector2 hitKick;       // pitch, yaw
        private float hitShake;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            follow = GetComponent<PlayerCarCameraController>();
            shakeSeed = Random.value * 100f;

            if (gameEvents != null)
            {
                gameEvents.PlayerNitroFiredEvent.AddListener(OnNitroFired);
                gameEvents.PlayerHitEvent.AddListener(OnPlayerHit);
            }
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.PlayerNitroFiredEvent.RemoveListener(OnNitroFired);
            gameEvents.PlayerHitEvent.RemoveListener(OnPlayerHit);
        }

        /// <summary>
        /// a hit from behind pitches the view down and forward, the way a head snaps in a rear-end
        /// shunt; a side hit yaws it away from the impact. both recover over the decay
        /// </summary>
        private void OnPlayerHit(float impactSpeed, Vector3 localDirection)
        {
            var kick = Mathf.Min(impactSpeed * hitKickPerMetrePerSecond, maxHitKick);
            var fromBehind = localDirection.z < -0.3f;
            var fromFront = localDirection.z > 0.3f;
            var pitch = fromBehind ? -kick : (fromFront ? kick * 0.6f : 0f);
            var yaw = Mathf.Abs(localDirection.x) > 0.3f ? -Mathf.Sign(localDirection.x) * kick * 0.7f : 0f;
            hitKick = new Vector2(pitch, yaw);
            hitShake = Mathf.Max(hitShake, impactSpeed * hitShakePerMetrePerSecond);
        }

        private void Start()
        {
            ResolveCar();
            ApplyQualityGate();
        }

        /// <summary>
        /// the player car is spawned at race start, so the reference cannot be resolved in Awake.
        /// PlayerCarCameraController already holds it once the spawner has wired the cameras up
        /// </summary>
        private void ResolveCar()
        {
            if (follow == null || follow.playerCar == null) return;
            car = follow.playerCar.GetComponent<CarController>();
            nitro = follow.playerCar.GetComponent<NitroSystem>();
        }

        private void ApplyQualityGate()
        {
            if (postProcessingRoot == null) return;
            // the Mobile tier is quality level 0 in this project and runs with no shadows or AA, so
            // the heavier volume is reserved for Desktop rather than shipped to phones
            postProcessingRoot.SetActive(QualitySettings.GetQualityLevel() >= minimumQualityLevelForPost);
        }

        private void OnNitroFired(NitroLevel level, bool perfect)
        {
            // a perfect nitro should feel like the best thing that can happen, so it kicks hardest
            punch = nitroPunch * (perfect ? 1.6f : 1f) * LevelWeight(level);
        }

        private static float LevelWeight(NitroLevel level)
        {
            switch (level)
            {
                case NitroLevel.One: return 0.7f;
                case NitroLevel.Two: return 1f;
                case NitroLevel.Three: return 1.3f;
                default: return 0f;
            }
        }

        private void LateUpdate()
        {
            if (car == null)
            {
                ResolveCar();
                if (car == null) return;
            }

            var speedFraction = car.MaxSpeed > 1f ? Mathf.Clamp01(car.CurrentSpeed / car.MaxSpeed) : 0f;
            var boosting = nitro != null && nitro.IsActive;

            var targetFov = baseFieldOfView
                            + speedFieldOfViewGain * speedFraction
                            + (boosting ? nitroFieldOfViewGain : 0f);

            punch = Mathf.Lerp(punch, 0f, punchDecaySpeed * Time.deltaTime);
            var fov = Mathf.Lerp(cam.fieldOfView, targetFov, fieldOfViewLerpSpeed * Time.deltaTime) + punch;
            // clamp last, after the punch is added, or the transient kick still spikes past the ceiling
            cam.fieldOfView = Mathf.Min(fov, maxFieldOfView);

            ApplyShake(speedFraction, boosting);
            ApplyHitKick();
        }

        private void ApplyHitKick()
        {
            if (hitKick.sqrMagnitude < 0.0001f) return;
            transform.rotation *= Quaternion.Euler(hitKick.x, hitKick.y, 0f);
            // unscaled, so the kick still settles during the finish sequence's slow-motion beat
            hitKick = Vector2.Lerp(hitKick, Vector2.zero, hitDecaySpeed * Time.unscaledDeltaTime);
        }

        private void ApplyShake(float speedFraction, bool boosting)
        {
            var amount = maxShakeDegrees * speedFraction * (boosting ? nitroShakeMultiplier : 1f);
            amount += hitShake;
            hitShake = Mathf.Lerp(hitShake, 0f, hitDecaySpeed * Time.unscaledDeltaTime);
            if (amount <= 0.001f) return;

            // perlin rather than Random so the shake is smooth instead of a per-frame jitter
            var t = Time.time * shakeFrequency;
            var pitch = (Mathf.PerlinNoise(t, shakeSeed) * 2f - 1f) * amount;
            var yaw = (Mathf.PerlinNoise(shakeSeed, t) * 2f - 1f) * amount;

            transform.rotation *= Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
