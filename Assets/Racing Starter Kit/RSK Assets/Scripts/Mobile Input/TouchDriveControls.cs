using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// steering half of the touch layout: an analog thumb stick drives the Horizontal axis, while the
    /// accelerator and brake pads keep driving Vertical through their existing MobileButtonHandlers.
    /// splitting it this way means throttle and steering are genuinely independent, which the old
    /// four-arrow layout could not do.
    ///
    /// this is not the same as VirtualJoystickCarControls, which reads two sticks and owns both axes.
    /// </summary>
    public class TouchDriveControls : MonoBehaviour
    {
        [Tooltip("Thumb stick that steers. Only its horizontal axis is used.")]
        public Joystick steeringJoystick;

        [Header("Feel")]
        [Tooltip("Movement below this fraction of the stick's travel reads as centred, so resting a thumb does not weave the car")]
        [Range(0f, 0.4f)] public float deadZone = 0.08f;
        [Tooltip("Above 1 gives finer control near centre and full lock still available at the edge. 1 is a straight linear response.")]
        [Range(1f, 3f)] public float steeringCurve = 1.6f;

        /// <summary>last steering value sent, for anything that wants to display it</summary>
        public float Steering { get; private set; }

        private void Update()
        {
            if (steeringJoystick == null) return;

            var raw = steeringJoystick.Horizontal;
            var magnitude = Mathf.Abs(raw);

            float value;
            if (magnitude <= deadZone)
            {
                value = 0f;
            }
            else
            {
                // rescale what is left of the travel back to a full 0-1 range, so the dead zone costs
                // sensitivity near centre rather than costing the driver full lock at the edge
                var t = Mathf.InverseLerp(deadZone, 1f, magnitude);
                value = Mathf.Sign(raw) * Mathf.Pow(t, steeringCurve);
            }

            Steering = value;
            MobileInputManager.SetAxis("Horizontal", value);
        }

        private void OnDisable()
        {
            // let go of the axis, or the car keeps the last steering angle while the rig is hidden
            Steering = 0f;
            MobileInputManager.SetAxis("Horizontal", 0f);
        }
    }
}
