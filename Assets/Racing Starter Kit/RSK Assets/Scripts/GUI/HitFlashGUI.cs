using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// a red edge flash on the HUD when the player's car is hit, heavier on the side the hit came
    /// from. the vignette texture is drawn in code at start-up so the effect needs no art asset.
    ///
    /// the flash decays on unscaled time, so it still fades during a hit-stop.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class HitFlashGUI : MonoBehaviour
    {
        public GameEvents gameEvents;

        public Color flashColor = new Color(1f, 0.18f, 0.08f);
        // QA read the flash as "blinking" on the second track: the pack rear-ends a slow player over
        // and over, and at 0.75 alpha every tap washed the whole screen red. it is now an edge tint,
        // it needs a real hit to show at all, and it will not fire twice in quick succession
        [Tooltip("Alpha at a hard hit")]
        [Range(0f, 1f)] public float maxAlpha = 0.42f;
        [Tooltip("Impact speed (m/s) that reaches maxAlpha")]
        public float fullAlphaImpactSpeed = 12f;
        [Tooltip("Hits slower than this (m/s) do not flash at all")]
        public float minimumImpactSpeed = 4f;
        [Tooltip("Minimum real seconds between flashes")]
        public float minimumInterval = 1.2f;
        [Tooltip("How far, in canvas units, the vignette shifts towards the hit so one edge reads heavier")]
        public float directionalOffset = 140f;
        public float fadePerSecond = 3.2f;

        private Image image;
        private RectTransform rect;
        private float alpha;
        private Vector2 offset;
        private float lastFlashTime = -999f;

        private void Awake()
        {
            image = GetComponent<Image>();
            rect = (RectTransform)transform;
            image.raycastTarget = false;
            image.sprite = BuildVignette(256);
            image.type = Image.Type.Simple;
            image.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);

            if (gameEvents != null)
                gameEvents.PlayerHitEvent.AddListener(OnPlayerHit);
        }

        private void OnDestroy()
        {
            if (gameEvents != null)
                gameEvents.PlayerHitEvent.RemoveListener(OnPlayerHit);
        }

        private void OnPlayerHit(float impactSpeed, Vector3 localDirection)
        {
            if (impactSpeed < minimumImpactSpeed) return;
            if (Time.unscaledTime - lastFlashTime < minimumInterval) return;
            lastFlashTime = Time.unscaledTime;

            var strength = Mathf.Clamp01(impactSpeed / fullAlphaImpactSpeed);
            alpha = Mathf.Max(alpha, maxAlpha * strength);
            // the hit came from behind: the vignette slides down so the bottom edge is the heavy one.
            // a side hit slides it towards that side
            offset = new Vector2(localDirection.x, localDirection.z) * directionalOffset;
        }

        private void Update()
        {
            if (alpha <= 0f) return;
            alpha = Mathf.MoveTowards(alpha, 0f, fadePerSecond * Time.unscaledDeltaTime);
            image.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, offset * (alpha / Mathf.Max(0.01f, maxAlpha)),
                                                 10f * Time.unscaledDeltaTime);
            if (alpha <= 0f) rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>transparent middle, opaque rim, smooth in between</summary>
        private static Sprite BuildVignette(int size)
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
                // squared-off radial falloff so the corners fill and the edges read as a rim
                var d = Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 3f) + Mathf.Pow(Mathf.Abs(dy), 3f), 1f / 3f);
                var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
