using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// chase camera in the Asphalt mould: it sits at a fixed offset behind the car but orients along
    /// the direction the car is actually travelling rather than the way its nose points, so a drifting
    /// car slews visibly across frame instead of staying pinned dead centre. it then pulls back under
    /// nitro, rolls into corners, leads the aim point into the turn, and backs off in the air so a
    /// jump shows the whole car and its landing.
    ///
    /// this replaces an earlier version that called transform.Translate every LateUpdate and then
    /// lerped most of it straight back out. that produced a usable shot, but only as the equilibrium
    /// of the two fighting each other: the real offset was Translate * (1-k)/k, so with the shipped
    /// stickiness of 49 a posZ of -140 actually meant 2.9 metres behind the car. touching the
    /// smoothing silently re-framed every camera. the offsets below are the decoded equivalents and
    /// now mean what they say.
    ///
    /// the heading the shot is built around is damped and bounded rather than read raw off the
    /// rigidbody -- see Heading(). taking it raw is what made the camera swing about on a twisty
    /// circuit: the chase cameras sit 9.5 m and 13.5 m back, so every wobble in the velocity vector
    /// was multiplied into metres of sideways camera travel, and it was fighting an aim point that
    /// swung the other way on raw steering input.
    /// </summary>
    public class PlayerCarCameraController : MonoBehaviour
    {
        public GameObject playerCar;

        [Header("Framing")]
        [Tooltip("Camera position relative to the car, in metres. Negative Z sits behind it.")]
        public Vector3 offset = new Vector3(0f, 0.9f, -2.9f);
        [Tooltip("Downward tilt applied on top of the look direction, in degrees")]
        public float pitch = 3f;

        [Header("Follow")]
        [Tooltip("How quickly the camera closes on its target position. Higher is tighter.")]
        public float positionSmoothing = 9f;
        public float rotationSmoothing = 7f;
        [Tooltip("Below this speed the car's own facing is used, since a near-zero velocity has no meaningful direction")]
        public float rotationThreshold = 1f;

        [Header("Bumper camera")]
        [Tooltip("Lock to the car body and skip the velocity swing, roll and pull-back. For first person views, where that motion is unpleasant.")]
        public bool rigid;

        [Header("Heading")]
        [Tooltip("How much the shot follows the direction of travel rather than the car's nose. 0 sits square behind the car, 1 follows velocity outright.")]
        [Range(0f, 1f)] public float velocityInfluence = 0.75f;
        [Tooltip("Furthest the frame may swing off the car's nose, in degrees. A big slide slews the shot, but only this far.")]
        public float maxHeadingOffset = 22f;
        [Tooltip("How quickly the heading itself settles. This damps the target the camera is chasing, not just the camera.")]
        public float headingSmoothing = 6f;
        [Tooltip("Speed in m/s at which the direction of travel is trusted in full. Below it the car's nose is blended back in, so a crawling car has no jitter and no snap.")]
        public float headingSpeedRange = 9f;

        [Header("Nitro")]
        public float nitroPullBack = 1.6f;
        public float nitroDrop = 0.25f;
        [Tooltip("How fast the pull-back eases in and out")]
        public float nitroBlendSpeed = 4f;

        [Header("Roll")]
        [Tooltip("Maximum roll into a corner or drift, in degrees")]
        public float maxRoll = 6f;
        public float rollSmoothing = 4f;

        [Header("Look ahead")]
        [Tooltip("How far in front of the car the camera aims, in metres")]
        public float lookAheadDistance = 8f;
        [Tooltip("How far the aim point swings towards the inside of a turn, in metres")]
        public float lookAheadLateral = 4f;
        [Tooltip("How quickly the aim follows the steering. Steering input is a hard step on a touch button, so taken raw it snaps the aim across the moment a button is pressed or released.")]
        public float steerSmoothing = 3.5f;
        [Tooltip("Hard ceiling on how far the aim may swing off the heading, in degrees. This bounds the swing whatever lookAheadLateral is set to.")]
        public float maxAimYaw = 12f;

        [Header("Air")]
        public float airPullBack = 2.5f;
        public float airRise = 1.2f;
        public float airBlendSpeed = 3f;

        private Transform car;
        private Rigidbody carPhysics;
        private CarController carController;
        private NitroSystem nitro;

        private float nitroBlend;
        private float airBlend;
        private float roll;

        /// <summary>the damped heading, as a yaw in degrees. see Heading()</summary>
        private float headingYaw;
        private bool headingStarted;
        /// <summary>steering input, damped, so the aim does not jump when a button goes down</summary>
        private float steerSmoothed;

        private void Start()
        {
            Resolve();
            if (car != null)
            {
                headingYaw = car.eulerAngles.y;
                headingStarted = true;
                transform.position = car.position + car.rotation * offset;
            }
        }

        private void Resolve()
        {
            if (playerCar == null) return;
            car = playerCar.transform;
            carPhysics = playerCar.GetComponent<Rigidbody>();
            carController = playerCar.GetComponent<CarController>();
            nitro = playerCar.GetComponent<NitroSystem>();
        }

        private void LateUpdate()
        {
            if (car == null)
            {
                Resolve();
                if (car == null) return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var heading = Heading(dt);
            UpdateBlends(dt);
            steerSmoothed = Mathf.Lerp(steerSmoothed, SteerFraction(), Blend(steerSmoothing, dt));

            var framing = offset;
            if (!rigid)
            {
                framing += Vector3.back * (nitroPullBack * nitroBlend + airPullBack * airBlend);
                framing += Vector3.down * (nitroDrop * nitroBlend);
                framing += Vector3.up * (airRise * airBlend);
            }

            var targetPosition = car.position + heading * framing;

            // exponential smoothing, so the follow feels the same at 30fps and 120fps. the previous
            // version multiplied by Time.fixedDeltaTime inside LateUpdate, which made the closing rate
            // depend on how many frames happened to be rendered
            transform.position = Vector3.Lerp(transform.position, targetPosition, Blend(positionSmoothing, dt));

            var aim = AimPoint(heading);
            var toAim = aim - transform.position;
            if (toAim.sqrMagnitude < 0.0001f) return;

            var targetRotation = Quaternion.LookRotation(toAim.normalized, Vector3.up)
                                 * Quaternion.Euler(pitch, 0f, rigid ? 0f : RollAngle(dt));

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Blend(rotationSmoothing, dt));
        }

        /// <summary>frame-rate independent lerp factor for a given closing speed</summary>
        private static float Blend(float speed, float dt)
        {
            return 1f - Mathf.Exp(-speed * dt);
        }

        /// <summary>
        /// the direction the shot is built around. following velocity rather than the car's nose is
        /// what makes a drift read: the car rotates inside a frame that keeps pointing down the road.
        ///
        /// it is taken as a damped, bounded fraction of the travel direction rather than the travel
        /// direction itself, because the raw vector is a poor camera target in three separate ways.
        /// reversing points it at the back of the car, so the shot whipped round to the front. a car
        /// creeping at the shipped 0.1 m/s threshold has a velocity direction that is mostly noise,
        /// and crossing that threshold snapped the frame. and on a twisty circuit the slip angle
        /// swings several degrees each way through every corner entry, which at 13.5 m behind the car
        /// is metres of sideways camera travel per turn -- the reported "moves weird on every left
        /// and right".
        /// </summary>
        private Quaternion Heading(float dt)
        {
            if (rigid || carPhysics == null)
                return car.rotation;

            var facing = Flatten(car.forward);
            var target = facing;

            var velocity = carPhysics.linearVelocity;
            velocity.y = 0f;
            var speed = velocity.magnitude;

            if (speed > rotationThreshold)
            {
                var travel = velocity / speed;

                // reversing is still forwards as far as the shot is concerned. taking the travel
                // direction here would put the camera in front of the bonnet looking back
                if (Vector3.Dot(travel, facing) > 0f)
                {
                    // ramp the travel direction in with speed rather than switching to it at a
                    // threshold, so there is no frame where the heading jumps
                    var authority = headingSpeedRange > 0.01f
                        ? Mathf.Clamp01((speed - rotationThreshold) / headingSpeedRange)
                        : 1f;

                    target = Flatten(Vector3.Slerp(facing, travel, authority * velocityInfluence));
                    target = ClampYaw(facing, target, maxHeadingOffset);
                }
            }

            var targetYaw = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg;
            if (!headingStarted)
            {
                headingYaw = targetYaw;
                headingStarted = true;
            }
            else
            {
                headingYaw = Mathf.LerpAngle(headingYaw, targetYaw, Blend(headingSmoothing, dt));
            }

            return Quaternion.Euler(0f, headingYaw, 0f);
        }

        /// <summary>holds a direction within a given yaw of a reference direction</summary>
        private static Vector3 ClampYaw(Vector3 reference, Vector3 direction, float maxDegrees)
        {
            if (maxDegrees <= 0f) return reference;
            var angle = Vector3.SignedAngle(reference, direction, Vector3.up);
            if (Mathf.Abs(angle) <= maxDegrees) return direction;
            return Quaternion.Euler(0f, Mathf.Sign(angle) * maxDegrees, 0f) * reference;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        private void UpdateBlends(float dt)
        {
            var boosting = nitro != null && nitro.IsActive;
            nitroBlend = Mathf.Lerp(nitroBlend, boosting ? 1f : 0f, Blend(nitroBlendSpeed, dt));

            var airborne = carController != null && !carController.IsGrounded;
            airBlend = Mathf.Lerp(airBlend, airborne ? 1f : 0f, Blend(airBlendSpeed, dt));
        }

        /// <summary>
        /// aims ahead of the car and towards the inside of the turn, so at speed the shot shows where
        /// the car is going rather than where it already is.
        ///
        /// the swing is driven by the damped steering rather than the raw axis, and then clamped in
        /// degrees. raw, a touch button put the aim through its full travel in one frame on press and
        /// again on release: at the shipped 4 m over an 8 m lead that is a 27 degree slam each way,
        /// which is most of what a corner used to look like.
        /// </summary>
        private Vector3 AimPoint(Quaternion heading)
        {
            var point = car.position + heading * (Vector3.forward * lookAheadDistance);
            if (rigid) return point;

            var lateral = lookAheadLateral * steerSmoothed;
            var limit = Mathf.Abs(lookAheadDistance) * Mathf.Tan(Mathf.Clamp(maxAimYaw, 0f, 60f) * Mathf.Deg2Rad);
            lateral = Mathf.Clamp(lateral, -limit, limit);

            point += heading * (Vector3.right * lateral);
            return point;
        }

        private float SteerFraction()
        {
            if (carController == null || carController.m_MaximumSteerAngle <= 0.01f) return 0f;
            return Mathf.Clamp(carController.CurrentSteerAngle / carController.m_MaximumSteerAngle, -1f, 1f);
        }

        /// <summary>
        /// leans the frame into the corner. driven by how far sideways the car is actually travelling
        /// rather than by steering input, so a big slide rolls the camera even on opposite lock
        /// </summary>
        private float RollAngle(float dt)
        {
            var target = 0f;
            if (carPhysics != null)
            {
                var lateral = Vector3.Dot(carPhysics.linearVelocity, car.right);
                var speed = carPhysics.linearVelocity.magnitude;
                if (speed > 1f)
                    target = -Mathf.Clamp(lateral / speed, -1f, 1f) * maxRoll;
            }

            // a steering contribution on top, so the lean starts as the car turns in rather than only
            // once it is already sliding. damped like the aim, or a button press snaps the horizon
            target += -steerSmoothed * maxRoll * 0.4f;

            roll = Mathf.Lerp(roll, Mathf.Clamp(target, -maxRoll, maxRoll), Blend(rollSmoothing, dt));
            return roll;
        }
    }
}
