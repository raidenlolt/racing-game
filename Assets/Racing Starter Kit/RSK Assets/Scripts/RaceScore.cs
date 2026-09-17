using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the number a race is worth. the game used to show its internal progress figure (metres
    /// along the racing line plus laps) as "Score", which is meaningless to a player and identical
    /// for every finisher. the platform needs one integer per run, so this is now the score shown
    /// on the results panel and the score submitted.
    ///
    /// finishing is worth a fixed amount by place; a run cut short by the timer earns partial credit
    /// for how far it got, always less than last place among finishers so finishing is never the
    /// worse outcome.
    /// </summary>
    public static class RaceScore
    {
        private static readonly int[] PlacePoints = { 1000, 800, 650, 500, 400, 300, 200, 150 };
        private const int TimeoutMaxPoints = 140;

        public static int Compute(RaceFinishType type, int place, int fieldSize, float progressFraction)
        {
            if (type == RaceFinishType.Timeout)
                return Mathf.RoundToInt(Mathf.Clamp01(progressFraction) * TimeoutMaxPoints);

            var index = Mathf.Clamp(place - 1, 0, PlacePoints.Length - 1);
            return PlacePoints[index];
        }

        /// <summary>share of the whole race distance a car has covered, 0..1</summary>
        public static float ProgressFraction(RealTimeRacePositions positions, int car)
        {
            if (positions == null || car < 0 || car >= positions.RacePositionTotalScores.Count) return 0f;
            var lapLength = positions.LapLength;
            if (lapLength <= 0f || RaceData.LapsSelected <= 0) return 0f;
            var total = RaceData.LapsSelected * lapLength;
            return Mathf.Clamp01((float)(positions.RacePositionTotalScores[car] / total));
        }
    }
}
