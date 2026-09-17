using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// works out who is winning, by measuring how far around the circuit each car has actually driven.
    ///
    /// a car's progress is the distance it has covered along the racing line since the start/finish
    /// line, in metres, plus a full lap for every lap it has completed. comparing those numbers gives
    /// the running order directly.
    ///
    /// this replaces a scheme that scored cars on checkpoints passed plus the straight line distance
    /// from the last checkpoint. that had two failures which no amount of retuning fixes:
    ///
    ///   the terms fought each other. one checkpoint was worth 100 points while the distance term
    ///   counted up to the gap between checkpoints, which is 316m on Race_Track_01. so crossing a
    ///   checkpoint, which resets distance to zero, LOWERED a car's score, and the leader dropped to
    ///   last every time it passed one.
    ///
    ///   straight line distance is not progress. it is measured from the checkpoint to the car, so
    ///   through any corner that bends back on itself the distance shrinks while the car is driving
    ///   forwards, and the car goes backwards through the standings.
    ///
    /// arc length along the racing line has neither problem: it only ever increases as a car drives
    /// forwards, it does not jump at checkpoints, and it does not care how many checkpoints a track
    /// has or how far apart they are. it is also in metres, so the numbers mean something.
    ///
    /// laps are counted here from the racing line itself rather than read from the checkpoint counter,
    /// so that progress stays continuous across the start/finish line. the checkpoint lap counter is
    /// still what LapsGUI and RaceFinish use, because that is the one that enforces actually driving
    /// the whole circuit.
    /// </summary>
    public class RealTimeRacePositions : MonoBehaviour
    {
        public RealTimeRacePositionsItem realTimeRacePositionsRuntimeItem;
        public GameEvents gameEvents;
        public RaceManagerItem raceManager;

        [Tooltip("The racing line progress is measured along. Left empty, it is taken from the scene's AIWaypoints on race start.")]
        public AIWaypointSet aiWaypointSet;

        public List<CheckpointTracker> CarCheckpointTrackers = new();
        public List<double> DistanceFromCheckpointToCarTrackers = new();
        public List<int> CheckpointScores = new();
        public List<int> LapScores = new();

        /// <summary>metres driven around the circuit, laps included. higher is further ahead.</summary>
        public List<double> RacePositionTotalScores = new();

        /// <summary>the racing line, closed, taken from the AI waypoints</summary>
        private Vector3[] linePoints;
        /// <summary>distance from the first point to each point; the last entry is the lap length</summary>
        private float[] lineArc;
        private float lapLength;
        /// <summary>length of one lap along the racing line, in metres, once the line is built</summary>
        public float LapLength { get { return lapLength; } }
        /// <summary>where the start/finish line falls on the racing line, so progress is measured from it</summary>
        private float finishLineArc;

        private float[] previousArc;
        private int[] lapsFromLine;
        private Vector3[] previousPosition;

        private void Awake()
        {
            realTimeRacePositionsRuntimeItem.Set(this);
            gameEvents.PlayersCheckpointTrackersAssignedEvent.AddListener(OnPlayersCheckpointTrackersAssigned);
            gameEvents.RaceStartedEvent.AddListener(OnRaceStarted);
        }

        private void OnPlayersCheckpointTrackersAssigned(List<CheckpointTracker> playersCheckpointTrackers)
        {
            CarCheckpointTrackers.Clear();
            CarCheckpointTrackers.AddRange(playersCheckpointTrackers);
        }

        private void OnRaceStarted()
        {
            //clear previous races values when starting a new race
            DistanceFromCheckpointToCarTrackers.Clear();
            CheckpointScores.Clear();
            LapScores.Clear();
            RacePositionTotalScores.Clear();

            CarCheckpointTrackers.Sort((a, b) => a.GetCarRacePositionIndex().CompareTo(b.GetCarRacePositionIndex()));

            //here we add a checkpoint, distance, laps, score and player number position for all cars in the scene
            for (int i = 0; i < CarCheckpointTrackers.Count; i++)
            {
                DistanceFromCheckpointToCarTrackers.Add(0);
                CheckpointScores.Add(RaceData.CheckpointsCount);
                LapScores.Add(0);
                RacePositionTotalScores.Add(0);
            }

            BuildRacingLine();
            InitialiseGridProgress();
        }

        /// <summary>
        /// puts the whole grid on the same lap before the race starts.
        ///
        /// the start/finish line sits at the front of the grid, so the cars behind it are at the very
        /// END of a lap by arc length -- around 1687 of 1706 metres -- while any car level with the
        /// line reads nearly zero. left alone that makes the grid look like it is spread over a full
        /// lap, and it decides a car's lap offset from whichever frame it first happened to be
        /// sampled on: measured mid-start, the player read 73m while the bots read 1700m, purely
        /// because the player had already crossed the line by the time the first sample was taken and
        /// so was never seen to wrap.
        ///
        /// counting a car that starts behind the line as one lap back makes its progress a small
        /// negative number, so crossing the line runs -19, -5, +2 without a step, and every car is
        /// measured from the same place no matter when it is first sampled.
        /// </summary>
        private void InitialiseGridProgress()
        {
            var count = CarCheckpointTrackers.Count;
            previousArc = new float[count];
            lapsFromLine = new int[count];
            previousPosition = new Vector3[count];

            if (linePoints == null || lapLength <= 0f) return;

            for (int i = 0; i < count; i++)
            {
                var tracker = CarCheckpointTrackers[i];
                if (tracker == null) continue;

                var position = tracker.transform.root.position;
                var arc = ArcFromFinishLine(position);
                previousArc[i] = arc;
                previousPosition[i] = position;
                lapsFromLine[i] = arc > lapLength * 0.5f ? -1 : 0;
            }
        }

        /// <summary>
        /// caches the racing line and the running distance to each of its points, so a car's progress
        /// is a projection and a table lookup rather than a walk of the whole circuit every frame
        /// </summary>
        private void BuildRacingLine()
        {
            linePoints = null;
            lineArc = null;
            lapLength = 0f;
            finishLineArc = 0f;

            var set = ResolveWaypointSet();
            if (set == null) { Debug.LogError("[Positions] no AI waypoint set, race positions cannot be measured"); return; }

            var points = new List<Vector3>();
            foreach (var item in set.Items)
                if (item != null && item.aiWaypointTransform != null)
                    points.Add(item.aiWaypointTransform.position);

            if (points.Count < 3) { Debug.LogError("[Positions] racing line needs at least 3 waypoints"); return; }

            linePoints = points.ToArray();
            lineArc = new float[linePoints.Length + 1];
            for (int i = 0; i < linePoints.Length; i++)
                lineArc[i + 1] = lineArc[i] + Vector3.Distance(linePoints[i], linePoints[(i + 1) % linePoints.Length]);
            lapLength = lineArc[linePoints.Length];

            finishLineArc = FindFinishLineArc();
        }

        /// <summary>
        /// the waypoint loop the AI drives. taking it from the scene when it is not assigned means
        /// this works on a track without anyone having to remember to wire up another reference
        /// </summary>
        private AIWaypointSet ResolveWaypointSet()
        {
            if (aiWaypointSet != null && aiWaypointSet.Items.Count > 0) return aiWaypointSet;
            var source = FindFirstObjectByType<AIWaypoints>();
            return source != null ? source.aiWaypointSet : null;
        }

        /// <summary>
        /// where on the racing line the start/finish line sits. checkpoint 1 is the finish, since it
        /// is the one CheckpointTracker ticks a lap on.
        /// </summary>
        private float FindFinishLineArc()
        {
            foreach (var checkpoint in FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
                if (checkpoint.GetNumber() == 1)
                    return RawArc(checkpoint.transform.position);
            return 0f;
        }

        /// <summary>
        /// how far along the racing line a point falls, measured from the line's own first waypoint.
        ///
        /// the closest point is found against the segments rather than the waypoints, because the
        /// waypoints here are up to 136m apart and a car on the racing line can sit halfway between
        /// two of them.
        /// </summary>
        private float RawArc(Vector3 position)
        {
            var bestSqr = float.MaxValue;
            var bestArc = 0f;

            for (int i = 0; i < linePoints.Length; i++)
            {
                Vector3 a = linePoints[i], b = linePoints[(i + 1) % linePoints.Length];
                var ab = b - a;
                var lengthSqr = ab.sqrMagnitude;
                var t = lengthSqr < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(position - a, ab) / lengthSqr);
                var closest = a + ab * t;

                var offset = closest - position;
                offset.y = 0f;   // a car in the air over the track is still at that point on the track
                var sqr = offset.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestArc = lineArc[i] + ab.magnitude * t;
                }
            }
            return bestArc;
        }

        /// <summary>distance driven since the start/finish line, wrapped into a single lap</summary>
        private float ArcFromFinishLine(Vector3 position)
        {
            var arc = RawArc(position) - finishLineArc;
            if (arc < 0f) arc += lapLength;
            return arc;
        }

        public void Update()
        {
            if (raceManager == null || raceManager.Item == null || !raceManager.Item.IsRaceInProgress()) return;
            if (linePoints == null || lapLength <= 0f) return;
            if (previousArc == null || previousArc.Length != CarCheckpointTrackers.Count) return;
            if (previousPosition == null || previousPosition.Length != CarCheckpointTrackers.Count) return;

            for (int i = 0; i < CarCheckpointTrackers.Count; i++)
            {
                var tracker = CarCheckpointTrackers[i];
                if (tracker == null) continue;

                var position = tracker.transform.root.position;
                var arc = ArcFromFinishLine(position);

                // crossing the start/finish line makes the arc jump a whole lap; that jump, and only
                // that jump, is what a completed lap looks like from here. the backwards case matters
                // too, for a car driving the wrong way.
                //
                // a car that was moved rather than driven can jump the arc just as far without having
                // crossed anything, so the jump only counts when the car barely moved in the world:
                // a real crossing is one frame of driving, a couple of metres at most. without this,
                // putting a car back on the track can hand it or cost it a whole phantom lap.
                var delta = arc - previousArc[i];
                var wasDriven = Vector3.Distance(position, previousPosition[i]) < lapLength * 0.1f;

                if (wasDriven)
                {
                    if (delta < -lapLength * 0.5f) lapsFromLine[i]++;
                    else if (delta > lapLength * 0.5f) lapsFromLine[i]--;
                }

                previousArc[i] = arc;
                previousPosition[i] = position;

                RacePositionTotalScores[i] = lapsFromLine[i] * (double)lapLength + arc;
            }
        }

        /// <summary>
        /// what place a car is in, counting how many cars have driven further. cars level on distance
        /// share a position, which is the honest answer when nothing separates them.
        /// </summary>
        public int GetPlayerRacePosition(int nPlayer)
        {
            if (nPlayer < 0 || nPlayer >= RacePositionTotalScores.Count) return 1;

            var position = 1;
            for (int i = 0; i < RacePositionTotalScores.Count; i++)
                if (RacePositionTotalScores[nPlayer] < RacePositionTotalScores[i])
                    position++;

            return position;
        }
    }
}
