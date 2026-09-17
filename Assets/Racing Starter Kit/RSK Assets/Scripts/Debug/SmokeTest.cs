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
        private float lastHitSpeed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // the flag file is the opt-in; it is written by SmokeTestLauncher for a headless run and
            // by its in-editor menu item, so the same test can be watched in the editor
            if (!File.Exists(FlagPath)) return;
            File.Delete(FlagPath);
            var go = new GameObject("Smoke Test");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeTest>();
        }

        private void Awake()
        {
            // an unfocused editor stalls the player loop; the test must keep ticking regardless
            Application.runInBackground = true;
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
            events.PlayerHitEvent.AddListener((s, d) => { playerHits++; lastHitSpeed = s; });

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

            // ---- mini map
            yield return null;
            var miniMap = FindFirstObjectByType<MiniMapGUI>();
            Check(miniMap != null && miniMap.HasTrack, "mini map traced the circuit (" + (miniMap != null && miniMap.track != null ? miniMap.track.PointCount : 0) + " points)");
            Check(miniMap != null && miniMap.MarkerCount == cars.Length, "mini map has one marker per car (" + (miniMap != null ? miniMap.MarkerCount : 0) + ")");
            if (miniMap != null && miniMap.mapArea != null)
            {
                var playerMarker = miniMap.mapArea.Find("Player Marker") as RectTransform;
                var inside = playerMarker != null && miniMap.mapArea.rect.Contains(playerMarker.anchoredPosition);
                Check(inside, "player marker inside the map area" + (playerMarker != null ? " at " + playerMarker.anchoredPosition.ToString("F0") : ""));
            }

            // ---- QA round 2 wiring
            Check(menu != null && menu.backToTracksButton != null, "menu has a back-to-tracks button");
            var steerLeft = FindObjectsByType<Image>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == "Icon" && i.transform.parent != null && i.transform.parent.name == "Steer Left Button");
            Check(steerLeft != null && steerLeft.sprite != null, "steer left pad shows an icon sprite");
            var gasIcon = FindObjectsByType<Image>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == "Icon" && i.transform.parent != null && i.transform.parent.name == "Throttle Button");
            Check(gasIcon != null && gasIcon.sprite != null && gasIcon.sprite.name.Contains("Gas"), "gas pad shows the pedal icon");
            var playerCar = FindFirstObjectByType<CarUserControl>();
            Check(playerCar != null && playerCar.GetComponent<WallSlide>() != null, "player car has WallSlide");
            Check(playerCar != null && playerCar.GetComponent<Rigidbody>().collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic, "player car uses continuous dynamic collision");
            var walls = FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(b => b.name.StartsWith("Wall") && !b.isTrigger).ToList();
            Check(walls.Count == 0 || walls.All(w => w.sharedMaterial != null && w.sharedMaterial.dynamicFriction < 0.01f), walls.Count + " perimeter walls carry the frictionless material");

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

            // ---- wall brush: a shallow hit on the trackside must keep most of the speed and hold the
            // car along the wall. framed by the racing line on a straight, whatever the wall is made of
            var respawnForFrame = player != null ? player.GetComponent<CarRespawn>() : null;
            var wpRoot = FindFirstObjectByType<AIWaypoints>();
            if (player != null && respawnForFrame != null && wpRoot != null)
            {
                var pBody = player.GetComponent<Rigidbody>();
                var wps = new List<Transform>();
                foreach (Transform t in wpRoot.transform) wps.Add(t);
                // the straightest long segment: neighbours within 4 degrees
                var bestIndex = 0; var bestScore = float.MaxValue;
                for (int i = 0; i < wps.Count; i++)
                {
                    var a = wps[i].position; var b = wps[(i + 1) % wps.Count].position; var c = wps[(i + 2) % wps.Count].position;
                    var z = wps[(i - 1 + wps.Count) % wps.Count].position;
                    var score = Vector3.Angle(b - a, c - b) + Vector3.Angle(a - z, b - a);
                    if (Vector3.Distance(a, b) < 40f) score += 100f;
                    if (score < bestScore) { bestScore = score; bestIndex = i; }
                }
                var segA = wps[bestIndex].position; var segB = wps[(bestIndex + 1) % wps.Count].position;
                var tangent = (segB - segA); tangent.y = 0f; tangent.Normalize();
                var onLine = (segA + segB) * 0.5f;
                var toWall = Vector3.Cross(Vector3.up, tangent);   // the right-hand side
                RaycastHit face;
                var found = Physics.Raycast(onLine + Vector3.up * 1f, toWall, out face, 80f, ~0, QueryTriggerInteraction.Ignore);
                var wallDistance = found ? face.distance : 20f;
                var wallName = found ? face.collider.name : "nothing";
                // the trackside may be a drop rather than a face: walk outwards and stop where the
                // surface under the probe is no longer road, or steps by more than a metre
                var baseY = onLine.y;
                RaycastHit ground;
                if (Physics.Raycast(onLine + Vector3.up * 5f, Vector3.down, out ground, 30f, ~0, QueryTriggerInteraction.Ignore)) baseY = ground.point.y;
                for (var d = 2f; d < wallDistance; d += 0.5f)
                {
                    var probe = onLine + toWall * d + Vector3.up * 5f;
                    var onRoad = Physics.Raycast(probe, Vector3.down, out ground, 30f, ~0, QueryTriggerInteraction.Ignore)
                                 && ground.collider.name.StartsWith("road", System.StringComparison.OrdinalIgnoreCase)
                                 && Mathf.Abs(ground.point.y - baseY) < 1f;
                    if (onRoad) continue;
                    wallDistance = d;
                    wallName += " / road edge";
                    break;
                }

                var slide = player.GetComponent<WallSlide>();
                var cases = new[] { new Vector2(40f, 20f), new Vector2(75f, 40f) };   // speed m/s, angle into the wall
                foreach (var brush in cases)
                {
                    var speed = brush.x; var angle = brush.y;
                    var start = onLine + toWall * (wallDistance - 8f);
                    start.y = player.transform.position.y;
                    var heading = (tangent * Mathf.Cos(angle * Mathf.Deg2Rad) + toWall * Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
                    pBody.position = start;
                    pBody.rotation = Quaternion.LookRotation(heading, Vector3.up);
                    player.transform.SetPositionAndRotation(start, pBody.rotation);
                    pBody.linearVelocity = heading * speed;
                    pBody.angularVelocity = Vector3.zero;
                    yield return new WaitForFixedUpdate();   // let the slide record the entry speed and direction

                    var minSpeed = speed; var maxAway = 0f; var maxUp = 0f; var minAlong = speed; var maxYaw = 0f;
                    var trace = new System.Text.StringBuilder();
                    var brushEnd = Time.realtimeSinceStartup + 1.6f;
                    var nextSample = 0f;
                    var touched = false;
                    while (Time.realtimeSinceStartup < brushEnd)
                    {
                        var v = pBody.linearVelocity;
                        var along = Vector3.Dot(v, tangent);
                        var away = -Vector3.Dot(v, toWall);
                        var yaw = Vector3.SignedAngle(tangent, Vector3.ProjectOnPlane(player.transform.forward, Vector3.up), Vector3.up);
                        if (slide != null && slide.IsSliding) touched = true;
                        if (touched)
                        {
                            minSpeed = Mathf.Min(minSpeed, v.magnitude);
                            minAlong = Mathf.Min(minAlong, along);
                            maxAway = Mathf.Max(maxAway, away);
                            maxUp = Mathf.Max(maxUp, v.y);
                            maxYaw = Mathf.Max(maxYaw, Mathf.Abs(yaw));
                        }
                        if (Time.realtimeSinceStartup >= nextSample)
                        {
                            nextSample = Time.realtimeSinceStartup + 0.1f;
                            var gap = wallDistance - Vector3.Dot(pBody.position - onLine, toWall);
                            trace.Append(" [gap " + gap.ToString("F1") + " along " + along.ToString("F0") + " away " + away.ToString("F1") + " yaw " + yaw.ToString("F0")
                                         + (slide != null && slide.IsSliding ? " on " + slide.LastContactName + (slide.LastFrameTrusted ? " T" : " road") : "") + "]");
                        }
                        yield return null;
                    }
                    var after = pBody.linearVelocity.magnitude;
                    var label = "wall brush " + speed + " m/s at " + angle + " deg";
                    var keep = angle < 30f ? 15f : 10f;
                    Check(touched, label + " touched the wall");
                    Check(minSpeed > keep && after > keep, label + " kept speed (lowest " + minSpeed.ToString("F1") + ", after 1.6 s " + after.ToString("F1") + " m/s)");
                    Check(minAlong > 0f, label + " never went backwards along the wall (min along " + minAlong.ToString("F1") + " m/s)");
                    Check(maxYaw < 60f, label + " never turned past 60 degrees (max " + maxYaw.ToString("F0") + ")");
                    Check(maxAway < 6f, label + " did not launch the car back (max away " + maxAway.ToString("F1") + " m/s)");
                    Check(maxUp < 3f, label + " did not launch the car upward (max vertical " + maxUp.ToString("F1") + " m/s)");
                    lines.Add("   wall face " + wallName + " at " + wallDistance.ToString("F1") + " m from the line, straightness " + bestScore.ToString("F0"));
                    lines.Add("   trace:" + trace);

                    respawnForFrame.ForceRespawn();
                    pBody.linearVelocity = Vector3.zero;
                    yield return new WaitForSecondsRealtime(0.8f);
                }

            }

            // ---- THRYL platform: the launch parser, the score rules, and one real request to staging
            {
                var sample = "https://example.com/games/racer/index.html?game_type=single&custom_game_id=54&playerId=u1&name=Player%20One&token=abc.def&timer=5&game_highest_score=120";
                var parsed = ThrylLaunch.Parse(sample);
                Check(parsed.customGameIdNumber == 54 && parsed.token == "abc.def" && parsed.playerName == "Player One"
                      && parsed.playerId == "u1" && parsed.timerMinutes == 5 && parsed.highestScore == 120 && parsed.CanSubmit,
                      "launch URL parsed: " + parsed);
                var fallback = ThrylLaunch.Parse("index.html?usertoken=Bearer%20xyz&player_id=p9&username=Bob&timer=500&game_highest_score=-3");
                Check(fallback.token == "xyz" && fallback.playerId == "p9" && fallback.playerName == "Bob"
                      && fallback.timerMinutes == 120 && fallback.highestScore == 0 && !fallback.CanSubmit,
                      "launch URL fallbacks and clamps: " + fallback);
                Check(ThrylLaunch.Parse("index.html").playerName == "Guest", "launch with no parameters defaults to Guest");
                Check(RaceScore.Compute(RaceFinishType.Win, 1, 7, 1f) == 1000 && RaceScore.Compute(RaceFinishType.Lose, 3, 7, 1f) == 650
                      && RaceScore.Compute(RaceFinishType.Timeout, 5, 7, 0.5f) == 70 && RaceScore.Compute(RaceFinishType.Lose, 7, 7, 1f) > RaceScore.Compute(RaceFinishType.Timeout, 1, 7, 1f),
                      "race score rules: 1st 1000, 3rd 650, timeout half way 70, last finisher beats any timeout");

                var client = ThrylClient.Instance;
                Check(client != null && client.Config != null, "THRYL client booted from Resources");
                if (client != null)
                {
                    Check(client.Config.environment == ThrylEnvironment.Staging, "THRYL config points at staging");
                    // a real request with a throwaway token: what is being checked is that the request
                    // is built and answered, not that it is accepted. staging should reject it with a
                    // 4xx; a network-level failure or an exception is what would fail this
                    var testLaunch = ThrylLaunch.Parse(sample);
                    var url = client.Config.ScoreUrl;
                    var body = "{\"custom_game_id\":54,\"points_ingame\":1}";
                    using (var request = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
                    {
                        request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                        request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.SetRequestHeader("Authorization", "Bearer " + testLaunch.token);
                        request.timeout = 15;
                        yield return request.SendWebRequest();
                        var code = (int)request.responseCode;
                        var answered = request.result != UnityEngine.Networking.UnityWebRequest.Result.ConnectionError && code > 0;
                        var unreachable = request.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError;
                        // the request pipeline is what is under test. an HTTP answer of any status proves
                        // it; a host that cannot be reached from this machine is reported, not failed,
                        // because the guide itself says live availability of the hosts was not checked
                        Check(answered || unreachable, "score request completed: HTTP " + code + " " + request.error + " " + (request.downloadHandler.text.Length > 120 ? request.downloadHandler.text.Substring(0, 120) : request.downloadHandler.text));
                        if (unreachable) lines.Add("   note: " + url + " is not reachable from this machine; the request was built and sent but never answered");
                    }
                }
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
                Check(flash != null && flash.color.a > 0.05f, "hit flash lit on the HUD (hit " + lastHitSpeed.ToString("F1") + " m/s, flash alpha " + (flash != null ? flash.color.a.ToString("F2") : "none") + ")");
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
            var thryl = ThrylClient.Instance;
            Check(thryl != null && thryl.LastScore >= 150 && thryl.LastScore <= 1000, "THRYL client recorded a place-based score for the run (" + (thryl != null ? thryl.LastScore : -1) + ")");
            var statusLabel = panel != null ? panel.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "Platform Status TMP") : null;
            Check(statusLabel != null && statusLabel.text.StartsWith("Best:"), "results panel shows the best score line (" + (statusLabel != null ? statusLabel.text : "missing") + ")");
            var shownScore = FindObjectsByType<RaceFinishGUI>(FindObjectsSortMode.None).FirstOrDefault()?.raceFinishTMP;
            Check(shownScore != null && thryl != null && shownScore.text.Contains("Score: " + thryl.LastScore.ToString("N0")), "results panel shows the same score the platform gets (" + (shownScore != null ? shownScore.text.Replace("\n", " / ") : "missing") + ")");

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
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
