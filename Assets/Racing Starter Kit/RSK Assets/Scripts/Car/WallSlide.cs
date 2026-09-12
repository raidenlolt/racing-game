using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the arcade rail slide. before this, brushing a barrier at 160 mph scrubbed the car to a near
    /// stop: the wall's friction and the contact impulse took the speed and the car sat nose-in until
    /// the player reversed out. Asphalt does the opposite, and it is most of what makes its walls
    /// feel safe: the car is turned to run along the wall and keeps most of its speed.
    ///
    /// on contact with something static and roughly vertical, this strips the part of the velocity
    /// that points into the wall, tops the along-wall speed back up towards what the car had before
    /// the touch, and turns the nose parallel to the wall. the physics still stops the car going
    /// through anything; this only decides what happens to its momentum afterwards.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WallSlide : MonoBehaviour
    {
        [Tooltip("Share of the pre-contact speed kept while sliding along a wall")]
        [Range(0f, 1f)] public float retainedSpeed = 0.82f;
        [Tooltip("Contact normals steeper than this (dot with up) are floor or kerb, not wall")]
        public float maxNormalUp = 0.5f;
        [Tooltip("Small push away from the wall so the car peels off instead of grinding")]
        public float pushOff = 1.2f;
        [Tooltip("How quickly the nose is turned to run along the wall, per second")]
        public float alignRate = 6f;
        [Tooltip("Below this speed (m/s) the slide does nothing, so a parked car can still reverse away from a wall")]
        public float minimumSpeed = 6f;
        [Tooltip("Seconds after the last touch during which the entry speed is still remembered. A slide along a wall is a run of separate touches; forgetting between them let each one chip a bit more speed off.")]
        public float contactMemory = 0.6f;

        public bool IsSliding { get; private set; }

        private Rigidbody body;
        private float speedBeforeContact;
        private float lastContactTime = -1f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            // remember the speed the car carried into the wall; once in contact the current speed is
            // already the reduced one
            if (Time.time - lastContactTime > contactMemory)
            {
                speedBeforeContact = body.linearVelocity.magnitude;
                IsSliding = false;
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision.rigidbody != null) return;   // another car: CarImpactFX owns that
            if (collision.contactCount == 0) return;

            var normal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++) normal += collision.GetContact(i).normal;
            normal /= collision.contactCount;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.25f) return;      // floor, kerb or a ramp, not a wall
            normal.Normalize();
            if (Mathf.Abs(collision.GetContact(0).normal.y) > maxNormalUp) return;

            var velocity = body.linearVelocity;
            var into = Vector3.Dot(velocity, normal);
            var along = Vector3.ProjectOnPlane(velocity, normal);
            var alongFlat = along; alongFlat.y = 0f;
            if (alongFlat.magnitude < minimumSpeed && speedBeforeContact < minimumSpeed) return;

            lastContactTime = Time.time;
            IsSliding = true;

            // keep the along-wall speed up: the higher of what is left and the retained share of
            // what the car came in with
            var target = Mathf.Max(alongFlat.magnitude, speedBeforeContact * retainedSpeed);
            var direction = alongFlat.sqrMagnitude > 0.01f ? alongFlat.normalized
                                                          : Vector3.ProjectOnPlane(transform.forward, normal).normalized;
            var slide = direction * target + Vector3.up * velocity.y;
            if (into < 0f) slide += normal * pushOff;    // only peel off if still pressing in
            body.linearVelocity = slide;

            // turn the nose along the wall so the car does not stay pointed into it
            var heading = Vector3.ProjectOnPlane(transform.forward, normal);
            if (heading.sqrMagnitude > 0.01f)
            {
                var wanted = Quaternion.LookRotation(direction, Vector3.up);
                var current = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);
                var blended = Quaternion.Slerp(current, wanted, alignRate * Time.fixedDeltaTime);
                var yawDelta = Mathf.DeltaAngle(current.eulerAngles.y, blended.eulerAngles.y);
                body.MoveRotation(Quaternion.Euler(0f, yawDelta, 0f) * body.rotation);
            }
        }
    }
}
