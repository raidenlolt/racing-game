using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
/// <summary>
/// start race and switch main menu UI with race UI
/// </summary>
namespace SpinMotion
{
    public class MenuGUI : MonoBehaviour
    {
        public GameEvents gameEvents;
        public Button playRaceButton;
        public GameObject menuUI;
        public GameObject raceUI;

        [Tooltip("Track list the map selector picks from. When the chosen track is not the one already loaded, pressing Play loads it instead of racing here.")]
        public MapCatalogue mapCatalogue;

        private void Awake()
        {
            playRaceButton.onClick.AddListener(OnClickPlayRace);
        }

        private void Start()
        {
            // arriving from another track's menu: the choice was already made over there, so drop
            // straight into the countdown rather than making the player walk through a second menu
            if (!RaceData.AutoStartRace) return;
            RaceData.AutoStartRace = false;
            StartCoroutine(AutoStartNextFrame());
        }

        /// <summary>
        /// the wait is not cosmetic. starting the race directly from Start races every other Start in
        /// the scene, and two of them matter:
        ///
        /// AIWaypoints repopulates the shared AIWaypointSet in its own Start. that set is a
        /// ScriptableObject, so on a fresh scene it still holds the transforms of the track we just
        /// left. spawning AI before it is refilled makes AIWaypointTracker read destroyed transforms.
        ///
        /// and a car spawned during another object's Start can reach FixedUpdate before its own Start
        /// has assigned CarController's rigidbody, which null-refs on the first Move.
        ///
        /// one frame puts the spawn safely after every Start has run.
        /// </summary>
        private System.Collections.IEnumerator AutoStartNextFrame()
        {
            yield return null;
            StartRaceHere();
        }

        private void OnClickPlayRace()
        {
            if (TryLoadSelectedMap()) return;
            StartRaceHere();
        }

        /// <summary>
        /// loads the chosen track when it is not the one already open. returns true if a load was
        /// started, in which case this scene is going away and there is nothing else to do
        /// </summary>
        private bool TryLoadSelectedMap()
        {
            if (mapCatalogue == null || mapCatalogue.Count == 0) return false;

            var entry = mapCatalogue.Get(RaceData.MapSelected);
            if (entry == null || string.IsNullOrEmpty(entry.sceneName)) return false;
            if (entry.sceneName == SceneManager.GetActiveScene().name) return false;

            // survives the load because it is a static, and is consumed by Start on the far side
            RaceData.AutoStartRace = true;
            SceneManager.LoadScene(entry.sceneName);
            return true;
        }

        private void StartRaceHere()
        {
            menuUI.SetActive(false);
            raceUI.SetActive(true);
            gameEvents.OnClickPlayRaceEvent.Invoke();
        }
    }
}
