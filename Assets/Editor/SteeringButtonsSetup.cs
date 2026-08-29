using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// puts LEFT and RIGHT steering buttons back on the race HUD and takes the thumb stick away.
///
/// the buttons are cloned from the throttle pedal rather than built from scratch. the pedal already
/// carries the exact stack a working touch control needs -- Button, EventTrigger, MobileButtonHandler,
/// a background Image and a Label -- along with the styling applied to it earlier. copying it and
/// re-pointing three things is far less to get wrong than assembling one and hoping it matches.
///
/// steering goes through the same mechanism the pedals use: MobileButtonHandler writes to a named
/// virtual axis, driven by the EventTrigger. throttle is Vertical positive; left and right are
/// Horizontal negative and positive. releasing sends the axis back to zero, which is what stops the
/// car steering forever after a finger lifts.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class SteeringButtonsSetup
    {
        private const string PrefabPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";

        /// <summary>
        /// steering is neither go nor stop, so it must not be green or red.
        ///
        /// the buttons are cloned from the throttle pedal, which means they arrive the same green as
        /// GAS -- two green pads on one screen, one of which does not make the car go. a neutral
        /// blue-grey keeps the colour language honest: green accelerates, red brakes, this steers.
        /// </summary>
        private static readonly Color SteerColour = new Color(0.22f, 0.30f, 0.44f, 0.85f);

        private const float ButtonSize = 190f;

        /// <summary>
        /// these sit as far in from the bottom left corner as the pedals do from the bottom right.
        ///
        /// cloned from the throttle pedal, they inherited its position values but not its pivot: the
        /// pedal's rect is anchored by its bottom edge while these are anchored by their centre, so
        /// the same numbers put them most of a button's height lower. measured on screen they ended
        /// up 13 pixels off the bottom and 26 off the left, against the pedals' 78, which is what
        /// made them look jammed into the corner.
        ///
        /// 90 units of clearance on both axes matches the throttle exactly; the centres are then that
        /// plus half a button.
        /// </summary>
        private const float EdgeClearance = 90f;
        private const float LeftX = EdgeClearance + ButtonSize * 0.5f;
        /// <summary>keeps the original 20 unit gap between the two buttons</summary>
        private const float RightX = LeftX + ButtonSize + 20f;
        private const float ButtonY = EdgeClearance + ButtonSize * 0.5f;

        [MenuItem("Tools/Racing/Restore Steering Buttons")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var rig = Find(root.transform, "Mobile Controls");
                if (rig == null) { Debug.LogError("[Steering] Mobile Controls not found"); return; }

                var template = Find(root.transform, "Throttle Button");
                if (template == null) { Debug.LogError("[Steering] no throttle pedal to clone"); return; }

                RemoveStick(rig);

                Build(rig, template.gameObject, "Steer Left Button", "LEFT", LeftX, false);
                Build(rig, template.gameObject, "Steer Right Button", "RIGHT", RightX, true);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Steering] done");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// takes out the thumb stick and the component that read it. TouchDriveControls writes the
        /// Horizontal axis every frame from the stick, so leaving it behind would have it fighting
        /// the buttons for the same axis.
        /// </summary>
        private static void RemoveStick(Transform rig)
        {
            var stick = Find(rig, "Steering Joystick");
            if (stick != null)
            {
                Object.DestroyImmediate(stick.gameObject);
                Debug.Log("[Steering] removed the thumb stick");
            }

            var drive = rig.GetComponent<TouchDriveControls>();
            if (drive != null)
            {
                Object.DestroyImmediate(drive);
                Debug.Log("[Steering] removed TouchDriveControls, which owned the Horizontal axis");
            }
        }

        private static void Build(Transform rig, GameObject template, string name, string label,
                                  float x, bool positive)
        {
            var existing = Find(rig, name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = Object.Instantiate(template, rig);
            go.name = name;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;      // bottom left, mirroring the pedals on the right
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, ButtonY);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

            var handler = go.GetComponent<MobileButtonHandler>();
            handler.Name = "Horizontal";

            Wire(go.GetComponent<EventTrigger>(), handler, positive);

            foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
                tmp.text = label;

            // the pad's colour lives on its child Image, not on the Button, which only tints it
            foreach (var image in go.GetComponentsInChildren<Image>(true))
                if (image.gameObject != go) image.color = SteerColour;

            Debug.Log("[Steering] " + name + " -> Horizontal " + (positive ? "positive" : "negative")
                      + " at " + rect.anchoredPosition.ToString("F0"));
        }

        /// <summary>
        /// rebuilds the press and release calls.
        ///
        /// these have to be persistent listeners, which is why it goes through UnityEventTools: a
        /// listener added the ordinary way at edit time is not serialised, so the button would look
        /// correct in the inspector and do nothing in a build.
        /// </summary>
        private static void Wire(EventTrigger trigger, MobileButtonHandler handler, bool positive)
        {
            trigger.triggers.Clear();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            if (positive) UnityEventTools.AddVoidPersistentListener(down.callback, new UnityAction(handler.SetAxisPositiveState));
            else UnityEventTools.AddVoidPersistentListener(down.callback, new UnityAction(handler.SetAxisNegativeState));
            trigger.triggers.Add(down);

            // a finger can leave a button either by lifting or by sliding off it; both have to
            // recentre the steering or the car keeps turning
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            UnityEventTools.AddVoidPersistentListener(up.callback, new UnityAction(handler.SetAxisNeutralState));
            trigger.triggers.Add(up);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            UnityEventTools.AddVoidPersistentListener(exit.callback, new UnityAction(handler.SetAxisNeutralState));
            trigger.triggers.Add(exit);
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
