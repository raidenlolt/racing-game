using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
/// <summary>
/// display on screen the final player status when completing the race (timeout or finish pos)
/// </summary>
namespace SpinMotion
{
    public class RaceFinishGUI : MonoBehaviour
    {
        public RealTimeRacePositionsItem realTimeRacePositions;

        public GameEvents gameEvents;
        public GameObject raceUI;
        public GameObject raceFinishPanel;
        public TMP_Text raceFinishTMP;
        public Button raceFinishRestartButton;
        public Button raceFinishContinueButton;

        [Header("Reveal")]
        [Tooltip("Real seconds the panel takes to scale and fade in")]
        public float revealSeconds = 0.35f;
        [Tooltip("Real seconds the score counts up over")]
        public float scoreCountSeconds = 0.6f;

        private CanvasGroup panelGroup;
        private Coroutine reveal;

        private void Awake()
        {
            // the panel used to open on RaceFinishedEvent, in the same frame the car crossed the line.
            // RaceFinishSequence now plays the finish first and raises this when the panel may open
            gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);
            gameEvents.RaceResultsReadyEvent.AddListener(OnResultsReady);
            raceFinishRestartButton.onClick.AddListener(OnClickRestart);
            raceFinishContinueButton.onClick.AddListener(OnClickContinue);
        }

        private void OnRaceFinished(RaceFinishType raceFinishType)
        {
            // the HUD goes as soon as the race is over; the finish banner takes its place
            raceUI.SetActive(false);
        }

        private void OnResultsReady(RaceFinishType raceFinishType)
        {
            raceFinishPanel.SetActive(true);

            var playerRacePos = realTimeRacePositions.Item.GetPlayerRacePosition(0);
            // the same number the platform receives: speed points plus a bonus per completed lap,
            // nothing for a race the clock ended
            double playerScore = RaceScore.ForPlayer(raceFinishType, realTimeRacePositions.Item);

            if (reveal != null) StopCoroutine(reveal);
            reveal = StartCoroutine(Reveal(playerRacePos, playerScore));
        }

        private IEnumerator Reveal(int playerRacePos, double playerScore)
        {
            if (panelGroup == null)
            {
                panelGroup = raceFinishPanel.GetComponent<CanvasGroup>();
                if (panelGroup == null) panelGroup = raceFinishPanel.AddComponent<CanvasGroup>();
            }
            var panel = raceFinishPanel.transform;
            var place = playerRacePos + RacePositionGUI.CardinalPos(playerRacePos) + " Place";

            var start = Time.unscaledTime;
            var total = revealSeconds + scoreCountSeconds;
            while (true)
            {
                var t = Time.unscaledTime - start;

                var u = Mathf.Clamp01(t / revealSeconds);
                var eased = 1f - Mathf.Pow(1f - u, 3f);
                panel.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, eased);
                panelGroup.alpha = eased;

                var s = Mathf.Clamp01((t - revealSeconds * 0.5f) / scoreCountSeconds);
                var shown = System.Math.Round(playerScore * (1.0 - Mathf.Pow(1f - s, 2f)));
                raceFinishTMP.text = place + "\n" + $"Score: {shown:N0}";

                if (t >= total) break;
                yield return null;
            }

            panel.localScale = Vector3.one;
            panelGroup.alpha = 1f;
            raceFinishTMP.text = place + "\n" + $"Score: {playerScore:N0}";
            reveal = null;
        }

        private void OnClickRestart()
        {
            if (reveal != null) { StopCoroutine(reveal); reveal = null; }
            raceUI.SetActive(true);
            raceFinishPanel.SetActive(false);

            gameEvents.OnClickRestartRaceEvent.Invoke();
        }

        private void OnClickContinue()
        {
            gameEvents.OnClickRestartGameEvent.Invoke();
        }
    }
}
