using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SpinMotion
{
    /// <summary>
    /// the game's side of the THRYL single player integration. one persistent object, created on
    /// first load from the config in Resources, that:
    ///   reads the launch parameters off the page URL (WebGL) or the config's test URL (editor)
    ///   submits the final score once per completed race with the bearer token from the launch
    ///   keeps the best score for the HUD, seeded from game_highest_score
    ///
    /// the score request is exactly the guide's: POST {base}/in-game-score/points, JSON body with
    /// custom_game_id and points_ingame, Authorization: Bearer {token}. it is sent on
    /// RaceFinishedEvent rather than when the results panel opens, so a player closing the tab
    /// during the finish sequence still gets their score in.
    ///
    /// nothing here is scene-specific and no scene needs an object added: the bootstrap runs from
    /// RuntimeInitializeOnLoadMethod and the events come from the shared GameEvents asset.
    /// </summary>
    public class ThrylClient : MonoBehaviour
    {
        public static ThrylClient Instance { get; private set; }

        public ThrylLaunch Launch { get; private set; }
        public ThrylConfig Config { get; private set; }
        /// <summary>the best score seen this session, seeded from the launch URL</summary>
        public int BestScore { get; private set; }
        public int LastScore { get; private set; }
        public bool LastSubmissionSucceeded { get; private set; }
        public string LastSubmissionMessage { get; private set; } = "";
        public bool Submitting { get; private set; }
        public int SubmissionCount { get; private set; }

        /// <summary>raised after each attempt: success, message</summary>
        public event Action<bool, string> Submitted;

        private GameEvents events;
        private bool submittedThisRace;
        private RealTimeRacePositionsItem positions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            var config = Resources.Load<ThrylConfig>(ThrylConfig.ResourceName);
            if (config == null)
            {
                Debug.LogWarning("[THRYL] no ThrylConfig in Resources; platform integration is off");
                return;
            }
            var go = new GameObject("THRYL Client");
            DontDestroyOnLoad(go);
            var client = go.AddComponent<ThrylClient>();
            client.Initialise(config);
        }

        private void Initialise(ThrylConfig config)
        {
            Instance = this;
            Config = config;

            var url = Application.absoluteURL;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (string.IsNullOrEmpty(url) || url.IndexOf('?') < 0) url = config.editorLaunchUrl;
#endif
            Launch = ThrylLaunch.Parse(url);
            BestScore = Launch.highestScore;

            if (config.verbose)
                Debug.Log("[THRYL] launch: " + Launch + (Launch.CanSubmit ? "" : " (no session: scores will not be submitted)")
                          + " env " + config.environment);

            // the config carries the event asset because the first scene is the track menu, which
            // has none of the race objects FindEvents could take it from; a client that booted there
            // and searched would never hear a race finish. the search is only a fallback, retried on
            // every scene load until something is found
            if (config.gameEvents != null) Listen(config.gameEvents);
            else
            {
                Listen(FindEvents());
                if (events == null)
                {
                    Debug.LogWarning("[THRYL] config has no GameEvents reference; searching each scene for one");
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                }
            }
        }

        /// <summary>true once the client is subscribed to the race events; nothing is submitted before that</summary>
        public bool Listening { get { return events != null; } }

        private void Listen(GameEvents source)
        {
            if (source == null || events != null) return;
            events = source;
            events.RaceFinishedEvent.AddListener(OnRaceFinished);
            events.RestartRaceEvent.AddListener(OnRestartRace);
            events.RaceStartedEvent.AddListener(OnRaceStarted);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Listen(FindEvents());
            if (events != null) UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (events == null) return;
            events.RaceFinishedEvent.RemoveListener(OnRaceFinished);
            events.RestartRaceEvent.RemoveListener(OnRestartRace);
            events.RaceStartedEvent.RemoveListener(OnRaceStarted);
        }

        /// <summary>
        /// the shared GameEvents asset. every listener in the game references the same one, so any
        /// component in the loaded scene can hand it over; the mini map is present on every track
        /// </summary>
        private static GameEvents FindEvents()
        {
            var manager = FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
            if (manager != null && manager.gameEvents != null) return manager.gameEvents;
            var menu = FindFirstObjectByType<MenuGUI>(FindObjectsInactive.Include);
            if (menu != null && menu.gameEvents != null) return menu.gameEvents;
            var positions = FindFirstObjectByType<RealTimeRacePositions>(FindObjectsInactive.Include);
            return positions != null ? positions.gameEvents : null;
        }

        private void OnRaceStarted() { submittedThisRace = false; }
        private void OnRestartRace() { submittedThisRace = false; }

        private void OnRaceFinished(RaceFinishType type)
        {
            Debug.Log("[THRYL] Race finished: " + type);
            if (submittedThisRace) return;
            submittedThisRace = true;

            var positionsItem = FindFirstObjectByType<RealTimeRacePositions>();
            if (positionsItem == null) return;
            var place = positionsItem.GetPlayerRacePosition(0);
            var fieldSize = positionsItem.RacePositionTotalScores.Count;
            var progress = RaceScore.ProgressFraction(positionsItem, 0);
            var score = RaceScore.Compute(type, place, fieldSize, progress);

            RecordScore(score);
            if (Config.submitOnFinish) SubmitScore(score);
        }

        public void RecordScore(int score)
        {
            LastScore = score;
            if (score > BestScore) BestScore = score;
        }

        /// <summary>posts a score. safe to call without a session; it reports and does nothing</summary>
        public void SubmitScore(int score)
        {
            Debug.Log("[THRYL] SubmitScore: " + score);
            if (!Launch.CanSubmit)
            {
                Report(false, "Score: " + score + (Launch.HasToken ? "no game id in the launch URL" : "no platform session"));
                return;
            }
            StartCoroutine(Submit(score));
        }

        private IEnumerator Submit(int score)
        {
            Submitting = true;
            var body = BuildBody(score);
            var attempts = 0;
            while (true)
            {
                attempts++;
                using (var request = new UnityWebRequest(Config.ScoreUrl, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", "Bearer " + Launch.token);
                    request.timeout = Config.requestTimeoutSeconds;

                    if (Config.verbose) Debug.Log("[THRYL] POST " + Config.ScoreUrl + " " + body + " (attempt " + attempts + ")");
                    yield return request.SendWebRequest();

                    var status = (int)request.responseCode;
                    var text = request.downloadHandler != null ? request.downloadHandler.text : "";
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        SubmissionCount++;
                        Report(true, "score " + score + " submitted (" + status + ")");
                        break;
                    }

                    var transient = request.result == UnityWebRequest.Result.ConnectionError || status >= 500;
                    var message = "submit failed: " + status + " " + request.error + (string.IsNullOrEmpty(text) ? "" : " " + Trim(text));
                    if (transient && attempts <= Config.retries)
                    {
                        if (Config.verbose) Debug.LogWarning("[THRYL] " + message + ", retrying");
                        yield return new WaitForSecondsRealtime(1.5f * attempts);
                        continue;
                    }
                    Report(false, message);
                    break;
                }
            }
            Submitting = false;
        }

        private string BuildBody(int score)
        {
            if (Config.sendGameIdAsNumber && Launch.customGameIdNumber >= 0)
                return "{\"custom_game_id\":" + Launch.customGameIdNumber + ",\"points_ingame\":" + score + "}";
            return "{\"custom_game_id\":\"" + Escape(Launch.customGameId) + "\",\"points_ingame\":" + score + "}";
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Trim(string s)
        {
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > 160 ? s.Substring(0, 160) + "..." : s;
        }

        private void Report(bool ok, string message)
        {
            LastSubmissionSucceeded = ok;
            LastSubmissionMessage = message;
            if (Config.verbose || !ok) Debug.Log("[THRYL] " + message);
            var handler = Submitted;
            if (handler != null) handler(ok, message);
        }
    }
}
