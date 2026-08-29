using TMPro;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// shows the player's road speed.
    ///
    /// the car reference cannot be resolved in Awake, because the player car is instantiated by
    /// PlayersSpawner when a race starts, long after the GUI prefab is loaded. so it is looked up
    /// lazily and cached, and looked up again if it ever goes away -- picking a different car in the
    /// menu spawns a new one and destroys the old.
    ///
    /// speed is read from CarController.CurrentSpeed, which reports mph, the same unit the car's own
    /// top speed is authored in.
    /// </summary>
    public class SpeedGUI : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;

        [Header("Readout")]
        public TMP_Text speedTMP;
        public TMP_Text unitTMP;

        [Tooltip("Show km/h instead of mph. The car's speed is measured in mph, so this only converts for display.")]
        public bool useKmh;

        private const float MphToKmh = 1.609344f;

        private CarController playerCar;

        private void Awake()
        {
            if (gameEvents != null)
                gameEvents.PreRaceUpdateGuiEvent.AddListener(OnPreRaceUpdateGui);
            ApplyUnitLabel();
        }

        private void OnDestroy()
        {
            if (gameEvents != null)
                gameEvents.PreRaceUpdateGuiEvent.RemoveListener(OnPreRaceUpdateGui);
        }

        private void OnPreRaceUpdateGui()
        {
            // a new race may be a different car, so drop the cached one and show a clean zero
            playerCar = null;
            ApplyUnitLabel();
            if (speedTMP != null) speedTMP.text = "0";
        }

        private void ApplyUnitLabel()
        {
            if (unitTMP != null) unitTMP.text = useKmh ? "KM/H" : "MPH";
        }

        private void Update()
        {
            if (speedTMP == null) return;

            if (playerCar == null) ResolvePlayerCar();
            if (playerCar == null) { speedTMP.text = "0"; return; }

            var speed = playerCar.CurrentSpeed;
            if (useKmh) speed *= MphToKmh;
            // reversing still reads as speed on the dial, and rounding keeps the number from flickering
            speedTMP.text = Mathf.RoundToInt(Mathf.Abs(speed)).ToString();
        }

        /// <summary>
        /// finds the car the player is driving. the player is the only car carrying a NitroSystem
        /// flagged isPlayer -- the bots each have one too, which is how they fire nitro of their own.
        /// </summary>
        private void ResolvePlayerCar()
        {
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                var nitro = car.GetComponentInChildren<NitroSystem>();
                if (nitro != null && nitro.isPlayer)
                {
                    playerCar = car;
                    return;
                }
            }
        }
    }
}
