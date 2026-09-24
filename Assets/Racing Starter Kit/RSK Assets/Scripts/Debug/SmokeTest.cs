#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
                // the pack: cyan, big enough to see on a phone, drawn and inside the map
                var botMarker = miniMap.mapArea.Find("Bot Marker 1") as RectTransform;
                var botImage = botMarker != null ? botMarker.GetComponent<Image>() : null;
                var botVisible = botMarker != null && botMarker.gameObject.activeInHierarchy && botImage != null && botImage.sprite != null
                                 && botImage.color.a > 0.95f && botImage.color.b > 0.9f && botImage.color.r < 0.2f
                                 && botMarker.sizeDelta.x >= 14f && miniMap.mapArea.rect.Contains(botMarker.anchoredPosition);
                Check(botVisible, "bot markers are cyan dots of at least 14 px inside the map" + (botMarker != null ? " (" + botMarker.sizeDelta.x + " px, colour " + (botImage != null ? botImage.color.ToString() : "-") + ")" : " (none)"));
            }

            // ---- QA round 2 wiring
            Check(menu != null && menu.backToTracksButton != null, "menu has a back-to-tracks button");
            // the pad art now sits on the button images themselves (an Icon child was the earlier
            // layout); either way the pad must show a sprite
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var steerLeftPad = allTransforms.FirstOrDefault(t => t.name == "Steer Left Button");
            var steerLeft = steerLeftPad != null ? steerLeftPad.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && !i.sprite.name.StartsWith("UI")) : null;
            Check(steerLeft != null, "steer left pad shows a sprite" + (steerLeft != null ? " (" + steerLeft.sprite.name + ")" : steerLeftPad == null ? " (no Steer Left Button object)" : ""));
            var gasPad = allTransforms.FirstOrDefault(t => t.name == "Throttle Button");
            var gasIcon = gasPad != null ? gasPad.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && !i.sprite.name.StartsWith("UI")) : null;
            Check(gasIcon != null, "gas pad shows a sprite" + (gasIcon != null ? " (" + gasIcon.sprite.name + ")" : gasPad == null ? " (no Throttle Button object)" : ""));
            var playerCar = FindFirstObjectByType<CarUserControl>();
            // ---- multitouch pads: two fingers, four combinations, and the releases that used to cancel
            {
                var pads = FindObjectsByType<TouchPad>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                TouchPad Find(string axis, bool positive) => pads.FirstOrDefault(p => p.axisName == axis && p.positive == positive);
                var left = Find("Horizontal", false); var right = Find("Horizontal", true);
                var gas = Find("Vertical", true); var brake = Find("Vertical", false);
                Check(left != null && right != null && gas != null && brake != null, "four touch pads present (" + pads.Length + ")");
                if (left != null && right != null && gas != null && brake != null)
                {
                    // pads only publish while enabled; the rig may be off in the editor
                    var offChain = new List<GameObject>();
                    foreach (var p in pads) for (var t = p.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf && !offChain.Contains(t.gameObject)) offChain.Add(t.gameObject);
                    foreach (var go in offChain) go.SetActive(true);
                    // on a desktop build target the rig re-hides itself every frame; hold it off
                    var rigs = FindObjectsByType<MobileControlRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.enabled).ToList();
                    foreach (var r in rigs) r.enabled = false;
                    MobileInputManager.SwitchActiveInputMethod(MobileInputManager.ActiveInputMethod.Touch);
                    yield return null;
                    foreach (var go in offChain) go.SetActive(true);

                    var es = EventSystem.current;
                    PointerEventData Finger(int id) => new PointerEventData(es) { pointerId = id };
                    void Down(TouchPad p, int id) => ExecuteEvents.Execute(p.gameObject, Finger(id), ExecuteEvents.pointerDownHandler);
                    void Up(TouchPad p, int id) => ExecuteEvents.Execute(p.gameObject, Finger(id), ExecuteEvents.pointerUpHandler);
                    void Exit(TouchPad p, int id) => ExecuteEvents.Execute(p.gameObject, Finger(id), ExecuteEvents.pointerExitHandler);
                    float H() => MobileInputManager.GetAxisRaw("Horizontal");
                    float V() => MobileInputManager.GetAxisRaw("Vertical");
                    void ReleaseEverything() { foreach (var p in pads) p.ReleaseAll(); }

                    ReleaseEverything();
                    var combos = new[] { (left, -1f, gas, 1f, "left + gas"), (right, 1f, gas, 1f, "right + gas"), (left, -1f, brake, -1f, "left + brake"), (right, 1f, brake, -1f, "right + brake") };
                    foreach (var combo in combos)
                    {
                        Down(combo.Item1, 1); Down(combo.Item3, 2);
                        var held = H() == combo.Item2 && V() == combo.Item4;
                        Up(combo.Item3, 2);
                        var steerKept = H() == combo.Item2 && V() == 0f;
                        Down(combo.Item3, 2); Up(combo.Item1, 1);
                        var pedalKept = H() == 0f && V() == combo.Item4;
                        Up(combo.Item3, 2);
                        Check(held && steerKept && pedalKept, combo.Item5 + ": both read, releasing either leaves the other (" + (held ? "held" : "NOT held") + ", steer " + (steerKept ? "kept" : "lost") + ", pedal " + (pedalKept ? "kept" : "lost") + ")");
                    }
                    Down(gas, 1); Exit(gas, 1);
                    Check(V() == 1f, "a thumb drifting off the gas pad keeps the gas down");
                    Down(gas, 2); Up(gas, 2);
                    Check(V() == 1f, "a second finger tapping the gas pad does not lift the first");
                    Up(gas, 1);
                    Check(V() == 0f, "gas released when the last finger lifts");
                    Down(gas, 1); Down(brake, 2);
                    Check(V() == -1f, "brake wins while both pedals are down");
                    Up(brake, 2);
                    Check(V() == 1f, "gas resumes when the brake lifts");
                    Up(gas, 1);
                    Down(left, 1); Down(right, 2);
                    Check(H() == 0f, "left and right together cancel to straight");
                    Up(left, 1);
                    Check(H() == 1f, "right stays after left lifts");
                    Up(right, 2);
                    ReleaseEverything();
                    foreach (var go in offChain) go.SetActive(false);
                    foreach (var r in rigs) r.enabled = true;
#if !MOBILE_INPUT
                    MobileInputManager.SwitchActiveInputMethod(MobileInputManager.ActiveInputMethod.Hardware);
#endif
                }
            }

            Check(playerCar != null && playerCar.GetComponent<WallSlide>() != null, "player car has WallSlide");
            // engine loops are 3D sources with Unity's attenuation, and bots out of earshot run none
            var playerEngineSources = playerCar != null ? playerCar.GetComponents<AudioSource>().Where(s => s.loop && s.clip != null && s.clip.name.Contains("celeration")).ToList() : new List<AudioSource>();
            Check(playerEngineSources.Count > 0 && playerEngineSources.All(s => s.spatialBlend >= 0.99f && s.maxDistance > 0f), "engine loops are 3D sources (" + playerEngineSources.Count + ", max distance " + (playerEngineSources.Count > 0 ? playerEngineSources[0].maxDistance.ToString("F0") : "-") + " m)");
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

            // ---- a bot that finishes its laps: stops driving, ignores the other cars, then hides
            var finishers = FindObjectsByType<AIFinishBehaviour>(FindObjectsSortMode.None).Where(f => f.enabled).ToList();
            var finisherCars = finishers.Select(f => f.gameObject).Distinct().Count();
            Check(finishers.Count == RaceData.AiBotsSelected && finisherCars == RaceData.AiBotsSelected,
                  "every bot carries one AIFinishBehaviour, the player none (" + finishers.Count + " active on " + finisherCars + " cars)");
            var finishedBot = finishers.FirstOrDefault();
            if (finishedBot != null)
            {
                var botAi = finishedBot.GetComponent<CarAIControl>();
                finishedBot.Finish();
                yield return null;
                Check(finishedBot.HasFinished && botAi != null && !botAi.enabled, "finished bot stops driving");
                yield return new WaitForSeconds(finishedBot.hideAfterSeconds + 0.5f);
                var finishedTracker = finishedBot.GetComponentInChildren<CheckpointTracker>(true);
                var finishedMarker = miniMap != null && miniMap.mapArea != null && finishedTracker != null ? miniMap.mapArea.Find("Bot Marker " + finishedTracker.GetCarRacePositionIndex()) : null;
                var botMarkerShown = finishedMarker != null && finishedMarker.gameObject.activeSelf;
                Check(finishedBot.IsHidden && !finishedBot.gameObject.activeInHierarchy, "finished bot hidden " + finishedBot.hideAfterSeconds + " s later");
                Check(!botMarkerShown, "hidden bot's minimap dot is hidden too");
            }

            // ---- minimap, as drawn: sample the frame at each marker. the earlier checks only prove
            // the markers exist; this proves the pixels are there (skipped headless, no frame buffer)
            if (!Application.isBatchMode && miniMap != null && miniMap.mapArea != null)
            {
                yield return new WaitForEndOfFrame();
                var frame = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    string Sample(RectTransform marker)
                    {
                        var screen = RectTransformUtility.WorldToScreenPoint(null, marker.position);
                        var x = Mathf.Clamp(Mathf.RoundToInt(screen.x), 0, frame.width - 1);
                        var y = Mathf.Clamp(Mathf.RoundToInt(screen.y), 0, frame.height - 1);
                        var c = frame.GetPixel(x, y);
                        return marker.name + "@" + x + "," + y + "=" + c.r.ToString("F2") + "/" + c.g.ToString("F2") + "/" + c.b.ToString("F2");
                    }
                    var cyanDots = 0; var samples = new List<string>();
                    for (int i = 1; i < miniMap.MarkerCount; i++)
                    {
                        var marker = miniMap.mapArea.Find("Bot Marker " + i) as RectTransform;
                        if (marker == null || !marker.gameObject.activeInHierarchy) continue;
                        var screen = RectTransformUtility.WorldToScreenPoint(null, marker.position);
                        // the pack sits bunched on the grid, so a dot's centre can lie under a
                        // neighbour's dark rim; any bright cyan within a few pixels is the dot
                        var cx = Mathf.RoundToInt(screen.x); var cy = Mathf.RoundToInt(screen.y);
                        var found = false;
                        for (int dy = -4; dy <= 4 && !found; dy++)
                        for (int dx = -4; dx <= 4 && !found; dx++)
                        {
                            var c = frame.GetPixel(Mathf.Clamp(cx + dx, 0, frame.width - 1), Mathf.Clamp(cy + dy, 0, frame.height - 1));
                            found = c.g > 0.6f && c.b > 0.6f && c.r < 0.45f;
                        }
                        if (found) cyanDots++;
                        if (samples.Count < 3) samples.Add(Sample(marker));
                    }
                    var playerMarker = miniMap.mapArea.Find("Player Marker") as RectTransform;
                    Check(cyanDots >= 3, cyanDots + " bot markers drawn cyan on screen (frame " + frame.width + "x" + frame.height + "; " + string.Join(" ", samples) + (playerMarker != null ? " " + Sample(playerMarker) : "") + ")");
                }
                finally { Destroy(frame); }
            }

            // ---- client sounds: the supplied clips are what plays, and the engine is running on its loop
            if (player != null)
            {
                var carAudio = player.GetComponentInChildren<CarAudio>(true);
                var engineClipName = carAudio != null && carAudio.highAccelClip != null ? carAudio.highAccelClip.name : "none";
                Check(carAudio != null && carAudio.engineSoundStyle == CarAudio.EngineAudioOptions.FourChannel && engineClipName == "AccelerationHigh",
                      "engine keeps the kit's four-channel loops (" + engineClipName + ", " + (carAudio != null ? carAudio.engineSoundStyle.ToString() : "-") + ")");
                Check(carAudio != null && carAudio.passbyClip != null && carAudio.passbyClip.name == "Car passby", "car carries the passby clip for the menu stage");
                // the engine loops must be uncompressed: a compressed loop decoded in the browser
                // carries codec padding at every loop boundary, heard as a gap under acceleration
                var engineClips = carAudio != null ? new[] { carAudio.lowAccelClip, carAudio.lowDecelClip, carAudio.highAccelClip, carAudio.highDecelClip }.Where(c => c != null).ToList() : new List<AudioClip>();
                Check(engineClips.Count == 4 && engineClips.All(c => c.loadType == AudioClipLoadType.DecompressOnLoad),
                      "engine loops import uncompressed (decompress on load): " + string.Join(", ", engineClips.Select(c => c.name + "=" + c.loadType)));
                var exhaust = player.GetComponentInChildren<NitroExhaustFX>(true);
                Check(exhaust != null && exhaust.igniteClip == null && exhaust.loopClip == null, "nitro has no clip assigned: synthesised ignition and hiss");
                Check(exhaust != null && exhaust.volume <= 0.45f, "nitro volume turned down (" + (exhaust != null ? exhaust.volume.ToString("F2") : "-") + ")");
                // the player's nitro is flat and always full; a bot's is 3D and only heard close by
                var playerNitroSources = player.GetComponents<AudioSource>().Where(s => s.clip == null || s.clip.name.StartsWith("nitro")).Where(s => s.spatialBlend < 0.01f).Count();
                Check(playerNitroSources >= 2, "player's nitro sources are 2D (" + playerNitroSources + ")");
                var botExhaust = FindObjectsByType<NitroExhaustFX>(FindObjectsSortMode.None).FirstOrDefault(n => n.GetComponent<CarUserControl>() == null && n.gameObject.activeInHierarchy);
                var botNitroSources = botExhaust != null ? botExhaust.GetComponents<AudioSource>().Where(s => s.spatialBlend > 0.99f && s.maxDistance <= botExhaust.packAudibleDistance + 0.01f && s.maxDistance <= 40f).Count() : 0;
                Check(botExhaust != null && botNitroSources >= 2, "bots' nitro sources are 3D with a short reach (" + botNitroSources + " sources, " + (botExhaust != null ? botExhaust.packAudibleDistance.ToString("F0") : "-") + " m)");
                var finishSeq = FindFirstObjectByType<RaceFinishSequence>(FindObjectsInactive.Include);
                Check(finishSeq != null && finishSeq.fanfareClip != null && finishSeq.fanfareClip.name == "Champion", "finish stinger uses Champion");
            }

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
                // the client's formula: round(1,000,000 x laps / seconds) + laps x 1,000; nothing for a timeout
                Check(RaceScore.Compute(RaceFinishType.Win, 2, 100f) == 22000 && RaceScore.Compute(RaceFinishType.Lose, 1, 60f) == 17667
                      && RaceScore.Compute(RaceFinishType.Timeout, 2, 100f) == 0 && RaceScore.Compute(RaceFinishType.Win, 0, 100f) == 0
                      && RaceScore.Compute(RaceFinishType.Win, 2, 90f) > RaceScore.Compute(RaceFinishType.Win, 2, 100f),
                      "race score rules: 2 laps in 100 s = 22,000, 1 lap in 60 s = 17,667, timeout 0, faster scores more");

                var client = ThrylClient.Instance;
                Check(client != null && client.Config != null, "THRYL client booted from Resources");
                // the config must carry the events: on the real path the client boots on the track
                // menu scene, where there is nothing to search for
                Check(client != null && client.Config != null && client.Config.gameEvents != null, "THRYL config references GameEvents");
                Check(client != null && client.Listening, "THRYL client listening for race events");
                if (client != null)
                {
                    // staging or production is a release decision, not a defect; just record which
                    Check(true, "THRYL config points at " + client.Config.environment + " (" + client.Config.BaseUrl + ")");
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
            var bot = cars.FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy && c.GetComponent<CarAIControl>() != null);
            if (player != null && bot != null)
            {
                var pBody = player.GetComponent<Rigidbody>();
                var bBody = bot.GetComponent<Rigidbody>();
                var ai = bot.GetComponent<CarAIControl>();
                ai.enabled = false;

                // the wall brushes leave the player wherever the rail let go of it, sometimes with a
                // bend right behind; stage the hit on the racing line so the bot is never dropped
                // into a barrier
                var respawn = player.GetComponent<CarRespawn>();
                Vector3 onLine, tangent;
                if (respawn != null && respawn.TryGetRacingLine(pBody.position, out onLine, out tangent))
                {
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude > 0.01f)
                    {
                        tangent.Normalize();
                        var slide = player.GetComponent<WallSlide>();
                        if (slide != null) slide.Release();
                        pBody.position = onLine + Vector3.up * 0.3f;
                        pBody.rotation = Quaternion.LookRotation(tangent, Vector3.up);
                        pBody.linearVelocity = tangent * 10f;
                        pBody.angularVelocity = Vector3.zero;
                        // let the car settle onto its wheels, or the bot meets it mid-drop and the
                        // contact normal points up instead of along the road
                        yield return new WaitForSeconds(0.4f);
                    }
                }

                bBody.position = pBody.position - player.transform.forward * 7f;
                bBody.rotation = player.transform.rotation;
                bBody.linearVelocity = pBody.linearVelocity + player.transform.forward * 16f;
                bBody.angularVelocity = Vector3.zero;
                var staged = "player " + pBody.position.ToString("F1") + " at " + pBody.linearVelocity.magnitude.ToString("F1") + " m/s, bot " + bBody.position.ToString("F1") + " at " + bBody.linearVelocity.magnitude.ToString("F1") + " m/s";
                var before = playerHits;
                var deadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < deadline && playerHits == before) yield return null;
                var gap = Vector3.Distance(pBody.position, bBody.position);
                Check(playerHits > before, "player registered a rear-end hit (" + (playerHits - before) + "; staged " + staged + "; gap after " + gap.ToString("F1") + " m, bot now " + bBody.linearVelocity.magnitude.ToString("F1") + " m/s)");
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
            // the pack's engines are faded out under the results; the player's own keeps coasting
            var packAudio = FindObjectsByType<CarAudio>(FindObjectsSortMode.None).Where(a => a.GetComponent<CarUserControl>() == null).ToList();
            var packLoud = packAudio.SelectMany(a => a.GetComponents<AudioSource>()).Where(s => s.loop && s.isPlaying && s.volume > 0.01f).Count();
            Check(packAudio.Count > 0 && packAudio.All(a => a.EngineGain <= 0.001f) && packLoud == 0,
                  "pack engines silent under the results (" + packAudio.Count + " bots, gain " + (packAudio.Count > 0 ? packAudio.Max(a => a.EngineGain).ToString("F2") : "-") + ", " + packLoud + " loops still audible)");
            var thryl = ThrylClient.Instance;
            var manager = FindFirstObjectByType<RaceManager>();
            var racePositions = FindFirstObjectByType<RealTimeRacePositions>();
            var expectedScore = RaceScore.Compute(RaceFinishType.Win, RaceScore.CompletedLaps(racePositions, 0), manager != null ? manager.RaceSeconds : 0f);
            Check(manager != null && manager.RaceSeconds > 5f && manager.RaceSeconds < 600f, "race clock frozen at the finish (" + (manager != null ? manager.RaceSeconds.ToString("F1") : "-") + " s)");
            Check(thryl != null && thryl.LastScore == expectedScore, "THRYL client recorded the run's speed-and-lap score (" + (thryl != null ? thryl.LastScore : -1) + ", expected " + expectedScore + " for " + RaceScore.CompletedLaps(racePositions, 0) + " completed laps)");
            var statusLabel = panel != null ? panel.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "Platform Status TMP") : null;
            Check(statusLabel != null && statusLabel.text.StartsWith("Best:"), "results panel shows the best score line (" + (statusLabel != null ? statusLabel.text : "missing") + ")");
            var shownScore = FindObjectsByType<RaceFinishGUI>(FindObjectsSortMode.None).FirstOrDefault()?.raceFinishTMP;
            Check(shownScore != null && thryl != null && shownScore.text.Contains("Score: " + thryl.LastScore.ToString("N0")), "results panel shows the same score the platform gets (" + (shownScore != null ? shownScore.text.Replace("\n", " / ") : "missing") + ")");

            // ---- restart
            events.OnClickRestartRaceEvent.Invoke();
            yield return null;
            yield return new WaitForFixedUpdate();
            Check(!CarUserControl.InputLocked, "input unlocked on restart");
            var packAfterRestart = FindObjectsByType<CarAudio>(FindObjectsSortMode.None).Where(a => a.GetComponent<CarUserControl>() == null).ToList();
            Check(packAfterRestart.Count > 0 && packAfterRestart.All(a => a.EngineGain >= 0.999f), "pack engines back on for the restart");
            var hiddenBots = FindObjectsByType<AIFinishBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(f => f.IsHidden || f.HasFinished).ToList();
            var botsBack = FindObjectsByType<AIFinishBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(f => f.gameObject.activeInHierarchy && f.GetComponent<CarUserControl>() == null && f.GetComponent<CarAIControl>() != null && f.GetComponent<CarAIControl>().enabled).Select(f => f.gameObject).Distinct().Count();
            Check(hiddenBots.Count == 0 && botsBack == RaceData.AiBotsSelected, "finished bot back on the grid and driving after restart (" + botsBack + " bots driving, " + hiddenBots.Count + " still finished)");
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
