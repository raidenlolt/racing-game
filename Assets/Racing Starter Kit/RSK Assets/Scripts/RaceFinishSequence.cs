using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// owns the few seconds between the finish line and the results panel.
    ///
    /// before this, one event did everything in a single frame: the HUD vanished, the camera cut to
    /// a fixed point in the scene and the score panel was already open while the car was still
    /// crossing the line. RaceFinishedEvent keeps its meaning (the race is over: stop the timer,
    /// disengage nitro, release the AI); this component listens to it and, a few seconds later,
    /// raises RaceResultsReadyEvent, which is what now opens the panel.
    ///
    /// the beat: input locks and the car coasts; the FINISH banner slams in over a white flash
    /// during a short slow-motion; the finish camera orbits the car; the position counts in; confetti
    /// on a win; then results. a tap after the banner has landed skips to the results.
    ///
    /// every tween here runs on unscaled time, so the slow-motion cannot slow its own banner, and
    /// a restart or a scene change during the sequence puts the time scale and input back.
    /// </summary>
    public class RaceFinishSequence : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RealTimeRacePositionsItem realTimeRacePositions;

        [Header("Banner, wired by the setup pass")]
        public GameObject bannerRoot;
        public Image flashImage;
        public TMP_Text finishText;
        public TMP_Text positionText;
        [Tooltip("Instantiated in front of the finish camera on a win")]
        public ParticleSystem confettiPrefab;

        [Header("Timing (real seconds)")]
        public float slowMotionScale = 0.35f;
        public float slowMotionSeconds = 1.2f;
        public float bannerInSeconds = 0.38f;
        public float positionAt = 1.2f;
        public float resultsAt = 3.4f;
        [Tooltip("A tap skips to the results once the banner has been up this long")]
        public float skippableAfter = 0.9f;

        [Header("Sound")]
        [Tooltip("Leave empty to use the synthesised stand-in")]
        public AudioClip fanfareClip;
        [Range(0f, 1f)] public float fanfareVolume = 0.8f;

        private Coroutine sequence;
        private AudioSource audioSource;
        private float defaultFixedDelta;
        private bool changedTimeScale;

        private void Awake()
        {
            defaultFixedDelta = Time.fixedDeltaTime;
            CarUserControl.InputLocked = false;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            if (bannerRoot != null) bannerRoot.SetActive(false);

            if (gameEvents == null) return;
            gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);
            gameEvents.RestartRaceEvent.AddListener(OnRestartRace);
            gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
        }

        private void OnDestroy()
        {
            RestoreTime();
            CarUserControl.InputLocked = false;
            if (gameEvents == null) return;
            gameEvents.RaceFinishedEvent.RemoveListener(OnRaceFinished);
            gameEvents.RestartRaceEvent.RemoveListener(OnRestartRace);
            gameEvents.RaceStartedEvent.RemoveListener(OnRaceStarted);
        }

        [Header("Pack engines")]
        [Tooltip("Seconds the bots' engines take to fade out once the race is over, so they are not revving under the results")]
        public float packFadeOutSeconds = 1.5f;

        private void OnRaceFinished(RaceFinishType type)
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = StartCoroutine(Sequence(type));
            FadePack(0f, packFadeOutSeconds);
        }

        private void OnRaceStarted()
        {
            FadePack(1f, 0f);
        }

        /// <summary>every engine but the player's; the player's own car is left to coast to its stop</summary>
        private void FadePack(float gain, float seconds)
        {
            foreach (var audio in FindObjectsByType<CarAudio>(FindObjectsSortMode.None))
                if (audio.GetComponent<CarUserControl>() == null) audio.FadeEngine(gain, seconds);
        }

        private void OnRestartRace()
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            RestoreTime();
            CarUserControl.InputLocked = false;
            FadePack(1f, 0f);
            if (bannerRoot != null) bannerRoot.SetActive(false);
        }

        private IEnumerator Sequence(RaceFinishType type)
        {
            CarUserControl.InputLocked = true;

            audioSource.PlayOneShot(fanfareClip != null ? fanfareClip : ProceduralSfx.FinishFanfareClip(), fanfareVolume);

            if (bannerRoot != null) bannerRoot.SetActive(true);
            if (finishText != null)
            {
                finishText.text = type == RaceFinishType.Timeout ? "TIME UP" : "FINISH";
                finishText.transform.localScale = Vector3.one * 3f;
                finishText.alpha = 0f;
            }
            if (positionText != null) positionText.alpha = 0f;
            if (flashImage != null) flashImage.color = new Color(1f, 1f, 1f, type == RaceFinishType.Timeout ? 0.35f : 0.8f);

            // slow motion is the reward for crossing the line; a timeout does not get it
            var slow = type != RaceFinishType.Timeout;
            if (slow) SetTimeScale(slowMotionScale);

            var start = Time.unscaledTime;
            var positionShown = false;
            var confettiDone = false;
            while (true)
            {
                var t = Time.unscaledTime - start;

                // banner: scale 3 -> 1 with a small overshoot, alpha up
                if (finishText != null)
                {
                    var u = Mathf.Clamp01(t / bannerInSeconds);
                    var eased = 1f - Mathf.Pow(1f - u, 3f);
                    var overshoot = u < 1f ? 1f + 0.08f * Mathf.Sin(u * Mathf.PI) : 1f;
                    finishText.transform.localScale = Vector3.one * Mathf.Lerp(3f, 1f, eased) * overshoot;
                    finishText.alpha = Mathf.Clamp01(u * 2f);
                }
                if (flashImage != null)
                {
                    var c = flashImage.color;
                    c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 1.6f);
                    flashImage.color = c;
                }

                // slow motion eases back to full speed over its window
                if (slow)
                {
                    var s = Mathf.Clamp01(t / slowMotionSeconds);
                    SetTimeScale(Mathf.Lerp(slowMotionScale, 1f, s * s));
                }

                if (!positionShown && t >= positionAt)
                {
                    positionShown = true;
                    ShowPosition();
                    if (!confettiDone && type == RaceFinishType.Win)
                    {
                        confettiDone = true;
                        Confetti();
                    }
                }
                if (positionShown && positionText != null)
                    positionText.alpha = Mathf.MoveTowards(positionText.alpha, 1f, Time.unscaledDeltaTime * 3f);

                var skip = t >= skippableAfter && (Input.GetMouseButtonDown(0) || Input.touchCount > 0);
                if (t >= resultsAt || skip) break;

                yield return null;
            }

            RestoreTime();
            if (bannerRoot != null) bannerRoot.SetActive(false);
            sequence = null;

            if (gameEvents != null) gameEvents.RaceResultsReadyEvent.Invoke(type);
        }

        private void ShowPosition()
        {
            if (positionText == null || realTimeRacePositions == null || realTimeRacePositions.Item == null) return;
            var pos = realTimeRacePositions.Item.GetPlayerRacePosition(0);
            positionText.text = pos + RacePositionGUI.CardinalPos(pos) + " PLACE";
        }

        /// <summary>
        /// the burst is parented to whichever camera is showing the finish, a few metres ahead of it,
        /// so it falls through the shot whatever the orbit is doing
        /// </summary>
        private void Confetti()
        {
            if (confettiPrefab == null) return;
            var finishCamera = FindFirstObjectByType<RaceFinishCamera>();
            var anchor = finishCamera != null ? finishCamera.transform : Camera.main != null ? Camera.main.transform : null;
            if (anchor == null) return;

            var burst = Instantiate(confettiPrefab, anchor);
            burst.transform.localPosition = new Vector3(0f, 2.5f, 5f);
            burst.transform.localRotation = Quaternion.identity;
            var main = burst.main;
            main.useUnscaledTime = true;
            burst.Play();
            Destroy(burst.gameObject, 6f);
        }

        private void SetTimeScale(float scale)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = defaultFixedDelta * scale;
            changedTimeScale = true;
        }

        private void RestoreTime()
        {
            if (!changedTimeScale) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = defaultFixedDelta;
            changedTimeScale = false;
        }
    }
}
