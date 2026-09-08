using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// shared helpers for the setup passes that build particle effects in code: an additive URP
/// particle material, and folder creation that the asset database recognises.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class VfxMaterials
    {
        /// <summary>
        /// an additive, unlit, transparent particle material on the URP particle shader, with the
        /// blend state written explicitly. relying on the shader GUI to derive it from _Blend only
        /// works when a material inspector has been opened, which never happens in a batch run
        /// </summary>
        public static Material AdditiveParticle(string path, string texturePath, Color tint)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            var texture = string.IsNullOrEmpty(texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }
            material.SetColor("_BaseColor", tint);
            material.color = tint;

            // surface: transparent, additive, no depth write, both sides
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// the same shader set up for ordinary alpha blending, for particles that should read as
        /// solid coloured pieces (confetti) rather than light
        /// </summary>
        public static Material AlphaBlendedParticle(string path, Color tint)
        {
            var material = AdditiveParticle(path, null, tint);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            var parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            var name = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
