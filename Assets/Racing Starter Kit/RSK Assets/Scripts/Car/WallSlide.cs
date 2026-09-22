using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the arcade rail slide. before this, brushing a barrier at 160 mph scrubbed the car to a near
    /// stop, and a first version of the slide then let the car ping off the rail instead: the contact
    /// yawed the nose away and the steer helper drove the car off at 10 m/s, which QA saw as being
    /// "launched back".
    ///
    /// on a wall contact, and for a short memory window after it, the velocity is rebuilt as
    /// along-the-wall speed (topped back up towards the entry speed) plus a small capped component
    /// away from the wall, vertical launch is capped, yaw spin is killed and the nose is eased to run
    /// along the wall.
    ///
    /// which way the wall runs is taken from the racing line, not from the contact. the track kit's
    /// railing meshes have end caps and sloped tops, and a trace showed their contact normals pointing
    /// along the road or upward; every wall and railing on these tracks follows the road, so the line's
    /// tangent at the car is the reliable answer. the contact normal is only used to tell a wall from
    /// the floor, and as a fallback on a car with no racing line available.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WallSlide : MonoBehaviour
    {
        [Tooltip("Share of the pre-contact speed kept while sliding along a wall")]
        [Range(0f, 1f)] public float retainedSpeed = 0.82f;
        [Tooltip("A contact whose normal has at least this much horizontal component counts as a wall; the rest is floor or kerb")]
        [Range(0.2f, 1f)] public float minimumHorizontalNormal = 0.4f;
        [Tooltip("Speed away from the wall (m/s) allowed while sliding and during the hold. Enough to stop grinding, not enough to bounce off.")]
        public float maxAwaySpeed = 0.8f;
        [Tooltip("Upward speed (m/s) allowed during the slide, so a sloped railing cannot flick the car into the air")]
        public float maxUpwardSpeed = 1f;
        [Tooltip("How quickly the nose is turned to run along the wall, per second")]
        public float alignRate = 9f;
        [Tooltip("Below this speed (m/s) the slide does nothing, so a parked car can still reverse away from a wall")]
        public float minimumSpeed = 6f;
        [Tooltip("Longest the slide keeps holding the car along the wall after the last touch. Released early the moment the player steers away from the wall.")]
        public float contactMemory = 1.5f;
        [Tooltip("Steering angle (degrees) towards the open side that counts as steering away and releases the hold. Small, so the first touch of the stick lets go of the rail.")]
        public float releaseSteerAngle = 1f;
        [Tooltip("Share of yaw spin removed each physics step while sliding")]
        [Range(0f, 1f)] public float yawDamping = 0.6f;

        public bool IsSliding { get; private set; }

        /// <summary>
        /// drops any hold in progress. called by CarRespawn when it teleports the car, since a
        /// slide remembered from before the teleport would rewrite the velocity on the new spot
        /// </summary>
        public void Release()
        {
            lastContactTime = -999f;
            touchedThisStep = false;
            IsSliding = false;
        }
        /// <summary>diagnostics: what the car last slid against, and the contact's averaged normal</summary>
        public string LastContactName { get; private set; }
        public Vector3 LastContactNormal { get; private set; }
        public bool LastFrameTrusted { get; private set; }

        private Rigidbody body;
        private CarRespawn respawn;
        private CarController car;
        private float speedBeforeContact;
        private float lastContactTime = -999f;
        private Vector3 wallNormal;       // points from the wall towards the road, horizontal
        private Vector3 wallDirection;    // along the wall, the way the car was travelling before the touch
        private Vector3 travelBeforeContact = Vector3.forward;   // flat unit vector, frozen for the hold
        private bool touchedThisStep;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            respawn = GetComponent<CarRespawn>();
            car = GetComponent<CarController>();
        }

        private void FixedUpdate()
        {
            // the hold ends when the player steers towards the open side, or after contactMemory
            // with no further touch. steering into the wall or driving straight keeps the car on it,
            // which is the Asphalt feel: the rail is a guide until you choose to leave it
            var remembering = Time.time - lastContactTime < contactMemory && !SteeringAway();
            if (!remembering)
            {
                // not near a wall: keep a fresh record of the speed and direction the car would carry
                // into one. the direction is what fixes the sign of the slide: QA's max-speed video
                // showed the car spun to face backwards, because the sign used to come from the
                // velocity after the impact, which a hard corner hit can leave pointing the wrong way
                var flat = body.linearVelocity; flat.y = 0f;
                speedBeforeContact = body.linearVelocity.magnitude;
                travelBeforeContact = flat.magnitude > 1f ? flat.normalized
                                                          : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                IsSliding = false;
                return;
            }

            // in the memory window with no touch this step: hold the line
            if (!touchedThisStep) HoldAlongWall(false);
            touchedThisStep = false;
        }

        private bool SteeringAway()
        {
            if (car == null) return false;
            var steer = car.CurrentSteerAngle;
            if (Mathf.Abs(steer) < releaseSteerAngle) return false;
            // positive steer turns right; away is whichever side the wall normal (towards the road) is on
            var rightIsAway = Vector3.Dot(transform.right, wallNormal) > 0f;
            return rightIsAway ? steer > 0f : steer < 0f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // the first step of a contact is where the impact impulse lands; correcting it here rather
            // than a step later is what keeps a sloped railing from flicking the car upward
            OnCollisionStay(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision.rigidbody != null) return;   // another car: CarImpactFX owns that
            if (collision.contactCount == 0) return;

            var contactNormal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++) contactNormal += collision.GetContact(i).normal;
            contactNormal /= collision.contactCount;
            contactNormal.y = 0f;
            if (contactNormal.magnitude < minimumHorizontalNormal) return;   // floor, kerb or a ramp
            contactNormal.Normalize();

            var velocity = body.linearVelocity;
            var flatSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            if (flatSpeed < minimumSpeed && speedBeforeContact < minimumSpeed) return;

            if (!ResolveWallFrame(contactNormal, velocity)) return;

            LastContactName = collision.collider != null ? collision.collider.name : "?";
            LastContactNormal = collision.GetContact(0).normal;
            lastContactTime = Time.time;
            touchedThisStep = true;
            IsSliding = true;

            // while the player is steering towards the open side the contact must not fight them:
            // no cap on the speed away from the wall, no yaw damping, no nose alignment. only the
            // along-wall top-up and the no-bounce clamp stay, so leaving the rail is immediate and
            // still does not launch the car. QA saw the old behaviour as steering that stuck
            HoldAlongWall(true, SteeringAway());
        }

        [Tooltip("Degrees a contact normal may differ from the road's cross direction and still be trusted as the wall's own face. Beyond this it is an end cap or a slope and the road direction is used instead.")]
        public float trustedNormalAngle = 35f;

        /// <summary>
        /// the frame the slide works in. the racing line gives the road's direction at the car, but
        /// only as a chord between waypoints 40 to 130 m apart, and a chord is not parallel to a curved
        /// railing: the first attempt steered the car gently into the rail and the contact shoved it
        /// back out. so the contact's own normal is preferred when it roughly agrees with the road's
        /// cross direction, which is exactly the flat face of a wall or railing; a normal that
        /// disagrees is an end cap or a sloped top, and the road frame stands in for it
        /// </summary>
        private bool ResolveWallFrame(Vector3 contactNormal, Vector3 velocity)
        {
            // the sign of the slide is the direction the car was going before it touched, never the
            // post-impact velocity, so a hard hit cannot flip the car around
            var travel = travelBeforeContact;

            Vector3 onLine, tangent;
            if (respawn != null && respawn.TryGetRacingLine(transform.position, out onLine, out tangent))
            {
                tangent.y = 0f;
                if (tangent.sqrMagnitude > 0.01f)
                {
                    tangent.Normalize();
                    var toLine = onLine - transform.position;
                    toLine.y = 0f;
                    toLine = Vector3.ProjectOnPlane(toLine, tangent);
                    var roadNormal = toLine.sqrMagnitude > 0.25f ? toLine.normalized : contactNormal;

                    // the wall's own face, when the contact says so; otherwise the road's cross direction
                    var trusted = Vector3.Angle(contactNormal, roadNormal) <= trustedNormalAngle;
                    LastFrameTrusted = trusted;
                    wallNormal = trusted ? contactNormal : roadNormal;

                    // the slide runs the way the road runs HERE, so a slide carried into a bend follows
                    // the bend instead of driving straight into its railing; only the sign is frozen,
                    // from the direction of travel before the first touch
                    var signedTangent = Vector3.Dot(travel, tangent) >= 0f ? tangent : -tangent;
                    var alongFace = Vector3.ProjectOnPlane(signedTangent, wallNormal);
                    if (alongFace.sqrMagnitude < 0.01f) alongFace = Vector3.ProjectOnPlane(travel, wallNormal);
                    wallDirection = alongFace.normalized;
                    return wallDirection.sqrMagnitude > 0.5f;
                }
            }

            wallNormal = contactNormal;
            var along = Vector3.ProjectOnPlane(travel, contactNormal);
            wallDirection = along.sqrMagnitude > 0.01f
                ? along.normalized
                : Vector3.ProjectOnPlane(transform.forward, contactNormal).normalized;
            return wallDirection.sqrMagnitude > 0.5f;
        }

        /// <summary>
        /// the shared correction: velocity along the wall, capped away from it and upward, nose
        /// turned along it and yaw spin damped. on a touch the along-wall speed is also topped up
        /// </summary>
        private void HoldAlongWall(bool touching, bool steeringAway = false)
        {
            var velocity = body.linearVelocity;
            var away = Vector3.Dot(velocity, wallNormal);
            var along = Vector3.Dot(velocity, wallDirection);

            if (touching)
            {
                // a glancing touch keeps most of the entry speed; a steep one keeps only the share
                // that was already pointing along the wall, so a near head-on hit still costs speed
                var glancing = Mathf.Clamp01(Vector3.Dot(travelBeforeContact, wallDirection));
                along = Mathf.Max(along, speedBeforeContact * retainedSpeed * glancing);
            }
            // never into the wall; away from it only as far as the cap unless the player is steering
            // away, in which case whatever the front wheels pull is theirs to keep
            away = steeringAway ? Mathf.Max(away, 0f) : Mathf.Clamp(away, 0f, maxAwaySpeed);
            var up = Mathf.Min(velocity.y, maxUpwardSpeed);

            body.linearVelocity = wallDirection * along + wallNormal * away + Vector3.up * up;

            if (steeringAway) return;

            // yaw: kill the spin the impact gave the car, then ease the nose along the wall
            var spin = body.angularVelocity;
            spin.y *= 1f - yawDamping;
            body.angularVelocity = spin;

            var flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f) return;
            var current = Quaternion.LookRotation(flatForward, Vector3.up);
            var wanted = Quaternion.LookRotation(wallDirection, Vector3.up);
            var blended = Quaternion.Slerp(current, wanted, alignRate * Time.fixedDeltaTime);
            var yawDelta = Mathf.DeltaAngle(current.eulerAngles.y, blended.eulerAngles.y);
            body.MoveRotation(Quaternion.Euler(0f, yawDelta, 0f) * body.rotation);
        }
    }
}
