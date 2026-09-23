using UnityEditor;
using UnityEngine;

/// <summary>
/// wires the client's sound files (feedback/Car racing music, copied to Audio/Client) into the game:
///   car acceleration  not wired: the client withdrew it (it was first the engine loop, then a
///                     pull-away flourish); the file stays in the folder, the engine is the kit's own
///   Turbo             the nitro ignition (NitroExhaustFX), the running hiss stays procedural
///   Champion          the finish stinger (RaceFinishSequence)
///   Car passby        the car sweeping onto the menu stage (CarAudio.passbyClip, played by
///                     MenuCarShowcase)
/// nothing in the scenes changes: every clip lives on a prefab. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ClientSoundsSetup
    {
        private const string Folder = "Assets/Racing Starter Kit/RSK Assets/Audio/Client/";
        private const string GuiPrefab = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Managers and Systems/GUI.prefab";
        private const string CarsFolder = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";

        [MenuItem("Tools/Racing/Apply Client Sounds")]
        public static void Run()
        {
            var turbo = Clip("Turbo.mp3");
            var champion = Clip("Champion.mp3");
            var passby = Clip("Car passby.mp3");
            if (turbo == null || champion == null || passby == null) return;

            var cars = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CarsFolder.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var controller = root.GetComponentInChildren<CarController>(true);
                    if (controller == null) continue;

                    var audio = root.GetComponentInChildren<CarAudio>(true);
                    if (audio != null) audio.passbyClip = passby;

                    var nitro = root.GetComponentInChildren<NitroExhaustFX>(true);
                    if (nitro != null)
                    {
                        nitro.igniteClip = turbo;
                        nitro.loopClip = null;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    cars++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[Sounds] nitro and passby clips set on " + cars + " car prefabs");

            var gui = PrefabUtility.LoadPrefabContents(GuiPrefab);
            try
            {
                var finish = gui.GetComponentInChildren<RaceFinishSequence>(true);
                if (finish == null) Debug.LogWarning("[Sounds] no RaceFinishSequence in the GUI prefab");
                else
                {
                    finish.fanfareClip = champion;
                    PrefabUtility.SaveAsPrefabAsset(gui, GuiPrefab);
                    Debug.Log("[Sounds] finish stinger set");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(gui); }

            AssetDatabase.SaveAssets();
            Debug.Log("[Sounds] done");
        }

        private static AudioClip Clip(string file)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + file);
            if (clip == null) Debug.LogError("[Sounds] missing " + Folder + file);
            return clip;
        }
    }
}
