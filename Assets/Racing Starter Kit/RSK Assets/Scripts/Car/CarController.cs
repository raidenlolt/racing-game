using System;
using UnityEngine;
/// <summary>
/// unity standard assets car controller, replace with your own
/// </summary>
namespace SpinMotion
{
    internal enum CarDriveType
    {
        FrontWheelDrive,
        RearWheelDrive,
        FourWheelDrive
    }

    internal enum SpeedType
    {
        MPH,
        KPH
    }

    public class CarController : MonoBehaviour
    {
        [SerializeField] private CarDriveType m_CarDriveType = CarDriveType.FourWheelDrive;
        [SerializeField] private WheelCollider[] m_WheelColliders = new WheelCollider[4];
        [SerializeField] private GameObject[] m_WheelMeshes = new GameObject[4];
        [SerializeField] private WheelEffects[] m_WheelEffects = new WheelEffects[4];
        [SerializeField] private Vector3 m_CentreOfMassOffset;
        [SerializeField] public float m_MaximumSteerAngle;
        [Range(0, 1)] [SerializeField] private float m_SteerHelper; // 0 is raw physics , 1 the car will grip in the direction it is facing
        [Range(0, 1)] [SerializeField] private float m_TractionControl; // 0 is no traction control, 1 is full interference
        [SerializeField] public float m_FullTorqueOverAllWheels;
        [SerializeField] private float m_ReverseTorque;
        [SerializeField] private float m_MaxHandbrakeTorque;
        [SerializeField] private float m_Downforce = 100f;
        [SerializeField] private SpeedType m_SpeedType;
        [SerializeField] public float m_Topspeed = 200;
        [SerializeField] private static int NoOfGears = 5;
        [SerializeField] private float m_RevRangeBoundary = 1f;
        [SerializeField] private float m_SlipLimit;
        [SerializeField] private float m_BrakeTorque;
        
        // the player's steering used to reach the wheels raw. on a phone the LEFT and RIGHT pads
        // write a full 1 or -1 into the axis with no ramp, so every touch was instant full lock, at
        // any speed. full lock at 190 mph saturates the tyres and the car snaps sideways; that is
        // most of what players meant by "the handling is very difficult". MoveArcade shapes the
        // input first: it ramps towards the pad, and it scales the lock down as speed rises
        [Header("Arcade steering (player input only)")]
        [Tooltip("Seconds for the steering to go from centre to full lock when a pad is held")]
        public float steerRiseSeconds = 0.22f;
        [Tooltip("Seconds for the steering to return to centre once released")]
        public float steerReturnSeconds = 0.12f;
        // with the steer helper and the lateral grip assist the car turns at very nearly the
        // kinematic rate for its wheel angle, whatever the tyres would have allowed. so the lock
        // angle is the handling: the lock is scaled so that the turn it commands never asks for more
        // than this much lateral acceleration. full lock is kept at low speed, where the budget is
        // not the limit
        [Tooltip("Lateral acceleration (m/s^2) full lock is allowed to command. Arcade rather than real: measured, 55 turns a 30 m/s car at about 55 deg/s with no slip, and an 80 m/s car at about 25 deg/s.")]
        public float steerLateralBudget = 55f;
        [Tooltip("Share of full lock always kept, so the wheel never goes numb at top speed")]
        [Range(0.02f, 1f)] public float minSteerFraction = 0.06f;
        private float wheelbase = 2.6f;

        /// <summary>the shaped steering the player is asking for, -1..1, before the speed scaling</summary>
        public float SteerInput { get; private set; }
        /// <summary>true once MoveArcade has driven this car, so assists know SteerInput is live</summary>
        public bool HasArcadeInput { get; private set; }

        [Header("Flipping")]
        [SerializeField] private float m_WaitTimeBeforeFlip = 2.5f; // Seconds to wait before auto-flipping
        private float m_FlippedTimer = 0f;

        private float m_SteerAngle;
        private int m_GearNum;
        private float m_GearFactor;
        private Vector3 m_OldForward;
        private float m_CurrentTorque;
        private Rigidbody m_Rigidbody;
        private const float k_ReversingThreshold = 0.01f;

