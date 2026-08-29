using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// decides when a bot spends its nitro. bots run the same NitroSystem as the player, so they earn
    /// it by drifting and jumping exactly as the player does and cannot simply boost forever.
    ///
    /// the behaviour is deliberately a bit human: they wait for a straight rather than boosting into a
    /// corner, they try to time the red zone for a perfect nitro, and each car gets its own randomised
    /// patience so a pack does not all light up on the same frame.
    /// </summary>
    [RequireComponent(typeof(NitroSystem))]
    public class AINitroDriver : MonoBehaviour
    {
        public RaceManagerItem raceManager;
        public AIRubberBanding rubberBanding;

        [Header("When to fire")]
        [Tooltip("Only boost when the car is pointed roughly straight, so nitro is not wasted mid-corner")]
        public float maxSteerFractionToFire = 0.35f;
        [Tooltip("Charge the bot will settle for if it never catches a red zone")]
        [Range(0f, 1f)] public float patienceChargeCeiling = 0.75f;
        [Tooltip("Chance per second of trying for a perfect nitro while the gauge is in the red zone")]
        [Range(0f, 1f)] public float perfectNitroSkill = 0.7f;

        [Header("Nitro income")]
        [Tooltip("Charge per second a bot earns just by racing. Measured mid-race, bots sat at their 0.25 starting charge forever: they do not drift or take the jump pads, so the drift and air income the player lives on never pays out for them and they never once fired nitro. this replaces that with a steady wage.")]
        public float passiveChargePerSecond = 0.09f;

        [Header("Stepping up")]
        [Tooltip("Seconds between follow-up taps when stepping a run up to level two and three")]
        public float stepUpInterval = 0.45f;
        [Tooltip("Chance a bot bothers stepping a run up at all")]
        [Range(0f, 1f)] public float stepUpChance = 0.6f;

        private NitroSystem nitro;
        private CarController car;
        private float nextDecisionTime;
        private float nextStepUpTime;
        private bool steppingUpThisRun;

        private void Awake()
        {
            nitro = GetComponent<NitroSystem>();
            car = GetComponent<CarController>();
            if (rubberBanding == null) rubberBanding = GetComponent<AIRubberBanding>();

            // stagger the very first decision so a grid full of bots does not fire in lockstep
            nextDecisionTime = Time.time + Random.Range(0f, 1.5f);
        }

        private void Update()
        {
            if (raceManager == null || raceManager.Item == null || !raceManager.Item.IsRaceInProgress())
                return;

            // paid continuously, including while boosting. level one drains at 0.28/s against this
            // 0.09/s, so a run still costs far more than it earns and cannot self-sustain; from empty
            // a bot is back to firing range in roughly eight seconds
            nitro.AddCharge(passiveChargePerSecond * Time.deltaTime);

            if (nitro.IsActive)
            {
                TryStepUp();
                return;
            }

            if (Time.time < nextDecisionTime) return;
            nextDecisionTime = Time.time + 0.2f;

            if (!PointedStraightEnough()) return;

            if (nitro.IsInRedZone)
            {
                // a perfect nitro is the best value in the game, so a skilled bot waits for it
                if (Random.value <= perfectNitroSkill * 0.2f)
                    Engage();
                return;
            }

            // behind the player is exactly when a bot should be spending, not hoarding
            var eager = rubberBanding != null && rubberBanding.CurrentSignedDistance < 0f;
            var ceiling = eager ? patienceChargeCeiling * 0.7f : patienceChargeCeiling;

            if (nitro.charge >= ceiling)
                Engage();
        }

        private void Engage()
        {
            nitro.Fire();
            steppingUpThisRun = Random.value <= stepUpChance;
            nextStepUpTime = Time.time + stepUpInterval;
        }

        private void TryStepUp()
        {
            if (!steppingUpThisRun) return;
            if (Time.time < nextStepUpTime) return;
            if (!PointedStraightEnough()) return;

            nitro.Fire();
            nextStepUpTime = Time.time + stepUpInterval;
        }

        private bool PointedStraightEnough()
        {
            if (car == null || car.m_MaximumSteerAngle <= 0.01f) return true;
            var steerFraction = Mathf.Abs(car.CurrentSteerAngle / car.m_MaximumSteerAngle);
            return steerFraction <= maxSteerFractionToFire;
        }
    }
}
