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

        [Tooltip("How fast the bar chases the real charge. The old bar snapped, which made a drain unreadable.")]
        public float fillLerpSpeed = 10f;
        [Tooltip("The bar flashes this colour when a level is fired, then settles back")]
        public Color fireFlashColor = Color.white;

        [Header("Speed lines")]
        [Tooltip("Full-screen overlay whose alpha follows the boost. The streak texture is drawn in code at start-up.")]
        public Image speedLinesImage;
        public Vector3 speedLineAlphaPerLevel = new Vector3(0.22f, 0.36f, 0.5f);
        public float speedLineBlendSpeed = 4f;

        [Header("Controls")]
        public Button fireNitroButton;
        [Tooltip("Desktop shortcut for testing without touching the button")]
        public KeyCode fireNitroKey = KeyCode.LeftShift;

        private bool inRedZone;
        private float targetFill;
        private float flash;
        private NitroLevel level = NitroLevel.None;
        private float speedLineAlpha;

        private void Awake()
        {
            if (gameEvents != null)
            {
                gameEvents.PlayerNitroChangedEvent.AddListener(OnNitroChanged);
                gameEvents.PreRaceUpdateGuiEvent.AddListener(OnPreRaceUpdateGui);
                gameEvents.PlayerNitroFiredEvent.AddListener(OnNitroFired);
                gameEvents.PlayerNitroEndedEvent.AddListener(OnNitroEnded);
            }

            if (fireNitroButton != null)
                fireNitroButton.onClick.AddListener(OnClickFireNitro);

            if (speedLinesImage != null)
            {
                speedLinesImage.sprite = BuildSpeedLines(256);
                speedLinesImage.raycastTarget = false;
                speedLinesImage.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.PlayerNitroChangedEvent.RemoveListener(OnNitroChanged);
            gameEvents.PreRaceUpdateGuiEvent.RemoveListener(OnPreRaceUpdateGui);
            gameEvents.PlayerNitroFiredEvent.RemoveListener(OnNitroFired);
            gameEvents.PlayerNitroEndedEvent.RemoveListener(OnNitroEnded);
        }

        private void OnPreRaceUpdateGui()
        {
            inRedZone = false;
            level = NitroLevel.None;
            flash = 0f;
            targetFill = 0f;
            if (nitroFillImage == null) return;
            nitroFillImage.fillAmount = 0f;
            nitroFillImage.color = normalColor;
        }

        private void OnNitroChanged(float charge, bool isInRedZone)
        {
            inRedZone = isInRedZone;
            targetFill = charge;
        }

        private void OnNitroFired(NitroLevel newLevel, bool perfect)
        {
            level = newLevel;
            flash = 1f;
        }

        private void OnNitroEnded()
        {
            level = NitroLevel.None;
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;

            if (nitroFillImage != null)
            {
                nitroFillImage.fillAmount = Mathf.Lerp(nitroFillImage.fillAmount, targetFill, fillLerpSpeed * dt);

                Color colour;
                if (inRedZone)
                {
                    // pulse between the two colours so the window reads at a glance while driving
                    var t = (Mathf.Sin(Time.time * redZonePulseSpeed) + 1f) * 0.5f;
                    colour = Color.Lerp(normalColor, redZoneColor, t);
                }
                else
                {
                    colour = normalColor;
                }
                flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
                nitroFillImage.color = Color.Lerp(colour, fireFlashColor, flash);
            }

            if (speedLinesImage != null)
            {
                var target = 0f;
                switch (level)
                {
                    case NitroLevel.One: target = speedLineAlphaPerLevel.x; break;
                    case NitroLevel.Two: target = speedLineAlphaPerLevel.y; break;
                    case NitroLevel.Three: target = speedLineAlphaPerLevel.z; break;
                }
                speedLineAlpha = Mathf.Lerp(speedLineAlpha, target, speedLineBlendSpeed * dt);
                speedLinesImage.color = new Color(1f, 1f, 1f, speedLineAlpha);
                // a slow rotation keeps the streaks alive without a second texture
                speedLinesImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 6f);
            }

            if (Input.GetKeyDown(fireNitroKey))
                OnClickFireNitro();
        }

        private void OnClickFireNitro()
        {
            if (gameEvents != null)
                gameEvents.OnClickFireNitroEvent.Invoke();
        }

        /// <summary>
        /// radial streaks, transparent in the middle where the road is, so the overlay says "speed"
        /// without hiding what the player is steering at. drawn once at start-up
        /// </summary>
        private static Sprite BuildSpeedLines(int size)
        {
            const int streakCount = 110;
            var rng = new System.Random(42);
            var angles = new float[streakCount];
            var widths = new float[streakCount];
            var starts = new float[streakCount];
            var gains = new float[streakCount];
            for (int i = 0; i < streakCount; i++)
            {
                angles[i] = (float)(rng.NextDouble() * Mathf.PI * 2f);
                widths[i] = 0.004f + (float)rng.NextDouble() * 0.012f;
                starts[i] = 0.35f + (float)rng.NextDouble() * 0.35f;
                gains[i] = 0.5f + (float)rng.NextDouble() * 0.5f;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var dx = (x + 0.5f - half) / half;
                var dy = (y + 0.5f - half) / half;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx);
                var value = 0f;
                for (int i = 0; i < streakCount; i++)
                {
                    if (r < starts[i]) continue;
                    var da = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, angles[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    // angular width narrows with radius so streaks stay thin lines, not wedges
                    var w = widths[i] / Mathf.Max(0.2f, r);
                    if (da > w) continue;
                    var along = Mathf.InverseLerp(starts[i], 1.2f, r);
                    value = Mathf.Max(value, (1f - da / w) * along * gains[i]);
                }
                var a = Mathf.Clamp01(value) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, r));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
