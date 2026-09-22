using System;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the number a race is worth: the client's speed-and-lap formula (change request of 2026-09-22).
    ///
    ///   score = round(1,000,000 x completed laps / finish time in seconds) + completed laps x 1,000
    ///
    /// faster races score more, every completed lap adds a flat bonus, and only a completed race
    /// scores at all: a run cut short by the timer is worth 0 and is not a leaderboard result.
    /// checkpoint skipping cannot inflate it because CheckpointTracker only counts a lap when every
    /// checkpoint was crossed in order, so the lap count here is the count the tracker vouches for.
    ///
    /// this is the score shown on the results panel and the score submitted to the platform, from
    /// one place so the two can never disagree.
    /// </summary>
    public static class RaceScore
    {
        public const double SpeedPoints = 1000000.0;
        public const int LapBonus = 1000;

        /// <summary>a race that ended at the line, not on the clock</summary>
        public static bool IsCompleted(RaceFinishType type)
        {
            return type != RaceFinishType.Timeout;
        }

        public static int Compute(RaceFinishType type, int completedLaps, float finishSeconds)
        {
            if (!IsCompleted(type) || completedLaps <= 0 || finishSeconds <= 0f) return 0;
            var speed = Math.Round(SpeedPoints * completedLaps / finishSeconds);
            var total = speed + (double)completedLaps * LapBonus;
            return (int)Math.Min(int.MaxValue, total);
        }

        /// <summary>
        /// laps a car has completed. the lap counter counts the start-line crossing at the flag as
        /// lap 1 (it is the lap in progress), so completed laps is one less, never above the race
        /// length and never below zero
        /// </summary>
        public static int CompletedLaps(RealTimeRacePositions positions, int car)
        {
            if (positions == null || car < 0 || car >= positions.LapScores.Count) return 0;
            return Mathf.Clamp(positions.LapScores[car] - 1, 0, Mathf.Max(0, RaceData.LapsSelected));
        }

        /// <summary>race time from the flag to the finish, from whichever RaceManager is in the scene</summary>
        public static float FinishSeconds()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
            return manager != null ? manager.RaceSeconds : 0f;
        }

        /// <summary>the player's score for the race that just ended, from the live race state</summary>
        public static int ForPlayer(RaceFinishType type, RealTimeRacePositions positions)
        {
            return Compute(type, CompletedLaps(positions, 0), FinishSeconds());
        }
    }
}
