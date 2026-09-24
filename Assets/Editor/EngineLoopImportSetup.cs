using UnityEditor;
using UnityEngine;

/// <summary>
/// imports the engine loops uncompressed.
///
/// the client heard a gap in the engine while accelerating, in the WebGL build. the loops themselves
/// are clean (no silence at either end, seam within one sample step) and a listener-level recording
/// in the editor shows no dip through two gear changes to 90 mph. what differs in the browser is the
/// decoder: the kit imported these WAVs with the default compression, and a compressed loop decoded
/// by the browser carries the codec's priming samples at every loop boundary. AccelerationHigh is
/// 0.42 s and the low loops cycle every 0.4 s at full revs, so that boundary comes round several
/// times a second under acceleration, which is the stutter that was reported. PCM has no boundary
/// padding, and these clips are small (0.4 to 3.8 s, mono), so there is nothing to save by
/// compressing them. safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class EngineLoopImportSetup
    {
        private const string Folder = "Assets/Racing Starter Kit/RSK Assets/Audio/Car";

        [MenuItem("Tools/Racing/Import Engine Loops As PCM")]
        public static void Run()
        {
            var changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                var settings = importer.defaultSampleSettings;
                var already = settings.compressionFormat == AudioCompressionFormat.PCM
                              && settings.loadType == AudioClipLoadType.DecompressOnLoad
                              && !importer.ContainsSampleSettingsOverride("WebGL")
                              && !importer.ContainsSampleSettingsOverride("Android");
                if (already) continue;

                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                // platform overrides would put compression straight back on the build
                importer.ClearSampleSettingOverride("WebGL");
                importer.ClearSampleSettingOverride("Android");
                importer.ClearSampleSettingOverride("iOS");
                importer.ClearSampleSettingOverride("Standalone");
                importer.forceToMono = true;
                importer.SaveAndReimport();
                changed++;
                Debug.Log("[Engine] " + System.IO.Path.GetFileName(path) + " now PCM, decompress on load");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Engine] done, " + changed + " clip(s) reimported");
        }
    }
}
