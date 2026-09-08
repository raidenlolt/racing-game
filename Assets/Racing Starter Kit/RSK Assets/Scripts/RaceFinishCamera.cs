using UnityEngine;
/// <summary>
/// enable this camera when finishing the race
/// </summary>
namespace SpinMotion
{
    /// <summary>
    /// the camera the finish is watched from. it used to sit at a fixed point in the scene, so the
    /// finish was whatever happened to be in front of it; now it orbits the player's car, which also
    /// shows the bots still racing behind. the orbit runs on unscaled time so the finish sequence's
    /// slow-motion does not slow the camera along with the cars.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RaceFinishCamera : MonoBehaviour
    {
        public GameEvents gameEvents;

        [Header("Orbit")]
        public float orbitRadius = 8f;
        public float orbitHeight = 2.6f;
        public float degreesPerSecond = 28f;
        [Tooltip("Point on the car the camera looks at, above its origin")]
        public float lookHeight = 0.8f;
        [Tooltip("Start angle relative to the car's heading: 180 is dead behind, 0 is head on")]
        public float startAngle = 150f;
        public float followSmoothing = 8f;

        public bool IsOrbiting { get { return orbiting; } }
        public Transform Target { get { return target; } }

        private Camera finishCamera;
        private AudioListener audioListener;
        private Transform target;
        private float angle;
        private bool orbiting;

        private void Awake()
        {
            finishCamera = GetComponent<Camera>();
            audioListener = GetComponent<AudioListener>();

            gameEvents.RestartRaceEvent.AddListener(OnRestartRace);
            gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.RestartRaceEvent.RemoveListener(OnRestartRace);
            gameEvents.RaceFinishedEvent.RemoveListener(OnRaceFinished);
        }

        private void OnRestartRace()
        {
            finishCamera.enabled = false;
            orbiting = false;
        }

        private void OnRaceFinished(RaceFinishType raceFinishType)
        {
            finishCamera.enabled = true;
            // the listener on this camera stays off: the player's camera rig already carries one, and
            // switching a second on here logged "2 audio listeners" every frame of the finish

            var player = FindFirstObjectByType<CarUserControl>();
            target = player != null ? player.transform : null;
            orbiting = target != null;
            if (!orbiting) return;

            angle = target.eulerAngles.y + startAngle;
            // no smoothing on the first frame, or the camera would sweep in from wherever it was parked
            transform.position = OrbitPosition();
            transform.rotation = LookRotation();
        }

        private void LateUpdate()
        {
            if (!orbiting || target == null) return;

            angle += degreesPerSecond * Time.unscaledDeltaTime;
            // position is pinned to the orbit rather than eased towards it: the car is still doing
            // 50 m/s when it crosses the line, and an eased follow lagged metres behind the orbit
            // point, which put the camera inside the car. the orbit is already smooth by itself
            transform.position = OrbitPosition();
            var blend = 1f - Mathf.Exp(-followSmoothing * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, LookRotation(), blend);
        }

        private Vector3 OrbitPosition()
        {
            var around = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
            return target.position + around + Vector3.up * orbitHeight;
        }

        private Quaternion LookRotation()
        {
            var to = target.position + Vector3.up * lookHeight - transform.position;
            return to.sqrMagnitude > 0.001f ? Quaternion.LookRotation(to, Vector3.up) : transform.rotation;
        }
    }
}
