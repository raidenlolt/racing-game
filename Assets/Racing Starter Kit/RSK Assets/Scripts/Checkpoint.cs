using System;
using UnityEngine;
/// <summary>
/// this script is attached to all the checkpoints in the race and measures the car position to the checkpoint
/// in that way it sends that distance measure to RealTimeRacePositions script to help calculating race position of each car
/// </summary>
namespace SpinMotion
{
    public class Checkpoint : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;
        public RealTimeRacePositionsItem realTimeRacePositions;

        private int checkpointNumber;
        public int GetNumber() { return checkpointNumber; }
        public void SetCheckpointNumber(int checkpointNumber) { this.checkpointNumber = checkpointNumber; }


        // this used to measure, every frame and for every car, the straight line distance from this
        // checkpoint to the car, and feed it to RealTimeRacePositions as the fine grained part of the
        // race score. that is no longer how positions are worked out: progress is measured as distance
        // along the racing line, which is continuous and always increases as a car drives forwards,
        // whereas distance-from-a-checkpoint shrinks again through any corner that bends back on
        // itself. nothing reads the value now, so the work is gone rather than left running.
        //
        // a checkpoint's job is only to be crossed. CheckpointTracker handles that from its own
        // OnTriggerEnter, which is what counts laps and keeps a car from skipping half the circuit.
    }
}