using System.Collections;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the menu's car carousel: real cars, on the real track, that drive in from behind the treeline,
    /// sweep round a curve and settle in front of the camera.
    ///
    /// this replaces a render-texture thumbnail. the car is an actual instance of the car prefab, so
    /// it is lit by the same sun, sits on the same tarmac, and its engine is heard in 3D from where
    /// it actually is -- running before it comes into view, loudest as it arrives, receding as the
    /// one it replaced drives away.
    ///
    /// every car finishes on the same heading. following the path's tangent all the way would mean a
    /// car going "back" arrives reversed, and spinning it 180 degrees on the spot to correct that is
    /// exactly the glitch this used to have. so the choreography is always forwards: pressing back
    /// plays the same drive-in with the previous car rather than mirroring the move.
    ///
    /// the cars are stripped on the way in. a car prefab carries its own controller, user input, AI
    /// hooks, wheel colliders, particle systems and -- importantly -- three cameras and three audio
    /// listeners. left alone, spawning three of them would put nine of each in the menu scene.
    /// everything is switched off except the bodywork, and the engine note is played from a clip read
    /// off the car's own CarAudio rather than by running it, because CarAudio derives its pitch from
    /// CarController.Revs, which only updates while the car is actually being driven.
    /// </summary>
    public class MenuCarShowcase : MonoBehaviour
    {
        public CarCatalogue carCatalogue;
        public GameEvents gameEvents;

        [Header("Stage")]
        [Tooltip("Where the hero car stands. Its forward axis is the way every car faces once parked, and the way they drive.")]
        public Transform stageAnchor;
        [Tooltip("Shortest distance back along the road a car may start from. The real distance is worked out from the camera at runtime and is usually further, so this is a floor rather than the answer.")]
        public float entryDistance = 52f;
        [Tooltip("How far outside the edge of the frame a car must be before it is allowed to appear, as a fraction of screen width. Covers the length of the car itself.")]
        public float offscreenMargin = 0.14f;
        [Tooltip("How far the start and end of the path sit AWAY from the camera, which is what puts them back among the trees and gives the path its curve.")]
        public float entryDepth = 22f;
        [Tooltip("Seconds for a car to drive the curve into the hero pose. The path is long because leaving a shot aimed along the road takes distance, but the ease-out spends most of that distance in the first instants while the car is still a speck, and the last stretch slowly.")]
        public float travelSeconds = 1.8f;
        [Tooltip("Lifts the car so its wheels sit on the tarmac rather than in it")]
        public float groundOffset = 0f;

        [Header("Engine")]
        [Tooltip("Idle loop volume. The car is heard in 3D, so this is the level at point blank range.")]
        [Range(0f, 1f)] public float idleVolume = 0.75f;
        [Tooltip("Idle is the acceleration clip played slowly; this is how far down")]
        public float idlePitch = 0.55f;
        [Tooltip("Within this many metres the engine is at full volume")]
        public float minAudioDistance = 15f;
        [Tooltip("Beyond this many metres it cannot be heard. Sized so a car out at the treeline is faint rather than silent.")]
        public float maxAudioDistance = 62f;
        [Tooltip("Pitch shift from movement. Off by default: a car is placed at the far end of its path in a single frame, which Unity reads as thousands of metres per second and turns into a screech, and even the drive itself peaks at 313 m/s against a speed of sound of 343.")]
        [Range(0f, 1f)] public float dopplerLevel = 0f;

        private GameObject[] cars;
        private AudioSource[] idles;
        private GameObject[][] wheels;
        private float[] wheelRadii;

        private int current = -1;
        private Coroutine move;
        /// <summary>false once the race has been started, so the stage stays clear until the menu returns</summary>
        private bool menuShowing = true;

        private void Awake()
        {
            if (gameEvents != null)
                gameEvents.OnClickPlayRaceEvent.AddListener(OnClickPlayRace);
        }

        private void OnDestroy()
        {
            if (gameEvents != null)
                gameEvents.OnClickPlayRaceEvent.RemoveListener(OnClickPlayRace);
        }

        /// <summary>the menu is over; the stage has to be clear before the race camera cuts in</summary>
        private void OnClickPlayRace()
        {
            // this has to come first. the keep-alive below re-shows the selected car the moment it
            // finds it hidden, and the stage sits 34m down the racing line from the front of the
            // grid -- so without this flag the showcase car reappears in the middle of the track and
            // the whole field drives straight into it.
            menuShowing = false;

            if (cars == null) return;
            for (int i = 0; i < cars.Length; i++)
            {
                if (cars[i] == null) continue;
                // silence the engine before the object goes. deactivating a GameObject cuts its audio
                // mid waveform, and the car on stage is at full volume right up to the moment PLAY is
                // pressed, so that cut is an audible click at the head of every race.
                if (idles[i] != null) { idles[i].volume = 0f; idles[i].Stop(); }
                cars[i].SetActive(false);
            }
        }

        /// <summary>
        /// keeps the selected car in the hero pose whenever nothing is being animated.
        ///
        /// the drive-in is a coroutine, and a coroutine can be cut short -- interrupted by another
        /// press, or stopped by anything that goes wrong part way through. when that happens the car
        /// is left wherever it had got to, which on a long path means parked somewhere down the road
        /// or off stage entirely, and the menu shows an empty road with a car name under it. this
        /// costs a comparison per frame and means that state cannot survive.
        /// </summary>
        private void LateUpdate()
        {
            if (!menuShowing) return;
            if (move != null || cars == null || current < 0 || current >= cars.Length) return;
            if (stageAnchor == null) return;

            var car = cars[current];
            if (car == null) return;

            if (!car.activeSelf) car.SetActive(true);

            var home = stageAnchor.position + Vector3.up * groundOffset;
            if ((car.transform.position - home).sqrMagnitude > 0.01f)
                car.transform.SetPositionAndRotation(home, Quaternion.LookRotation(stageAnchor.forward, Vector3.up));

            PlayIdle(current);
        }

        /// <summary>brings a car to the hero pose, driving it in from the treeline</summary>
        public void Show(int index)
        {
            if (carCatalogue == null || carCatalogue.Count == 0 || stageAnchor == null) return;
            index = Mathf.Clamp(index, 0, carCatalogue.Count - 1);

            // picking a car means the menu is back up, so the stage is live again
            menuShowing = true;

            EnsureCars();
            if (index == current) return;

            var outgoing = current;
            current = index;

            if (move != null) StopCoroutine(move);
            move = StartCoroutine(Swap(outgoing, index));
        }

        private IEnumerator Swap(int outgoing, int incoming)
        {
            // flicking through the roster interrupts a run part way. the coroutine driving it is
            // stopped, which leaves whichever car it was moving sitting wherever it had got to, still
            // switched on -- press quickly a few times and the stage fills up with parked cars. only
            // the two cars in this swap belong on stage, so everything else goes now.
            for (int i = 0; i < cars.Length; i++)
            {
                if (i == incoming || i == outgoing || cars[i] == null) continue;
                if (idles[i] != null) idles[i].Stop();
                cars[i].SetActive(false);
            }

            Vector3 a0, a1, a2, a3;   // the arriving car's curve
            Vector3 d0, d1, d2, d3;   // the departing car's curve
            BuildPaths(out a0, out a1, out a2, out a3, out d0, out d1, out d2, out d3);

            var car = cars[incoming];
            car.SetActive(true);
            car.transform.SetPositionAndRotation(a0, Quaternion.LookRotation(a1 - a0, Vector3.up));
            // the engine is already running out where it starts, so it is heard approaching
            PlayIdle(incoming);
            PlayPassby(incoming);

            var arrivingAt = a0;
            var leavingAt = d0;
            var elapsed = 0f;

            while (elapsed < travelSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                // ease out so the car arrives settling rather than stopping dead
                var t = Mathf.Clamp01(elapsed / travelSeconds);
                var eased = 1f - Mathf.Pow(1f - t, 3f);

                arrivingAt = Drive(incoming, arrivingAt, Bezier(a0, a1, a2, a3, eased));

                if (outgoing >= 0 && cars[outgoing] != null)
                    leavingAt = Drive(outgoing, leavingAt, Bezier(d0, d1, d2, d3, eased));

                yield return null;
            }

            // land exactly on the hero pose rather than wherever the last frame happened to fall
            car.transform.SetPositionAndRotation(a3, Quaternion.LookRotation(stageAnchor.forward, Vector3.up));

            if (outgoing >= 0 && cars[outgoing] != null)
            {
                if (idles[outgoing] != null) idles[outgoing].Stop();
                cars[outgoing].SetActive(false);
            }
            move = null;
        }

        /// <summary>
        /// moves a car to a point on its path, turning it to face the way it is going and rolling the
        /// wheels by the distance covered. steering the car off its own motion is what makes the
        /// curve look driven rather than slid.
        /// </summary>
        private Vector3 Drive(int index, Vector3 from, Vector3 to)
        {
            var step = to - from;
            var distance = step.magnitude;

            var car = cars[index];
            car.transform.position = to;

            if (distance > 0.0001f)
            {
                var heading = step;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.0001f)
                    car.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
                SpinWheels(index, distance);
            }
            return to;
        }

        /// <summary>
        /// the two curves: one bringing a car in from the treeline, one taking the old one away.
        ///
        /// both are cubic curves whose control points at the stage end lie along the anchor's forward
        /// axis, which is what makes a car straighten out onto the hero heading as it arrives and
        /// pull away along it as it leaves. the far ends are pushed AWAY from the camera as well as
        /// along the road, which is the bit that puts them behind the trees rather than sliding in
        /// flatly from the side of the screen.
        /// </summary>
        private void BuildPaths(out Vector3 a0, out Vector3 a1, out Vector3 a2, out Vector3 a3,
                                out Vector3 d0, out Vector3 d1, out Vector3 d2, out Vector3 d3)
        {
            var distance = ResolveOffstageDistance();

            var home = stageAnchor.position + Vector3.up * groundOffset;
            var forward = stageAnchor.forward;
            // the anchor's right points away from the camera, into the scenery
            var back = stageAnchor.right * entryDepth;

            a0 = StartPoint(distance);
            a1 = a0 + forward * (distance * 0.5f);
            a2 = home - forward * (distance * 0.32f);
            a3 = home;

            d0 = home;
            d1 = home + forward * (distance * 0.32f);
            d2 = home + forward * (distance * 0.5f) + back;
            d3 = EndPoint(distance);
        }

        private Vector3 StartPoint(float distance)
        {
            return stageAnchor.position + Vector3.up * groundOffset
                 - stageAnchor.forward * distance + stageAnchor.right * entryDepth;
        }

        private Vector3 EndPoint(float distance)
        {
            return stageAnchor.position + Vector3.up * groundOffset
                 + stageAnchor.forward * distance + stageAnchor.right * entryDepth;
        }

        /// <summary>
        /// how far off stage the path has to reach before a car is genuinely out of shot.
        ///
        /// a fixed number cannot do this. the stage runs down the road and the camera looks along it
        /// at an angle, so pushing a car further away moves it TOWARDS the vanishing point, not out
        /// of frame -- at 52m the car was still sitting at the right hand edge of the picture, in
        /// view, which is exactly the pop. and the answer depends on the screen: the editor is 1.64
        /// wide where the phone is 2.22, so a distance tuned in one is wrong in the other.
        ///
        /// so it is measured instead: walk outwards until both ends of the path fall outside the
        /// frame by a margin wide enough to hide the length of the car.
        /// </summary>
        private float ResolveOffstageDistance()
        {
            var camera = ResolveMenuCamera();
            if (camera == null) return entryDistance;

            for (var distance = entryDistance; distance <= 320f; distance += 4f)
                if (OutOfShot(camera, StartPoint(distance)) && OutOfShot(camera, EndPoint(distance)))
                    return distance;

            return 320f;
        }

        private bool OutOfShot(Camera camera, Vector3 point)
        {
            var viewport = camera.WorldToViewportPoint(point);
            if (viewport.z <= 0f) return true;   // behind the lens
            return viewport.x < -offscreenMargin || viewport.x > 1f + offscreenMargin
                || viewport.y < -offscreenMargin || viewport.y > 1f + offscreenMargin;
        }

        private Camera ResolveMenuCamera()
        {
            if (menuCamera != null) return menuCamera;

            var start = FindFirstObjectByType<StartMenuCamera>();
            if (start != null) menuCamera = start.GetComponent<Camera>();
            return menuCamera;
        }

        private Camera menuCamera;

        private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            return u * u * u * p0
                 + 3f * u * u * t * p1
                 + 3f * u * t * t * p2
                 + t * t * t * p3;
        }

        /// <summary>
        /// rolls the wheels by the distance the car actually covered, so the spin matches the motion
        /// instead of being a fixed rate that looks glued on
        /// </summary>
        private void SpinWheels(int index, float distance)
        {
            var set = wheels[index];
            if (set == null) return;

            var radius = wheelRadii[index];
            if (radius <= 0.01f) return;

            var degrees = distance / (2f * Mathf.PI * radius) * 360f;
            var axis = cars[index].transform.right;
            foreach (var wheel in set)
                if (wheel != null) wheel.transform.Rotate(axis, degrees, Space.World);
        }

        private void PlayIdle(int index)
        {
            var source = idles[index];
            if (source == null) return;
            source.volume = idleVolume;
            if (!source.isPlaying) source.Play();
        }

        private void EnsureCars()
        {
            if (cars != null) return;

            var count = carCatalogue.Count;
            cars = new GameObject[count];
            idles = new AudioSource[count];
            wheels = new GameObject[count][];
            wheelRadii = new float[count];

            for (int i = 0; i < count; i++)
            {
                var entry = carCatalogue.Get(i);
                if (entry == null || entry.playerPrefab == null) continue;

                var car = Instantiate(entry.playerPrefab, stageAnchor.position, stageAnchor.rotation, transform);
                car.name = "Showcase " + entry.displayName;
                Prepare(car, i);
                car.SetActive(false);
                cars[i] = car;
            }
        }

        /// <summary>
        /// turns a driveable car into a display model: nothing that moves it, listens, renders or
        /// collides survives. the engine clip is lifted off CarAudio before it is switched off.
        /// </summary>
        private void Prepare(GameObject car, int index)
        {
            var controller = car.GetComponent<CarController>();
            if (controller != null) wheels[index] = controller.WheelMeshes;

            var collider = car.GetComponentInChildren<WheelCollider>(true);
            wheelRadii[index] = collider != null ? collider.radius * car.transform.lossyScale.y : 0.4f;

            AudioClip idleClip = null;
            var audio = car.GetComponentInChildren<CarAudio>(true);
            if (audio != null) idleClip = audio.lowAccelClip;

            // a car prefab brings three cameras and three audio listeners with it; three cars would
            // put nine of each into the menu scene and Unity would render whichever won
            foreach (var camera in car.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
            foreach (var listener in car.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;

            foreach (var behaviour in car.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var col in car.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var particles in car.GetComponentsInChildren<ParticleSystem>(true)) particles.gameObject.SetActive(false);
            foreach (var source in car.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;

            var body = car.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;   // the transform is animated directly, physics must not fight it
                body.useGravity = false;
            }

            idles[index] = MakeIdleSource(car, idleClip);
        }

        [Header("Arrival")]
        [Tooltip("Volume of the car's passby clip (CarAudio.passbyClip) as it sweeps onto the stage")]
        [Range(0f, 1f)] public float passbyVolume = 0.8f;
        private AudioSource passbySource;

        /// <summary>the whoosh of the car sweeping in, a flat one-shot over the top of the 3D idle</summary>
        private void PlayPassby(int index)
        {
            if (cars == null || index < 0 || index >= cars.Length || cars[index] == null) return;
            var audio = cars[index].GetComponentInChildren<CarAudio>(true);
            if (audio == null || audio.passbyClip == null) return;
            if (passbySource == null)
            {
                passbySource = gameObject.AddComponent<AudioSource>();
                passbySource.playOnAwake = false;
                passbySource.spatialBlend = 0f;
            }
            passbySource.PlayOneShot(audio.passbyClip, passbyVolume);
        }

        /// <summary>
        /// the engine, positioned on the car and heard in 3D so it arrives and recedes with it
        /// </summary>
        private AudioSource MakeIdleSource(GameObject car, AudioClip clip)
        {
            if (clip == null) return null;

            var holder = new GameObject("Showcase Idle");
            holder.transform.SetParent(car.transform, false);

            var source = holder.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.pitch = idlePitch;
            source.volume = idleVolume;

            source.spatialBlend = 1f;                                  // fully 3D
            source.rolloffMode = AudioRolloffMode.Linear;              // predictable, and reaches silence
            source.minDistance = minAudioDistance;
            source.maxDistance = maxAudioDistance;
            source.dopplerLevel = dopplerLevel;
            return source;
        }
    }
}
