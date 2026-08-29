using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// strips the jump pads back out of the tracks, and gives the levels an actual post-processing look.
///
/// the tracks already had the plumbing for post: a Global Post Volume in Race_Track_01, cameras with
/// renderPostProcessing on and SMAA, and PostProcessData assigned on every renderer including the
/// mobile one. what was missing is that the profile it pointed at contained no effects at all, so
/// none of that machinery was doing anything and the game rendered flat.
///
/// the stack chosen here is deliberately mobile-affordable. tonemapping, bloom, colour grading and
/// vignette are the ones that buy the most look per millisecond; motion blur is left out because it
/// is the expensive one and the camera already sells speed through FOV and shake.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class LevelPolishSetup
    {
        private const string ProfilePath = "Assets/Settings/Racing Post Profile.asset";

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        [MenuItem("Tools/Racing/Remove Jumps And Add Post Processing")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var profile = BuildProfile();

            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var removed = RemoveJumpPads();
                ApplyVolume(profile);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Polish] " + scene.name + ": removed " + removed + " jump pad(s), post volume applied");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Polish] done");
        }

        private static int RemoveJumpPads()
        {
            var pads = Object.FindObjectsByType<JumpBoostPad>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var count = pads.Length;

            // the pads live under a container this project's wiring tool created; take the whole thing
            var container = GameObject.Find("Jump Pads");
            if (container != null) Object.DestroyImmediate(container);

            // and any that were placed outside it
            foreach (var pad in Object.FindObjectsByType<JumpBoostPad>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pad != null) Object.DestroyImmediate(pad.gameObject);

            return count;
        }

        /// <summary>
        /// creates or refreshes the shared post-processing profile.
        ///
        /// values are tuned for an arcade racer read on a phone screen: enough contrast and saturation
        /// to look punchy in daylight, bloom high-threshold so only genuinely bright things glow
        /// rather than the whole road hazing over, and a light vignette to pull the eye to the centre.
        /// </summary>
        private static VolumeProfile BuildProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // rebuild from scratch so re-running cannot stack duplicates
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                AssetDatabase.RemoveObjectFromAsset(c);
                Object.DestroyImmediate(c, true);
            }

            // filmic response curve. without this the highlights clip flat and everything looks washed
            var tone = AddEffect<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            // only real highlights glow: a low threshold on a bright daylight track fogs the whole image
            var bloom = AddEffect<Bloom>(profile);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(false);   // the expensive toggle, off for mobile

            var grade = AddEffect<ColorAdjustments>(profile);
            grade.postExposure.Override(0.12f);
            grade.contrast.Override(16f);
            grade.saturation.Override(14f);

            var vignette = AddEffect<Vignette>(profile);
            vignette.intensity.Override(0.27f);
            vignette.smoothness.Override(0.42f);

            // a touch of fringing at the edges reads as speed without being noticeable head-on
            var ca = AddEffect<ChromaticAberration>(profile);
            ca.intensity.Override(0.09f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ProfilePath);
            Debug.Log("[Polish] profile rebuilt: ACES tonemapping, bloom, colour grading, vignette, chromatic aberration");
            return profile;
        }


        /// <summary>
        /// adds a volume component AND registers it as a sub-asset of the profile.
        ///
        /// VolumeProfile.Add on its own only creates the component in memory. without the
        /// AddObjectToAsset call the effect is not serialised, so the profile saves with an empty
        /// component list and the level renders exactly as flat as before, while every log line
        /// claims the effects were added.
        /// </summary>
        private static T AddEffect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>puts a global volume in the scene, or repoints the one already there</summary>
        private static void ApplyVolume(VolumeProfile profile)
        {
            var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.isGlobal);

            if (volume == null)
            {
                var go = new GameObject("Global Post Volume");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 0f;
                Debug.Log("[Polish] created a Global Post Volume");
            }
            volume.sharedProfile = profile;
            volume.weight = 1f;
            volume.gameObject.SetActive(true);
        }
    }
}
