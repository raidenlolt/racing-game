using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// the nitro presentation pass: the new boost icon on the fire button, the button animation, the
/// speed-line overlay, and exhaust flame and streak particles on every car.
///
/// the pickups on the track keep the bottle artwork. only the button changes, because the bottle
/// still reads as "collect me" on the road while the flame reads as "press me" under a thumb.
///
/// safe to re-run: objects are found by name before being created.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class NitroFxSetup
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string VfxFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/VFX";
        private const string FlamePrefabPath = VfxFolder + "/Nitro Flame.prefab";
        private const string StreakPrefabPath = VfxFolder + "/Nitro Streak.prefab";
        private const string FlameMaterialPath = "Assets/Settings/Nitro Flame.mat";
        private const string StreakMaterialPath = "Assets/Settings/Nitro Streak.mat";
        private const string ParticleTexture = "Assets/Racing Starter Kit/RSK Assets/VFX/ParticleCloudWhite.png";
        private const string IconPath = "Assets/Racing Starter Kit/RSK Assets/Textures/UI/Nitro Boost Icon.png";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string EventsPath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset";

        private static readonly string[] BaseCars =
        {
            "Player Car 2", "Player Car 3", "Player Car 4",
            "Player Car 5", "Player Car 6", "Player Car 7",
        };

        [MenuItem("Tools/Racing/Set Up Nitro FX")]
        public static void Run()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(EventsPath);
            if (events == null) { Debug.LogError("[NitroFX] GameEvents asset not found"); return; }

            var icon = ImportIcon();
            var flame = BuildFlamePrefab();
            var streak = BuildStreakPrefab();

            foreach (var car in BaseCars)
                WireCar(Cars + car + ".prefab", flame, streak);
            WireHud(events, icon);

            AssetDatabase.SaveAssets();
            Debug.Log("[NitroFX] done");
        }

        private static Sprite ImportIcon()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) { Debug.LogError("[NitroFX] icon not found at " + IconPath); return null; }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }

        /// <summary>
        /// short-lived, fast, additive puffs pushed straight out of the exhaust in the car's own
        /// space, so the flame stays attached to the pipe at any speed
        /// </summary>
        private static ParticleSystem BuildFlamePrefab()
        {
            VfxMaterials.EnsureFolder(VfxFolder);
            var material = VfxMaterials.AdditiveParticle(FlameMaterialPath, ParticleTexture, Color.white);

            var go = new GameObject("Nitro Flame");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSize = 0.45f;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.06f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, FlamePrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// thin stretched streaks left in world space behind the car, so a boosting car draws a
        /// trail that hangs in the air for a moment
        /// </summary>
        private static ParticleSystem BuildStreakPrefab()
        {
            var material = VfxMaterials.AdditiveParticle(StreakMaterialPath, ParticleTexture, Color.white);

            var go = new GameObject("Nitro Streak");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 22f);
            main.startSize = 0.18f;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4f;
            shape.radius = 0.12f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 2.5f;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, StreakPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// exhaust tips are derived from the biggest box collider on the car, which is the body:
        /// the rear face, a little below its centre, one tip either side
        /// </summary>
        private static void WireCar(string path, ParticleSystem flame, ParticleSystem streak)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("[NitroFX] missing " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) return;
            try
            {
                var fx = root.GetComponent<NitroExhaustFX>();
                if (fx == null) fx = root.AddComponent<NitroExhaustFX>();
                fx.flamePrefab = flame;
                fx.streakPrefab = streak;

                var body = root.GetComponentsInChildren<BoxCollider>(true)
                    .Where(b => !b.isTrigger)
                    .OrderByDescending(b => b.size.x * b.size.y * b.size.z)
                    .FirstOrDefault();
                if (body != null)
                {
                    // collider values are in the collider's own space; bring them into the car root
                    var localCentre = root.transform.InverseTransformPoint(body.transform.TransformPoint(body.center));
                    var rearZ = localCentre.z - body.size.z * 0.5f + 0.05f;
                    var y = localCentre.y - body.size.y * 0.2f;
                    var x = body.size.x * 0.24f;
                    fx.exhaustOffsets = new[] { new Vector3(-x, y, rearZ), new Vector3(x, y, rearZ) };
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[NitroFX] wired " + root.name + " exhausts at " + fx.exhaustOffsets[0].ToString("F2"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void WireHud(GameEvents events, Sprite icon)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var raceUi = Find(root.transform, "RaceUI");
                var hud = Find(root.transform, "Nitro HUD");
                var button = Find(root.transform, "Fire Nitro Button");
                if (raceUi == null || hud == null || button == null)
                {
                    Debug.LogError("[NitroFX] RaceUI / Nitro HUD / Fire Nitro Button not all found in the GUI prefab");
                    return;
                }

                // the icon
                var buttonImage = button.GetComponent<Image>();
                if (icon != null && buttonImage != null)
                {
                    buttonImage.sprite = icon;
                    buttonImage.type = Image.Type.Simple;
                    buttonImage.preserveAspect = true;
                    buttonImage.color = Color.white;
                }
                var buttonRect = (RectTransform)button;
                var buttonCentre = buttonRect.anchoredPosition
                                   + new Vector2((0.5f - buttonRect.pivot.x) * buttonRect.sizeDelta.x,
                                                 (0.5f - buttonRect.pivot.y) * buttonRect.sizeDelta.y);

                // the glow ring, behind the button
                var ring = Find(hud, "Nitro Glow Ring");
                if (ring == null)
                {
                    ring = new GameObject("Nitro Glow Ring", typeof(RectTransform), typeof(Image)).transform;
                    ring.SetParent(hud, false);
                    ring.SetSiblingIndex(button.GetSiblingIndex());
                }
                var ringRect = (RectTransform)ring;
                ringRect.anchorMin = ringRect.anchorMax = buttonRect.anchorMin;
                ringRect.pivot = new Vector2(0.5f, 0.5f);
                ringRect.anchoredPosition = buttonCentre;
                ringRect.sizeDelta = new Vector2(150f, 150f);
                var ringImage = ring.GetComponent<Image>();
                ringImage.raycastTarget = false;
                ringImage.color = new Color(1f, 1f, 1f, 0f);

                // the level pips, above the button
                var pipsRoot = Find(hud, "Nitro Pips");
                if (pipsRoot == null)
                {
                    pipsRoot = new GameObject("Nitro Pips", typeof(RectTransform)).transform;
                    pipsRoot.SetParent(hud, false);
                }
                var pipsRect = (RectTransform)pipsRoot;
                pipsRect.anchorMin = pipsRect.anchorMax = buttonRect.anchorMin;
                pipsRect.pivot = new Vector2(0.5f, 0.5f);
                pipsRect.anchoredPosition = buttonCentre + new Vector2(0f, buttonRect.sizeDelta.y * 0.5f + 16f);
                pipsRect.sizeDelta = new Vector2(80f, 16f);
                var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                var pips = new Image[3];
                for (int i = 0; i < 3; i++)
                {
                    var name = "Pip " + (i + 1);
                    var pip = Find(pipsRoot, name);
                    if (pip == null)
                    {
                        pip = new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
                        pip.SetParent(pipsRoot, false);
                    }
                    var pipRect = (RectTransform)pip;
                    pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0.5f);
                    pipRect.pivot = new Vector2(0.5f, 0.5f);
                    pipRect.anchoredPosition = new Vector2((i - 1) * 24f, 0f);
                    pipRect.sizeDelta = new Vector2(14f, 14f);
                    var pipImage = pip.GetComponent<Image>();
                    pipImage.sprite = uiSprite;
                    pipImage.raycastTarget = false;
                    pipImage.color = new Color(1f, 1f, 1f, 0.25f);
                    pips[i] = pipImage;
                }

                var buttonFx = hud.GetComponent<NitroButtonFX>();
                if (buttonFx == null) buttonFx = hud.gameObject.AddComponent<NitroButtonFX>();
                buttonFx.gameEvents = events;
                buttonFx.buttonRect = buttonRect;
                buttonFx.buttonImage = buttonImage;
                buttonFx.glowRing = ringImage;
                buttonFx.pips = pips;

                // the speed lines, full screen, under the HUD but over the hit flash
                var lines = Find(raceUi, "Speed Lines");
                if (lines == null)
                {
                    lines = new GameObject("Speed Lines", typeof(RectTransform), typeof(Image)).transform;
                    lines.SetParent(raceUi, false);
                    var flash = Find(raceUi, "Hit Flash");
                    lines.SetSiblingIndex(flash != null ? flash.GetSiblingIndex() + 1 : 0);
                }
                var linesRect = (RectTransform)lines;
                linesRect.anchorMin = Vector2.zero;
                linesRect.anchorMax = Vector2.one;
                linesRect.pivot = new Vector2(0.5f, 0.5f);
                linesRect.anchoredPosition = Vector2.zero;
                // square and larger than the screen so the rotation never shows a corner
                linesRect.sizeDelta = new Vector2(900f, 900f);
                var linesImage = lines.GetComponent<Image>();
                linesImage.raycastTarget = false;
                linesImage.preserveAspect = false;
                linesImage.color = new Color(1f, 1f, 1f, 0f);

                var nitroGui = root.GetComponentInChildren<NitroGUI>(true);
                if (nitroGui != null) nitroGui.speedLinesImage = linesImage;
                else Debug.LogWarning("[NitroFX] no NitroGUI on the GUI prefab; speed lines left unwired");

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[NitroFX] HUD wired: icon, ring, pips, speed lines");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
