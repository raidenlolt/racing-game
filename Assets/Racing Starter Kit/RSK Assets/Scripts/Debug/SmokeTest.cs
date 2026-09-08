#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// the runtime half of the headless smoke test (see SmokeTestLauncher). editor-only, compiled
    /// out of every build. runs a race on the open track and checks the five client items:
    /// nobody launches off the grid, the bots wait for GO, nitro lights the exhausts and the button,
    /// a staged rear-end hit registers on the player, and the finish plays before the results.
    /// </summary>
    public class SmokeTest : MonoBehaviour
    {
        private const string FlagPath = "Library/smoke_test.flag";
        private const string ReportPath = "Library/smoke_report.txt";

        private readonly List<string> lines = new List<string>();
        private int failures;
        private int errorsLogged;
        private readonly List<string> errorMessages = new List<string>();
        private GameEvents events;
        private bool raceStarted;
        private bool resultsReady;
        private int playerHits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Application.isBatchMode || !File.Exists(FlagPath)) return;
            File.Delete(FlagPath);
            var go = new GameObject("Smoke Test");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeTest>();
        }

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            events = AssetDatabase.LoadAssetAtPath<GameEvents>(
                "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset");
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                errorsLogged++;
                if (errorMessages.Count < 12) errorMessages.Add(type + ": " + message.Split('\n')[0]);
            }
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private void Check(bool ok, string what)
        {
            lines.Add((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
            Debug.Log("[Smoke] " + lines[lines.Count - 1]);
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (events == null)
            {
                Check(false, "GameEvents asset loaded");
                Finish();
                yield break;
            }

            events.RaceStartedEvent.AddListener(() => raceStarted = true);
            events.RaceResultsReadyEvent.AddListener(_ => resultsReady = true);
            events.PlayerHitEvent.AddListener((s, d) => playerHits++);

            // ---- spawn. the menu's Play handler swaps the menu for the race HUD before it raises
            // the event, so do the same or the HUD objects never become active
            var menu = FindFirstObjectByType<MenuGUI>(FindObjectsInactive.Include);
            if (menu != null)
            {
                if (menu.menuUI != null) menu.menuUI.SetActive(false);
                if (menu.raceUI != null) menu.raceUI.SetActive(true);
            }
            events.OnClickPlayRaceEvent.Invoke();
            yield return null;
            yield return new WaitForFixedUpdate();

            var cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            Check(cars.Length == RaceData.AiBotsSelected + 1, "spawned " + cars.Length + " cars, expected " + (RaceData.AiBotsSelected + 1));

            var spawnY = cars.ToDictionary(c => c, c => c.transform.position.y);
            var maxRise = 0f;
            var maxVerticalSpeed = 0f;
            var maxBotSpeedBeforeGo = 0f;
            var minPairDistance = float.MaxValue;
            for (int a = 0; a < cars.Length; a++)
                for (int b = a + 1; b < cars.Length; b++)
                    minPairDistance = Mathf.Min(minPairDistance,
                        Vector3.Distance(cars[a].transform.position, cars[b].transform.position));
            Check(minPairDistance > 4f, "closest pair of cars on the grid is " + minPairDistance.ToString("F1") + " m apart");

            // ---- countdown: watch for launches and for bots creeping
            var watchUntil = Time.realtimeSinceStartup + 6f;
            var player = FindFirstObjectByType<CarUserControl>();
            while (Time.realtimeSinceStartup < watchUntil)
            {
                foreach (var c in cars)
                {
                    if (c == null) continue;
                    var rb = c.GetComponent<Rigidbody>();
                    maxRise = Mathf.Max(maxRise, c.transform.position.y - spawnY[c]);
                    maxVerticalSpeed = Mathf.Max(maxVerticalSpeed, Mathf.Abs(rb.linearVelocity.y));
                    if (!raceStarted && c.GetComponent<CarAIControl>() != null)
                    {
                        // horizontal only: the short drop onto the road at spawn is not creeping
                        var flat = rb.linearVelocity; flat.y = 0f;
                        maxBotSpeedBeforeGo = Mathf.Max(maxBotSpeedBeforeGo, flat.magnitude);
                    }
                }
                yield return null;
            }
            Check(raceStarted, "race started after the countdown");
            Check(maxRise < 1.0f, "no car rose more than 1 m off its spawn (max " + maxRise.ToString("F2") + " m)");
            Check(maxVerticalSpeed < 4f, "no car had vertical speed over 4 m/s (max " + maxVerticalSpeed.ToString("F2") + ")");
            Check(maxBotSpeedBeforeGo < 1.5f, "bots held on the grid before GO (max " + maxBotSpeedBeforeGo.ToString("F2") + " m/s)");

            var botsMoving = cars.Count(c => c != null && c.GetComponent<CarAIControl>() != null
                                             && c.GetComponent<Rigidbody>().linearVelocity.magnitude > 3f);
            Check(botsMoving >= 3, botsMoving + " bots moving a couple of seconds after GO");

            // ---- nitro
            Check(player != null, "player car present");
            var nitro = player != null ? player.GetComponent<NitroSystem>() : null;
            if (nitro != null)
            {
                nitro.AddCharge(0.5f);
                events.OnClickFireNitroEvent.Invoke();
                yield return null;
                Check(nitro.IsActive, "nitro engaged on tap (level " + nitro.Level + ")");
                var flame = player.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(p => p.name == "Nitro Flame");
                Check(flame != null && flame.emission.rateOverTime.constant > 0f, "exhaust flame emitting while boosting");
                var speedLines = FindObjectsByType<Image>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == "Speed Lines");
                yield return new WaitForSecondsRealtime(0.6f);
                Check(speedLines != null && speedLines.color.a > 0.05f, "speed lines overlay visible while boosting");
                var buttonFx = FindFirstObjectByType<NitroButtonFX>();
                Check(buttonFx != null && buttonFx.buttonImage != null && buttonFx.buttonImage.sprite != null
                      && buttonFx.buttonImage.sprite.name.Contains("Boost"),
                      "fire button shows the new boost icon");
                Check(buttonFx != null && buttonFx.glowRing != null && buttonFx.glowRing.color.a > 0.1f, "glow ring lit while boosting");
                yield return new WaitForSecondsRealtime(1.5f);
            }

            // ---- staged rear-end hit
            var bot = cars.FirstOrDefault(c => c != null && c.GetComponent<CarAIControl>() != null);
            if (player != null && bot != null)
            {
                var pBody = player.GetComponent<Rigidbody>();
                var bBody = bot.GetComponent<Rigidbody>();
                var ai = bot.GetComponent<CarAIControl>();
                ai.enabled = false;
                bBody.position = pBody.position - player.transform.forward * 7f + Vector3.up * 0.2f;
                bBody.rotation = player.transform.rotation;
                bBody.linearVelocity = pBody.linearVelocity + player.transform.forward * 14f;
                var before = playerHits;
                var deadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < deadline && playerHits == before) yield return null;
                Check(playerHits > before, "player registered a rear-end hit (" + (playerHits - before) + ")");
                var flash = FindObjectsByType<Image>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == "Hit Flash");
                Check(flash != null && flash.color.a > 0.05f, "hit flash lit on the HUD");
                Check(bot.GetComponent<CarImpactFX>() != null && bot.GetComponent<CarImpactFX>().HitCount > 0, "the bot registered the hit too");
                ai.enabled = true;
            }

            // ---- finish
            var t0 = Time.realtimeSinceStartup;
            events.RaceFinishedEvent.Invoke(RaceFinishType.Win);
            yield return null;
            Check(Time.timeScale < 0.9f, "slow motion engaged at the line (timeScale " + Time.timeScale.ToString("F2") + ")");
            Check(CarUserControl.InputLocked, "player input locked at the line");
            var banner = GameObject.Find("Finish Banner");
            Check(banner != null && banner.activeInHierarchy, "finish banner shown");
            var panel = FindObjectsByType<RaceFinishGUI>(FindObjectsSortMode.None).FirstOrDefault()?.raceFinishPanel;
            Check(panel != null && !panel.activeInHierarchy, "results panel NOT shown at the line");
            yield return new WaitForSecondsRealtime(1.5f);
            var finishCam = FindFirstObjectByType<RaceFinishCamera>();
            var camDistance = finishCam != null && player != null
                ? Vector3.Distance(finishCam.transform.position, player.transform.position) : -1f;
            if (finishCam != null && player != null)
                Debug.Log("[Smoke] finish camera diag: cam " + finishCam.transform.position.ToString("F1")
                          + " active=" + finishCam.isActiveAndEnabled + " orbiting=" + finishCam.IsOrbiting
                          + " target=" + (finishCam.Target != null ? finishCam.Target.name : "null")
                          + " player " + player.transform.position.ToString("F1")
                          + " speed " + player.GetComponent<Rigidbody>().linearVelocity.magnitude.ToString("F1"));
            Check(camDistance > 3f && camDistance < 15f, "finish camera orbiting the player (" + camDistance.ToString("F1") + " m away)");
            while (!resultsReady && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            var elapsed = Time.realtimeSinceStartup - t0;
            Check(resultsReady, "results ready after " + elapsed.ToString("F1") + " s");
            Check(elapsed > 2.5f, "results held back at least 2.5 s");
            Check(Mathf.Approximately(Time.timeScale, 1f), "time scale restored (" + Time.timeScale.ToString("F2") + ")");
            yield return new WaitForSecondsRealtime(1.2f);
            Check(panel != null && panel.activeInHierarchy, "results panel shown after the sequence");

            // ---- restart
            events.OnClickRestartRaceEvent.Invoke();
            yield return null;
            yield return new WaitForFixedUpdate();
            Check(!CarUserControl.InputLocked, "input unlocked on restart");
            Check(Mathf.Approximately(Time.timeScale, 1f), "time scale 1 on restart");
            Check(banner != null && !banner.activeInHierarchy, "banner hidden on restart");
            var rise2 = 0f;
            var end = Time.realtimeSinceStartup + 2f;
            var restartY = cars.Where(c => c != null).ToDictionary(c => c, c => c.transform.position.y);
            while (Time.realtimeSinceStartup < end)
            {
                foreach (var c in cars) if (c != null) rise2 = Mathf.Max(rise2, c.transform.position.y - restartY[c]);
                yield return null;
            }
            Check(rise2 < 1.0f, "no launch after restart (max rise " + rise2.ToString("F2") + " m)");

            Finish();
        }

        private void Finish()
        {
            Check(errorsLogged == 0, errorsLogged + " errors or exceptions logged during the run");
            foreach (var e in errorMessages) lines.Add("   " + e);
            lines.Add(failures == 0 ? "RESULT PASS" : "RESULT FAIL (" + failures + ")");
            File.WriteAllLines(ReportPath, lines);
            Debug.Log("[Smoke] " + lines[lines.Count - 1]);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
#endif
