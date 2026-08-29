using UnityEngine;
/// <summary>
/// use two raycasts, one at each side of the car to detect obstacles and steer the other way
/// </summary>
namespace SpinMotion
{
    public class AICarAvoidanceSmartSteering : MonoBehaviour
    {
        public CarController aiCarController;

        [Header("Detection")]
        [Tooltip("What the avoidance rays can hit. Include the Player and AIPlayer layers plus any track walls")]
        public LayerMask obstacleLayers;
        [Tooltip("Lookahead at a standstill, in metres")]
        public float baseRaycastDistance = 4f;
        [Tooltip("Extra metres of lookahead per m/s of speed, so fast cars react earlier")]
        public float speedLookaheadFactor = 0.35f;
        [Tooltip("Ray start, in car local space. Should sit near the front left corner, at body height")]
        public Vector3 leftRaycastOffset;
        [Tooltip("Ray start, in car local space. Should sit near the front right corner, at body height")]
        public Vector3 rightRaycastOffset;

        [Header("Response")]
        [Tooltip("How hard the avoidance pushes the steering, as a fraction of the car's maximum steer angle")]
        [Range(0f, 1f)] public float steeringStrength = 0.4f;

        private const float MphToMetresPerSecond = 1f / 2.23693629f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];

        private Vector3 leftRayOrigin;
        private Vector3 rightRayOrigin;
        private float currentRaycastDistance;
        private Transform selfRoot;

        private void Awake()
        {
            // a freshly added LayerMask deserialises to 0 on existing prefabs, which would silently disable
            // every raycast. fall back to the layers this system has always been meant to watch
            if (obstacleLayers.value == 0)
            {
                obstacleLayers = LayerMask.GetMask("Player", "AIPlayer");
                Debug.LogWarning($"{name}: no obstacle layers assigned for AI smart steering, defaulting to Player + AIPlayer");
            }

            aiCarController.forceSteeringFactor = steeringStrength;

            // the AI layer is now part of the mask, so the rays would otherwise hit this very car
            selfRoot = aiCarController.transform.root;
        }

        // run every physics step rather than polling on a coroutine delay: the old version held one decision
        // for the whole delay, so a single spurious hit locked a steering bias in for seconds of racing
        private void FixedUpdate()
        {
            currentRaycastDistance = baseRaycastDistance
                                     + aiCarController.CurrentSpeed * MphToMetresPerSecond * speedLookaheadFactor;

            bool leftHit = HasObstacle(CalculateLeftRayOrigin());
            bool rightHit = HasObstacle(CalculateRightRayOrigin());

            // steer away from whichever side is blocked (positive steering turns right)
            if (leftHit && !rightHit)
            {
                aiCarController.forceSteering = 1;
            }
            else if (rightHit && !leftHit)
            {
                aiCarController.forceSteering = -1;
            }
            else
            {
                aiCarController.forceSteering = 0;
            }
        }

        // non alloc cast so we can skip hits belonging to this car without generating garbage every step
        private bool HasObstacle(Vector3 origin)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, transform.forward, HitBuffer, currentRaycastDistance,
                                                   obstacleLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                if (HitBuffer[i].collider.transform.root != selfRoot)
                    return true;
            }

            return false;
        }

        private void OnDrawGizmos()
        {
            float gizmoDistance = Application.isPlaying ? currentRaycastDistance : baseRaycastDistance;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(CalculateLeftRayOrigin(), leftRayOrigin + transform.forward * gizmoDistance);
            Gizmos.DrawLine(CalculateRightRayOrigin(), rightRayOrigin + transform.forward * gizmoDistance);
        }

        // the offsets are car local: rotate them with the car but do not scale them, so the numbers in the
        // inspector stay in metres regardless of what the car prefab is scaled to
        private Vector3 CalculateLeftRayOrigin()
        {
            return leftRayOrigin = transform.position + transform.rotation * leftRaycastOffset;
        }

        private Vector3 CalculateRightRayOrigin()
        {
            return rightRayOrigin = transform.position + transform.rotation * rightRaycastOffset;
        }
    }
}
