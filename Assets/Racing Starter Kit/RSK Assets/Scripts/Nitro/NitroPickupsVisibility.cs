using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// keeps the nitro bottles off screen while the start menu is up.
    ///
    /// the bottles are spaced along the racing line, and the menu camera is aimed at a spot on that
    /// same line -- the showroom stage sits on the track. so a row of bottles lands square in front
    /// of the camera and floats over the car being shown off.
    ///
    /// they are hidden rather than moved, because their positions are the whole point of where they
    /// are. this component stays enabled while its children go dark, which is why the bottles are
    /// switched off individually instead of switching off the container they live in -- a disabled
    /// container could not listen for the race starting and would never bring them back.
    /// </summary>
    public class NitroPickupsVisibility : MonoBehaviour
    {
        public GameEvents gameEvents;

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

        private void Start()
        {
            // Start rather than Awake: the bottles' own Awake caches its collider and audio first
            SetBottlesVisible(false);
        }

        private void OnClickPlayRace()
        {
            SetBottlesVisible(true);
        }

        private void SetBottlesVisible(bool visible)
        {
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(visible);
        }
    }
}
