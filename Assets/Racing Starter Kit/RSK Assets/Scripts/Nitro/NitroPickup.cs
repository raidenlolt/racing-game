using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// a nitro bottle sitting on the track. drive through it and the gauge goes up.
    ///
    /// this is the only way to earn nitro that does not ask anything of the driver's technique. the
    /// other routes -- drifting, and the passive trickle the bots get -- reward doing something well
    /// or simply reward waiting. picking these up rewards taking a line, which is what makes them
    /// worth placing off centre rather than straight down the middle.
    ///
    /// it works for the AI as well as the player. a bottle that only the human could take would hand
    /// them an advantage the rubber banding would then have to undo.
    ///
    /// collected bottles come back after a delay rather than disappearing for the race, so a second
    /// or third lap is not a dry one.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class NitroPickup : MonoBehaviour
    {
        public RaceManagerItem raceManager;

        [Header("Reward")]
        [Tooltip("How much of the gauge one bottle is worth, as a fraction. The gauge runs 0 to 1 and level one nitro needs 0.08 to fire.")]
        [Range(0.02f, 1f)] public float chargeAmount = 0.22f;

        [Header("Respawn")]
        [Tooltip("Seconds before the bottle returns. Long enough that a driver cannot sit on one, short enough that the next lap still has them.")]
        public float respawnSeconds = 9f;

        [Header("Look")]
        [Tooltip("The visible part. Its material carries the nitro logo; the trigger lives on the parent so the visual can be replaced freely.")]
        public GameObject visual;
        [Tooltip("How far it rises and falls, in metres")]
        public float bobHeight = 0.3f;
        public float bobSpeed = 1.6f;

        [Header("Sound")]
        public AudioClip collectClip;
        [Range(0f, 1f)] public float collectVolume = 0.6f;

        [Header("Artwork")]
        [Tooltip("Roll applied to the billboard after it turns to the camera, in degrees, counter-clockwise as seen by the player. The current bottle artwork is drawn leaning about 40 degrees to the right; this stands it up. 0 shows the texture as drawn.")]
        public float visualRollDegrees = 40f;

        private Collider trigger;
        private AudioSource audioSource;
        private Vector3 visualHome;
        private float hiddenUntil = -1f;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            trigger.isTrigger = true;   // it must never block a car, only notice one

            if (visual != null) visualHome = visual.transform.localPosition;

            if (collectClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = collectClip;
                audioSource.playOnAwake = false;
                audioSource.volume = collectVolume;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 8f;
                audioSource.maxDistance = 60f;
            }
        }

        private void Update()
        {
            if (hiddenUntil > 0f && Time.time >= hiddenUntil) Reappear();

            if (visual == null || !visual.activeSelf) return;

            visual.transform.localPosition = visualHome
                + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);

            FaceTheCamera();
        }

        /// <summary>
        /// keeps the bottle turned towards whoever is looking at it.
        ///
        /// the alternative, spinning it about its own axis, turns a flat card edge on twice a
        /// revolution. that is fine for a generic coin but not for a logo, which is only worth having
        /// if it can be read. billboarding also means it stays legible from any camera angle the
        /// player switches to.
        /// </summary>
        private void FaceTheCamera()
        {
            var camera = ResolveCamera();
            if (camera == null) return;

            var away = visual.transform.position - camera.transform.position;
            away.y = 0f;   // stay upright; tipping towards a low camera looks like it is falling over
            if (away.sqrMagnitude < 0.0001f) return;

            // turn to face the viewer, then roll about the line of sight so the artwork stands up
            visual.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up)
                                        * Quaternion.Euler(0f, 0f, visualRollDegrees);
        }

        /// <summary>
        /// the camera currently rendering. this cannot be cached once: the menu camera hands over to
        /// the car cameras at race start, and the player can cycle between three of those mid race.
        /// </summary>
        private Camera ResolveCamera()
        {
            // the finish camera takes over the view after the line while the car cameras stay
            // enabled underneath it, so it is checked first or the bottles would keep facing the
            // wrong camera and show their mirrored backs during the orbit
            if (finishCamera == null) finishCamera = FindFirstObjectByType<RaceFinishCamera>();
            if (finishCamera != null)
            {
                var finishView = finishCamera.GetComponent<Camera>();
                if (finishView != null && finishView.isActiveAndEnabled) return finishView;
            }

            if (viewer != null && viewer.isActiveAndEnabled) return viewer;

            viewer = Camera.main;
            if (viewer != null && viewer.isActiveAndEnabled) return viewer;

            foreach (var candidate in Camera.allCameras)
            {
                if (!candidate.isActiveAndEnabled) continue;
                viewer = candidate;
                return viewer;
            }
            return null;
        }

        private Camera viewer;
        private RaceFinishCamera finishCamera;

        /// <summary>diagnostic: how many collider entries this bottle has seen, and how many paid out</summary>
        public int EnterCount { get; private set; }
        public int PayoutCount { get; private set; }

        private void OnTriggerEnter(Collider other)
        {
            EnterCount++;
            if (hiddenUntil > 0f) return;
            if (raceManager != null && raceManager.Item != null && !raceManager.Item.IsRaceInProgress()) return;

            // the collider that touches this belongs to a child of the car, so the nitro system has
            // to be looked up from the root rather than from whatever piece of bodywork arrived
            var nitro = other.transform.root.GetComponentInChildren<NitroSystem>();
            if (nitro == null) return;

            PayoutCount++;
            nitro.AddCharge(chargeAmount);
            Collect();
        }

        private void Collect()
        {
            hiddenUntil = Time.time + respawnSeconds;
            if (visual != null) visual.SetActive(false);
            trigger.enabled = false;

            if (audioSource != null) audioSource.Play();
        }

        /// <summary>
        /// brings the bottle back, but not underneath whoever just took it.
        ///
        /// re-enabling a collider that is already overlapping something counts as a fresh entry in
        /// Unity, so a car sitting still on a bottle collects it again the instant it returns, and
        /// again every respawn after that. parking on one was worth unlimited nitro. the bottle now
        /// waits until the space above it is clear before coming back.
        /// </summary>
        private void Reappear()
        {
            if (IsOccupied())
            {
                // check again shortly rather than every frame; nothing is visible in the meantime
                hiddenUntil = Time.time + 1f;
                return;
            }

            hiddenUntil = -1f;
            if (visual != null) visual.SetActive(true);
            trigger.enabled = true;
        }

        private bool IsOccupied()
        {
            var radius = trigger is SphereCollider ? ((SphereCollider)trigger).radius : 3f;
            foreach (var found in Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (found.transform.root.GetComponentInChildren<NitroSystem>() != null) return true;
            }
            return false;
        }
    }
}