        public bool Skidding { get; private set; }
        public float BrakeInput { get; private set; }
        public float CurrentSteerAngle{ get { return m_SteerAngle; }}
        // the rigidbody is cached in Start, but this property is read from other components' Update
        // and LateUpdate as well as from the AI. any of those can run on the frame a car is spawned,
        // before this car's own Start has happened, so guard rather than make every caller do it
        public float CurrentSpeed
        {
            get { return m_Rigidbody == null ? 0f : m_Rigidbody.linearVelocity.magnitude*2.23693629f; }
        }
        public float MaxSpeed{get { return EffectiveTopSpeed; }}
        public float Revs { get; private set; }
        public float AccelInput { get; private set; }
        /// <summary>
        /// the four visible wheels, in the order front-left, front-right, rear-left, rear-right.
        ///
        /// exposed because which meshes are the wheels is not guessable from the outside: each car
        /// model names them differently -- Player Car 2's are Paint.007 through Paint.013 -- and only
        /// this array says which is which. the menu showcase spins them while a car drives into frame.
        /// </summary>
        public GameObject[] WheelMeshes { get { return m_WheelMeshes; } }

        // wiring with AICarAvoidanceBehaviour:
        [HideInInspector] public int forceSteering = 0;
        [HideInInspector] public float forceSteeringFactor = 0.1f;
        [HideInInspector] public int forceBraking = 0;

        // performance multiplier layers.
        // AICarAvoidanceBehaviour borrows m_Topspeed and m_FullTorqueOverAllWheels and restores the
        // values it cached at Awake. anything else that writes those fields races it: whichever system
        // restores last wins and the other one's baseline is gone for the rest of the race. so nitro
        // and rubber-banding multiply through here instead of ever touching the base fields
        [HideInInspector] public float boostSpeedMultiplier = 1f;
        [HideInInspector] public float boostTorqueMultiplier = 1f;
        [HideInInspector] public float bandingSpeedMultiplier = 1f;
        [HideInInspector] public float bandingTorqueMultiplier = 1f;

        /// <summary>top speed after boost and rubber-banding, in the units of m_SpeedType</summary>
        public float EffectiveTopSpeed
        {
            get { return m_Topspeed * boostSpeedMultiplier * bandingSpeedMultiplier; }
        }

        /// <summary>true while at least one wheel is touching the ground. sampled each Move()</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>largest absolute sideways slip across the four wheels. drives drift detection</summary>
        public float MaxSidewaysSlip { get; private set; }

        private float EffectiveTorque
        {
            get { return m_CurrentTorque * boostTorqueMultiplier * bandingTorqueMultiplier; }
        }

        // Use this for initialization
        private void Start()
        {
            m_WheelColliders[0].attachedRigidbody.centerOfMass = m_CentreOfMassOffset;

            m_MaxHandbrakeTorque = float.MaxValue;

            m_Rigidbody = GetComponent<Rigidbody>();
            m_CurrentTorque = m_FullTorqueOverAllWheels - (m_TractionControl*m_FullTorqueOverAllWheels);

            // front and rear axle spacing, for the lock scaling. wheels 0/1 are the front pair
            if (m_WheelColliders[0] != null && m_WheelColliders[2] != null)
            {
                var span = Mathf.Abs(m_WheelColliders[0].transform.localPosition.z - m_WheelColliders[2].transform.localPosition.z);
                if (span > 1f) wheelbase = span;
            }
        }

        private void Update()
        {
            CheckIfFlipped();
        }

        private void GearChanging()
        {
            float f = Mathf.Abs(CurrentSpeed/MaxSpeed);
            float upgearlimit = (1/(float) NoOfGears)*(m_GearNum + 1);
            float downgearlimit = (1/(float) NoOfGears)*m_GearNum;

            if (m_GearNum > 0 && f < downgearlimit)
            {
                m_GearNum--;
            }

            if (f > upgearlimit && (m_GearNum < (NoOfGears - 1)))
            {
                m_GearNum++;
            }
        }


        // simple function to add a curved bias towards 1 for a value in the 0-1 range
        private static float CurveFactor(float factor)
        {
            return 1 - (1 - factor)*(1 - factor);
        }


        // unclamped version of Lerp, to allow value to exceed the from-to range
        private static float ULerp(float from, float to, float value)
        {
            return (1.0f - value)*from + value*to;
        }


        private void CalculateGearFactor()
        {
            float f = (1/(float) NoOfGears);
            // gear factor is a normalised representation of the current speed within the current gear's range of speeds.
            // We smooth towards the 'target' gear factor, so that revs don't instantly snap up or down when changing gear.
            var targetGearFactor = Mathf.InverseLerp(f*m_GearNum, f*(m_GearNum + 1), Mathf.Abs(CurrentSpeed/MaxSpeed));
            m_GearFactor = Mathf.Lerp(m_GearFactor, targetGearFactor, Time.deltaTime*5f);
        }


