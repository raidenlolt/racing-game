using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// attaches and wires the nitro / arcade-feel components onto the car prefabs. done from an editor
/// script rather than by hand because every car needs the same five or six references hooked to the
/// same ScriptableObjects, and doing that eight times through the inspector is where mistakes live.
///
/// safe to re-run: components are only added when missing, and existing tuning is left alone.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class AsphaltFeelSetup
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string SO = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/";
        private const string CamerasPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/PlayerCarCameras.prefab";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string JumpPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Jump.prefab";

        /// <summary>set by SetUp before the per-car helpers run, so CarRespawn can be wired</summary>
        private static AIWaypointSet Waypoints;

        private static readonly string[] BaseCars =
        {
            "Player Car 1", "Player Car 2", "Player Car 3", "Player Car 4",
        };

        /// <summary>
        /// runs the whole setup in dependency order in one pass. exists so a headless run needs a
        /// single -executeMethod and therefore a single editor launch:
        ///   Unity.exe -batchmode -quit -projectPath . -executeMethod
        ///   SpinMotion.EditorTools.AsphaltFeelSetup.RunEverything
        /// the jump prefab is repaired before pads are placed so the instances inherit the corrected
        /// trigger box rather than the 1cm one
        /// </summary>
        [MenuItem("Tools/Racing/Run Full Setup")]
        public static void RunEverything()
        {
            Debug.Log("[AsphaltFeel] === full setup starting ===");
            TrackWiring.WireAll();
            SetUp();
            // jump pads are deliberately not placed any more: they were removed from the tracks by
            // request. the prefab and the placement pass both still exist, so putting them back is a
            // matter of calling SetUpJumpPads() and TrackWiring.PlaceJumpPads() again
            Debug.Log("[AsphaltFeel] === full setup finished ===");
        }

        [MenuItem("Tools/Racing/Set Up Nitro And Arcade Feel")]
        public static void SetUp()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(SO + "GameEvents.asset");
            var raceManager = AssetDatabase.LoadAssetAtPath<RaceManagerItem>(SO + "Race Manager Item.asset");
            var positions = AssetDatabase.LoadAssetAtPath<RealTimeRacePositionsItem>(
                SO + "Real Time Race Positions Item.asset");
            Waypoints = AssetDatabase.LoadAssetAtPath<AIWaypointSet>(SO + "AI Waypoint Set.asset");

            if (events == null || raceManager == null || positions == null || Waypoints == null)
            {
                Debug.LogError("[AsphaltFeel] could not load the ScriptableObject assets, aborting");
                return;
            }

            foreach (var carName in BaseCars)
            {
                SetUpBaseCar(Cars + carName + ".prefab", events, raceManager, positions);
                SetUpAiVariant(Cars + carName + " (AI Variant).prefab", events, raceManager, positions);
            }

            SetUpCameras(events);
            SetUpNitroHud(events);

            AssetDatabase.SaveAssets();
            Debug.Log("[AsphaltFeel] done");
        }

        /// <summary>
        /// base cars are the ones PlayersSpawner uses as playerPrefab, so their NitroSystem is the
        /// player-facing one that raises HUD and camera events
        /// </summary>
        private static void SetUpBaseCar(string path, GameEvents events, RaceManagerItem raceManager,
                                         RealTimeRacePositionsItem positions)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[AsphaltFeel] missing " + path); return; }

            try
            {
                var nitro = GetOrAdd<NitroSystem>(root);
                nitro.gameEvents = events;
                nitro.raceManager = raceManager;
                nitro.isPlayer = true;

                var charger = GetOrAdd<NitroCharger>(root);
                charger.raceManager = raceManager;

                GetOrAdd<ArcadeAssists>(root);

                var respawn = GetOrAdd<CarRespawn>(root);
                respawn.raceManager = raceManager;
                respawn.aiWaypointSet = Waypoints;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[AsphaltFeel] wired player car " + System.IO.Path.GetFileName(path));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// AI variants inherit NitroSystem from their base car, so isPlayer has to be overridden back
        /// to false here or every bot on the grid would be driving the player's HUD
        /// </summary>
        private static void SetUpAiVariant(string path, GameEvents events, RaceManagerItem raceManager,
                                           RealTimeRacePositionsItem positions)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[AsphaltFeel] missing " + path); return; }

            try
            {
                var nitro = GetOrAdd<NitroSystem>(root);
                nitro.gameEvents = events;
                nitro.raceManager = raceManager;
                nitro.isPlayer = false;

                var charger = GetOrAdd<NitroCharger>(root);
                charger.raceManager = raceManager;

                GetOrAdd<ArcadeAssists>(root);

                var banding = GetOrAdd<AIRubberBanding>(root);
                banding.gameEvents = events;
                banding.raceManager = raceManager;
                banding.realTimeRacePositions = positions;

                var respawn = GetOrAdd<CarRespawn>(root);
                respawn.raceManager = raceManager;
                respawn.aiWaypointSet = Waypoints;

                var nitroDriver = GetOrAdd<AINitroDriver>(root);
                nitroDriver.raceManager = raceManager;
                nitroDriver.rubberBanding = banding;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[AsphaltFeel] wired AI car " + System.IO.Path.GetFileName(path));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetUpCameras(GameEvents events)
        {
            var root = PrefabUtility.LoadPrefabContents(CamerasPrefab);
            if (root == null) { Debug.LogWarning("[AsphaltFeel] missing " + CamerasPrefab); return; }

            try
            {
                var cameras = root.GetComponentsInChildren<Camera>(true);
                foreach (var cam in cameras)
                {
                    var fx = cam.GetComponent<SpeedCameraFX>();
                    if (fx == null) fx = cam.gameObject.AddComponent<SpeedCameraFX>();
                    fx.gameEvents = events;
                    // start from whatever the camera was already framed at, so adding the effect does
                    // not silently re-frame a camera someone tuned by hand
                    fx.baseFieldOfView = cam.fieldOfView;

                    ApplyCameraFraming(cam);
                }

                PrefabUtility.SaveAsPrefabAsset(root, CamerasPrefab);
                Debug.Log("[AsphaltFeel] wired " + cameras.Length + " player camera(s)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// makes the jump pad actually launch a car, and actually be hittable.
        ///
        /// the numbers are derived rather than guessed. the cars weigh 600kg and ApplyJumpBoost uses
        /// ForceMode.Impulse, so launch speed is force/mass and hang time is 2v/g. what caps the force
        /// is not feel but the track: the longest straight in the whole road kit is 78m, and a car at
        /// the 140mph top speed covers 62.6 m/s, so any hang time over about 1.25s lands the car past
        /// the end of the straight and into the corner wall.
        ///   the shipped 1000 gives 0.34s of air, under NitroCharger's 0.35s minimum, so a pad could
        ///   never pay out any air charge at all
        ///   3100 gives 5.2 m/s, 1.05s of air and 66m of flight at top speed, which fits inside the
        ///   78m straight with room to land
        /// TrackWiring overrides this per pad from the actual runway ahead of each one, so this value
        /// only applies to pads placed by hand.
        ///
        /// the trigger box mattered more than the force. it shipped at 1cm cubed on an unscaled
        /// transform, and a car covers about 1.25m per 0.02s physics step at top speed, so it would
        /// tunnel clean through the trigger on all but the slowest approach
        /// </summary>
        [MenuItem("Tools/Racing/Set Up Jump Pads")]
        public static void SetUpJumpPads()
        {
            const float jumpForce = 3100f;
            const float forwardBoostForce = 5000f;
            var triggerSize = new Vector3(10f, 4f, 6f);

            var root = PrefabUtility.LoadPrefabContents(JumpPrefab);
            if (root == null) { Debug.LogError("[AsphaltFeel] missing " + JumpPrefab); return; }

            try
            {
                var pad = root.GetComponentInChildren<JumpBoostPad>(true);
                if (pad == null)
                {
                    Debug.LogError("[AsphaltFeel] Jump.prefab has no JumpBoostPad");
                    return;
                }

                // the two force fields are [SerializeField] private, so they have to go through
                // SerializedObject rather than plain assignment
                var so = new SerializedObject(pad);
                var jump = so.FindProperty("m_JumpForce");
                var forward = so.FindProperty("m_ForwardBoostForce");
                if (jump == null || forward == null)
                {
                    Debug.LogError("[AsphaltFeel] JumpBoostPad fields not found, was the script renamed?");
                    return;
                }
                jump.floatValue = jumpForce;
                forward.floatValue = forwardBoostForce;
                so.ApplyModifiedPropertiesWithoutUndo();

                var box = pad.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.isTrigger = true;
                    box.size = triggerSize;
                    // lift the box so it straddles the road surface instead of sinking under it
                    box.center = new Vector3(0f, triggerSize.y * 0.5f, 0f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, JumpPrefab);
                Debug.Log("[AsphaltFeel] jump pad set to " + jumpForce + "N impulse (" +
                          (jumpForce / 600f).ToString("F1") + " m/s launch, " +
                          (2f * (jumpForce / 600f) / 9.81f).ToString("F2") + "s air), trigger " + triggerSize);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// builds the gauge and fire button into the race HUD. deliberately plain: a filled bar and a
        /// round button anchored bottom right, sized for a thumb on the landscape phone layout this
        /// game targets. restyle it in the inspector afterwards, the component only needs the two
        /// references to keep working
        /// </summary>
        private static void SetUpNitroHud(GameEvents events)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            if (root == null) { Debug.LogWarning("[AsphaltFeel] missing " + GuiPrefab); return; }

            try
            {
                var raceUi = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "RaceUI");
                if (raceUi == null)
                {
                    Debug.LogWarning("[AsphaltFeel] no RaceUI object in the GUI prefab, skipping HUD");
                    return;
                }

                if (raceUi.Find("Nitro HUD") != null)
                {
                    Debug.Log("[AsphaltFeel] nitro HUD already present, leaving it alone");
                    return;
                }

                var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

                var hud = NewUiObject("Nitro HUD", raceUi);
                Anchor(hud, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(320f, 150f));

                // track sits behind the fill so an empty gauge still reads as a gauge
                var track = NewUiObject("Gauge Track", hud.transform);
                Anchor(track, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(300f, 26f));
                var trackImage = track.AddComponent<Image>();
                trackImage.sprite = sprite;
                trackImage.type = Image.Type.Sliced;
                trackImage.color = new Color(0f, 0f, 0f, 0.55f);

                var fill = NewUiObject("Gauge Fill", track.transform);
                Anchor(fill, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(292f, 18f));
                var fillImage = fill.AddComponent<Image>();
                fillImage.sprite = sprite;
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                fillImage.fillAmount = 0f;

                var button = NewUiObject("Fire Nitro Button", hud.transform);
                Anchor(button, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(110f, 92f));
                var buttonImage = button.AddComponent<Image>();
                buttonImage.sprite = sprite;
                buttonImage.type = Image.Type.Sliced;
                buttonImage.color = new Color(0.25f, 0.7f, 1f, 0.85f);
                var buttonComponent = button.AddComponent<Button>();
                buttonComponent.targetGraphic = buttonImage;

                var nitroGui = GetOrAdd<NitroGUI>(root);
                nitroGui.gameEvents = events;
                nitroGui.nitroFillImage = fillImage;
                nitroGui.fireNitroButton = buttonComponent;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[AsphaltFeel] built the nitro HUD into RaceUI");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// camera framing, in metres behind and above the car.
        ///
        /// these were originally the decoded equivalents of the old controller's numbers, which
        /// worked out at 0.98m up and 2.86m back for the far camera. that turned out to be wrong in
        /// practice: the car models are scaled 2x to 4x and their bodies are 8.6 to 9.2 metres long,
        /// so an offset of 2.86m sat inside the bodywork and the camera rendered from within the car.
        /// the values below are sized against the actual measured body instead, putting the chase
        /// cameras several metres clear of the rear bumper.
        /// </summary>
        private static void ApplyCameraFraming(Camera cam)
        {
            var follow = cam.GetComponent<PlayerCarCameraController>();
            if (follow == null) return;

            switch (cam.gameObject.name)
            {
                case "FarCamera":
                    follow.offset = new Vector3(0f, 5.5f, -13.5f);
                    follow.pitch = 8f;
                    follow.rigid = false;
                    break;

                case "NormalCamera":
                    follow.offset = new Vector3(0f, 4.0f, -9.5f);
                    follow.pitch = 6f;
                    follow.rigid = false;
                    break;

                case "FPCamera":
                    // a bumper cam locked to the body: velocity swing and roll are unpleasant this
                    // close to the road, so it keeps only the FOV punch from SpeedCameraFX
                    follow.offset = new Vector3(0f, 2.6f, 2.0f);
                    follow.pitch = 0f;
                    follow.rigid = true;
                    break;

                default:
                    Debug.LogWarning("[AsphaltFeel] unrecognised camera '" + cam.gameObject.name +
                                     "', leaving its framing at the component defaults");
                    return;
            }

            Debug.Log("[AsphaltFeel] framed " + cam.gameObject.name + " at " + follow.offset +
                      (follow.rigid ? " (rigid)" : ""));
        }

        private static GameObject NewUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Anchor(GameObject go, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        private static T GetOrAdd<T>(GameObject root) where T : Component
        {
            var existing = root.GetComponent<T>();
            return existing != null ? existing : root.AddComponent<T>();
        }
    }
}
