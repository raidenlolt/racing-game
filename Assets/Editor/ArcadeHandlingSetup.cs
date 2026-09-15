using UnityEditor;
using UnityEngine;

/// <summary>
/// the per-car side of the arcade handling pass. the code side (shaped player steering, yaw
/// assist) lives in CarController and ArcadeAssists; this writes the prefab values that go with it:
///   steer helper   every car's velocity follows its heading. the default car had this at 0, so it
///                  was the one car that slid instead of gripped, and it is the one most players drive
///   centre of mass lowered, so hard cornering leans rather than tips
///   assists        the retuned ArcadeAssists defaults, applied explicitly since serialised values on
///                  the prefabs would otherwise keep the old numbers
/// safe to re-run.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class ArcadeHandlingSetup
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";

        private static readonly string[] BaseCars =
        {
            "Player Car 2", "Player Car 3", "Player Car 4", "Player Car 5", "Player Car 6", "Player Car 7",
        };

        private const float SteerHelper = 0.85f;
        private static readonly Vector3 CentreOfMass = new Vector3(0f, -0.35f, 0f);

        [MenuItem("Tools/Racing/Apply Arcade Handling")]
        public static void Run()
        {
            foreach (var car in BaseCars)
                Apply(Cars + car + ".prefab");
            AssetDatabase.SaveAssets();
            Debug.Log("[Handling] done");
        }

        private static void Apply(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("[Handling] missing " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var controller = root.GetComponent<CarController>();
                if (controller != null)
                {
                    var so = new SerializedObject(controller);
                    so.FindProperty("m_SteerHelper").floatValue = SteerHelper;
                    so.FindProperty("m_CentreOfMassOffset").vector3Value = CentreOfMass;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                var assists = root.GetComponent<ArcadeAssists>();
                if (assists != null)
                {
                    assists.lateralGrip = 7f;
                    assists.gripReleaseSlip = 0.45f;
                    assists.driftSlipThreshold = 0.5f;
                    assists.maxYawRate = 110f;
                    assists.spinDamping = 9f;
                    assists.yawRateLowSpeed = 110f;
                    assists.lateralAccelBudget = 30f;
                    assists.yawAssistGain = 0.025f;
                    assists.yawAssistMax = 2.5f;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[Handling] " + root.name + ": steer helper " + SteerHelper + ", centre of mass " + CentreOfMass + ", assists retuned");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