        private void CalculateRevs()
        {
            // calculate engine revs (for display / sound)
            // (this is done in retrospect - revs are not used in force/power calculations)
            CalculateGearFactor();
            var gearNumFactor = m_GearNum/(float) NoOfGears;
            var revsRangeMin = ULerp(0f, m_RevRangeBoundary, CurveFactor(gearNumFactor));
            var revsRangeMax = ULerp(m_RevRangeBoundary, 1f, gearNumFactor);
            Revs = ULerp(revsRangeMin, revsRangeMax, m_GearFactor);
        }


        /// <summary>
        /// the player's entry point. shapes the steering and hands everything to Move. bots keep
        /// calling Move directly: their steering is already a smooth, speed-aware output of their own
        /// controller and scaling it again would make them run wide
        /// </summary>
        public void MoveArcade(float steering, float accel, float footbrake, float handbrake)
        {
            HasArcadeInput = true;
            var target = Mathf.Clamp(steering, -1f, 1f);

            // ramp: rising towards the pad takes steerRiseSeconds, letting go returns faster
            var returning = Mathf.Abs(target) < Mathf.Abs(SteerInput) || Mathf.Sign(target) != Mathf.Sign(SteerInput);
            var seconds = returning ? steerReturnSeconds : steerRiseSeconds;
            var rate = seconds > 0.001f ? Time.fixedDeltaTime / seconds : 1f;
            SteerInput = Mathf.MoveTowards(SteerInput, target, rate);

            // the largest wheel angle whose kinematic turn stays inside the lateral budget:
            // a_lat = v^2 * tan(angle) / wheelbase
            var v = m_Rigidbody != null ? m_Rigidbody.linearVelocity.magnitude : 0f;
            var allowed = Mathf.Atan(steerLateralBudget * wheelbase / Mathf.Max(1f, v * v));
            var full = Mathf.Max(0.01f, m_MaximumSteerAngle * Mathf.Deg2Rad);
            var scale = Mathf.Clamp(allowed / full, minSteerFraction, 1f);

            Move(SteerInput * scale, accel, footbrake, handbrake);
        }

        public void Move(float steering, float accel, float footbrake, float handbrake)
        {
            for (int i = 0; i < 4; i++)
            {
                Quaternion quat;
                Vector3 position;
                m_WheelColliders[i].GetWorldPose(out position, out quat);
                m_WheelMeshes[i].transform.position = position;
                m_WheelMeshes[i].transform.rotation = quat;
            }

            SampleWheelState();

            //forced values by AICarAvoidanceBehaviour
            if (forceSteering != 0)
                steering += forceSteering * forceSteeringFactor;

            if (forceBraking != 0)
                footbrake *= forceBraking;

            //clamp input values
            steering = Mathf.Clamp(steering, -1, 1);
            AccelInput = accel = Mathf.Clamp(accel, 0, 1);
            BrakeInput = footbrake = -1*Mathf.Clamp(footbrake, -1, 0);
            handbrake = Mathf.Clamp(handbrake, 0, 1);

            //Set the steer on the front wheels.
            //Assuming that wheels 0 and 1 are the front wheels.
            m_SteerAngle = steering*m_MaximumSteerAngle;
            m_WheelColliders[0].steerAngle = m_SteerAngle;
            m_WheelColliders[1].steerAngle = m_SteerAngle;

            SteerHelper();
            ApplyDrive(accel, footbrake);
            CapSpeed();

            //Set the handbrake.
            //Assuming that wheels 2 and 3 are the rear wheels.
            if (handbrake > 0f)
            {
                var hbTorque = handbrake*m_MaxHandbrakeTorque;
                m_WheelColliders[2].brakeTorque = hbTorque;
                m_WheelColliders[3].brakeTorque = hbTorque;
            }
            else
            {
                m_WheelColliders[2].brakeTorque = 0f;
                m_WheelColliders[3].brakeTorque = 0f;
            }

            CalculateRevs();
            GearChanging();

            AddDownForce();
            CheckForWheelSpin();
            TractionControl();
        }


