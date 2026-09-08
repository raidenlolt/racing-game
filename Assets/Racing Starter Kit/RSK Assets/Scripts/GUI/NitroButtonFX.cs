using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// the nitro button answers the player's tap. before this it was a static icon: firing a level
    /// changed the camera and nothing under the thumb. now a tap punches the icon, flashes it in the
    /// level's colour, lights a level pip, and while the boost runs the icon pulses over a slowly
    /// turning glow ring. below the minimum charge the icon dims so a dead tap is expected.
    ///
    /// everything animates on unscaled time: a pause or the finish sequence's slow-motion must not
    /// leave the button frozen mid-punch.
    /// </summary>
    public class NitroButtonFX : MonoBehaviour
    {
        public GameEvents gameEvents;

        [Header("Targets, wired by the setup pass")]
        public RectTransform buttonRect;
        public Image buttonImage;
        public Image glowRing;
        public Image[] pips = new Image[3];

        [Header("Colours")]
        public Color idleColor = Color.white;
        public Color levelOneColor = new Color(0.45f, 0.78f, 1f);
        public Color levelTwoColor = new Color(1f, 0.62f, 0.2f);
        public Color levelThreeColor = new Color(1f, 0.93f, 0.8f);
        public Color perfectColor = new Color(1f, 1f, 1f);
        public Color pipOffColor = new Color(1f, 1f, 1f, 0.25f);

        [Header("Motion")]
        public float punchScale = 1.28f;
        public float punchSeconds = 0.26f;
        [Tooltip("Scale wobble while a boost is running")]
        public float pulseAmount = 0.06f;
        public float pulseSpeed = 7f;
        public float ringSpinDegreesPerSecond = 80f;
        [Tooltip("Icon alpha when the gauge is too low to fire")]
        [Range(0f, 1f)] public float dimAlpha = 0.4f;
        [Tooltip("Mirror of NitroSystem.minimumChargeToFire: charge below which the icon dims")]
        public float minimumChargeToFire = 0.08f;

        private NitroLevel level = NitroLevel.None;
        private bool perfect;
        private float charge;
        private float punchTime = -1f;
        private Color flashColor = Color.white;
        private float flash;
        private Vector3 baseScale = Vector3.one;
        private float ringAlpha;

        private void Awake()
        {
            if (buttonRect != null) baseScale = buttonRect.localScale;
            if (glowRing != null)
            {
                glowRing.sprite = BuildRing(128);
                glowRing.raycastTarget = false;
                glowRing.color = new Color(1f, 1f, 1f, 0f);
            }

            if (gameEvents == null) return;
            gameEvents.PlayerNitroFiredEvent.AddListener(OnFired);
            gameEvents.PlayerNitroEndedEvent.AddListener(OnEnded);
            gameEvents.PlayerNitroChangedEvent.AddListener(OnChanged);
            gameEvents.PreRaceUpdateGuiEvent.AddListener(OnPreRace);
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.PlayerNitroFiredEvent.RemoveListener(OnFired);
            gameEvents.PlayerNitroEndedEvent.RemoveListener(OnEnded);
            gameEvents.PlayerNitroChangedEvent.RemoveListener(OnChanged);
            gameEvents.PreRaceUpdateGuiEvent.RemoveListener(OnPreRace);
        }

        private void OnPreRace()
        {
            level = NitroLevel.None;
            perfect = false;
            flash = 0f;
            punchTime = -1f;
        }

        private void OnFired(NitroLevel newLevel, bool isPerfect)
        {
            level = newLevel;
            perfect = perfect || isPerfect;
            punchTime = Time.unscaledTime;
            flashColor = isPerfect ? perfectColor : ColorFor(newLevel);
            flash = 1f;
        }

        private void OnEnded()
        {
            level = NitroLevel.None;
            perfect = false;
        }

        private void OnChanged(float newCharge, bool inRedZone)
        {
            charge = newCharge;
        }

        private Color ColorFor(NitroLevel l)
        {
            switch (l)
            {
                case NitroLevel.One: return levelOneColor;
                case NitroLevel.Two: return levelTwoColor;
                case NitroLevel.Three: return levelThreeColor;
                default: return idleColor;
            }
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var active = level != NitroLevel.None;

            // scale: a punch that overshoots and settles, plus a slow pulse while running
            var scale = 1f;
            if (punchTime >= 0f)
            {
                var t = (Time.unscaledTime - punchTime) / punchSeconds;
                if (t < 1f)
                    scale += (punchScale - 1f) * Mathf.Sin(t * Mathf.PI);   // up and back
                else
                    punchTime = -1f;
            }
            if (active)
                scale += Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
            if (buttonRect != null) buttonRect.localScale = baseScale * scale;

            // colour: the flash decays into the level tint, or into idle, dimmed when empty
            flash = Mathf.MoveTowards(flash, 0f, dt / Mathf.Max(0.01f, punchSeconds * 1.5f));
            var tint = active ? Color.Lerp(idleColor, perfect ? perfectColor : ColorFor(level), 0.6f) : idleColor;
            tint = Color.Lerp(tint, flashColor, flash);
            var canFire = charge >= minimumChargeToFire;
            tint.a = active || canFire ? 1f : dimAlpha;
            if (buttonImage != null) buttonImage.color = tint;

            // ring: fades in with the boost, spins faster with the level
            var ringTarget = active ? 0.85f : 0f;
            ringAlpha = Mathf.MoveTowards(ringAlpha, ringTarget, dt * 4f);
            if (glowRing != null)
            {
                var ringColor = perfect ? perfectColor : ColorFor(level == NitroLevel.None ? NitroLevel.One : level);
                ringColor.a = ringAlpha;
                glowRing.color = ringColor;
                var spin = ringSpinDegreesPerSecond * (1f + (active ? ((int)level - 1) * 0.5f : 0f));
                glowRing.rectTransform.Rotate(0f, 0f, -spin * dt);
                glowRing.rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * 0.5f) * 0.04f);
            }

            // pips: one per level engaged
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null) continue;
                var lit = (int)level > i;
                pips[i].color = lit ? (perfect ? perfectColor : ColorFor((NitroLevel)(i + 1))) : pipOffColor;
                pips[i].rectTransform.localScale = Vector3.one * (lit ? 1.15f : 1f);
            }
        }

        /// <summary>a soft annulus, fully transparent inside and out</summary>
        private static Sprite BuildRing(int size)
        {
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
                // ring centred at r = 0.8, soft on both sides, with a faint inner glow
                var ring = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.09f, 2f));
                var glow = Mathf.Clamp01(1f - r / 0.8f) * 0.18f;
                // a few brighter segments so the spin is visible
                var angle = Mathf.Atan2(dy, dx);
                var segments = 0.65f + 0.35f * Mathf.Max(0f, Mathf.Sin(angle * 3f));
                var a = Mathf.Clamp01(ring * segments + glow);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
