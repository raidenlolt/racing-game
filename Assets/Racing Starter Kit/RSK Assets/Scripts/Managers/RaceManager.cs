using UnityEngine;
/// <summary>
/// setup the game state to begin, restart and finish a race
/// and manage current race data and flags
/// </summary>
namespace SpinMotion
{
    public class RaceManager : MonoBehaviour
    {
        public RaceManagerItem raceManagerRuntimeItem;
        public GameEvents gameEvents;
        [Header("Race Timer Settings:")]
        public bool isTimedRace;
        public int raceTimerSeconds;

        public bool IsRaceInProgress() { return isRaceInProgress; }
        private bool isRaceInProgress;

        private void Awake()
        {
            raceManagerRuntimeItem.Set(this);

            gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
            gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);
            // gui buttons:
            gameEvents.OnClickPlayRaceEvent.AddListener(OnPlayRace);
            gameEvents.OnClickRestartRaceEvent.AddListener(OnRestartRace);
        }

        private void OnPlayRace()
        {
            gameEvents.SpawnPlayersEvent.Invoke();
            gameEvents.PreRaceUpdateGuiEvent.Invoke();
            gameEvents.PlayPreRaceCountdownEvent.Invoke();
            gameEvents.ChangeToRaceCamerasEvent.Invoke();
        }

        // 3,2,1 countdown ended:
        private void OnRaceStarted()
        {
            isRaceInProgress = true;
        }

        private void OnRaceFinished(RaceFinishType raceFinishType)
        {
            isRaceInProgress = false;
        }

        private void OnRestartRace()
        {
            isRaceInProgress = false;
            
            gameEvents.RestartRaceEvent.Invoke();
            gameEvents.PreRaceUpdateGuiEvent.Invoke();
            gameEvents.PlayPreRaceCountdownEvent.Invoke();
        }
    }

    public class RaceData
    {
        public static int CheckpointsCount;
        /// <summary>
        /// fixed field size. the menu no longer exposes a bot count, so this value is what every race
        /// uses. each track needs at least this many spawn points plus one for the player
        /// </summary>
        public static int AiBotsSelected = 6;
        public static int LapsSelected = 6;

        /// <summary>index into the CarCatalogue of the car the player picked in the menu</summary>
        public static int CarSelected = 0;
        /// <summary>index into the MapCatalogue of the track the player picked in the menu</summary>
        public static int MapSelected = -1;

        /// <summary>
        /// set just before loading a different track scene from the menu, and consumed by MenuGUI on
        /// the way in so the race starts immediately instead of showing that track's own menu again.
        /// these are statics rather than a ScriptableObject on purpose: a static survives a scene
        /// load, which is exactly the lifetime a menu selection needs, and it matches how laps and
        /// bot count were already carried
        /// </summary>
        public static bool AutoStartRace;
    }
}