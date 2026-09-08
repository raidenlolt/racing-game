using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// builds the finish banner into the GUI prefab and attaches RaceFinishSequence to drive it, plus
/// a confetti burst prefab for wins.
///
/// the banner is a sibling of RaceUI rather than a child, because RaceUI is switched off the moment
/// the race ends and the banner has to be the thing that replaces it. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class FinishSequenceSetup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string EventsPath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/GameEvents.asset";
        private const string PositionsPath = "Assets/Racing Starter Kit/RSK Assets/ScriptableObjects/Real Time Race Positions Item.asset";
        private const string VfxFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/VFX";
        private const string ConfettiPrefabPath = VfxFolder + "/Finish Confetti.prefab";
        private const string ConfettiMaterialPath = "Assets/Settings/Finish Confetti.mat";

        [MenuItem("Tools/Racing/Set Up Finish Sequence")]
        public static void Run()
        {
            var events = AssetDatabase.LoadAssetAtPath<GameEvents>(EventsPath);
            var positions = AssetDatabase.LoadAssetAtPath<RealTimeRacePositionsItem>(PositionsPath);
            if (events == null || positions == null) { Debug.LogError("[Finish] ScriptableObjects not found"); return; }

            var confetti = BuildConfettiPrefab();
            WireGui(events, positions, confetti);

            AssetDatabase.SaveAssets();
            Debug.Log("[Finish] done");
        }

        private static ParticleSystem BuildConfettiPrefab()
        {
            VfxMaterials.EnsureFolder(VfxFolder);
            var material = VfxMaterials.AlphaBlendedParticle(ConfettiMaterialPath, Color.white);

            var go = new GameObject("Finish Confetti");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.useUnscaledTime = true;
            var palette = new Gradient();
            palette.mode = GradientMode.Fixed;
            palette.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.2f),
                    new GradientColorKey(new Color(0.3f, 0.75f, 1f), 0.4f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.45f), 0.6f),
                    new GradientColorKey(new Color(0.45f, 0.95f, 0.5f), 0.8f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(palette) { mode = ParticleSystemGradientMode.RandomColor };

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 220) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // up

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.6f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ConfettiPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ParticleSystem>();
        }

        private static void WireGui(GameEvents events, RealTimeRacePositionsItem positions, ParticleSystem confetti)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var raceUi = Find(root.transform, "RaceUI");
                var countdown = Find(root.transform, "Countdown TMP");
                if (raceUi == null) { Debug.LogError("[Finish] RaceUI not found"); return; }
                var canvas = raceUi.parent;
                var font = countdown != null ? countdown.GetComponent<TMP_Text>()?.font : null;

                // banner root, full screen, off until the sequence turns it on
                var banner = Find(canvas, "Finish Banner");
                if (banner == null)
                {
                    banner = new GameObject("Finish Banner", typeof(RectTransform)).transform;
                    banner.SetParent(canvas, false);
                    banner.SetSiblingIndex(raceUi.GetSiblingIndex() + 1);
                }
                Stretch((RectTransform)banner);
                banner.gameObject.SetActive(false);

                // white flash over everything
                var flash = Find(banner, "Flash");
                if (flash == null)
                {
                    flash = new GameObject("Flash", typeof(RectTransform), typeof(Image)).transform;
                    flash.SetParent(banner, false);
                }
                Stretch((RectTransform)flash);
                var flashImage = flash.GetComponent<Image>();
                flashImage.raycastTarget = false;
                flashImage.color = new Color(1f, 1f, 1f, 0f);

                // a dark band the text sits on, so FINISH reads over any track
                var band = Find(banner, "Band");
                if (band == null)
                {
                    band = new GameObject("Band", typeof(RectTransform), typeof(Image)).transform;
                    band.SetParent(banner, false);
                }
                var bandRect = (RectTransform)band;
                bandRect.anchorMin = new Vector2(0f, 0.5f);
                bandRect.anchorMax = new Vector2(1f, 0.5f);
                bandRect.pivot = new Vector2(0.5f, 0.5f);
                bandRect.anchoredPosition = new Vector2(0f, 40f);
                bandRect.sizeDelta = new Vector2(0f, 230f);
                var bandImage = band.GetComponent<Image>();
                bandImage.raycastTarget = false;
                bandImage.color = new Color(0f, 0f, 0f, 0.45f);

                var finish = TextObject(banner, "Finish TMP", font, 190f, new Vector2(0f, 70f), new Vector2(1200f, 220f));
                finish.text = "FINISH";
                var position = TextObject(banner, "Position TMP", font, 70f, new Vector2(0f, -50f), new Vector2(900f, 90f));
                position.text = "";
                position.color = new Color(1f, 0.85f, 0.25f);

                var sequence = root.GetComponent<RaceFinishSequence>();
                if (sequence == null) sequence = root.AddComponent<RaceFinishSequence>();
                sequence.gameEvents = events;
                sequence.realTimeRacePositions = positions;
                sequence.bannerRoot = banner.gameObject;
                sequence.flashImage = flashImage;
                sequence.finishText = finish;
                sequence.positionText = position;
                sequence.confettiPrefab = confetti;

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[Finish] banner and sequence wired into the GUI prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static TMP_Text TextObject(Transform parent, string name, TMP_FontAsset font, float size,
                                           Vector2 position, Vector2 box)
        {
            var t = Find(parent, name);
            if (t == null)
            {
                t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).transform;
                t.SetParent(parent, false);
            }
            var rect = (RectTransform)t;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = box;

            var text = t.GetComponent<TMP_Text>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
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
