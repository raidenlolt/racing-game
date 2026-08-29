using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpinMotion
{
    /// <summary>
    /// track picker. same minus/plus widget as the other selectors, but the label shows the track
    /// name and the choice is only acted on when Play is pressed, so flicking through the list does
    /// not thrash scene loads.
    ///
    /// it opens on whichever track is actually loaded rather than on index zero, so the menu always
    /// tells the truth about where you are.
    /// </summary>
    public class MapSelectorGUI : SelectorGUI
    {
        public MapCatalogue mapCatalogue;

        [Tooltip("The menu panel this selector lives in. Hidden while there is only one track to pick, since a chooser with one choice is just a dead control. Reappears on its own if more tracks are added.")]
        public GameObject containerToHide;

        private void Awake()
        {
            min = 0;
            max = mapCatalogue != null ? Mathf.Max(0, mapCatalogue.Count - 1) : 0;

            // the script itself keeps running so RaceData.MapSelected is still set; only the panel goes
            if (containerToHide != null)
                containerToHide.SetActive(mapCatalogue != null && mapCatalogue.Count > 1);

            var current = mapCatalogue != null
                ? mapCatalogue.IndexOfScene(SceneManager.GetActiveScene().name)
                : -1;

            // prefer a selection carried in from a previous menu, then the scene we are actually in
            var start = RaceData.MapSelected >= 0 ? RaceData.MapSelected : current;
            if (start < 0) start = 0;

            defaultQuantity = Mathf.Clamp(start, min, max);
        }

        protected override void QuantityUpdated()
        {
            RaceData.MapSelected = quantity;

            if (mapCatalogue == null || mapCatalogue.Count == 0)
            {
                if (quantityTMP != null) quantityTMP.text = "-";
                return;
            }

            var entry = mapCatalogue.Get(quantity);
            if (quantityTMP != null && entry != null) quantityTMP.text = entry.displayName;
        }
    }
}
