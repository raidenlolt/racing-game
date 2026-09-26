using System.Collections;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// makes a collision between cars visible. before this there was nothing: both cars weigh 600 kg
    /// and the grip assist bleeds sideways velocity off every step, so a bot tapping the player's
    /// bumper produced a small forward shove and no other sign that anything had happened.
    ///
    /// on every car: sparks at the contact point, an impact sound pitched by severity, and a
    /// physical jolt (an extra shove along the contact and a short yaw and pitch torque) so the hit
    /// car visibly rocks. the anti-spin assist settles it within half a second.
    ///
    /// on the player's car only: a PlayerHitEvent carrying impact speed and direction, which the
    /// camera answers with a kick and the HUD with a red edge flash, plus a short hit-stop and a
    /// phone buzz on hard hits.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarImpactFX : MonoBehaviour
    {
        public GameEvents gameEvents;

        [Header("Detection")]
        [Tooltip("Relative speed along the contact (m/s) below which a touch is ignored. 3 m/s is a firm bumper tap.")]
        public float minimumImpactSpeed = 3f;
        [Tooltip("Impact speed that counts as a hard hit: hit-stop, vibration and the heavy sound")]
        public float hardImpactSpeed = 8f;
        [Tooltip("Seconds between reactions on this car, so grinding along another car does not strobe")]
        public float cooldown = 0.25f;
        [Tooltip("Only react to other cars. Walls and scenery are handled by the respawn logic and would otherwise flash on every kerb.")]
        public bool carsOnly = true;

        [Header("Jolt")]
        [Tooltip("Extra impulse along the contact per m/s of impact speed, as a fraction of mass. The physics shove alone is barely visible.")]
        public float shovePerMetrePerSecond = 0.35f;
        [Tooltip("Yaw torque impulse per m/s, so the car twitches instead of only sliding")]
        public float yawJoltPerMetrePerSecond = 260f;
        [Tooltip("Nose-up pitch impulse on a rear hit, per m/s")]
        public float pitchJoltPerMetrePerSecond = 180f;
        public float maxJoltSpeed = 15f;

        [Header("Sparks")]
        [Tooltip("Particle prefab instantiated once under the car and moved to each contact point")]
        public ParticleSystem sparksPrefab;
        public int sparksPerHit = 24;

        [Header("Sound")]
        [Tooltip("Leave empty to use the synthesised stand-ins")]
        public AudioClip lightHitClip;
        public AudioClip mediumHitClip;
        public AudioClip heavyHitClip;
        [Tooltip("Hit sound level. 0.8 originally; the client asked for 20 percent less.")]
        [Range(0f, 1f)] public float volume = 0.64f;

        [Header("Player only")]
        [Tooltip("Time scale during the hit-stop on a hard hit")]
        [Range(0.2f, 1f)] public float hitStopTimeScale = 0.7f;
        [Tooltip("Real seconds the hit-stop lasts. 0 disables it.")]
        public float hitStopSeconds = 0.08f;
        public bool vibrateOnHardHit = true;

        public int HitCount { get; private set; }

        private Rigidbody body;
        private AudioSource audioSource;
        private ParticleSystem sparks;
        private bool isPlayer;
        private float lastHitTime = -999f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            isPlayer = GetComponent<CarUserControl>() != null;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 5f;
            audioSource.maxDistance = 90f;
            audioSource.dopplerLevel = 0f;

            if (sparksPrefab != null)
            {
                sparks = Instantiate(sparksPrefab, transform);
                sparks.transform.localPosition = Vector3.zero;
                var main = sparks.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = false;
                sparks.Stop();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time - lastHitTime < cooldown) return;
            if (collision.contactCount == 0) return;

            var otherBody = collision.rigidbody;
            var otherIsCar = otherBody != null && otherBody.GetComponent<CarController>() != null;
            if (carsOnly && !otherIsCar) return;

            var contact = collision.GetContact(0);
            // speed along the contact normal is what a bumper actually feels; a glancing slide with
            // a large relative velocity across the contact is not a hit
            var impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (impactSpeed < minimumImpactSpeed) return;

            lastHitTime = Time.time;
            HitCount++;

            var localDirection = transform.InverseTransformPoint(contact.point);
            localDirection.y = 0f;
            localDirection = localDirection.sqrMagnitude > 0.001f ? localDirection.normalized : Vector3.back;

            Jolt(impactSpeed, contact, localDirection);
            EmitSparks(contact.point);
            PlayHit(impactSpeed);

            if (isPlayer && gameEvents != null)
                gameEvents.PlayerHitEvent.Invoke(impactSpeed, localDirection);

            if (isPlayer && impactSpeed >= hardImpactSpeed)
            {
                if (hitStopSeconds > 0f) StartCoroutine(HitStop());
#if UNITY_ANDROID && !UNITY_EDITOR
                if (vibrateOnHardHit) Handheld.Vibrate();
#endif
            }
        }

        private void Jolt(float impactSpeed, ContactPoint contact, Vector3 localDirection)
        {
            var speed = Mathf.Min(impactSpeed, maxJoltSpeed);

            // push away from the contact. the normal's sign is not reliable between the two cars in a
            // pair, so the direction is taken from the contact point instead
            var away = transform.position - contact.point;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = transform.forward;
            body.AddForce(away.normalized * speed * shovePerMetrePerSecond * body.mass, ForceMode.Impulse);

            // twitch towards the side that was hit: a hit on the rear-left kicks the tail right
            var yawSign = localDirection.x > 0.05f ? -1f : (localDirection.x < -0.05f ? 1f : (Random.value < 0.5f ? -1f : 1f));
            var rearHit = localDirection.z < -0.3f;
            var torque = Vector3.up * yawSign * speed * yawJoltPerMetrePerSecond * (rearHit ? 0.6f : 1f);
            if (rearHit)
                torque += transform.right * -speed * pitchJoltPerMetrePerSecond;   // nose up
            body.AddTorque(torque, ForceMode.Impulse);
        }

        private void EmitSparks(Vector3 at)
        {
            if (sparks == null) return;
            sparks.transform.position = at;
            sparks.Emit(sparksPerHit);
        }

        private void PlayHit(float impactSpeed)
        {
            AudioClip clip;
            if (impactSpeed >= hardImpactSpeed)
                clip = heavyHitClip != null ? heavyHitClip : ProceduralSfx.ImpactClip(ProceduralSfx.Impact.Heavy);
            else if (impactSpeed >= (minimumImpactSpeed + hardImpactSpeed) * 0.5f)
                clip = mediumHitClip != null ? mediumHitClip : ProceduralSfx.ImpactClip(ProceduralSfx.Impact.Medium);
            else
                clip = lightHitClip != null ? lightHitClip : ProceduralSfx.ImpactClip(ProceduralSfx.Impact.Light);

            audioSource.pitch = Random.Range(0.92f, 1.08f);
            audioSource.PlayOneShot(clip, volume);
        }

        /// <summary>
        /// a very short slow-down that makes a hard hit register. it only ever lowers a time scale of
        /// one and only ever restores it if nothing else has changed it since, so it cannot fight the
        /// finish sequence's slow-motion beat or a pause
        /// </summary>
        private IEnumerator HitStop()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f)) yield break;
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(hitStopSeconds);
            if (Mathf.Approximately(Time.timeScale, hitStopTimeScale))
                Time.timeScale = 1f;
        }
    }
}
