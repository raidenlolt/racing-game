using UnityEngine;

namespace SpinMotion
{
    [RequireComponent(typeof(Collider))]
    public class JumpBoostPad : MonoBehaviour
    {
        [Header("Jump & Boost Settings")]
        [SerializeField] private float m_JumpForce = 0; // Upward force to lift the car
        [SerializeField] private float m_ForwardBoostForce = 5000; // Forward momentum to clear gaps
        
        private void OnTriggerEnter(Collider other)
        {
            // Use GetComponentInParent in case the collider that hits the trigger 
            // is a child object (like a WheelCollider or bumper) rather than the root.
            CarController car = other.GetComponentInParent<CarController>();

            // If we found the CarController, trigger the boost
            if (car != null)
            {
                car.ApplyJumpBoost(m_JumpForce, m_ForwardBoostForce);
            }
        }
    }
}