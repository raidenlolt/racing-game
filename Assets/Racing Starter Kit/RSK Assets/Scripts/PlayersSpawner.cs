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

        [Tooltip("Roster the menu's car selector picks from. When set, the player drives the car chosen there and playerPrefab is only the fallback.")]
        public CarCatalogue carCatalogue;

        [Tooltip("Pick AI cars at random, never repeating one until the whole set has been used. Otherwise cycle through the set in order.")]
        public bool randomizeAiCars = true;

        [Header("Player Spawn Settings")]
        public PlayerSpawnIndex playerSpawnIndex = PlayerSpawnIndex.First;
        public int customPlayerSpawnIndex = 0; // only used if playerSpawnIndex is Custom

        // cars used to be instantiated at the raw spawn point transform. two things went wrong with
        // that on the tracks whose grids were placed by hand: a point could sit inside the tarmac,
        // and when a scene had fewer points than cars the index was clamped, so the last bot was
        // instantiated inside the previous one. two overlapping body colliders are separated by
        // depenetration at up to 10 m/s, which is the "cars flying at the start" the client saw
        [Header("Placement")]
        [Tooltip("How far above a spawn point the ground probe starts. Has to clear any bump in the road under the grid.")]
        public float groundProbeHeight = 10f;
        [Tooltip("Metres above the surface a car is placed. Wheels hang about 0.1 m below the car origin, so this is a short drop, not a fall.")]
        public float groundClearance = 0.35f;
        [Tooltip("Half extents of the box used to test whether a grid slot already holds a car")]
        public Vector3 occupancyHalfExtents = new Vector3(1.0f, 0.5f, 2.2f);
        [Tooltip("If a slot is occupied the car is moved back down the grid by this much and tested again")]
        public float occupiedStepBack = 9f;
        [Tooltip("Cap on how fast physics may push a car out of any residual overlap. Unity's default of 10 m/s is a launch; this is a nudge.")]
        public float maxDepenetrationVelocity = 1f;

        private List<(GameObject go, Vector3 spawnPos, Quaternion spawnRot)> spawnedPlayers = new();
        private List<CheckpointTracker> playersCheckpointTrackers = new();
        private List<GameObject> aiCarPrefabPool = new(); // remaining prefabs for the current random pass
        private int nextAiCarPrefabIndex = 0; // used when randomizeAiCars is off
        private readonly HashSet<int> usedSlots = new();
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];
        private static readonly Collider[] Nearby = new Collider[16];

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

            var carsPerRace = RaceData.AiBotsSelected + 1;
            if (spawnPoints.Count < carsPerRace)
            {
                Debug.LogError("[Spawner] " + gameObject.scene.name + " has " + spawnPoints.Count
                               + " spawn points but a race needs " + carsPerRace
                               + ". Bots beyond the last free slot will not be spawned. "
                               + "Run Tools > Racing > Repair Spawn Grids.");
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
            usedSlots.Clear();
            var placed = 0;

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
                    Vector3 pos; Quaternion rot;
                    ResolveSpawnPose(spawnPoints[playerSpawnIndex], out pos, out rot);
                    var player = Instantiate(GetPlayerPrefab(), pos, rot);
                    SoftenDepenetration(player);
                    usedSlots.Add(playerSpawnIndex);
                    placed++;
                    spawnedPlayers.Add((player, player.transform.position, player.transform.rotation));

                    var checkpointTracker = player.GetComponentInChildren<CheckpointTracker>();
                    checkpointTracker.SetCarRacePositionIndex(0);
                    playersCheckpointTrackers.Add(checkpointTracker);
                }
                else
                {
                    // spawn AI at their respective indexes
                    int aiSpawnIdx = (i <= playerSpawnIndex) ? i - 1 : i; // adjust AI index if it overlaps with player

                    // never double-book a slot. the old code clamped the index here, which silently put
                    // the seventh car inside the sixth on any track with only six points
                    if (aiSpawnIdx >= totalSpawns || usedSlots.Contains(aiSpawnIdx))
                    {
                        Debug.LogError("[Spawner] no free spawn point for bot " + i + " (" + totalSpawns
                                       + " points in scene). Bot skipped rather than spawned inside another car.");
                        continue;
                    }

                    var aiCarPrefab = GetNextAiCarPrefab();
                    if (aiCarPrefab == null) { continue; }

                    Vector3 pos; Quaternion rot;
                    ResolveSpawnPose(spawnPoints[aiSpawnIdx], out pos, out rot);
                    var aiCar = Instantiate(aiCarPrefab, pos, rot);
                    SoftenDepenetration(aiCar);
                    usedSlots.Add(aiSpawnIdx);
                    placed++;
                    spawnedPlayers.Add((aiCar, aiCar.transform.position, aiCar.transform.rotation));

                    var aiTracker = Instantiate(aiWaypointTrackerPrefab).GetComponent<AIWaypointTracker>();
                    aiTracker.SetupAICarCollider(aiCar.GetComponentInChildren<AICarWaypointTrackerColliderTrigger>().GetColliderTrigger());

                    aiCar.GetComponent<CarAIControl>().SetTarget(aiTracker.transform); // replace with your car controller ai target to aim/follow

                    var checkpointTracker = aiCar.GetComponentInChildren<CheckpointTracker>();
                    checkpointTracker.SetCarRacePositionIndex(i);
                    playersCheckpointTrackers.Add(checkpointTracker);
                }
            }
            Debug.Log("[Spawner] placed " + placed + " of " + (aiCount + 1) + " cars on "
                      + totalSpawns + " spawn points");
            gameEvents.PlayersCheckpointTrackersAssignedEvent.Invoke(playersCheckpointTrackers);
        }

        /// <summary>
        /// where a car should actually be put for a spawn point: on the road surface under the point,
        /// upright, and not inside a car that is already there.
        ///
        /// the surface is found by a ray from well above the point, so a point that was dragged a
        /// little into the tarmac by hand still spawns a car on top of it. cars already on the grid
        /// are ignored by the ray and instead tested for with a box the size of a car body; an
        /// occupied slot moves the new car one row further back until it finds room
        /// </summary>
        private void ResolveSpawnPose(Transform point, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(0f, point.eulerAngles.y, 0f);
            position = SnapToGround(point.position);

            var forward = rotation * Vector3.forward;
            for (int attempt = 0; attempt < 4 && SlotOccupied(position, rotation); attempt++)
            {
                Debug.LogWarning("[Spawner] " + point.name + " already holds a car, stepping back "
                                 + occupiedStepBack + " m");
                position = SnapToGround(position - forward * occupiedStepBack);
            }
        }

        private Vector3 SnapToGround(Vector3 at)
        {
            var origin = at + Vector3.up * groundProbeHeight;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, groundProbeHeight * 2f,
                                                ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var found = false;
            var surface = at;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.attachedRigidbody != null) continue;   // a car, not the road
                if (hit.distance < best)
                {
                    best = hit.distance;
                    surface = hit.point;
                    found = true;
                }
            }
            if (!found) return at;
            return surface + Vector3.up * groundClearance;
        }

        private bool SlotOccupied(Vector3 position, Quaternion rotation)
        {
            // colliders instantiated this frame are only guaranteed visible to queries after a sync
            Physics.SyncTransforms();
            var centre = position + Vector3.up * (occupancyHalfExtents.y + 0.2f);
            var count = Physics.OverlapBoxNonAlloc(centre, occupancyHalfExtents, Nearby, rotation, ~0,
                                                   QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = Nearby[i];
                if (c != null && c.attachedRigidbody != null) return true;
            }
            return false;
        }

        private void SoftenDepenetration(GameObject car)
        {
            foreach (var rb in car.GetComponentsInChildren<Rigidbody>())
                rb.maxDepenetrationVelocity = maxDepenetrationVelocity;
        }

        /// <summary>
        /// the car the player drives. the menu's selection wins when a roster is wired, so a scene's
        /// own playerPrefab becomes the fallback for tracks that predate the car selector
        /// </summary>
        private GameObject GetPlayerPrefab()
        {
            if (carCatalogue != null && carCatalogue.Count > 0)
            {
                var entry = carCatalogue.Get(RaceData.CarSelected);
                if (entry != null && entry.playerPrefab != null)
                    return entry.playerPrefab;
            }
            return playerPrefab;
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
                // the cached position was already resolved against the road at spawn time, but the
                // snap is cheap and protects a restart on a scene whose colliders were rebuilt since
                player.go.transform.position = SnapToGround(player.spawnPos);

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