        /// <summary>
        /// one pass over the wheels for grounded state and peak sideways slip, so the nitro charger
        /// can tell drifting from airborne without casting its own rays every physics step
        /// </summary>
        private void SampleWheelState()
        {
            var grounded = false;
            var maxSlip = 0f;
            for (int i = 0; i < 4; i++)
            {
                WheelHit hit;
                if (!m_WheelColliders[i].GetGroundHit(out hit)) continue;
                grounded = true;
                var slip = Mathf.Abs(hit.sidewaysSlip);
                if (slip > maxSlip) maxSlip = slip;
            }
            IsGrounded = grounded;
            MaxSidewaysSlip = maxSlip;
        }


        private void CapSpeed()
        {
            float speed = m_Rigidbody.linearVelocity.magnitude;
            var topSpeed = EffectiveTopSpeed;
            switch (m_SpeedType)
            {
                case SpeedType.MPH:

                    speed *= 2.23693629f;
                    if (speed > topSpeed)
                        m_Rigidbody.linearVelocity = (topSpeed/2.23693629f) * m_Rigidbody.linearVelocity.normalized;
                    break;

                case SpeedType.KPH:
                    speed *= 3.6f;
                    if (speed > topSpeed)
                        m_Rigidbody.linearVelocity = (topSpeed/3.6f) * m_Rigidbody.linearVelocity.normalized;
                    break;
            }
        }


        private void ApplyDrive(float accel, float footbrake)
        {

            float thrustTorque;
            var driveTorque = EffectiveTorque;
            switch (m_CarDriveType)
            {
                case CarDriveType.FourWheelDrive:
                    thrustTorque = accel * (driveTorque / 4f);
                    for (int i = 0; i < 4; i++)
                    {
                        m_WheelColliders[i].motorTorque = thrustTorque;
                    }
                    break;

                case CarDriveType.FrontWheelDrive:
                    thrustTorque = accel * (driveTorque / 2f);
                    m_WheelColliders[0].motorTorque = m_WheelColliders[1].motorTorque = thrustTorque;
                    break;

                case CarDriveType.RearWheelDrive:
                    thrustTorque = accel * (driveTorque / 2f);
                    m_WheelColliders[2].motorTorque = m_WheelColliders[3].motorTorque = thrustTorque;
                    break;

            }

            for (int i = 0; i < 4; i++)
            {
                if (CurrentSpeed > 5 && Vector3.Angle(transform.forward, m_Rigidbody.linearVelocity) < 50f)
                {
                    m_WheelColliders[i].brakeTorque = m_BrakeTorque*footbrake;
                }
                else if (footbrake > 0)
                {
                    m_WheelColliders[i].brakeTorque = 0f;
                    m_WheelColliders[i].motorTorque = -m_ReverseTorque*footbrake;
                }
            }
        }


        private void SteerHelper()
        {
            for (int i = 0; i < 4; i++)
            {
                WheelHit wheelhit;
                m_WheelColliders[i].GetGroundHit(out wheelhit);
                if (wheelhit.normal == Vector3.zero)
                    return; // wheels arent on the ground so dont realign the rigidbody velocity
            }

            // avoid gimbal lock problems on steep curves by using local forward instead of eulerAngles
            if (m_OldForward != Vector3.zero)
            {
                float angle = Vector3.SignedAngle(m_OldForward, transform.forward, transform.up);
                if (Mathf.Abs(angle) < 10f)
                {
                    var turnadjust = angle * m_SteerHelper;
                    Quaternion velRotation = Quaternion.AngleAxis(turnadjust, transform.up);
                    m_Rigidbody.linearVelocity = velRotation * m_Rigidbody.linearVelocity;
                }
            }
            m_OldForward = transform.forward;
        }


        // this is used to add more grip in relation to speed
        private void AddDownForce()
        {
            m_WheelColliders[0].attachedRigidbody.AddForce(-transform.up*m_Downforce*
                                                         m_WheelColliders[0].attachedRigidbody.linearVelocity.magnitude);
        }


