using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// stops the engines cutting in and out during a race.
///
/// the cause is voice stealing. every car runs Unity's four channel engine model -- accelerate and
/// decelerate at both low and high rev ranges, four AudioSources -- and every wheel carries a skid
/// source of its own. with seven cars that is 28 engine sources and 28 wheel sources against a real
/// voice budget of 32. measured mid race, 29 were already sounding with the field barely sliding, so
/// the moment enough wheels break traction Unity drops whichever voices it judges least important
/// and engines audibly vanish and return.
///
/// two changes, because raising the ceiling alone would just spend more CPU on sounds nobody can
/// pick out:
///
///   the budget goes up, giving headroom for the skid sources that come and go
///   the AI cars drop to the single channel engine model
///
/// the player keeps four channel audio, which is the one car whose engine is actually listened to.
/// the bots are heard as distant noise, and one channel each takes 18 sources out of the mix.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class AudioBudgetFix
    {
        private const int RealVoices = 64;

        private static readonly string[] AiCars =
        {
            "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/Player Car 2 (AI Variant).prefab",
            "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/Player Car 3 (AI Variant).prefab",
            "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/Player Car 4 (AI Variant).prefab",
        };

        private static readonly string[] Scenes =
        {
            "Assets/Racing_Track_Pack/Scenes/Race_Track_01.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_02.unity",
            "Assets/Racing_Track_Pack/Scenes/Race_Track_03.unity",
        };

        [MenuItem("Tools/Racing/Fix Audio Glitching")]
        public static void Run()
        {
            RaiseVoiceBudget();
            SimplifyBotEngines();
            FixShowcaseAudio();

            AssetDatabase.SaveAssets();
            Debug.Log("[Audio] done");
        }

        private static void RaiseVoiceBudget()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets.Length == 0) { Debug.LogError("[Audio] AudioManager.asset not found"); return; }

            var so = new SerializedObject(assets[0]);
            var voices = so.FindProperty("m_RealVoiceCount");
            if (voices == null) { Debug.LogError("[Audio] real voice count property not found"); return; }

            var before = voices.intValue;
            voices.intValue = RealVoices;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Audio] real voice budget " + before + " -> " + RealVoices);
        }

        /// <summary>
        /// bots drop from four engine channels to one. the player is untouched -- theirs is the
        /// engine actually being listened to, and it keeps the full model.
        /// </summary>
        private static void SimplifyBotEngines()
        {
            foreach (var path in AiCars)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var audio = root.GetComponentInChildren<CarAudio>(true);
                    if (audio == null) { Debug.LogWarning("[Audio] no CarAudio in " + path); continue; }

                    if (audio.engineSoundStyle == CarAudio.EngineAudioOptions.Simple)
                    {
                        Debug.Log("[Audio] " + root.name + " already single channel");
                        continue;
                    }

                    audio.engineSoundStyle = CarAudio.EngineAudioOptions.Simple;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("[Audio] " + root.name + ": engine audio 4 channels -> 1");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>
        /// silences the menu showcase's doppler.
        ///
        /// the showcase moves a car 188m in 1.8 seconds and, at the start of a run, places it at the
        /// far end of the path in a single frame. Unity derives a source's velocity from how far it
        /// moved since the last frame, so that placement reads as thousands of metres per second and
        /// the doppler shift that comes out of it is a screech. even during the drive itself the peak
        /// is 313 m/s against a speed of sound of 343, which is the region where the doppler equation
        /// stops behaving. a parked showroom car gains nothing from doppler, so it goes.
        /// </summary>
        private static void FixShowcaseAudio()
        {
            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var showcase = Object.FindFirstObjectByType<MenuCarShowcase>(FindObjectsInactive.Include);
                if (showcase == null) { Debug.LogWarning("[Audio] no showcase in " + scene.name); continue; }

                showcase.dopplerLevel = 0f;
                EditorUtility.SetDirty(showcase);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Audio] " + scene.name + ": showcase doppler off");
            }
        }
    }
}
