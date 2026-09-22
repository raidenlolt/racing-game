using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpinMotion
{
    /// <summary>
    /// one on-screen driving pad (left, right, gas or brake) that is safe under several fingers.
    ///
    /// the pads used to be an EventTrigger firing SetAxisPositive on pointer down and SetAxisZero on
    /// pointer up AND pointer exit. that breaks under real thumbs: a second finger touching the same
    /// pad and lifting zeroed it while the first was still down, a thumb drifting a few pixels off
    /// the pad while pressing zeroed it ("exit"), and left and right shared one axis so releasing
    /// either zeroed the other. any of those reads as steering that freezes while gas is held, or a
    /// pedal that cancels when the other hand moves.
    ///
    /// this keeps the set of pointer ids currently pressing the pad, so only the finger that pressed
    /// it can release it and it stays held until that finger lifts, wherever it drifts. the axis
    /// value is then computed from every pad on the axis: right minus left, gas minus brake, with
    /// the brake winning when both pedals are down. pads never touch each other's state.
    /// </summary>
    public class TouchPad : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("Virtual axis this pad drives: Horizontal for steering, Vertical for the pedals")]
        public string axisName = "Vertical";
        [Tooltip("This pad pushes the axis to +1 (right, gas); off means -1 (left, brake)")]
        public bool positive = true;
        [Tooltip("When the opposite pad is held at the same time this one wins instead of the two cancelling. Set on the brake.")]
        public bool overridesOpposite;

        /// <summary>at least one finger is on the pad</summary>
        public bool IsHeld { get { return pointers.Count > 0; } }
        public int PointerCount { get { return pointers.Count; } }

        private readonly HashSet<int> pointers = new HashSet<int>();
        private static readonly List<TouchPad> pads = new List<TouchPad>();

        private void OnEnable()
        {
            pads.Add(this);
        }

        private void OnDisable()
        {
            pointers.Clear();
            pads.Remove(this);
            Publish(axisName);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointers.Add(eventData.pointerId);
            Publish(axisName);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointers.Remove(eventData.pointerId);
            Publish(axisName);
        }

        // a finger that the OS took away (call, notification, tab switch) never sends pointer up
        private void OnApplicationFocus(bool focus) { if (!focus) ReleaseAll(); }
        private void OnApplicationPause(bool paused) { if (paused) ReleaseAll(); }

        /// <summary>lets go of every finger on the pad</summary>
        public void ReleaseAll()
        {
            if (pointers.Count == 0) return;
            pointers.Clear();
            Publish(axisName);
        }

        /// <summary>the combined value of every enabled pad on an axis</summary>
        public static float Value(string axis)
        {
            bool positiveHeld = false, negativeHeld = false, positiveWins = false, negativeWins = false;
            foreach (var pad in pads)
            {
                if (pad == null || pad.axisName != axis || !pad.IsHeld) continue;
                if (pad.positive) { positiveHeld = true; positiveWins |= pad.overridesOpposite; }
                else { negativeHeld = true; negativeWins |= pad.overridesOpposite; }
            }
            if (positiveHeld && negativeHeld)
            {
                if (negativeWins && !positiveWins) return -1f;
                if (positiveWins && !negativeWins) return 1f;
                return 0f;
            }
            return positiveHeld ? 1f : negativeHeld ? -1f : 0f;
        }

        private static void Publish(string axis)
        {
            MobileInputManager.SetAxis(axis, Value(axis));
        }
    }
}
