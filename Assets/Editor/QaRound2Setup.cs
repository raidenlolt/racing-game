using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// the second QA round, in one re-runnable pass:
///   menu      a TRACKS button that returns to LevelSelect from the car menu, and chevron icons on
///             the car selector's minus/plus buttons
///   HUD       chevron and pedal icons in place of the LEFT / RIGHT / BRAKE / GAS captions
///   walls     a frictionless physics material on every perimeter wall and barrier collider, so a
///             glancing hit slides instead of scrubbing 160 mph to nothing
///   cars      WallSlide on every car, and continuous dynamic collision instead of speculative,
///             whose ghost contacts are part of why a brush with a wall stopped the car dead
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class QaRound2Setup
    {
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";
        private const string Icons = "Assets/Racing Starter Kit/RSK Assets/Textures/UI/";
        private const string WallMaterialPath = "Assets/Settings/Wall.physicMaterial";

        private static readonly string[] BaseCars =
        {
            "Player Car 2", "Player Car 3", "Player Car 4", "Player Car 5", "Player Car 6", "Player Car 7",
        };

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_04.unity",
        };

        [MenuItem("Tools/Racing/Apply QA Round 2")]
        public static void RunMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            var chevronLeft = ImportIcon("Icon Chevron Left.png");
            var chevronRight = ImportIcon("Icon Chevron Right.png");
            var pedalBrake = ImportIcon("Icon Pedal Brake.png");
            var pedalGas = ImportIcon("Icon Pedal Gas.png");

            WireGui(chevronLeft, chevronRight, pedalBrake, pedalGas);
            foreach (var car in BaseCars) WireCar(Cars + car + ".prefab");
            var wallMaterial = BuildWallMaterial();
            foreach (var scene in Scenes) AssignWallMaterial(scene, wallMaterial);

            AssetDatabase.SaveAssets();
            Debug.Log("[QA2] done");
        }

        private static Sprite ImportIcon(string file)
        {
            var path = Icons + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError("[QA2] icon not found: " + path); return null; }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void WireGui(Sprite left, Sprite right, Sprite brake, Sprite gas)
        {
            var root = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                // car selector: chevrons instead of - and +
                IconOverCaption(Find(root.transform, "Car Down BTN"), left, 0.28f);
                IconOverCaption(Find(root.transform, "Car Up BTN"), right, 0.28f);

                // touch controls: glyphs instead of words
                IconOverCaption(Find(root.transform, "Steer Left Button"), left, 0.2f);
                IconOverCaption(Find(root.transform, "Steer Right Button"), right, 0.2f);
                IconOverCaption(Find(root.transform, "Brake/Reverse Button"), brake, 0.22f);
                IconOverCaption(Find(root.transform, "Throttle Button"), gas, 0.2f);

                BuildBackButton(root, left);

                PrefabUtility.SaveAsPrefabAsset(root, GuiPrefab);
                Debug.Log("[QA2] GUI prefab updated");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// hides the button's text caption and puts an icon image over it. the caption object is
        /// kept, only switched off, so anything still referencing it keeps its reference
        /// </summary>
        private static void IconOverCaption(Transform button, Sprite sprite, float inset)
        {
            if (button == null) { Debug.LogWarning("[QA2] a button to restyle was not found"); return; }
            if (sprite == null) return;

            foreach (var tmp in button.GetComponentsInChildren<TMP_Text>(true))
                if (tmp.transform != button) tmp.gameObject.SetActive(false);

            var icon = button.Find("Icon");
            if (icon == null)
            {
                icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).transform;
                icon.SetParent(button, false);
            }
            var rect = (RectTransform)icon;
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;   // never steal the touch from the pad underneath
            image.color = Color.white;
            icon.SetAsLastSibling();
            Debug.Log("[QA2] " + button.name + " -> icon " + sprite.name);
        }

        /// <summary>
        /// top-left of the car menu, styled like the other menu buttons, chevron plus TRACKS
        /// </summary>
        private static void BuildBackButton(GameObject root, Sprite chevron)
        {
            var free = Find(root.transform, "Free Layout");
            var menuGui = root.GetComponentInChildren<MenuGUI>(true);
            if (free == null || menuGui == null) { Debug.LogError("[QA2] Free Layout or MenuGUI missing"); return; }

            var reference = Find(root.transform, "Car Down BTN");
            var referenceImage = reference != null ? reference.GetComponent<Image>() : null;
            var referenceLabel = Find(root.transform, "Car Value TMP")?.GetComponent<TMP_Text>();

            var back = Find(free, "Back To Tracks BTN");
            if (back == null)
            {
                back = new GameObject("Back To Tracks BTN", typeof(RectTransform), typeof(Image), typeof(Button)).transform;
                back.SetParent(free, false);
            }
            var rect = (RectTransform)back;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(40f, -40f);
            rect.sizeDelta = new Vector2(300f, 96f);

            var image = back.GetComponent<Image>();
            image.sprite = referenceImage != null ? referenceImage.sprite : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = referenceImage != null ? referenceImage.color : new Color(0.16f, 0.19f, 0.24f, 0.95f);

            var button = back.GetComponent<Button>();
            button.targetGraphic = image;
            if (reference != null && reference.GetComponent<Button>() != null)
                button.colors = reference.GetComponent<Button>().colors;

            var icon = back.Find("Icon");
            if (icon == null)
            {
                icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).transform;
                icon.SetParent(back, false);
            }
            var iconRect = (RectTransform)icon;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(18f, 0f);
            iconRect.sizeDelta = new Vector2(56f, 56f);
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = chevron;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var label = back.Find("Label");
            if (label == null)
            {
                label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).transform;
                label.SetParent(back, false);
            }
            var labelRect = (RectTransform)label;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(80f, 0f);
            labelRect.offsetMax = new Vector2(-16f, 0f);
            var text = label.GetComponent<TMP_Text>();
            text.text = "TRACKS";
            if (referenceLabel != null) text.font = referenceLabel.font;
            text.fontSize = 40f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.raycastTarget = false;

            menuGui.backToTracksButton = button;
            Debug.Log("[QA2] TRACKS button wired to MenuGUI");
        }

        private static void WireCar(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("[QA2] missing " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<WallSlide>() == null) root.AddComponent<WallSlide>();
                var body = root.GetComponent<Rigidbody>();
                if (body != null) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[QA2] " + root.name + ": WallSlide, continuous dynamic collision");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static PhysicsMaterial BuildWallMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WallMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial("Wall");
                AssetDatabase.CreateAsset(material, WallMaterialPath);
            }
            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// the invisible perimeter walls and the track kit's own barrier meshes are the two things a
        /// car can scrape along; both get the slippery material
        /// </summary>
        private static void AssignWallMaterial(string scenePath, PhysicsMaterial material)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var count = 0;
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (collider.isTrigger) continue;
                var isWall = collider.gameObject.name.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase);
                var isBarrier = false;
                for (var p = collider.transform.parent; p != null && !isBarrier; p = p.parent)
                    if (p.name.Trim() == "Barriers") isBarrier = true;
                if (!isWall && !isBarrier) continue;
                collider.sharedMaterial = material;
                EditorUtility.SetDirty(collider);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[QA2] " + scene.name + ": frictionless material on " + count + " wall/barrier colliders");
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
