#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// play-mode check that a race ends after exactly the number of laps the player picked. editor
    /// only, compiled out of every build. started by its flag file (see SmokeTestLauncher): it
    /// starts a race with the lap count in the flag, then carries the player car through every
    /// checkpoint in order, lap after lap, noting the lap counter and the moment the race finishes.
    /// </summary>
    public class LapCountTest : MonoBehaviour
    {
        public const string FlagPath = "Library/lap_test.flag";
        public const string ReportPath = "Library/lap_report.txt";

        private readonly List<string> lines = new List<string>();
        private int failures;
        private GameEvents events;
        private bool raceStarted;
        private int lapsCompletedEvents;
        private int finishedAtCrossing = -1;
        private RaceFinishType finishType;
        private int crossings;
        private int lapsWanted = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var text = File.ReadAllText(FlagPath).Trim();
            File.Delete(FlagPath);
            var go = new GameObject("Lap Count Test");
            DontDestroyOnLoad(go);
            var test = go.AddComponent<LapCountTest>();
            int laps;
            if (int.TryParse(text, out laps) && laps > 0) test.lapsWanted = laps;
        }

        private void Awake()
        {
            Application.runInBackground = true;
            events = AssetDatabase.LoadAssetAtPath<GameEvents>(
                "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset");
        }

        private void Start() { StartCoroutine(Run()); }

        private void Check(bool ok, string what)
        {
            lines.Add((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
            Debug.Log("[LapTest] " + lines[lines.Count - 1]);
        }

        private void Note(string what)
        {
            lines.Add("     " + what);
            Debug.Log("[LapTest] " + what);
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (events == null) { Check(false, "GameEvents asset loaded"); Finish(); yield break; }

            events.RaceStartedEvent.AddListener(() => raceStarted = true);
            events.LapCompletedEvent.AddListener(() => lapsCompletedEvents++);
            events.RaceFinishedEvent.AddListener(t => { if (finishedAtCrossing < 0) { finishedAtCrossing = crossings + 1; finishType = t; } });

            // what the lap selector does when the player picks a count
            RaceData.LapsSelected = lapsWanted;
            Note("laps selected " + RaceData.LapsSelected);

            var menu = FindFirstObjectByType<MenuGUI>(FindObjectsInactive.Include);
            if (menu != null)
            {
                if (menu.menuUI != null) menu.menuUI.SetActive(false);
                if (menu.raceUI != null) menu.raceUI.SetActive(true);
            }
            events.OnClickPlayRaceEvent.Invoke();

            var deadline = Time.realtimeSinceStartup + 20f;
            while (!raceStarted && Time.realtimeSinceStartup < deadline) yield return null;
            Check(raceStarted, "race started after the countdown");
            if (!raceStarted) { Finish(); yield break; }

            var player = FindObjectsByType<CarUserControl>(FindObjectsSortMode.None).FirstOrDefault();
            var positions = FindFirstObjectByType<RealTimeRacePositions>();
            var checkpoints = FindFirstObjectByType<Checkpoints>();
            Check(player != null && positions != null && checkpoints != null && checkpoints.checkpoints.Count > 1,
                  "player car, race positions and checkpoints found (" + (checkpoints != null ? checkpoints.checkpoints.Count : 0) + " checkpoints)");
            if (player == null || positions == null || checkpoints == null) { Finish(); yield break; }

            var rb = player.GetComponent<Rigidbody>();
            var tracker = player.GetComponent<CheckpointTracker>();
            Note("player tracker index " + (tracker != null ? tracker.GetCarRacePositionIndex().ToString() : "none")
                 + ", laps before the start line " + positions.LapScores[0]);

            // carry the car through the line and then round the circuit, lap after lap, until the
            // race ends or two laps more than selected have been driven
            var ordered = checkpoints.checkpoints.OrderBy(c => c.GetNumber()).ToList();
            var maxCrossings = 1 + ordered.Count * (lapsWanted + 2);
            var lapsDoneWhenFinished = -1;
            for (int i = 0; i < maxCrossings && finishedAtCrossing < 0; i++)
            {
                var cp = ordered[i % ordered.Count];
                // the first crossing is the real one: the car drives off its grid slot to the line
                if (i == 0) yield return DriveFromGrid(rb, positions);
                else
                {
                    // bots are racing round the same track, so a teleported car can be shoved off
                    // its line before it reaches the box; that is traffic, not the counter, so try again
                    var wanted = cp.GetNumber();
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        yield return Cross(rb, cp);
                        // the tracker goes back to 0 once the last checkpoint of a lap is taken
                        var reached = CurrentCheckpoint(player) == wanted || (wanted == ordered.Count && CurrentCheckpoint(player) == 0);
                        if (reached || finishedAtCrossing >= 0) break;
                        Note("   checkpoint " + wanted + " not reached on attempt " + (attempt + 1) + ", car at " + rb.position.ToString("F1") + ", retrying");
                    }
                }
                crossings++;
                var lapsDone = positions.LapScores[0];
                Note("crossing " + crossings + " checkpoint " + cp.GetNumber() + ": lap counter " + lapsDone
                     + ", lap events " + lapsCompletedEvents + (finishedAtCrossing >= 0 ? ", RACE FINISHED " + finishType : "")
                     + " | " + TrackerState(player) + " car at " + rb.position.ToString("F1"));
                if (finishedAtCrossing >= 0) lapsDoneWhenFinished = lapsDone;
            }

            // the start-line crossing at the flag is crossing 1; each full lap is one crossing per
            // checkpoint; the race should end on the start-line crossing that completes the last lap
            var expectedCrossing = 1 + ordered.Count * lapsWanted;
            var lapsDriven = finishedAtCrossing > 0 ? (finishedAtCrossing - 1) / (float)ordered.Count : -1f;
            Check(finishedAtCrossing > 0, "race finished on its own (" + (finishedAtCrossing > 0 ? "at crossing " + finishedAtCrossing : "never") + ")");
            Check(finishedAtCrossing == expectedCrossing,
                  "race with " + lapsWanted + " lap(s) selected ended after " + lapsDriven.ToString("F1") + " lap(s), expected " + lapsWanted);
            Check(lapsCompletedEvents == lapsWanted + 1 || finishedAtCrossing != expectedCrossing,
                  "lap events: " + lapsCompletedEvents + " (start line plus one per lap)");

            // the score for the run: speed points for the laps over the race clock, plus the lap bonus
            yield return null;
            var manager = FindFirstObjectByType<RaceManager>();
            var client = ThrylClient.Instance;
            var seconds = manager != null ? manager.RaceSeconds : 0f;
            var expectedScore = RaceScore.Compute(finishType, lapsWanted, seconds);
            Check(finishedAtCrossing > 0 && seconds > 1f, "race clock stopped at the finish (" + seconds.ToString("F1") + " s)");
            Check(client != null && client.LastScore == expectedScore && expectedScore > RaceScore.LapBonus * lapsWanted,
                  "score recorded for " + lapsWanted + " lap(s) in " + seconds.ToString("F1") + " s: " + (client != null ? client.LastScore : -1) + " (expected " + expectedScore + ")");
            Finish();
        }

        private static int CurrentCheckpoint(CarUserControl player)
        {
            var tracker = player.GetComponentInChildren<CheckpointTracker>(true);
            if (tracker == null) return -1;
            var field = tracker.GetType().GetField("currentCheckpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (int)field.GetValue(tracker);
        }

        private static string TrackerState(CarUserControl player)
        {
            var tracker = player.GetComponentInChildren<CheckpointTracker>(true);
            if (tracker == null) return "no tracker";
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var current = tracker.GetType().GetField("currentCheckpoint", flags).GetValue(tracker);
            var next = tracker.GetType().GetField("nextCheckpoint", flags).GetValue(tracker);
            var col = tracker.GetComponent<Collider>();
            return "tracker on " + tracker.name + " idx " + tracker.GetCarRacePositionIndex() + " current " + current + " next " + next
                   + " collider " + (col != null ? col.GetType().Name + (col.enabled ? " on" : " OFF") + (col.isTrigger ? " trigger" : "") : "none")
                   + (tracker.gameObject.activeInHierarchy ? "" : " INACTIVE")
                   + " inProgress " + tracker.raceManager.Item.IsRaceInProgress();
        }

        /// <summary>drive straight ahead from the grid until the lap counter moves or 6 s pass</summary>
        private IEnumerator DriveFromGrid(Rigidbody rb, RealTimeRacePositions positions)
        {
            // let the bots in front pull away first, as a player would
            yield return new WaitForSeconds(2.5f);
            var startPos = rb.position;
            Note("grid position " + startPos.ToString("F1") + " facing " + rb.transform.forward.ToString("F2"));
            var before = positions.LapScores[0];
            for (int step = 0; step < 300 && positions.LapScores[0] == before; step++)
            {
                rb.linearVelocity = rb.transform.forward * 20f;
                yield return new WaitForFixedUpdate();
            }
            Note("drove " + Vector3.Distance(startPos, rb.position).ToString("F1") + " m from the grid before the counter moved (" + (positions.LapScores[0] != before ? "it did" : "it did not") + ")");
            rb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// put the car a few metres before a checkpoint and push it through at speed, so the trigger
        /// sees a real entry the same way it does when driven
        /// </summary>
        private IEnumerator Cross(Rigidbody rb, Checkpoint cp)
        {
            var forward = cp.transform.forward;
            var bounds = cp.GetComponent<Collider>().bounds;
            // the thin axis of the box is the direction of travel through it
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.right)) > 0.9f && bounds.size.x > bounds.size.z) forward = Vector3.forward;
            else if (Mathf.Abs(Vector3.Dot(forward, Vector3.forward)) > 0.9f && bounds.size.z > bounds.size.x) forward = Vector3.right;
            var start = bounds.center - forward * 8f;
            // the box stands on the road; keep the car's own height unless it has been knocked well
            // off it, in which case go through the bottom of the box like a car on the tarmac would
            start.y = Mathf.Abs(rb.position.y - bounds.min.y) < 2f ? rb.position.y : bounds.min.y + 0.3f;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = start;
            rb.rotation = Quaternion.LookRotation(forward, Vector3.up);
            yield return new WaitForFixedUpdate();
            for (int step = 0; step < 60; step++)
            {
                rb.linearVelocity = forward * 25f;
                yield return new WaitForFixedUpdate();
                if (Vector3.Dot(rb.position - bounds.center, forward) > 6f) break;
            }
            rb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }

        private void Finish()
        {
            lines.Add(failures == 0 ? "RESULT PASS" : "RESULT FAIL (" + failures + ")");
            File.WriteAllLines(ReportPath, lines);
            Debug.Log("[LapTest] " + lines[lines.Count - 1]);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
