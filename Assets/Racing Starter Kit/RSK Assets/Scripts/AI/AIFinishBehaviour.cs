using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// what a bot does once it has completed its laps. until now nothing: the AI kept lapping, so a
    /// finished bot was still traffic for a player on their last lap and still revving under the
    /// results. now the bot stops driving, brakes to a halt, stops colliding with the other cars
    /// (so a player right behind it at the line drives through, not into, it), and disappears a
    /// couple of seconds later. a restart brings it back exactly as spawned.
    ///
    /// on every car prefab; switches itself off on the player's car.
    /// </summary>
    public class AIFinishBehaviour : MonoBehaviour
    {
        [Tooltip("Seconds after the finish before the car is hidden; it brakes throughout")]
        public float hideAfterSeconds = 2.5f;
        [Tooltip("Hide sooner once the car is below this speed (mph)")]
        public float stoppedSpeed = 2f;

        public bool HasFinished { get; private set; }
        public bool IsHidden { get; private set; }

        private CarController car;
        private CarAudio engine;
        private CheckpointTracker tracker;
        private GameEvents events;
        private readonly List<Behaviour> drivers = new List<Behaviour>();
        private readonly List<(Collider mine, Collider theirs)> ignored = new List<(Collider, Collider)>();
        private Coroutine hiding;

        private void Awake()
        {
            if (GetComponent<CarUserControl>() != null) { enabled = false; return; }
            car = GetComponent<CarController>();
            engine = GetComponentInChildren<CarAudio>(true);
            tracker = GetComponentInChildren<CheckpointTracker>(true);
            var ai = GetComponent<CarAIControl>();
            if (ai != null) { drivers.Add(ai); events = ai.gameEvents; }
            foreach (var b in GetComponents<Behaviour>())
                if (b != null && b != ai && b.GetType().Name.StartsWith("AI") && b != this) drivers.Add(b);
            if (events == null)
            {
                var nitro = GetComponent<NitroSystem>();
                if (nitro != null) events = nitro.gameEvents;
            }
        }

        private void OnEnable()
        {
            if (tracker != null) tracker.Finished += Finish;
            if (events != null) events.RestartRaceEvent.AddListener(Restore);
        }

        private void OnDisable()
        {
            if (tracker != null) tracker.Finished -= Finish;
            // the restart listener stays while the car is merely hidden: it is what brings it back
            if (events != null && !IsHidden) events.RestartRaceEvent.RemoveListener(Restore);
        }

        private void OnDestroy()
        {
            if (events != null) events.RestartRaceEvent.RemoveListener(Restore);
        }

        /// <summary>the bot has completed its laps (also callable by tests)</summary>
        public void Finish()
        {
            if (HasFinished || !enabled) return;
            HasFinished = true;
            foreach (var d in drivers) if (d != null) d.enabled = false;
            IgnoreOtherCars(true);
            if (engine != null) engine.FadeEngine(0f, 1f);
            hiding = StartCoroutine(StopAndHide());
        }

        private void FixedUpdate()
        {
            if (!HasFinished || car == null) return;
            car.Move(0f, 0f, -1f, 1f);
        }

        private IEnumerator StopAndHide()
        {
            var deadline = Time.time + hideAfterSeconds;
            while (Time.time < deadline && (car == null || car.CurrentSpeed > stoppedSpeed)) yield return null;
            // the last bit, even if stopped early, so the disappearance is not on top of the player
            var linger = Mathf.Max(0f, Mathf.Min(0.75f, deadline - Time.time));
            if (linger > 0f) yield return new WaitForSeconds(linger);
            IsHidden = true;
            hiding = null;
            gameObject.SetActive(false);
        }

        /// <summary>a restart: back on the grid, driving, colliding, visible</summary>
        private void Restore()
        {
            if (!HasFinished) return;
            if (hiding != null) { StopCoroutine(hiding); hiding = null; }
            HasFinished = false;
            IsHidden = false;
            gameObject.SetActive(true);
            IgnoreOtherCars(false);
            foreach (var d in drivers) if (d != null) d.enabled = true;
            if (engine != null) engine.FadeEngine(1f, 0f);
        }

        private void IgnoreOtherCars(bool ignore)
        {
            if (ignore)
            {
                ignored.Clear();
                var mine = GetComponentsInChildren<Collider>(true);
                foreach (var other in FindObjectsByType<CarController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (other.gameObject == gameObject) continue;
                    foreach (var a in mine)
                    foreach (var b in other.GetComponentsInChildren<Collider>(true))
                    {
                        if (a.isTrigger || b.isTrigger) continue;
                        Physics.IgnoreCollision(a, b, true);
                        ignored.Add((a, b));
                    }
                }
            }
            else
            {
                foreach (var pair in ignored)
                    if (pair.mine != null && pair.theirs != null) Physics.IgnoreCollision(pair.mine, pair.theirs, false);
                ignored.Clear();
            }
        }
    }
}