        // checks if the wheels are spinning and is so does three things
        // 1) emits particles
        // 2) plays tiure skidding sounds
        // 3) leaves skidmarks on the ground
        // these effects are controlled through the WheelEffects class
        private void CheckForWheelSpin()
        {
            // loop through all wheels
            for (int i = 0; i < 4; i++)
            {
                WheelHit wheelHit;
                m_WheelColliders[i].GetGroundHit(out wheelHit);

                // is the tire slipping above the given threshhold
                if (Mathf.Abs(wheelHit.forwardSlip) >= m_SlipLimit || Mathf.Abs(wheelHit.sidewaysSlip) >= m_SlipLimit)
                {
                    m_WheelEffects[i].EmitTyreSmoke();

                    // avoiding all four tires screeching at the same time
                    // if they do it can lead to some strange audio artefacts
                    if (!AnySkidSoundPlaying())
                    {
                        m_WheelEffects[i].PlayAudio();
                    }
                    continue;
                }

                // if it wasnt slipping stop all the audio
                if (m_WheelEffects[i].PlayingAudio)
                {
                    m_WheelEffects[i].StopAudio();
                }
                // end the trail generation
                m_WheelEffects[i].EndSkidTrail();
            }
        }

        // crude traction control that reduces the power to wheel if the car is wheel spinning too much
        private void TractionControl()
        {
            WheelHit wheelHit;
            switch (m_CarDriveType)
            {
                case CarDriveType.FourWheelDrive:
                    // loop through all wheels
                    for (int i = 0; i < 4; i++)
                    {
                        m_WheelColliders[i].GetGroundHit(out wheelHit);

                        AdjustTorque(wheelHit.forwardSlip);
                    }
                    break;

                case CarDriveType.RearWheelDrive:
                    m_WheelColliders[2].GetGroundHit(out wheelHit);
                    AdjustTorque(wheelHit.forwardSlip);

                    m_WheelColliders[3].GetGroundHit(out wheelHit);
                    AdjustTorque(wheelHit.forwardSlip);
                    break;

                case CarDriveType.FrontWheelDrive:
                    m_WheelColliders[0].GetGroundHit(out wheelHit);
                    AdjustTorque(wheelHit.forwardSlip);

                    m_WheelColliders[1].GetGroundHit(out wheelHit);
                    AdjustTorque(wheelHit.forwardSlip);
                    break;
            }
        }


        private void AdjustTorque(float forwardSlip)
        {
            if (forwardSlip >= m_SlipLimit && m_CurrentTorque >= 0)
            {
                m_CurrentTorque -= 10 * m_TractionControl;
            }
            else
            {
                m_CurrentTorque += 10 * m_TractionControl;
                if (m_CurrentTorque > m_FullTorqueOverAllWheels)
                {
                    m_CurrentTorque = m_FullTorqueOverAllWheels;
                }
            }
        }


        private bool AnySkidSoundPlaying()
        {
            for (int i = 0; i < 4; i++)
            {
                if (m_WheelEffects[i].PlayingAudio)
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Boosts the car upwards and forwards to jump over track elements.
        /// </summary>
        public void ApplyJumpBoost(float jumpForce,  float forwardBoostForce)
        {
            // 1. Check if the car is grounded to prevent infinite mid-air jumping
            var isGrounded = false;
            for (var i = 0; i < 4; i++)
            {
                if (!m_WheelColliders[i].GetGroundHit(out var wheelHit)) continue;
                isGrounded = true;
                break;
            }

            // 2. Apply the boost if we are on the ground
            if (!isGrounded) return;
            // Apply sudden impulse force upwards (Jump)
            m_Rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
                
            // Apply sudden impulse force forwards (Boost)
            m_Rigidbody.AddForce(transform.forward * forwardBoostForce, ForceMode.Impulse);
        }
        
        private void CheckIfFlipped()
        {
            // Check if the car's up vector is pointing sideways or downwards (less than 0.2f threshold)
            // AND check if the car has mostly stopped moving to ensure we aren't mid-crash or mid-air flip
            if (transform.up.y < 0.2f && m_Rigidbody.linearVelocity.magnitude < 1.0f)
            {
                m_FlippedTimer += Time.deltaTime;

                if (m_FlippedTimer >= m_WaitTimeBeforeFlip)
                {
                    FlipCar();
                    m_FlippedTimer = 0f;
                }
            }
            else
            {
                // Reset the timer if the car corrects itself or starts moving quickly again
                m_FlippedTimer = 0f; 
            }
        }

        public void FlipCar()
        {
            // Maintain the car's current Y rotation (yaw) but flatten X (pitch) and Z (roll)
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            
            // Nudge the car upward to avoid clipping through the floor immediately after resetting
            transform.position += Vector3.up * 1.5f;

            // Kill all physics momentum to prevent sliding upon landing
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}
