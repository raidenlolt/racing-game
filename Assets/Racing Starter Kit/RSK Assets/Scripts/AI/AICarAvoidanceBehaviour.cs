using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
/// <summary>
/// This script detects the AI car speed to see if the car it’s stuck so it will start going reverse for 1 second to get back on track
/// Also, it uses a Box collider with IsTrigger option checked to see if an AI Car or the player car is in front of this car to brake
/// </summary>
namespace SpinMotion
{
    [RequireComponent(typeof(Collider))]
    public class AICarAvoidanceBehaviour : MonoBehaviour
    {
        public GameEvents gameEvents;
        public CarController aiCarController;
        public Rigidbody aiCarRigidbody;
        
        public float slowSpeedThreshold = 0.25f;
        public float reducedSpeed = 15f;

        private WheelCollider[] allWheelColliders;
        private Coroutine checkReverseCoroutine;
        private Coroutine reverseCoroutine;
        private float aiCarSpeed;
        private bool checkReverse, startReverse;
        private float normalSteering, normalTopSpeed, normalTorque;
        private int playerLayer, aiPlayerLayer;
        private readonly HashSet<Collider> blockersInFront = new(); // cars currently inside the avoidance trigger box
        
        private void Awake()
        {
            gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
            gameEvents.RestartRaceEvent.AddListener(OnRestartRace);
            gameEvents.RaceFinishedEvent.AddListener(OnRaceFinished);

            allWheelColliders = aiCarController.transform.GetComponentsInChildren<WheelCollider>();
            if (allWheelColliders.Count() == 0)
                Debug.LogWarning("No wheel colliders were found for AI car avoidance detection");
            
            normalSteering = aiCarController.m_MaximumSteerAngle;
            normalTopSpeed = aiCarController.m_Topspeed;
            normalTorque = aiCarController.m_FullTorqueOverAllWheels;

            playerLayer = LayerMask.NameToLayer("Player");
            aiPlayerLayer = LayerMask.NameToLayer("AIPlayer");
        }

        private void OnDisable()
        {
            // a reverse can be cut short by a restart or by the car being despawned. restore everything the
            // reverse coroutine borrowed, otherwise this car drives backwards unable to steer for good
            RestoreNormalDriving();
        }

        private void RestoreNormalDriving()
        {
            if (aiCarController == null) // the whole car is being destroyed, nothing left to restore
                return;

            if (reverseCoroutine != null)
            {
                StopCoroutine(reverseCoroutine);
                reverseCoroutine = null;
            }

            aiCarController.forceBraking = 0;
            aiCarController.m_FullTorqueOverAllWheels = normalTorque;
            aiCarController.m_MaximumSteerAngle = normalSteering;

            blockersInFront.Clear();
            aiCarController.m_Topspeed = normalTopSpeed;
        }

        private void OnRaceStarted()
        {
            StopCheckReverse();
            checkReverseCoroutine = StartCoroutine(CheckReverseCoroutine());
        }

        private void OnRestartRace()
        {
            StopCheckReverse();
            RestoreNormalDriving();
        }

        private void OnRaceFinished(RaceFinishType raceFinishType)
        {
            StopCheckReverse();
            RestoreNormalDriving();
        }

        private void StopCheckReverse()
        {
            if (checkReverseCoroutine != null)
                StopCoroutine(checkReverseCoroutine);

            checkReverse = false;
        }

        private IEnumerator CheckReverseCoroutine()
        {
            // the script will wait 3 seconds so the AI car can get enough speed after starting the race
            // otherwise if you don't wait, the car always start at 0 speed so it will trigger the reverse coroutine
            yield return new WaitForSeconds(3); //now that 3 seconds passed we can check if the AI car needs to use the 1 second reverse
            checkReverse = true;
        }

        private void Update()
        {
            aiCarSpeed = aiCarRigidbody.linearVelocity.magnitude;

            if (aiCarSpeed < slowSpeedThreshold && checkReverse)
            {
                checkReverse = false;
                startReverse = true;
            }

            if (startReverse)
            {
                startReverse = false;
                aiCarController.m_FullTorqueOverAllWheels = -normalTorque; // reverse enabled
                aiCarController.m_MaximumSteerAngle = 0; // block turns to back up in reverse straight
                reverseCoroutine = StartCoroutine(ReverseCoroutine());
            }

            PruneDespawnedBlockers();
        }

        private IEnumerator ReverseCoroutine()
        {
            aiCarController.forceBraking = -1;// failsafe for glitchy wheel colliders: (prevents getting stalled)
            yield return new WaitForSeconds(1);
            aiCarController.forceBraking = 0;// unapply failsafe after 1s, resume normal reverse coroutine
            aiCarController.m_FullTorqueOverAllWheels = normalTorque;
            // after one second, the car will be able to turn again (we don't want to turn while reversing, go straight backing up)
            yield return new WaitForSeconds(1);
            aiCarController.m_MaximumSteerAngle = normalSteering;
            reverseCoroutine = null;
            StopCheckReverse();
            checkReverseCoroutine = StartCoroutine(CheckReverseCoroutine());
        }
        
        private bool IsCarCollider(Collider collider)
        {
            int layer = collider.gameObject.layer;
            return layer == playerLayer || layer == aiPlayerLayer;
        }

        // a car despawned or was disabled while inside the trigger box, so OnTriggerExit never fired for it.
        // without this the AI stays capped at reducedSpeed for the rest of the race
        private void PruneDespawnedBlockers()
        {
            if (blockersInFront.Count == 0)
                return;

            if (blockersInFront.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy) > 0
                && blockersInFront.Count == 0)
            {
                aiCarController.m_Topspeed = normalTopSpeed;
            }
        }

        // set "Player" layer on player car colliders prefabs and "AIPlayer" for AI car colliders
        void OnTriggerEnter(Collider collider)
        {
            // count the cars in the box rather than tracking a single one: with two cars in front, the first
            // one to leave would otherwise restore full top speed while the second is still ahead of us
            if (IsCarCollider(collider) && blockersInFront.Add(collider))
            {
                aiCarController.m_Topspeed = reducedSpeed;
            }
        }
        
        void OnTriggerExit(Collider collider)
        {
            if (blockersInFront.Remove(collider) && blockersInFront.Count == 0)
            {
                aiCarController.m_Topspeed = normalTopSpeed;
            }
        }
    }
}