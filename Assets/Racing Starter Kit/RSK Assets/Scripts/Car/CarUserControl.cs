using UnityEngine;
/// <summary>
/// send player input info to car controller
/// </summary>
namespace SpinMotion
{
    [RequireComponent(typeof (CarController))]
    public class CarUserControl : MonoBehaviour
    {
        /// <summary>
        /// set by RaceFinishSequence for the seconds after the line: the car coasts to a stop under
        /// a light brake and ignores the player until the results are up. static, because the
        /// sequence lives on the HUD and the car is spawned at race time
        /// </summary>
        public static bool InputLocked;

        [Tooltip("Speed (mph) above which the locked car brakes gently rather than holding the handbrake")]
        public float coastBrakeAboveSpeed = 8f;
        [Range(0f, 1f)] public float coastBrake = 0.35f;

        private CarController m_Car; // the car controller we want to use

        private void Awake()
        {
            // get the car controller
            m_Car = GetComponent<CarController>();
        }

        private void FixedUpdate()
        {
            if (InputLocked)
            {
                // below the threshold the footbrake would become reverse torque, so hold the handbrake
                if (m_Car.CurrentSpeed > coastBrakeAboveSpeed)
                    m_Car.Move(0f, 0f, -coastBrake, 0f);
                else
                    m_Car.Move(0f, 0f, 0f, 1f);
                return;
            }

            // pass the input to the car!
            float h = MobileInputManager.GetAxis("Horizontal");
            float v = MobileInputManager.GetAxis("Vertical");
#if !MOBILE_INPUT
            float handbrake = MobileInputManager.GetAxis("Jump");
            m_Car.MoveArcade(h, v, v, handbrake);
#else
            m_Car.MoveArcade(h, v, v, 0f);
#endif
        }
    }
}
