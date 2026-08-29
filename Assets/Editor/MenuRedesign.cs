using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// rebuilds the main menu: strips the asset kit's branding, promotes the car to the hero of the
/// screen, and restyles everything dark with a single bright accent so the menu matches the race HUD
/// instead of looking like a different game.
///
/// what goes:
///   RSK          the "Racing Starter Kit" title plate
///   Ad Panel     "Racing Openworld Kit now available on the Asset Store!"
///
/// removing those frees 275px of the 900px tall canvas, which is what makes room for the car. the
/// preview was 196x110, a thumbnail; it becomes 1100x380, about nineteen times the area.
///
/// the layout groups here all have childControlWidth/Height off, so children keep their own
/// sizeDelta and resizing the preview is enough -- the vertical group re-flows everything under it.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class MenuRedesign
    {
        private const string PrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";

        /// <summary>the one bright colour. matches the nitro gauge so menu and HUD read as one game.</summary>
        private static readonly Color Accent = new Color(0.16f, 0.85f, 1f, 1f);
        /// <summary>near black, for text sitting on the accent</summary>
        private static readonly Color OnAccent = new Color(0.03f, 0.06f, 0.09f, 1f);
        /// <summary>
        /// panels are nearly opaque on purpose. at 0.72 enough of the track showed through that a
        /// panel over grass read visibly lighter than the identical panel over tarmac, which looked
        /// like a styling mistake rather than transparency.
        /// </summary>
        private static readonly Color PanelFill = new Color(0.02f, 0.03f, 0.04f, 0.93f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.19f, 0.24f, 1f);
        /// <summary>full screen scrim, dark enough to read against a sunlit racetrack</summary>
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.5f);

        [MenuItem("Tools/Racing/Rebuild Menu")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                StripBranding(root.transform);
                EnlargeCarPreview(root.transform);
                Restyle(root.transform);
                WireMapAutoHide(root.transform);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Menu] rebuilt");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void StripBranding(Transform root)
        {
            foreach (var name in new[] { "RSK", "Ad Panel" })
            {
                var t = Find(root, name);
                if (t == null) { Debug.Log("[Menu] '" + name + "' already gone"); continue; }
                Object.DestroyImmediate(t.gameObject);
                Debug.Log("[Menu] removed " + name);
            }
        }

        private static void EnlargeCarPreview(Transform root)
        {
            var holder = Find(root, "Selection Panels");
            var preview = Find(root, "Car Preview");
            if (preview == null) { Debug.LogError("[Menu] Car Preview not found"); return; }

            // the row the preview sits in has to grow with it, or the vertical group keeps
            // reserving the old 110px slot and the preview overlaps its neighbours
            if (holder != null) ((RectTransform)holder).sizeDelta = new Vector2(1200f, 400f);

            var rect = (RectTransform)preview;
            var before = rect.sizeDelta;
            rect.sizeDelta = new Vector2(1100f, 380f);

            // these are all already-serialised fields on the component, so changing their defaults in
            // the script would never reach this prefab; they have to be written here
            var cam = preview.GetComponent<CarPreview>();
            if (cam != null)
            {
                // the render texture is built from this and the panel's aspect; at thumbnail size 512
                // was plenty, at hero size it has to go up or the car turns soft
                cam.textureSize = 640;
                // a black plate was invisible at 196x110 and impossible to miss at 1100x380
                cam.background = new Color(0.06f, 0.07f, 0.09f, 0f);
                // the framing solver decides the distance now; this is only a floor
                cam.cameraDistance = 3f;
                cam.fieldOfView = 30f;
                // fills the panel height without the car's corners touching the edge as it turns
                cam.fillFraction = 0.92f;
                EditorUtility.SetDirty(cam);
            }

            var raw = preview.GetComponent<RawImage>();
            if (raw != null) raw.color = Color.white;

            Debug.Log("[Menu] car preview " + before.ToString("F0") + " -> " + rect.sizeDelta.ToString("F0")
                      + "  (" + (rect.sizeDelta.x * rect.sizeDelta.y / (before.x * before.y)).ToString("F0") + "x the area)");
        }

        private static void Restyle(Transform root)
        {
            // full screen scrim behind the menu.
            //
            // the sprite has to go, not just the colour. it was InputFieldBackground, a sliced sprite
            // carrying its own alpha, so the tint got multiplied down by it: asking for 50% black
            // produced a barely visible haze over a sunlit racetrack. a null sprite draws a flat quad
            // at exactly the alpha given.
            var menu = Find(root, "MenuUI");
            if (menu != null)
            {
                var img = menu.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = null;
                    img.type = Image.Type.Simple;
                    img.color = Scrim;
                }
            }

            // the three selector panels
            foreach (var container in new[] { "Laps Container", "Car Container", "Map Container" })
            {
                var t = Find(root, container);
                if (t == null) continue;

                var bg = Find(t, "Background");
                if (bg != null)
                {
                    var img = bg.GetComponent<Image>();
                    if (img != null) img.color = PanelFill;
                }

                // the label stays white, the value takes the accent so the eye lands on the choice
                foreach (var tmp in t.GetComponentsInChildren<TMP_Text>(true))
                {
                    var isValue = tmp.gameObject.name.Contains("Qty") || tmp.gameObject.name.Contains("Value");
                    tmp.color = isValue ? Accent : new Color(1f, 1f, 1f, 0.82f);
                }

                StyleStepButtons(t);
            }

            // PLAY becomes the one solid block of colour on the screen
            var play = Find(root, "Play BTN");
            if (play != null)
            {
                var img = play.GetComponent<Image>();
                if (img != null) img.color = Accent;

                var outline = play.GetComponent<Outline>();
                if (outline != null) outline.effectColor = new Color(0f, 0f, 0f, 0.35f);

                foreach (var tmp in play.GetComponentsInChildren<TMP_Text>(true))
                    tmp.color = OnAccent;

                Debug.Log("[Menu] PLAY restyled to the accent colour");
            }
        }

        /// <summary>
        /// darkens the - and + steppers, which shipped as bright white blocks that shout louder than
        /// the value they change.
        ///
        /// the colour has to go on the Button's ColorBlock, not on the Image. a Button with a colour
        /// tint transition writes its own state colour onto the target graphic every time the state
        /// changes, so anything set directly on the Image is overwritten the first time the button is
        /// touched -- or immediately, on enable.
        /// </summary>
        private static void StyleStepButtons(Transform container)
        {
            foreach (var button in container.GetComponentsInChildren<Button>(true))
            {
                var colours = button.colors;
                colours.normalColor = ButtonFill;
                colours.highlightedColor = Color.Lerp(ButtonFill, Accent, 0.35f);
                colours.pressedColor = Accent;
                colours.selectedColor = ButtonFill;
                // a stepper at the end of its range should read as unavailable, not just dimmer
                colours.disabledColor = new Color(ButtonFill.r, ButtonFill.g, ButtonFill.b, 0.25f);
                button.colors = colours;

                // the arrow itself lives on a child image; keep it bright against the dark button
                foreach (var glyph in button.GetComponentsInChildren<Image>(true))
                    if (glyph.gameObject != button.gameObject)
                        glyph.color = new Color(1f, 1f, 1f, 0.92f);
            }
        }

        /// <summary>
        /// points the map selector at its own panel so it can hide it while only one track exists
        /// </summary>
        private static void WireMapAutoHide(Transform root)
        {
            var selector = root.GetComponentInChildren<MapSelectorGUI>(true);
            var container = Find(root, "Map Container");
            if (selector == null || container == null) { Debug.LogWarning("[Menu] could not wire the map auto-hide"); return; }

            selector.containerToHide = container.gameObject;
            EditorUtility.SetDirty(selector);
            Debug.Log("[Menu] map panel will hide itself while there is only one track");
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
