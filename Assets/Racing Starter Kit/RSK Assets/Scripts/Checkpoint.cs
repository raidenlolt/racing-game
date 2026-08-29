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


        private void Update()
        {
            if (!raceManager.Item.IsRaceInProgress()) return;
            if (realTimeRacePositions.Item.CarCheckpointTrackers.Count == 0) return;

            for (int i = 0; i < realTimeRacePositions.Item.CarCheckpointTrackers.Count; i++)
            {
                if (realTimeRacePositions.Item.CheckpointScores[i] == checkpointNumber)
                {
                    var checkpointDistanceToCarCheckpointTracker = Vector3.Distance(transform.position, realTimeRacePositions.Item.CarCheckpointTrackers[i].transform.position);
                    realTimeRacePositions.Item.DistanceFromCheckpointToCarTrackers[i] = checkpointDistanceToCarCheckpointTracker;//and we send the information to the ChkManager.cs script (checkpoint manager)
                    //checkpoint manager will compare the distance, checkpoints passed and laps done of each car of the race to obtain real time positioning
                }
            }
        }
    }
}