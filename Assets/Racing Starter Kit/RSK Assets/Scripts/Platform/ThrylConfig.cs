using UnityEngine;

namespace SpinMotion
{
    public enum ThrylEnvironment { Staging, Production }

    /// <summary>
    /// where the game talks to, and how, for the THRYL platform. lives in Resources so the client
    /// can find it from any scene without wiring. the guide says to validate against staging before
    /// switching to production, so staging is the default.
    /// </summary>
    [CreateAssetMenu(fileName = "ThrylConfig", menuName = "Scriptable Objects/THRYL Config")]
    public class ThrylConfig : ScriptableObject
    {
        public const string ResourceName = "ThrylConfig";

        public ThrylEnvironment environment = ThrylEnvironment.Staging;
        public string stagingBaseUrl = "https://thryl-staging.zapto.org";
        public string productionBaseUrl = "https://thryl-prod.com";
        public string scorePath = "/in-game-score/points";

        [Header("Behaviour")]
        [Tooltip("Post the score when a race finishes. Off leaves everything else working with no network traffic.")]
        public bool submitOnFinish = true;
        [Tooltip("Seconds before a score request is abandoned")]
        public int requestTimeoutSeconds = 15;
        [Tooltip("Retries after a network failure (not after a 4xx, which will not change)")]
        public int retries = 2;
        [Tooltip("Send custom_game_id as a number when it parses as one; the contract shows a number, the reference implementation sends a string")]
        public bool sendGameIdAsNumber = true;

        [Header("Testing outside the platform")]
        [Tooltip("In the editor or a standalone build there is no page URL. This one is parsed instead, so the flow can be exercised. Leave empty to run without a platform session.")]
        public string editorLaunchUrl = "";
        [Tooltip("Log every request and response to the console")]
        public bool verbose = true;

        public string BaseUrl
        {
            get { return (environment == ThrylEnvironment.Production ? productionBaseUrl : stagingBaseUrl).TrimEnd('/'); }
        }

        public string ScoreUrl { get { return BaseUrl + scorePath; } }
    }
}
