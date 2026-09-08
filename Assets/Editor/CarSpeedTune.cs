using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// the client asked for a faster player car. the numbers live on each base prefab's CarController
/// and the AI variants inherit them, so this is done as one reviewable pass rather than by hand in
/// twelve prefabs.
///
/// top speed rises about 20 percent and torque about 30 percent. torque rises more because a higher
/// ceiling that takes longer to reach reads as slower, not faster. the AI variants keep the new
/// torque and get a top speed override at 94 percent of the player's, so the player wins a straight
/// drag while rubber-banding keeps the pack close.
///
/// at 195 mph the chassis covers 1.7 m per physics step, more than the bottom collider is tall, so
/// the car rigidbodies also move to continuous speculative collision or a full-speed hit on a
/// perimeter wall can pass straight through it.
///
/// safe to re-run: it writes the same values each time.
/// </summary>
namespace SpinMotion.EditorTools
{
    public static class CarSpeedTune
    {
        private const string Cars = "Assets/Racing Starter Kit/RSK Assets/Prefabs/Player Cars/";

        /// <summary>share of the player's top speed the bots get</summary>
        private const float BotTopSpeedShare = 0.94f;

        private struct Tune
        {
            public float topSpeed;   // mph, CarController.m_SpeedType is MPH on every car
            public float torque;     // across all wheels
            public Tune(float t, float q) { topSpeed = t; torque = q; }
        }

        // previous values in the comment, for the record
        private static readonly Dictionary<string, Tune> Table = new Dictionary<string, Tune>
        {
            { "Player Car 2", new Tune(195f, 3050f) },   // was 162 / 2350, the default car
            { "Player Car 3", new Tune(160f, 3800f) },   // was 132 / 2950
            { "Player Car 4", new Tune(180f, 3500f) },   // was 150 / 2700
            { "Player Car 5", new Tune(205f, 2850f) },   // was 170 / 2200
            { "Player Car 6", new Tune(165f, 3750f) },   // was 136 / 2880
            { "Player Car 7", new Tune(170f, 3750f) },   // was 142 / 2900
        };

        [MenuItem("Tools/Racing/Tune Car Speed")]
        public static void Run()
        {
            foreach (var entry in Table)
            {
                TuneBase(Cars + entry.Key + ".prefab", entry.Value);
                TuneAiVariant(Cars + entry.Key + " (AI Variant).prefab", entry.Value);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[SpeedTune] done");
        }

        private static void TuneBase(string path, Tune tune)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[SpeedTune] missing " + path); return; }
            try
            {
                var car = root.GetComponent<CarController>();
                if (car == null) { Debug.LogWarning("[SpeedTune] no CarController on " + path); return; }

                var oldTop = car.m_Topspeed;
                var oldTorque = car.m_FullTorqueOverAllWheels;
                car.m_Topspeed = tune.topSpeed;
                car.m_FullTorqueOverAllWheels = tune.torque;

                var body = root.GetComponent<Rigidbody>();
                if (body != null)
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[SpeedTune] " + root.name + ": top " + oldTop + " -> " + tune.topSpeed
                          + " mph, torque " + oldTorque + " -> " + tune.torque);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// the variant inherits torque and collision mode from its base. only top speed is written,
        /// which the variant records as an override
        /// </summary>
        private static void TuneAiVariant(string path, Tune tune)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning("[SpeedTune] missing " + path); return; }
            try
            {
                var car = root.GetComponent<CarController>();
                if (car == null) return;
                car.m_Topspeed = Mathf.Round(tune.topSpeed * BotTopSpeedShare);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[SpeedTune] " + root.name + ": bot top speed " + car.m_Topspeed + " mph");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
