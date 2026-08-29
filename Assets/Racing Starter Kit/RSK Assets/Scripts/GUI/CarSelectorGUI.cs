using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// car picker. reuses the base SelectorGUI so it gets the same minus/plus buttons as laps and bot
    /// count, then overwrites the label with the car's name instead of the raw index and pushes the
    /// choice into RaceData for PlayersSpawner to read when the race starts.
    ///
    /// the stat bars are normalised across the whole roster rather than against absolute numbers, so
    /// the fastest car always shows a full speed bar and the comparison between cars stays readable
    /// no matter how the individual figures are tuned later.
    /// </summary>
    public class CarSelectorGUI : SelectorGUI
    {
        public CarCatalogue carCatalogue;

        [Header("Preview (optional)")]
        public CarPreview preview;
        [Tooltip("The cars standing on the track behind the menu. Driven instead of the render-texture preview when both are present.")]
        public MenuCarShowcase showcase;

        [Header("Stat bars (optional)")]
        public Image topSpeedBar;
        public Image accelerationBar;
        public Image handlingBar;
        public TMP_Text statsTMP;

        private void Awake()
        {
            min = 0;
            max = carCatalogue != null ? Mathf.Max(0, carCatalogue.Count - 1) : 0;
            // keep the inherited default in range, or SelectorGUI logs an error on Start
            defaultQuantity = Mathf.Clamp(RaceData.CarSelected, min, max);
        }

        protected override void QuantityUpdated()
        {
            RaceData.CarSelected = quantity;

            if (carCatalogue == null || carCatalogue.Count == 0)
            {
                if (quantityTMP != null) quantityTMP.text = "-";
                return;
            }

            var entry = carCatalogue.Get(quantity);
            // the base class already wrote the index into this label, so replace it with the name
            if (quantityTMP != null && entry != null) quantityTMP.text = entry.displayName;

            UpdateStats();
            if (showcase != null) showcase.Show(quantity);
            else if (preview != null) preview.Show(quantity);
        }

        private void UpdateStats()
        {
            float speed, torque, steer;
            if (!carCatalogue.TryGetStats(quantity, out speed, out torque, out steer))
            {
                SetBar(topSpeedBar, 0f);
                SetBar(accelerationBar, 0f);
                SetBar(handlingBar, 0f);
                if (statsTMP != null) statsTMP.text = string.Empty;
                return;
            }

            float maxSpeed = 0f, maxTorque = 0f, maxSteer = 0f;
            for (int i = 0; i < carCatalogue.Count; i++)
            {
                float s, t, a;
                if (!carCatalogue.TryGetStats(i, out s, out t, out a)) continue;
                if (s > maxSpeed) maxSpeed = s;
                if (t > maxTorque) maxTorque = t;
                if (a > maxSteer) maxSteer = a;
            }

            SetBar(topSpeedBar, maxSpeed > 0f ? speed / maxSpeed : 0f);
            SetBar(accelerationBar, maxTorque > 0f ? torque / maxTorque : 0f);
            // more steering lock means it turns in harder, which is what "handling" means to a player
            SetBar(handlingBar, maxSteer > 0f ? steer / maxSteer : 0f);

            if (statsTMP != null)
                statsTMP.text = Mathf.RoundToInt(speed) + " MPH";
        }

        private static void SetBar(Image bar, float fraction)
        {
            if (bar == null) return;
            bar.fillAmount = Mathf.Clamp01(fraction);
        }
    }
}
