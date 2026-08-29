using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// three changes that were asked for together.
///
/// 1. the car colour picker leaves the menu. it also has to leave the car, because CarColor reads
///    CarColorPickerGUI.SelectedCarColor, and with no picker that static stays at its default of
///    transparent black. removing only the panel would repaint Player Car 1 black.
///
/// 2. the AI is made competitive. measured mid-race, the bots were crawling for specific reasons:
///    m_CautiousMaxAngle of 20 degrees meant almost every corner triggered maximum caution, and
///    maximum caution meant m_CautiousSpeedFactor of 0.5, so half speed nearly all the time.
///    m_SteerSensitivity of 0.01 needed a 100 degree error before full lock, so they ran wide too.
///
/// 3. the four arrow buttons become an analog thumb stick plus accelerator and brake pads, so
///    steering and throttle are finally independent of each other.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ControlsAndAiSetup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string JoystickPrefab = "Assets/Joystick Pack/Prefabs/Fixed Joystick.prefab";

        // a corner has to be genuinely tight before the AI backs right off, and backing off is no
        // longer a halving. together these are what stop the bots crawling round every bend
        private const float CautiousMaxAngle = 55f;
        private const float CautiousSpeedFactor = 0.72f;
        private const float SteerSensitivity = 0.045f;
        private const float AccelSensitivity = 1.4f;

        private static readonly Vector2 JoystickSize = new Vector2(300f, 300f);
        /// <summary>gap from the screen corner, in canvas units, clear of the gesture bar</summary>
        private const float JoystickMargin = 110f;

        [MenuItem("Tools/Racing/Improve AI And Touch Controls")]
        public static void Run()
        {
            RemoveCarColour();
            TuneAi();
            RebuildTouchControls();
            AssetDatabase.SaveAssets();
            Debug.Log("[Tune] done");
        }

        private static void RemoveCarColour()
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            if (root == null) { Debug.LogError("[Tune] missing " + GuiPrefab); return; }
            try
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var panel = all.FirstOrDefault(t => t.name == "Car Color Container");
                if (panel != null) { Object.DestroyImmediate(panel.gameObject); Debug.Log("[Tune] removed Car Color Container"); }

                var script = root.GetComponentInChildren<CarColorPickerGUI>(true);
                if (script != null) { Object.DestroyImmediate(script.gameObject); Debug.Log("[Tune] removed Car Color Picker GUI"); }

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // and the component that depended on it, or the car paints itself transparent black
            foreach (var name in new[] { "Player Car 1", "Player Car 2", "Player Car 3", "Player Car 4" })
            {
                var path = Cars + name + ".prefab";
                var car = PrefabUtility.LoadPrefabContents(path);
                if (car == null) continue;
                try
                {
                    var colours = car.GetComponentsInChildren<CarColor>(true);
                    if (colours.Length == 0) continue;
                    foreach (var c in colours) Object.DestroyImmediate(c);
                    PrefabUtility.SaveAsPrefabAsset(car, path);
                    Debug.Log("[Tune] removed " + colours.Length + " CarColor component(s) from " + name);
                }
                finally { PrefabUtility.UnloadPrefabContents(car); }
            }
        }

        private static void TuneAi()
        {
            foreach (var name in new[] { "Player Car 1", "Player Car 2", "Player Car 3", "Player Car 4" })
            {
                var path = Cars + name + " (AI Variant).prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Debug.LogWarning("[Tune] missing " + path); continue; }
                try
                {
                    var ai = root.GetComponentInChildren<CarAIControl>(true);
                    if (ai == null) { Debug.LogWarning("[Tune] no CarAIControl on " + name); continue; }

                    // these fields are [SerializeField] private, so they have to go through SerializedObject
                    var so = new SerializedObject(ai);
                    Set(so, "m_CautiousMaxAngle", CautiousMaxAngle);
                    Set(so, "m_CautiousSpeedFactor", CautiousSpeedFactor);
                    Set(so, "m_SteerSensitivity", SteerSensitivity);
                    Set(so, "m_AccelSensitivity", AccelSensitivity);
                    so.ApplyModifiedPropertiesWithoutUndo();

                    // the band values have to be written onto the prefab, not just changed in the
                    // script. these components were serialised with the old numbers, and a changed
                    // C# default never reaches an instance that already exists, so softening the
                    // hold-back in code alone left every bot still pinned at 0.88 speed / 0.80 torque
                    // the single biggest reason bots hit walls: their obstacle avoidance was masked to
                    // Player + AIPlayer only, so it could see other cars and was completely blind to
                    // the track boundary. adding Default lets it steer away from walls, barriers and
                    // scenery. the rays are horizontal at body height, so the road itself is not hit
                    foreach (var steer in root.GetComponentsInChildren<AICarAvoidanceSmartSteering>(true))
                    {
                        steer.obstacleLayers = LayerMask.GetMask("Default", "Player", "AIPlayer");
                        // 4m of lookahead is a fifth of a second at racing speed; give it room to react
                        steer.baseRaycastDistance = 10f;
                        steer.speedLookaheadFactor = 0.55f;
                        steer.steeringStrength = 0.55f;
                        Debug.Log("[Tune] " + name + " smart steering can now see walls, lookahead 10m + 0.55/mps");
                    }

                    // proper stuck detection. the kit's own unstick only fires below 0.4 mph, so a car
                    // grinding along a wall at 5-10 mph never recovered
                    var stuck = root.GetComponent<AIStuckRecovery>();
                    if (stuck == null) stuck = root.AddComponent<AIStuckRecovery>();
                    stuck.raceManager = AssetDatabase.LoadAssetAtPath<RaceManagerItem>(
                        "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/Race Manager Item.asset");

                    var band = root.GetComponentInChildren<AIRubberBanding>(true);
                    if (band != null)
                    {
                        band.speedBandMin = 0.97f;
                        band.speedBandMax = 1.14f;
                        band.torqueBandMin = 0.94f;
                        band.torqueBandMax = 1.35f;
                        Debug.Log("[Tune] " + name + " banding: hold-back softened to 0.97 / 0.94");
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("[Tune] " + name + " AI: cautiousAngle " + CautiousMaxAngle + ", cautiousSpeed " +
                              CautiousSpeedFactor + ", steer " + SteerSensitivity + ", accel " + AccelSensitivity);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        private static void Set(SerializedObject so, string field, float value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[Tune] field not found: " + field); return; }
            p.floatValue = value;
        }

        private static void RebuildTouchControls()
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            if (root == null) { Debug.LogError("[Tune] missing " + GuiPrefab); return; }
            try
            {
                // resolve everything BEFORE destroying anything. the cached array keeps holding the
                // destroyed entries, and LINQ reading .name on one of those throws
                // MissingReferenceException, which is exactly what killed the first run of this tool
                var all = root.GetComponentsInChildren<Transform>(true);
                var rig = all.FirstOrDefault(t => t.name == "Mobile Controls");
                var turnLeft = all.FirstOrDefault(t => t.name == "Turn Left Button");
                var turnRight = all.FirstOrDefault(t => t.name == "Turn Right Button");
                var throttle = all.FirstOrDefault(t => t.name == "Throttle Button");
                var brake = all.FirstOrDefault(t => t.name == "Brake/Reverse Button");
                var nitro = all.FirstOrDefault(t => t.name == "Nitro HUD");
                if (rig == null) { Debug.LogError("[Tune] no Mobile Controls rig"); return; }

                // the arrows go: a stick replaces them
                if (turnLeft != null) { Object.DestroyImmediate(turnLeft.gameObject); Debug.Log("[Tune] removed Turn Left Button"); }
                if (turnRight != null) { Object.DestroyImmediate(turnRight.gameObject); Debug.Log("[Tune] removed Turn Right Button"); }

                // accelerator bottom right under the thumb, brake just inboard of it
                Place(throttle, new Vector2(1f, 0f), new Vector2(-90f, 90f), new Vector2(220f, 220f), "accelerator");
                Place(brake, new Vector2(1f, 0f), new Vector2(-330f, 110f), new Vector2(170f, 170f), "brake");
                StyleAsPedal(throttle, new Color(0.15f, 0.75f, 0.25f, 0.85f), "GAS");
                StyleAsPedal(brake, new Color(0.85f, 0.20f, 0.18f, 0.85f), "BRAKE");

                // the nitro strip sat in the bottom right corner, which is now the accelerator
                if (nitro != null)
                {
                    var rt = (RectTransform)nitro;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-40f, 340f);
                    Debug.Log("[Tune] moved Nitro HUD clear of the accelerator");
                }

                var existingStick = rig.Find("Steering Joystick");
                if (existingStick == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JoystickPrefab);
                    if (prefab == null) { Debug.LogError("[Tune] missing " + JoystickPrefab); return; }
                    var stick = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig);
                    stick.name = "Steering Joystick";
                    existingStick = stick.transform;
                    Debug.Log("[Tune] added Steering Joystick");
                }

                // positioned every run, not only on creation, so a bad placement can be corrected.
                //
                // the pivot MUST be centre. Joystick.Start does `background.pivot = (0.5, 0.5)` on its
                // own RectTransform, so any other pivot set here is silently overwritten the moment
                // the game runs. with a corner pivot stored and a centre pivot applied, the stick
                // jumped half its own size and hung off the bottom left of the screen.
                // so anchoredPosition is the CENTRE of where the stick should sit
                var jrt = (RectTransform)existingStick;
                jrt.anchorMin = jrt.anchorMax = new Vector2(0f, 0f);
                jrt.pivot = new Vector2(0.5f, 0.5f);
                jrt.sizeDelta = JoystickSize;
                jrt.anchoredPosition = new Vector2(JoystickMargin + JoystickSize.x * 0.5f,
                                                   JoystickMargin + JoystickSize.y * 0.5f);
                Debug.Log("[Tune] Steering Joystick centred at " + jrt.anchoredPosition +
                          " so it spans " + JoystickMargin + ".." + (JoystickMargin + JoystickSize.x) + " from the corner");

                var drive = rig.GetComponent<TouchDriveControls>();
                if (drive == null) drive = rig.gameObject.AddComponent<TouchDriveControls>();
                drive.steeringJoystick = rig.GetComponentInChildren<Joystick>(true);
                Debug.Log("[Tune] TouchDriveControls wired to " +
                          (drive.steeringJoystick != null ? drive.steeringJoystick.gameObject.name : "NOTHING"));

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Place(Transform t, Vector2 anchor, Vector2 pos, Vector2 size, string label)
        {
            if (t == null) { Debug.LogWarning("[Tune] could not find the " + label + " button"); return; }
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Debug.Log("[Tune] placed " + label + " at " + pos + " size " + size);
        }

        /// <summary>
        /// turns a control into a round pedal pad. moving the triangles was not enough on its own:
        /// they still read as arrows on the device, which is the thing being replaced. the round knob
        /// sprite plus a colour that means something (green go, red stop) is what makes them read as
        /// pedals at a glance
        /// </summary>
        private static void StyleAsPedal(Transform t, Color colour, string caption)
        {
            if (t == null) return;

            // the arrow is NOT on the button. these buttons carry only Button, EventTrigger and
            // MobileButtonHandler; the triangle sprite lives on a child called "Image", and the brake
            // is the same sprite rotated 180 degrees. an earlier version of this looked for an Image
            // on the button itself, found nothing, changed nothing, and still logged success
            var image = t.GetComponentsInChildren<Image>(true).FirstOrDefault();
            if (image == null)
            {
                Debug.LogWarning("[Tune] " + t.name + " has no Image anywhere, cannot restyle it");
                return;
            }

            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (knob == null) knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (knob == null) { Debug.LogWarning("[Tune] could not load a builtin sprite for the pedals"); return; }

            image.sprite = knob;
            image.type = Image.Type.Simple;
            image.color = colour;
            // the brake's arrow was flipped upside down; a round pad must not inherit that
            image.rectTransform.localRotation = Quaternion.identity;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = Vector2.zero;
            image.rectTransform.offsetMax = Vector2.zero;

            // the button had no targetGraphic of its own either, so give it one for the press tint
            var button = t.GetComponent<Button>();
            if (button != null) button.targetGraphic = image;

            var label = t.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).FirstOrDefault();
            if (label == null)
            {
                var go = new GameObject("Label", typeof(RectTransform));
                go.transform.SetParent(t, false);
                label = go.AddComponent<TMPro.TextMeshProUGUI>();
            }
            label.text = caption;
            label.fontSize = 30f;
            label.fontStyle = TMPro.FontStyles.Bold;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;   // never steal the touch from the pedal underneath
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            Debug.Log("[Tune] " + t.name + " -> round '" + caption + "' pad, sprite now '" + image.sprite.name +
                      "' on child '" + image.gameObject.name + "'");
        }
    }
}
