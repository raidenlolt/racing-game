using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// draws the nitro gauge and owns the fire button. the gauge turns red while the charge sits in
    /// the perfect-nitro window, which is the only cue the player gets that tapping now is worth more
    /// than tapping a second later, so it has to be unmissable.
    ///
    /// listens to the player's nitro events rather than polling a car reference, because the player
    /// car does not exist until the race is spawned.
    /// </summary>
    public class NitroGUI : MonoBehaviour
    {
        public GameEvents gameEvents;

        [Header("Gauge")]
        public Image nitroFillImage;
        public Color normalColor = new Color(0.25f, 0.7f, 1f);
        public Color redZoneColor = new Color(1f, 0.35f, 0.15f);
        [Tooltip("Pulse speed of the gauge while it is in the red zone")]
        public float redZonePulseSpeed = 6f;

        [Header("Controls")]
        public Button fireNitroButton;
        [Tooltip("Desktop shortcut for testing without touching the button")]
        public KeyCode fireNitroKey = KeyCode.LeftShift;

        private bool inRedZone;

        private void Awake()
        {
            if (gameEvents != null)
            {
                gameEvents.PlayerNitroChangedEvent.AddListener(OnNitroChanged);
                gameEvents.PreRaceUpdateGuiEvent.AddListener(OnPreRaceUpdateGui);
            }

            if (fireNitroButton != null)
                fireNitroButton.onClick.AddListener(OnClickFireNitro);
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.PlayerNitroChangedEvent.RemoveListener(OnNitroChanged);
            gameEvents.PreRaceUpdateGuiEvent.RemoveListener(OnPreRaceUpdateGui);
        }

        private void OnPreRaceUpdateGui()
        {
            inRedZone = false;
            if (nitroFillImage == null) return;
            nitroFillImage.fillAmount = 0f;
            nitroFillImage.color = normalColor;
        }

        private void OnNitroChanged(float charge, bool isInRedZone)
        {
            inRedZone = isInRedZone;
            if (nitroFillImage == null) return;
            nitroFillImage.fillAmount = charge;
        }

        private void Update()
        {
            if (nitroFillImage != null)
            {
                if (inRedZone)
                {
                    // pulse between the two colours so the window reads at a glance while driving
                    var t = (Mathf.Sin(Time.time * redZonePulseSpeed) + 1f) * 0.5f;
                    nitroFillImage.color = Color.Lerp(normalColor, redZoneColor, t);
                }
                else
                {
                    nitroFillImage.color = normalColor;
                }
            }

            if (Input.GetKeyDown(fireNitroKey))
                OnClickFireNitro();
        }

        private void OnClickFireNitro()
        {
            if (gameEvents != null)
                gameEvents.OnClickFireNitroEvent.Invoke();
        }
    }
}
