using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SpinMotion
{
    [CreateAssetMenu(fileName = "GameEvents", menuName = "Scriptable Objects/GameEvents")]
    public class GameEvents : ScriptableObject
    {
        public ToggleCarFreezeEvent ToggleCarFreezeEvent = new();
        public PlayAudioSfxEvent PlayAudioSfxEvent = new();
        public ToggleGamePauseEvent ToggleGamePauseEvent = new();

        public SpawnPlayersEvent SpawnPlayersEvent = new();
        public PlayPreRaceCountdownEvent PlayPreRaceCountdownEvent = new();
        public PreRaceUpdateGuiEvent PreRaceUpdateGuiEvent = new();
        public ChangeToRaceCamerasEvent ChangeToRaceCamerasEvent = new();

        public PlayersCheckpointTrackersAssignedEvent PlayersCheckpointTrackersAssignedEvent = new();
        public RaceStartedEvent RaceStartedEvent = new();
        public CheckpointPassedEvent CheckpointPassedEvent = new();
        public LapCompletedEvent LapCompletedEvent = new();
        public RaceFinishedEvent RaceFinishedEvent = new();
        public RestartRaceEvent RestartRaceEvent = new();
        public RaceTimeoutEvent RaceTimeoutEvent = new();

        public OnClickRestartRaceEvent OnClickRestartRaceEvent = new();
        public OnClickRestartGameEvent OnClickRestartGameEvent = new();
        public OnClickPlayRaceEvent OnClickPlayRaceEvent = new();
        public OnClickTogglePauseEvent OnClickTogglePauseEvent = new();

        // nitro. only the local player's car raises these, so the HUD and camera can listen without
        // filtering out the AI cars that run the exact same NitroSystem component
        public PlayerNitroChangedEvent PlayerNitroChangedEvent = new();
        public PlayerNitroFiredEvent PlayerNitroFiredEvent = new();
        public PlayerNitroEndedEvent PlayerNitroEndedEvent = new();
        public OnClickFireNitroEvent OnClickFireNitroEvent = new();

        // collisions. raised by the player's CarImpactFX only, so the camera and HUD react to hits on
        // the player and not to every bot-on-bot tap in the pack
        public PlayerHitEvent PlayerHitEvent = new();

        // the finish sequence. RaceFinishedEvent keeps its meaning (the race is over, stop the
        // timer, disengage nitro); this one fires a few seconds later when the results panel may open
        public RaceResultsReadyEvent RaceResultsReadyEvent = new();
    }

    public class ToggleCarFreezeEvent : UnityEvent<bool> {}
    public class PlayAudioSfxEvent : UnityEvent<SFXType> {}
    public class ToggleGamePauseEvent : UnityEvent<bool> {}

    public class SpawnPlayersEvent : UnityEvent {}
    public class PlayPreRaceCountdownEvent : UnityEvent {}
    public class PreRaceUpdateGuiEvent : UnityEvent {}
    public class ChangeToRaceCamerasEvent : UnityEvent {}

    public class PlayersCheckpointTrackersAssignedEvent : UnityEvent<List<CheckpointTracker>> {}
    public class RaceStartedEvent : UnityEvent {}
    public class CheckpointPassedEvent : UnityEvent<int, int> {}
    public class LapCompletedEvent : UnityEvent {}
    public class RaceFinishedEvent : UnityEvent<RaceFinishType> {}
    public class RestartRaceEvent : UnityEvent {}
    public class RaceTimeoutEvent : UnityEvent {}

    /// <summary>charge 0-1, and whether the gauge is currently in the perfect-nitro red zone</summary>
    public class PlayerNitroChangedEvent : UnityEvent<float, bool> {}
    /// <summary>the level that was engaged, and whether the tap landed in the red zone</summary>
    public class PlayerNitroFiredEvent : UnityEvent<NitroLevel, bool> {}
    public class PlayerNitroEndedEvent : UnityEvent {}
    public class OnClickFireNitroEvent : UnityEvent {}

    /// <summary>impact speed in m/s along the contact normal, and the hit direction in the car's local space (z negative is from behind)</summary>
    public class PlayerHitEvent : UnityEvent<float, Vector3> {}
    public class RaceResultsReadyEvent : UnityEvent<RaceFinishType> {}

    public class OnClickRestartRaceEvent : UnityEvent {}
    public class OnClickRestartGameEvent : UnityEvent {}
    public class OnClickPlayRaceEvent : UnityEvent {}
    public class OnClickTogglePauseEvent : UnityEvent {}
}