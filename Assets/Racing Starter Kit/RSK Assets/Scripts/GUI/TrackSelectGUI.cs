using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// the standalone track picker that fills the LevelSelect scene.
    ///
    /// it reads MapCatalogue rather than holding a list of its own, so the tracks offered here and
    /// the tracks the in-race menu offers cannot drift apart. cards are matched to catalogue entries
    /// by index, which is the same convention RaceData.MapSelected already uses.
    ///
    /// picking a card only records the choice; nothing loads until START, so flicking through the
    /// list does not thrash scene loads. START deliberately does not set RaceData.AutoStartRace:
    /// the track's own menu is where the car and the lap count get chosen, and dropping the player
    /// straight into the countdown would take both of those away. arriving with MapSelected already
    /// set is what makes that menu open on the track picked here.
    /// </summary>
    public class TrackSelectGUI : MonoBehaviour
    {
        [Tooltip("Track list to offer. Cards are matched to entries by index.")]
        public MapCatalogue mapCatalogue;

        [Tooltip("One card per track, in the same order as the catalogue.")]
        public List<Button> cards = new List<Button>();

        [Tooltip("The thumbnail on each card, in the same order as the cards.")]
        public List<Image> thumbnails = new List<Image>();

        public Button startButton;

        [Tooltip("Optional label that names the currently selected track.")]
        public TMP_Text selectionLabel;

        [Header("Card colours")]
        public Color cardIdle = new Color(0.16f, 0.19f, 0.24f, 1f);
        public Color cardChosen = new Color(0.16f, 0.85f, 1f, 1f);
        public Color textIdle = new Color(1f, 1f, 1f, 0.82f);
        public Color textChosen = new Color(0.03f, 0.06f, 0.09f, 1f);

        private int chosen;

        private void Awake()
        {
            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                // captured per iteration: one shared variable and every card would pick the last track
                var index = i;
                cards[i].onClick.AddListener(delegate { Choose(index); });
            }

            if (startButton != null) startButton.onClick.AddListener(OnClickStart);

            ApplyThumbnails();
        }

        /// <summary>
        /// the shots come off the catalogue rather than being dropped onto the cards, so replacing
        /// a track's picture is one edit on the asset and never a hunt through this scene
        /// </summary>
        private void ApplyThumbnails()
        {
            for (var i = 0; i < thumbnails.Count; i++)
            {
                if (thumbnails[i] == null) continue;

                var entry = mapCatalogue != null && i < mapCatalogue.Count ? mapCatalogue.Get(i) : null;
                var shot = entry != null ? entry.thumbnail : null;

                thumbnails[i].sprite = shot;
                // a card with no shot yet shows the card behind it rather than a white box
                thumbnails[i].enabled = shot != null;
            }
        }

        private void Start()
        {
            // opens on the track you last chose, so coming back from a race lands where you left
            var start = RaceData.MapSelected;
            if (start < 0 || start >= cards.Count) start = 0;
            Choose(start);
        }

        private void Choose(int index)
        {
            chosen = index;
            RaceData.MapSelected = index;

            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                var isChosen = i == index;

                var background = cards[i].GetComponent<Image>();
                if (background != null) background.color = isChosen ? cardChosen : cardIdle;

                foreach (var label in cards[i].GetComponentsInChildren<TMP_Text>(true))
                    label.color = isChosen ? textChosen : textIdle;
            }

            var entry = mapCatalogue != null ? mapCatalogue.Get(index) : null;
            if (selectionLabel != null) selectionLabel.text = entry != null ? entry.displayName : "-";
            if (startButton != null)
                startButton.interactable = entry != null && !string.IsNullOrEmpty(entry.sceneName);
        }

        private void OnClickStart()
        {
            var entry = mapCatalogue != null ? mapCatalogue.Get(chosen) : null;
            if (entry == null || string.IsNullOrEmpty(entry.sceneName))
            {
                Debug.LogWarning("[TrackSelect] the chosen track has no scene name, nothing to load");
                return;
            }

            // a name missing from Build Settings fails silently in a player, so say so here instead
            if (!Application.CanStreamedLevelBeLoaded(entry.sceneName))
            {
                Debug.LogError("[TrackSelect] " + entry.sceneName + " is not an enabled scene in Build " +
                               "Settings, so it cannot be loaded");
                return;
            }

            SceneManager.LoadScene(entry.sceneName);
        }
    }
}
