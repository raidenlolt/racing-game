using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// This script will move the AI waypoint tracker when the AI car reaches a point. The AI car will always follow the tracker
/// and the tracker will take the position of the next point that is placed on the racetrack
/// in this way the AI car will roam around the track all the time
/// (enable AI tracker & AI waypoints mesh renderer to understand better)
/// </summary>
namespace SpinMotion
{
    public class AIWaypointTracker : MonoBehaviour
    {
        public GameEvents gameEvents;
        public AIWaypointSet aiWaypointSet;
        public Vector3 trackerScale;

        [Header("Waypoint Advance Settings")]
        [Tooltip("How close the AI car has to get to the tracker before it moves on to the next waypoint")]
        public float arriveRadius = 6f;
        [Tooltip("Also advance once the car has driven past the waypoint, even if it passed wide of the arrive radius")]
        public bool advanceWhenPassed = true;

        private List<AIWaypoint> aiWaypoints = new();
        private Transform currentWaypoint;
        private int currentIndex;
        private Transform aiCarTransform;
        private Collider waypointTrackerCollider;
        private Vector3 currentTangent = Vector3.forward;

        private void Awake()
        {
            gameEvents.RestartRaceEvent.AddListener(OnRestartRace);

            // the tracker used to advance from OnTriggerEnter, which meant disabling its collider for 0.1s after
            // every hit. at racing speed that blind window is metres long, so a closely spaced waypoint could be
            // tunnelled through and the AI would drive a whole lap to recover. we do a distance check instead
            waypointTrackerCollider = GetComponent<BoxCollider>();
            if (waypointTrackerCollider != null)
                waypointTrackerCollider.enabled = false;

            transform.localScale = trackerScale;
            ResetTracker();
        }

        private void OnRestartRace()
        {
            ResetTracker();
        }

        private void ResetTracker()
        {
            this.aiWaypoints = aiWaypointSet.Items;
            currentIndex = 0;
            UpdateTrackerPosition();
        }

        public void SetupAICarCollider(Collider aiCarCollider)
        {
            this.aiCarTransform = aiCarCollider.transform;
        }

        private void FixedUpdate()
        {
            if (aiCarTransform == null || aiWaypoints.Count == 0)
                return;

            Vector3 waypointToCar = aiCarTransform.position - transform.position;

            bool arrived = waypointToCar.sqrMagnitude < arriveRadius * arriveRadius;
            // a radius alone can be missed if the car rounds the corner wide, so also treat the waypoint as
            // reached once the car is on the far side of it along the direction of travel
            bool passed = advanceWhenPassed && Vector3.Dot(waypointToCar, currentTangent) > 0f;

            if (arrived || passed)
                AdvanceToNextWaypoint();
        }

        private void AdvanceToNextWaypoint()
        {
            currentIndex++;
            if (currentIndex >= aiWaypoints.Count)
                currentIndex = 0;

            UpdateTrackerPosition();
        }

        private void UpdateTrackerPosition()
        {
            if (aiWaypoints.Count == 0)
            {
                Debug.LogError("AI waypoint set is empty, the AI tracker has nothing to follow");
                return;
            }

            currentWaypoint = aiWaypoints[currentIndex].aiWaypointTransform;
            this.transform.position = currentWaypoint.position;

            // the waypoint boxes are axis aligned, so their own rotation is meaningless. take the path
            // tangent from the next waypoint instead: CarAIControl reads target.forward to anticipate
            // corners and target.right to offset its line, both need to follow the track direction
            var nextWaypoint = aiWaypoints[(currentIndex + 1) % aiWaypoints.Count].aiWaypointTransform;
            Vector3 tangent = nextWaypoint.position - currentWaypoint.position;
            if (tangent.sqrMagnitude > 0.001f)
            {
                currentTangent = tangent.normalized;
                this.transform.rotation = Quaternion.LookRotation(currentTangent, Vector3.up);
            }
        }
    }
}
