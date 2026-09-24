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
    /// listens to what the player hears while the car accelerates flat out from the grid, and looks
    /// for gaps: the listener's output is sampled every audio frame and any stretch where the level
    /// drops well below its recent average is reported with the car's gear, revs and speed at that
    /// moment. editor only. started by its flag file (see SmokeTestLauncher), writes
    /// Library/engine_report.txt.
    /// </summary>
    public class EngineSoundTest : MonoBehaviour
    {
        public const string FlagPath = "Library/engine_test.flag";
        public const string ReportPath = "Library/engine_report.txt";

        private const float DriveSeconds = 12f;
        private const float DipRatio = 0.4f;        // level below this share of the recent average is a dip
        private const float MinDipSeconds = 0.03f;  // shorter than this is a waveform trough, not a gap

        private readonly List<string> lines = new List<string>();
        private int failures;
        private GameEvents events;
        private bool raceStarted;
        private readonly float[] buffer = new float[2048];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            File.Delete(FlagPath);
            var go = new GameObject("Engine Sound Test");
            DontDestroyOnLoad(go);
            go.AddComponent<EngineSoundTest>();
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
            Debug.Log("[Engine] " + lines[lines.Count - 1]);
        }

        private void Note(string what)
        {
            lines.Add("     " + what);
            Debug.Log("[Engine] " + what);
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (events == null) { Check(false, "GameEvents asset loaded"); Finish(); yield break; }
            events.RaceStartedEvent.AddListener(() => raceStarted = true);

            var menu = FindFirstObjectByType<MenuGUI>(FindObjectsInactive.Include);
            if (menu != null)
            {
                if (menu.menuUI != null) menu.menuUI.SetActive(false);
                if (menu.raceUI != null) menu.raceUI.SetActive(true);
            }
            events.OnClickPlayRaceEvent.Invoke();
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!raceStarted && Time.realtimeSinceStartup < deadline) yield return null;
            Check(raceStarted, "race started");
            if (!raceStarted) { Finish(); yield break; }

            var player = FindObjectsByType<CarUserControl>(FindObjectsSortMode.None).FirstOrDefault();
            var car = player != null ? player.GetComponent<CarController>() : null;
            var audio = player != null ? player.GetComponentInChildren<CarAudio>(true) : null;
            Check(car != null && audio != null, "player car with CarAudio found");
            if (car == null || audio == null) { Finish(); yield break; }

            // a clear road and silence everything but the engine, so the measurement is the engine
            var parked = 0;
            foreach (var other in FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (other != car) { other.gameObject.SetActive(false); parked++; }
            Note(parked + " bots taken off the track");
            var mutedElsewhere = 0;
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.transform.root != car.transform.root) { s.mute = true; mutedElsewhere++; }
            Note("engine sources: " + audio.GetComponents<AudioSource>().Count(s => s.isPlaying) + " playing (" + audio.engineSoundStyle + "), " + mutedElsewhere + " other sources muted");

            // flat out from the grid, keeping the car straight on the racing line
            var respawn = player.GetComponent<CarRespawn>();
            MobileInputManager.SwitchActiveInputMethod(MobileInputManager.ActiveInputMethod.Touch);
            MobileInputManager.SetAxis("Vertical", 1f);
            MobileInputManager.SetAxis("Horizontal", 0f);

            var samples = new List<(float t, float level, int gear, float revs, float speed, float pitchHigh, float volHigh, float volLow)>();
            var start = Time.time;
            var highSource = audio.GetComponents<AudioSource>().FirstOrDefault(s => s.clip == audio.highAccelClip);
            var lowSource = audio.GetComponents<AudioSource>().FirstOrDefault(s => s.clip == audio.lowAccelClip);
            while (Time.time - start < DriveSeconds)
            {
                yield return null;
                // hold the car on the line so a wall never enters the recording
                Vector3 onLine, tangent;
                if (respawn != null && respawn.TryGetRacingLine(car.transform.position, out onLine, out tangent))
                {
                    tangent.y = 0f; tangent.Normalize();
                    var steer = Vector3.SignedAngle(Vector3.ProjectOnPlane(car.transform.forward, Vector3.up), tangent, Vector3.up);
                    var side = Vector3.Dot(onLine - car.transform.position, Vector3.Cross(Vector3.up, tangent));
                    MobileInputManager.SetAxis("Horizontal", Mathf.Clamp(steer / 25f + side * 0.05f, -1f, 1f));
                }
                AudioListener.GetOutputData(buffer, 0);
                var sum = 0f;
                for (int i = 0; i < buffer.Length; i++) sum += buffer[i] * buffer[i];
                var level = Mathf.Sqrt(sum / buffer.Length);
                samples.Add((Time.time - start, level, GearNumber(car), car.Revs, car.CurrentSpeed,
                             highSource != null ? highSource.pitch : 0f, highSource != null ? highSource.volume : 0f, lowSource != null ? lowSource.volume : 0f));
            }
            MobileInputManager.SetAxis("Vertical", 0f);
            MobileInputManager.SetAxis("Horizontal", 0f);
