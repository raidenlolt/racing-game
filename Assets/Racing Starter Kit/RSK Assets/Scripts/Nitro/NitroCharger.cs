using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// fills the nitro gauge from how the car is being driven rather than from pickups, which is what
    /// makes the Asphalt loop go: drift the corners, take the air, and you always have boost. holding
    /// a drift and staying airborne both pay per second, and every completed 360 while airborne pays
    /// a lump sum, so a flat spin off a jump is worth more than the same air driven straight.
    ///
    /// reads the grounded and slip state CarController already samples each physics step, so this
    /// adds no extra raycasts.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(NitroSystem))]
    public class NitroCharger : MonoBehaviour
    {
        public RaceManagerItem raceManager;

        [Header("Drift")]
        [Tooltip("Sideways slip above this counts as a drift. WheelCollider slip, not a speed.")]
        public float driftSlipThreshold = 0.35f;
        [Tooltip("Below this speed (mph) a slide is just a scrabble off the line, not a drift")]
        public float minimumDriftSpeed = 25f;
        public float chargePerDriftSecond = 0.16f;

        [Header("Air")]
        public float chargePerAirSecond = 0.20f;
        [Tooltip("Air shorter than this pays nothing, so kerbs and bumps do not trickle-charge the gauge")]
        public float minimumAirTime = 0.35f;

        [Header("Spins")]
        [Tooltip("Charge awarded per complete 360 while airborne")]
        public float chargePerSpin = 0.25f;

        /// <summary>true while the car is sliding hard enough to be earning drift charge</summary>
        public bool IsDrifting { get; private set; }
        public float AirTime { get; private set; }
        public int SpinsThisJump { get; private set; }

        private CarController car;
        private NitroSystem nitro;
        private float accumulatedYaw;
        private float lastYaw;

        private void Awake()
        {
            car = GetComponent<CarController>();
            nitro = GetComponent<NitroSystem>();
            lastYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (raceManager != null && raceManager.Item != null && !raceManager.Item.IsRaceInProgress())
            {
                ResetAirState();
                IsDrifting = false;
                lastYaw = transform.eulerAngles.y;
                return;
            }

            if (car.IsGrounded)
                UpdateGrounded();
            else
                UpdateAirborne();

            lastYaw = transform.eulerAngles.y;
        }

        private void UpdateGrounded()
        {
            ResetAirState();

            IsDrifting = car.MaxSidewaysSlip >= driftSlipThreshold && car.CurrentSpeed >= minimumDriftSpeed;
            if (IsDrifting)
                nitro.AddCharge(chargePerDriftSecond * Time.deltaTime);
        }

        private void UpdateAirborne()
        {
            IsDrifting = false;
            AirTime += Time.deltaTime;

            if (AirTime >= minimumAirTime)
                nitro.AddCharge(chargePerAirSecond * Time.deltaTime);

            // DeltaAngle keeps this correct across the 360/0 wrap, and taking the absolute means a
            // flat spin pays the same whichever way the car is rotating
            accumulatedYaw += Mathf.Abs(Mathf.DeltaAngle(lastYaw, transform.eulerAngles.y));
            while (accumulatedYaw >= 360f)
            {
                accumulatedYaw -= 360f;
                SpinsThisJump++;
                nitro.AddCharge(chargePerSpin);
            }
        }

        private void ResetAirState()
        {
            AirTime = 0f;
            accumulatedYaw = 0f;
            SpinsThisJump = 0;
        }
    }
}
