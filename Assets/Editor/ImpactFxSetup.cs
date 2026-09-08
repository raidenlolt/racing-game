using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// builds and wires the collision feedback: a spark burst prefab, CarImpactFX on every base car
/// (the AI variants inherit it), and the red hit flash on the race HUD.
///
/// the spark particle and its material are generated here rather than authored, so the whole
/// feature is reproducible from a clean checkout. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ImpactFxSetup
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string VfxFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/VFX";
        private const string SparksPrefabPath = VfxFolder + "/Impact Sparks.prefab";
        private const string SparksMaterialPath = "Assets/Settings/Impact Sparks.mat";
        private const string ParticleTexture = "Assets/Racing Starter Kit/RSK Assets/VFX/ParticleCloudWhite.png";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string EventsPath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset";

        private static readonly string[] BaseCars =
        {
            "Player Car 2", "Player Car 3", "Player Car 4",
            "Player Car 5", "Player Car 6", "Player Car 7",
        };

        [MenuItem("Tools/Racing/Set Up Impact Feedback")]
        public static void Run()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(EventsPath);
            if (events == null) { Debug.LogError("[Impact] GameEvents asset not found"); return; }

            var sparks = BuildSparksPrefab();
            foreach (var car in BaseCars)
                WireCar(Cars + car + ".prefab", events, sparks);
            WireHud(events);

            AssetDatabase.SaveAssets();
            Debug.Log("[Impact] done");
        }

        /// <summary>
        /// a burst of short, stretched, additive streaks that fall under gravity: reads as sparks and
        /// costs a single draw call per car
        /// </summary>
        private static ParticleSystem BuildSparksPrefab()
        {
            VfxMaterials.EnsureFolder(VfxFolder);
            var material = VfxMaterials.AdditiveParticle(SparksMaterialPath, ParticleTexture, new Color(1f, 0.75f, 0.35f));

            var go = new GameObject("Impact Sparks");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, SparksPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[Impact] spark prefab written to " + SparksPrefabPath);
            return prefab.GetComponent<ParticleSystem>();
        }

        private static void WireCar(string path, GameEvents events, ParticleSystem sparks)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning("[Impact] missing " + path);
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) return;
            try
            {
                var fx = root.GetComponent<CarImpactFX>();
                if (fx == null) fx = root.AddComponent<CarImpactFX>();
                fx.gameEvents = events;
                fx.sparksPrefab = sparks;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Impact] wired " + root.name);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// the flash goes in as the first child of RaceUI so every other HUD element draws over it
        /// </summary>
        private static void WireHud(GameEvents events)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var raceUi = Find(root.transform, "RaceUI");
                if (raceUi == null) { Debug.LogError("[Impact] RaceUI not found in GUI prefab"); return; }

                var flash = Find(raceUi, "Hit Flash");
                if (flash == null)
                {
                    var go = new GameObject("Hit Flash", typeof(RectTransform), typeof(Image), typeof(HitFlashGUI));
                    flash = go.transform;
                    flash.SetParent(raceUi, false);
                    flash.SetAsFirstSibling();
                }

                var rect = (RectTransform)flash;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                // larger than the screen so the directional shift never exposes a bare edge
                rect.sizeDelta = new Vector2(320f, 320f);

                var image = flash.GetComponent<Image>();
                image.raycastTarget = false;
                image.color = new Color(1f, 0.18f, 0.08f, 0f);

                var gui = flash.GetComponent<HitFlashGUI>();
                gui.gameEvents = events;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[Impact] hit flash on the race HUD");
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
