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
        [Range(0f, 12f)] public float lateralGrip = 5f;
        [Tooltip("Grip is eased off above this much sideways slip so deliberate drifts still slide")]
        public float gripReleaseSlip = 0.35f;

        [Header("Drift assist")]
        [Tooltip("Yaw torque applied towards the steering direction while sliding")]
        public float driftAssistTorque = 9000f;
        [Tooltip("Sideways slip above which the car counts as drifting")]
        public float driftSlipThreshold = 0.35f;

        [Header("Anti spin")]
        [Tooltip("Yaw rate (deg/s) above which spin damping kicks in")]
        public float maxYawRate = 160f;
        public float spinDamping = 6f;

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
                ApplyDriftAssist(steer);
                ApplyAntiSpin();
            }
            else
            {
                ApplyAirControl(steer);
            }
        }

        /// <summary>steering input in -1..1, recovered from the angle the controller applied</summary>
        private float NormalisedSteer()
        {
            if (car.m_MaximumSteerAngle <= 0.01f) return 0f;
            return Mathf.Clamp(car.CurrentSteerAngle / car.m_MaximumSteerAngle, -1f, 1f);
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
