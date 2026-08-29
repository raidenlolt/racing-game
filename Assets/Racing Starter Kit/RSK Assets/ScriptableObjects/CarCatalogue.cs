using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    [System.Serializable]
    public class CarEntry
    {
        public string displayName = "Car";
        [Tooltip("The base car prefab, used when this car is the player's")]
        public GameObject playerPrefab;
        [Tooltip("The AI variant of the same car, so the bots can also draw from the roster")]
        public GameObject aiPrefab;
    }

    /// <summary>
    /// the roster the car selector reads. holding it in an asset rather than a list on the spawner
    /// means every track shares one roster, and adding a car is an edit in one place instead of once
    /// per scene.
    ///
    /// performance figures are deliberately not stored here. they live on each prefab's CarController
    /// and are read back off it for the stat bars, so the numbers the menu shows are by construction
    /// the numbers you actually drive rather than a copy that can drift out of date.
    /// </summary>
    [CreateAssetMenu(fileName = "Car Catalogue", menuName = "Scriptable Objects/Car Catalogue")]
    public class CarCatalogue : ScriptableObject
    {
        public List<CarEntry> cars = new List<CarEntry>();

        public int Count { get { return cars.Count; } }

        public CarEntry Get(int index)
        {
            if (cars.Count == 0) return null;
            return cars[Mathf.Clamp(index, 0, cars.Count - 1)];
        }

        /// <summary>
        /// reads the driving numbers straight off the prefab. returns false when the entry has no
        /// prefab wired, so the caller can hide the stat bars rather than draw three empty ones
        /// </summary>
        public bool TryGetStats(int index, out float topSpeed, out float torque, out float steerAngle)
        {
            topSpeed = torque = steerAngle = 0f;
            var entry = Get(index);
            if (entry == null || entry.playerPrefab == null) return false;

            var car = entry.playerPrefab.GetComponent<CarController>();
            if (car == null) return false;

            topSpeed = car.m_Topspeed;
            torque = car.m_FullTorqueOverAllWheels;
            steerAngle = car.m_MaximumSteerAngle;
            return true;
        }
    }
}
