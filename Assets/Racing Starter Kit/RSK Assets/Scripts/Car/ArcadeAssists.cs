using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// pulls the standard-assets car towards arcade handling without replacing the physics under it.
    /// four assists, all applied as forces on the rigidbody:
    ///   lateral grip   - bleeds off sideways velocity so corners bite instead of washing wide
    ///   drift assist   - while sliding, torques the nose towards where the player is steering, so a
    ///                    drift is something you hold rather than something you recover from
    ///   anti spin      - caps yaw rate so a tank slapper cannot develop
    ///   air control    - lets the car be rotated in the air, which is both the Asphalt feel and how
    ///                    the player earns spin charge off a jump
    ///
    /// deliberately touches only the Rigidbody. m_MaximumSteerAngle, m_Topspeed and
    /// m_FullTorqueOverAllWheels are all borrowed-and-restored by AICarAvoidanceBehaviour, so writing
    /// any of them here would corrupt the baseline it restores to.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(Rigidbody))]
    public class ArcadeAssists : MonoBehaviour
    {
        [Header("Lateral grip")]
        [Tooltip("Fraction of sideways velocity cancelled per second while gripping. 0 disables.")]
        [Range(0f, 12f)] public float lateralGrip = 7f;
        [Tooltip("Grip is eased off above this much sideways slip so deliberate drifts still slide")]
        public float gripReleaseSlip = 0.45f;

        [Header("Drift assist")]
        [Tooltip("Yaw torque applied towards the steering direction while sliding")]
        public float driftAssistTorque = 9000f;
        [Tooltip("Sideways slip above which the car counts as drifting. Raised so ordinary hard cornering no longer trips it.")]
        public float driftSlipThreshold = 0.5f;

        [Header("Anti spin")]
        [Tooltip("Yaw rate (deg/s) above which spin damping kicks in")]
        public float maxYawRate = 110f;
        public float spinDamping = 9f;

        // the arcade core: steering asks for a yaw rate and the car is turned to deliver it, so a
        // corner is taken by pointing the car rather than by managing the tyres. the wheel physics
        // still runs underneath; this tops it up where the tyres would have given out
        [Header("Yaw assist")]
        [Tooltip("Yaw rate (deg/s) full steering asks for at walking pace; the cap at low speed")]
        public float yawRateLowSpeed = 110f;
        [Tooltip("Lateral acceleration budget (m/s^2) the assist steers to: yaw rate = budget / speed. 24 is about 2.4 g, generous next to the tyres' own 1.5 g at 30 m/s, without asking for the impossible.")]
        public float lateralAccelBudget = 30f;
        [Tooltip("Angular acceleration (rad/s^2) per deg/s of yaw-rate error. A first version used 0.16 and pivoted the car sideways in a tenth of a second.")]
        public float yawAssistGain = 0.025f;
        [Tooltip("Ceiling on the assist (rad/s^2) so it can never spin the car on its own")]
        public float yawAssistMax = 2.5f;
        [Tooltip("Below this speed (m/s) the assist is off, so a parked car does not twitch")]
        public float yawAssistMinSpeed = 2f;

        [Header("Air control")]
        public float airPitchTorque = 2600f;
        public float airYawTorque = 4200f;
        public float airRollTorque = 3400f;

        private CarController car;
        private Rigidbody body;

        private void Awake()
        {
            car = GetComponent<CarController>();
            body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            var steer = NormalisedSteer();

            if (car.IsGrounded)
            {
                ApplyLateralGrip();
                ApplyYawAssist(steer);
                ApplyDriftAssist(steer);
                ApplyAntiSpin();
            }
            else
            {
                ApplyAirControl(steer);
            }
        }

        /// <summary>
        /// steering input in -1..1. the player's shaped input when the car is player driven (the
        /// speed scaling has already shrunk the wheel angle, and the assist wants the intent, not the
        /// angle); otherwise recovered from the angle the controller applied, which is what bots get
        /// </summary>
        private float NormalisedSteer()
        {
            if (car.HasArcadeInput) return Mathf.Clamp(car.SteerInput, -1f, 1f);
            if (car.m_MaximumSteerAngle <= 0.01f) return 0f;
            return Mathf.Clamp(car.CurrentSteerAngle / car.m_MaximumSteerAngle, -1f, 1f);
        }

        private void ApplyYawAssist(float steer)
        {
            if (yawAssistGain <= 0f) return;
            var forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            if (forwardSpeed < yawAssistMinSpeed) return;   // off when stopped or reversing

            // the rate a car at this speed can turn without exceeding the lateral budget, capped at
            // low speed where budget / speed would be enormous
            var byBudget = lateralAccelBudget / Mathf.Max(1f, forwardSpeed) * Mathf.Rad2Deg;
            var wanted = steer * Mathf.Min(yawRateLowSpeed, byBudget);
            var current = body.angularVelocity.y * Mathf.Rad2Deg;
            var error = wanted - current;
            // only ever help the car turn the way it is being steered; never fight a release
            if (Mathf.Abs(steer) < 0.05f) return;
            var torque = Mathf.Clamp(error * yawAssistGain, -yawAssistMax, yawAssistMax);
            body.AddTorque(Vector3.up * torque, ForceMode.Acceleration);
        }

        private void ApplyLateralGrip()
        {
            if (lateralGrip <= 0f) return;

            // full grip when planted, tapering to none once the car is sliding on purpose, so this
            // sharpens normal cornering without fighting the drift the player asked for
            var slide = Mathf.InverseLerp(0f, gripReleaseSlip, car.MaxSidewaysSlip);
            var strength = lateralGrip * (1f - slide);
            if (strength <= 0f) return;

            var sideways = Vector3.Dot(body.linearVelocity, transform.right);
            body.AddForce(-transform.right * sideways * strength, ForceMode.Acceleration);
        }

        private void ApplyDriftAssist(float steer)
        {
            if (driftAssistTorque <= 0f) return;
            if (car.MaxSidewaysSlip < driftSlipThreshold) return;
            if (Mathf.Abs(steer) < 0.05f) return;

            body.AddTorque(Vector3.up * steer * driftAssistTorque, ForceMode.Force);
        }

        private void ApplyAntiSpin()
        {
            var yawRate = body.angularVelocity.y * Mathf.Rad2Deg;
            var excess = Mathf.Abs(yawRate) - maxYawRate;
            if (excess <= 0f) return;

            body.AddTorque(Vector3.up * -Mathf.Sign(yawRate) * excess * Mathf.Deg2Rad * spinDamping,
                           ForceMode.Acceleration);
        }

        private void ApplyAirControl(float steer)
        {
            // steering in the air spins the car flat, which is what the charger reads as a spin.
            // pitch is left level-seeking so cars land on their wheels more often than not
            body.AddTorque(transform.up * steer * airYawTorque, ForceMode.Force);

            var pitch = Vector3.Dot(transform.forward, Vector3.up);
            body.AddTorque(transform.right * -pitch * airPitchTorque, ForceMode.Force);

            var roll = Vector3.Dot(transform.right, Vector3.up);
            body.AddTorque(transform.forward * -roll * airRollTorque, ForceMode.Force);
        }
    }
}
