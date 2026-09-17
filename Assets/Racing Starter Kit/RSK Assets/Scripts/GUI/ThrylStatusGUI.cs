using TMPro;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the platform's presence on screen: the player's name on the car menu, and on the results
    /// panel the best score plus whether this run's score reached the platform. all optional; each
    /// label that is not wired is simply left alone.
    /// </summary>
    public class ThrylStatusGUI : MonoBehaviour
    {
        public GameEvents gameEvents;
        [Tooltip("Menu: greets the player by the name the platform sent")]
        public TMP_Text playerNameTMP;
        [Tooltip("Results: best score and submission status")]
        public TMP_Text resultsStatusTMP;

        private void Awake()
        {
            if (gameEvents != null) gameEvents.RaceResultsReadyEvent.AddListener(OnResultsReady);
        }

        private void OnEnable()
        {
            RefreshName();
            var client = ThrylClient.Instance;
            if (client != null) client.Submitted += OnSubmitted;
        }

        private void OnDisable()
        {
            var client = ThrylClient.Instance;
            if (client != null) client.Submitted -= OnSubmitted;
        }

        private void OnDestroy()
        {
            if (gameEvents != null) gameEvents.RaceResultsReadyEvent.RemoveListener(OnResultsReady);
        }

        private void RefreshName()
        {
            if (playerNameTMP == null) return;
            var client = ThrylClient.Instance;
            playerNameTMP.text = client != null && client.Launch != null ? client.Launch.playerName : "";
        }

        private void OnResultsReady(RaceFinishType type) { RefreshResults(); }
        private void OnSubmitted(bool ok, string message) { RefreshResults(); }

        private void RefreshResults()
        {
            if (resultsStatusTMP == null) return;
            var client = ThrylClient.Instance;
            if (client == null) { resultsStatusTMP.text = ""; return; }

            var line = "Best: " + client.BestScore.ToString("N0");
            if (client.Launch != null && client.Launch.CanSubmit)
            {
                if (client.Submitting) line += "   Saving...";
                else if (client.LastSubmissionSucceeded) line += "   Saved";
                else if (!string.IsNullOrEmpty(client.LastSubmissionMessage)) line += "   Not saved";
            }
            resultsStatusTMP.text = line;
        }
    }
}
