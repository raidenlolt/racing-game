using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the client's "car acceleration" clip, played once each time the car pulls away hard: throttle
    /// down from low speed, at the flag and after every slow corner or stop. the normal engine loops
    /// keep running underneath; this is the flourish on top, so it never loops and it is cut short
    /// with a quick fade if the driver lifts off. player car only: six bots doing it would be noise.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AccelerationSfx : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Throttle (0..1) that counts as pulling away")]
        [Range(0.1f, 1f)] public float triggerThrottle = 0.6f;
        [Tooltip("Only fires when the car is at or below this speed (mph) when the throttle goes down")]
        public float maxSpeedMph = 30f;
        [Tooltip("Seconds between two plays")]
        public float cooldown = 5f;
        [Tooltip("Throttle below which a running play is faded out")]
        [Range(0f, 1f)] public float liftThrottle = 0.2f;
        [Tooltip("Seconds of the fade when the driver lifts")]
        public float liftFade = 0.25f;
        [Tooltip("Only the car with a CarUserControl plays it")]
        public bool playerOnly = true;

        public bool IsPlaying { get { return source != null && source.isPlaying; } }
        public int PlayCount { get; private set; }

        private CarController car;
        private AudioSource source;
        private float lastPlay = -999f;
        private bool wasBelowTrigger = true;

        private void Awake()
        {
            car = GetComponent<CarController>();
            if (playerOnly && GetComponent<CarUserControl>() == null) { enabled = false; return; }
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = volume;
        }

        private void Update()
        {
            if (clip == null || source == null) return;
            var throttle = car.AccelInput;

            if (source.isPlaying)
            {
                // lifting off cuts the flourish; the engine loops carry on by themselves
                if (throttle < liftThrottle)
                {
                    source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * volume / Mathf.Max(0.01f, liftFade));
                    if (source.volume <= 0f) source.Stop();
                }
                else source.volume = Mathf.MoveTowards(source.volume, volume, Time.deltaTime * 4f);
            }

            // fire on the rising edge of the throttle, from low speed, not too often
            var pulling = throttle >= triggerThrottle;
            if (pulling && wasBelowTrigger && !source.isPlaying && car.CurrentSpeed <= maxSpeedMph && Time.time - lastPlay >= cooldown)
                Play();
            wasBelowTrigger = !pulling;
        }

        public void Play()
        {
            if (clip == null || source == null) return;
            source.volume = volume;
            source.clip = clip;
            source.Play();
            lastPlay = Time.time;
            PlayCount++;
        }
    }
}
