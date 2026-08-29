using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// puts the supplied NOS bottle artwork on the nitro bottles and on the nitro button.
///
/// one texture, two jobs, so it is imported as a Sprite: that type can be handed to a UI Image AND
/// read as a texture by a material, where a Default-type texture cannot be assigned to an Image at
/// all. mip maps are switched back on afterwards, which sprites disable by default -- without them
/// the bottles shimmer badly as you approach one at speed.
///
/// the material also changes here. it was additive so an untextured panel would glow; additive
/// blending multiplies the artwork against what is behind it, which would wash the logo out and lose
/// its dark outline. straight alpha blending keeps the art looking like the art. it is also made
/// double sided, so a bottle is still visible in the moment before billboarding has turned it.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class NitroLogoSetup
    {
        private const string TexturePath = "Assets/Racing Starter Kit/RSK Assets/Textures/Nitro Logo.png";
        private const string MaterialPath = "Assets/Settings/Nitro Pickup.mat";

        [MenuItem("Tools/Racing/Apply Nitro Logo")]
        public static void Run()
        {
            var sprite = ImportAsSprite();
            if (sprite == null) { Debug.LogError("[Logo] " + TexturePath + " not found"); return; }

            StyleMaterial(sprite.texture);
            PutOnNitroButton(sprite);

            AssetDatabase.SaveAssets();
            Debug.Log("[Logo] done");
        }

        private static Sprite ImportAsSprite()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;      // sprites default this off; distant bottles need it
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();

            Debug.Log("[Logo] imported as a Sprite with alpha and mip maps");
            return AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
        }

        private static void StyleMaterial(Texture texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { Debug.LogWarning("[Logo] " + MaterialPath + " not found; run Place Nitro Pickups first"); return; }

            material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);

            // white, so the artwork's own colours come through instead of being tinted cyan
            material.color = Color.white;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            // alpha blend rather than additive, and visible from both sides
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;

            EditorUtility.SetDirty(material);
            Debug.Log("[Logo] bottle material now shows the logo, alpha blended and double sided");
        }

        /// <summary>
        /// the fire button becomes the bottle. it was a bare coloured rectangle that gave no clue
        /// what it did; the artwork says nitro without a word on it.
        /// </summary>
        private static void PutOnNitroButton(Sprite sprite)
        {
            const string guiPath = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
            var root = PrefabUtility.LoadPrefabContents(guiPath);
            try
            {
                var button = Find(root.transform, "Fire Nitro Button");
                if (button == null) { Debug.LogWarning("[Logo] Fire Nitro Button not found"); return; }

                var image = button.GetComponent<Image>();
                if (image == null) { Debug.LogWarning("[Logo] the nitro button has no Image"); return; }

                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;

                PrefabUtility.SaveAsPrefabAsset(root, guiPath);
                Debug.Log("[Logo] nitro button now uses the bottle artwork");
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
