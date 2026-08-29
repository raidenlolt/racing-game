using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// spawn player and AI bots, manages race start position, and restart race pos/rotations
/// </summary>
namespace SpinMotion
{
    public enum PlayerType
    {
        Player,
        AI
    }
    
    public enum PlayerSpawnIndex
    {
        First,
        Last,
        Custom
    }

    public class PlayersSpawner : MonoBehaviour
    {
        public GameEvents gameEvents;
        public GameObject playerPrefab;
        public List<GameObject> aiCarPrefabs = new();
        public GameObject aiWaypointTrackerPrefab;
        public List<Transform> spawnPoints = new();

        [Tooltip("Pick AI cars at random, never repeating one until the whole set has been used. Otherwise cycle through the set in order.")]
        public bool randomizeAiCars = true;

        [Header("Player Spawn Settings")]
        public PlayerSpawnIndex playerSpawnIndex = PlayerSpawnIndex.First;
        public int customPlayerSpawnIndex = 0; // only used if playerSpawnIndex is Custom

        private List<(GameObject go, Vector3 spawnPos, Quaternion spawnRot)> spawnedPlayers = new();
        private List<CheckpointTracker> playersCheckpointTrackers = new();
        private List<GameObject> aiCarPrefabPool = new(); // remaining prefabs for the current random pass
        private int nextAiCarPrefabIndex = 0; // used when randomizeAiCars is off

        private void Awake()
        {
            if (spawnPoints.Count == 0)
            {
                Debug.LogError("No spawn points assigned");
            }

            if (aiCarPrefabs.Count == 0)
            {
                Debug.LogError("No AI car prefabs assigned");
            }

            gameEvents.SpawnPlayersEvent.AddListener(OnSpawnPlayers);
            gameEvents.RestartRaceEvent.AddListener(OnRestartRace);

            foreach (var spawnPoint in spawnPoints)
                if (spawnPoint.TryGetComponent<Renderer>(out var renderer)) { renderer.enabled = false; }
        }

        private void OnSpawnPlayers()
        {
            int totalSpawns = spawnPoints.Count;
            int aiCount = RaceData.AiBotsSelected;

            aiCarPrefabPool.Clear();
            nextAiCarPrefabIndex = 0;

            // determine player spawn index
            int playerSpawnIndex = 0;
            switch (this.playerSpawnIndex)
            {
                case PlayerSpawnIndex.Last:
                    playerSpawnIndex = Mathf.Clamp(aiCount, 0, totalSpawns - 1); // player spawns after all AI
                    break;
                case PlayerSpawnIndex.Custom:
                    playerSpawnIndex = Mathf.Clamp(customPlayerSpawnIndex, 0, totalSpawns - 1);
                    break;
                case PlayerSpawnIndex.First:
                default:
                    playerSpawnIndex = 0;
                    break;
            }

            for (int i = 0; i <= aiCount; i++)
            {
                if (i == 0)
                {
                    // spawn player at the determined index
                    var player = Instantiate(playerPrefab, spawnPoints[playerSpawnIndex].position, spawnPoints[playerSpawnIndex].rotation);
                    spawnedPlayers.Add((player, player.transform.position, player.transform.rotation));

                    var checkpointTracker = player.GetComponentInChildren<CheckpointTracker>();
                    checkpointTracker.SetCarRacePositionIndex(0);
                    playersCheckpointTrackers.Add(checkpointTracker);
                }
                else
                {
                    // spawn AI at their respective indexes
                    int aiSpawnIdx = (i <= playerSpawnIndex) ? i - 1 : i; // adjust AI index if it overlaps with player
                    aiSpawnIdx = Mathf.Clamp(aiSpawnIdx, 0, totalSpawns - 1);

                    var aiCarPrefab = GetNextAiCarPrefab();
                    if (aiCarPrefab == null) { continue; }

                    var aiCar = Instantiate(aiCarPrefab, spawnPoints[aiSpawnIdx].position, spawnPoints[aiSpawnIdx].rotation);
                    spawnedPlayers.Add((aiCar, aiCar.transform.position, aiCar.transform.rotation));

                    var aiTracker = Instantiate(aiWaypointTrackerPrefab).GetComponent<AIWaypointTracker>();
                    aiTracker.SetupAICarCollider(aiCar.GetComponentInChildren<AICarWaypointTrackerColliderTrigger>().GetColliderTrigger());

                    aiCar.GetComponent<CarAIControl>().SetTarget(aiTracker.transform); // replace with your car controller ai target to aim/follow

                    var checkpointTracker = aiCar.GetComponentInChildren<CheckpointTracker>();
                    checkpointTracker.SetCarRacePositionIndex(i);
                    playersCheckpointTrackers.Add(checkpointTracker);
                }
            }
            gameEvents.PlayersCheckpointTrackersAssignedEvent.Invoke(playersCheckpointTrackers);
        }

        /// <summary>
        /// returns the prefab to use for the next AI car, either random (no repeats until the set is
        /// exhausted) or cycling through the set in order
        /// </summary>
        private GameObject GetNextAiCarPrefab()
        {
            if (aiCarPrefabs.Count == 0)
            {
                Debug.LogError("No AI car prefabs assigned");
                return null;
            }

            if (!randomizeAiCars)
            {
                var prefab = aiCarPrefabs[nextAiCarPrefabIndex % aiCarPrefabs.Count];
                nextAiCarPrefabIndex++;
                return prefab;
            }

            if (aiCarPrefabPool.Count == 0)
            {
                aiCarPrefabPool.AddRange(aiCarPrefabs);
            }

            int poolIdx = Random.Range(0, aiCarPrefabPool.Count);
            var randomPrefab = aiCarPrefabPool[poolIdx];
            aiCarPrefabPool.RemoveAt(poolIdx);
            return randomPrefab;
        }

        private void OnRestartRace()
        {
            // reallocate players position and rotation to initial spawn points
            foreach (var player in spawnedPlayers)
            {
                player.go.transform.position = player.spawnPos;

                // check for main and child Rigidbodies
                Rigidbody[] rigidbodies = player.go.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in rigidbodies)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.rotation = player.spawnRot;
                }

                if (rigidbodies.Length == 0)
                {
                    player.go.transform.rotation = player.spawnRot;
                }
            }
        }
    }
}