#if !MOBILE_INPUT
            MobileInputManager.SwitchActiveInputMethod(MobileInputManager.ActiveInputMethod.Hardware);
#endif

            // the analysis: a dip is a run of frames well under the average of the second before it
            var levels = samples.Select(s => s.level).ToList();
            var overall = levels.Count > 0 ? levels.Average() : 0f;
            Check(overall > 0.005f, "engine audible during the run (average level " + overall.ToString("F4") + ")");
            Check(samples.Count > 0 && samples.Last().speed > 40f, "car got up to speed (" + (samples.Count > 0 ? samples.Last().speed.ToString("F0") : "-") + " mph, gear " + (samples.Count > 0 ? samples.Last().gear : -1) + ")");

            var dips = new List<string>();
            var inDip = false; var dipStart = 0f; var dipMin = 0f; var dipInfo = "";
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                var window = samples.Where(w => w.t < s.t && w.t >= s.t - 1f).Select(w => w.level).ToList();
                if (window.Count < 5 || s.t < 0.6f) continue;
                var recent = window.Average();
                var low = s.level < recent * DipRatio;
                if (low && !inDip) { inDip = true; dipStart = s.t; dipMin = s.level; dipInfo = "gear " + s.gear + " revs " + s.revs.ToString("F2") + " speed " + s.speed.ToString("F0") + " mph, high pitch " + s.pitchHigh.ToString("F2") + " vol " + s.volHigh.ToString("F2") + " low vol " + s.volLow.ToString("F2") + " (recent avg " + recent.ToString("F4") + ")"; }
                else if (low && inDip) dipMin = Mathf.Min(dipMin, s.level);
                else if (!low && inDip)
                {
                    inDip = false;
                    var length = s.t - dipStart;
                    if (length >= MinDipSeconds) dips.Add("dip at " + dipStart.ToString("F2") + " s for " + (length * 1000f).ToString("F0") + " ms down to " + dipMin.ToString("F4") + ": " + dipInfo);
                }
            }
            foreach (var d in dips) Note(d);
            Check(dips.Count == 0, dips.Count + " gap(s) in the engine sound over " + DriveSeconds + " s of full-throttle acceleration");

            // the gear changes, for the record
            var shifts = new List<string>();
            for (int i = 1; i < samples.Count; i++)
                if (samples[i].gear != samples[i - 1].gear) shifts.Add(samples[i].t.ToString("F2") + " s -> gear " + samples[i].gear + " revs " + samples[i - 1].revs.ToString("F2") + " -> " + samples[i].revs.ToString("F2"));
            Note("gear changes: " + (shifts.Count > 0 ? string.Join("; ", shifts) : "none"));
            Finish();
        }

        private static int GearNumber(CarController car)
        {
            var field = typeof(CarController).GetField("m_GearNum", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field != null ? (int)field.GetValue(car) : -1;
        }

        private void Finish()
        {
            lines.Add(failures == 0 ? "RESULT PASS" : "RESULT FAIL (" + failures + ")");
            File.WriteAllLines(ReportPath, lines);
            Debug.Log("[Engine] " + lines[lines.Count - 1]);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
