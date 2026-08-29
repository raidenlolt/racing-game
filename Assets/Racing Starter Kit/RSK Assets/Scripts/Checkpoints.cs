using System.Collections.Generic;
using System.Linq;
using UnityEngine;
/// <summary>
/// gather all provided checkpoints on the public list to track them in the race for all cars
/// </summary>
namespace SpinMotion
{
    public class Checkpoints : MonoBehaviour
    {
        public List<Checkpoint> checkpoints = new();

        [Tooltip("Hide the checkpoint marker meshes while racing. They are 39m wide and 11m tall, so driving through one fills the screen with a translucent slab.")]
        public bool hideCheckpointMeshes = true;

        private void Start()
        {
            checkpoints.Clear();
            checkpoints.AddRange(GetComponentsInChildren<Checkpoint>().ToList());
            for (int i = 0; i < checkpoints.Count; i++)
            {
                checkpoints[i].SetCheckpointNumber(i + 1);
                checkpoints[i].gameObject.name = checkpoints[i].gameObject.name + (i + 1).ToString();
                //hide before unparenting, while the marker group is still reachable from the checkpoint
                if (hideCheckpointMeshes) HideMarker(checkpoints[i]);
                //unparent checkpoints so we can get precise transforms values for RealTimeRacePositions class
                checkpoints[i].transform.SetParent(null);
            }
            RaceData.CheckpointsCount = checkpoints.Count;
        }

        /// <summary>
        /// turns off a checkpoint's visuals while leaving its trigger working.
        ///
        /// the markers are scenery for the level designer, the same as the AI waypoint boxes and the
        /// spawn point spheres, both of which the kit already hides on load. these were the ones it
        /// missed, and because each is a 39m by 11m slab standing across the road, passing through one
        /// washes the whole view over.
        ///
        /// a checkpoint's visuals are two separate things and both have to go: the slab mesh on the
        /// trigger object itself, and a floating "Checkpoint" label which is a world space Canvas
        /// sitting alongside it rather than under it. the label draws through a CanvasRenderer, not a
        /// Renderer, so hiding meshes alone leaves it hanging over the track. the whole marker group
        /// is taken, which covers both.
        ///
        /// only Renderer and Canvas components are disabled -- never a collider, never a GameObject's
        /// active state -- so checkpoint counting is completely unaffected.
        /// </summary>
        private static void HideMarker(Checkpoint checkpoint)
        {
            // the group holds the trigger and its label side by side; fall back to the trigger alone
            // if this checkpoint was authored without a parent
            var group = checkpoint.transform.parent != null
                ? checkpoint.transform.parent
                : checkpoint.transform;

            foreach (var renderer in group.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;

            foreach (var canvas in group.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = false;
        }
    }
}