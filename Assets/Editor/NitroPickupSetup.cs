using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// scatters nitro bottles around each circuit.
///
/// each site is a row of three ACROSS the road, centred on the AI waypoint loop: one on the racing
/// line and one out towards each edge of the tarmac. they are spaced so a car can only reach one, so
/// every site asks which lane is worth taking rather than handing over nitro for holding the
/// throttle.
///
/// spacing is derived from the lap rather than fixed, so a longer track gets proportionally more and
/// every circuit ends up with the same rhythm.
///
/// the visual is a quad with an unlit, additive material and no texture yet -- a glowing panel. when
/// the nitro logo arrives, dropping it into that one material's texture slot changes every bottle on
/// every track at once.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class NitroPickupSetup
    {
        private const string ContainerName = "Nitro Pickups";
        private const string MaterialPath = "Assets/Settings/Nitro Pickup.mat";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        /// <summary>
        /// roughly how far apart pickup sites sit along the lap, in metres.
        ///
        /// widened from 95 to 190 to 285 as the nitro economy was tightened. at a row every 95m nitro
        /// was almost never absent, which makes the gauge a formality rather than something to spend
        /// carefully. 285m puts six rows on Race_Track_01's 1706m lap; the other circuits get a count
        /// proportional to their own length, so the rhythm is the same wherever you race.
        /// </summary>
        private const float Spacing = 285f;

        /// <summary>bottles per site: one row spanning the road</summary>
        private const int PerSite = 3;

        /// <summary>
        /// how far the outer bottles sit either side of the racing line.
        ///
        /// the row runs ACROSS the road, not along it, so a site is a choice of lane rather than a
        /// run to sweep up. 10m is picked against two measurements: the narrowest tarmac on this
        /// circuit gives 11.5m either side of the line, so the outer bottles are always on the road;
        /// and a car reaches about 5.4m to a bottle's trigger, so at 10m apart it can only ever take
        /// one of the three.
        /// </summary>
        private const float RowSpacing = 10f;

        /// <summary>
        /// what one bottle is worth.
        ///
        /// only one of a row can be reached now, where a lengthwise row could be swept for three. at
        /// 0.10 that would have cut nitro income to a third overnight; 0.25 keeps a site worth about
        /// the 0.30 it is worth today, so the change is to how nitro is earned rather than how much.
        /// </summary>
        private const float ChargePerBottle = 0.25f;
        /// <summary>high enough to read against the road, low enough that a car body passes through it</summary>
        private const float Height = 1.5f;
        private const float TriggerRadius = 3.4f;
        /// <summary>
        /// sized to be picked out at racing speed on a phone. at 2.6m the bottle was legible standing
        /// still and a smudge at 90mph, which is the only speed anyone will see it at.
        /// </summary>
        private const float VisualSize = 3.6f;

        [MenuItem("Tools/Racing/Place Nitro Pickups")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var material = EnsureMaterial();

            foreach (var scenePath in Scenes) Place(scenePath, material);

            AssetDatabase.SaveAssets();
            Debug.Log("[Nitro] done. Drop the logo texture into " + MaterialPath + " to restyle every bottle.");
        }

        /// <summary>
        /// one shared material for every bottle on every track, so the logo only has to be set once.
        /// unlit and additive: it glows rather than being lit by the sun, which keeps it readable in
        /// the long shadows this track runs in.
        /// </summary>
        private static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");

            var material = new Material(shader);
            material.name = "Nitro Pickup";
            material.color = new Color(0.16f, 0.85f, 1f, 0.9f);   // the game's accent cyan

            // additive-ish transparency so it reads as a glow rather than a solid card
            material.SetFloat("_Surface", 1f);        // transparent
            material.SetFloat("_Blend", 1f);          // additive
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = 3000;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            AssetDatabase.CreateAsset(material, MaterialPath);
            Debug.Log("[Nitro] created the shared bottle material at " + MaterialPath);
            return material;
        }

        private static void Place(string scenePath, Material material)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var line = RacingLine();
            if (line.Count < 3)
            {
                Debug.LogWarning("[Nitro] " + scene.name + ": no racing line, skipped");
                return;
            }

            var old = GameObject.Find(ContainerName);
            if (old != null) Object.DestroyImmediate(old);

            var container = new GameObject(ContainerName);
            var raceManager = FindAsset<RaceManagerItem>();

            // the stage the menu camera looks at is on the racing line, so bottles would otherwise
            // hang in front of the car being shown off
            var visibility = container.AddComponent<NitroPickupsVisibility>();
            visibility.gameEvents = FindAsset<GameEvents>();

            var placed = 0;
            var lapLength = 0f;
            for (int i = 0; i < line.Count; i++) lapLength += Vector3.Distance(line[i], line[(i + 1) % line.Count]);

            // walk the lap at a constant step rather than per waypoint, because the waypoints are
            // 39m to 136m apart and would bunch the bottles up around the tight corners
            var travelled = 0f;
            var segment = 0;
            var alongSegment = 0f;
            while (travelled < lapLength)
            {
                var a = line[segment];
                var b = line[(segment + 1) % line.Count];
                var segmentLength = Vector3.Distance(a, b);

                if (alongSegment > segmentLength)
                {
                    alongSegment -= segmentLength;
                    segment = (segment + 1) % line.Count;
                    continue;
                }

                var forward = (b - a).normalized;
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                // centred on the racing line: the row already spans the road, so shifting the whole
                // row sideways as well would only push its outer bottle onto the grass
                var centre = a + forward * alongSegment + Vector3.up * Height;

                // a row of three ACROSS the road: left lane, racing line, right lane
                for (int n = 0; n < PerSite; n++)
                {
                    var offset = (n - (PerSite - 1) * 0.5f) * RowSpacing;
                    Build(container.transform, centre + right * offset, material, raceManager, placed);
                    placed++;
                }
                alongSegment += Spacing;
                travelled += Spacing;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Nitro] " + scene.name + ": " + placed + " bottle(s) over a " + lapLength.ToString("F0") + " m lap");
        }

        private static void Build(Transform parent, Vector3 position, Material material,
                                  RaceManagerItem raceManager, int index)
        {
            var go = new GameObject("Nitro Bottle " + index);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;

            // the visual is a separate child so it can be hidden while collected, and so replacing it
            // with the logo art later does not disturb the trigger
            var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * VisualSize;
            Object.DestroyImmediate(visual.GetComponent<Collider>());   // the parent owns the trigger
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;

            var pickup = go.AddComponent<NitroPickup>();
            pickup.visual = visual;
            pickup.raceManager = raceManager;
            pickup.chargeAmount = ChargePerBottle;
        }

        /// <summary>the AI waypoint loop, in the order the AI drives it</summary>
        private static List<Vector3> RacingLine()
        {
            var found = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("AI Waypoint") && t.GetComponent<MeshRenderer>() != null)
                .OrderBy(t => t.GetSiblingIndex())
                .Select(t => t.position)
                .ToList();
            return found;
        }

        private static T FindAsset<T>() where T : ScriptableObject
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
