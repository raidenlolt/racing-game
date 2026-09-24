using UnityEngine;
/// <summary>
/// this script takes the count of the checkpoints and laps passed of each player in the race
/// the information is stored in RealTimeRacePositions script that calculates the race position scoring
/// it can also be named CheckpointColliderTrigger but as it tracks the amount of checkpoint the player has passed, it was ultimately named CheckpointTracker
/// </summary>
namespace SpinMotion
{
    public class CheckpointTracker : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;
        public RealTimeRacePositionsItem realTimeRacePositions;

        private int carRacePositionIndex;

        public void SetCarRacePositionIndex(int carRacePositionIndex)
        {
            this.carRacePositionIndex = carRacePositionIndex;
        }

        /// <summary>this car has crossed the line after its last lap; raised once per race</summary>
        public event System.Action Finished;
        public bool HasFinished { get; private set; }
        public int GetCarRacePositionIndex() { return carRacePositionIndex; }

        private int currentCheckpoint;
        private int nextCheckpoint;

        private void Awake()
        {
            gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
        }

        private void OnRaceStarted()
        {
            currentCheckpoint = 0;
            nextCheckpoint = 1;
            HasFinished = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            Cross(other);
        }

        /// <summary>
        /// a car that is already inside a checkpoint when the race starts never gets an Enter for it,
        /// which used to cost a whole lap when a grid slot sat on the start line. Stay catches that
        /// case; once the checkpoint has been accepted the number no longer matches, so nothing is
        /// counted twice
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            Cross(other);
        }

        private void Cross(Collider other)
        {
            if (!raceManager.Item.IsRaceInProgress()) return;

            var checkpoint = other.GetComponent<Checkpoint>();
            if (checkpoint == null) return;
            
            // position 0 in chk manager arrays stands for player 1
            realTimeRacePositions.Item.DistanceFromCheckpointToCarTrackers[carRacePositionIndex] = 0;//set the distance from checkpoint to 0 when crossing it
            var checkpointNumber = checkpoint.GetNumber();
            
            if (currentCheckpoint + 1 == checkpointNumber)
            {
                //position 0 in chk manager arrays stands for player 1
                realTimeRacePositions.Item.CheckpointScores[carRacePositionIndex] = checkpointNumber;
                gameEvents.CheckpointPassedEvent.Invoke(carRacePositionIndex, checkpointNumber);
                
                currentCheckpoint += 1;
                nextCheckpoint += 1;

                if (checkpointNumber == 1) //if the next checkpoint you have to pass is number 1
                { //that means that you passed all the checkpoints
                    //position 0 in chk manager arrays stands for player 1
                    realTimeRacePositions.Item.LapScores[carRacePositionIndex] += 1;//and a lap is added to the lap counter
                    currentCheckpoint = 1;//and the current checkpoint will be 1 (being 2 the next checkpoint)
                    if (carRacePositionIndex == 0)//if trigchk it's located in player 1
                    {
                        gameEvents.LapCompletedEvent.Invoke();
                    }
                    // the lap counter counts the crossing at the flag as lap 1, so the race is done
                    // one past the selected count. RaceFinish handles the player; bots listen here
                    if (!HasFinished && realTimeRacePositions.Item.LapScores[carRacePositionIndex] > RaceData.LapsSelected)
                    {
                        HasFinished = true;
                        var handler = Finished;
                        if (handler != null) handler();
                    }
                }
            }
            //if the next checkpoint doesn't exist, then you reached the last checkpoint of the circuit
            if (nextCheckpoint > RaceData.CheckpointsCount)
            {
                //so the checkpoint counter resets and gets ready to receive the first checkpoint again
                currentCheckpoint = 0;
                nextCheckpoint = 1;
            }
        }
    }
}