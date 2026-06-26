namespace IronGOAL.Bus;

public enum GameEventType
{
    EntitySpawn,        // Request the host to instantiate a game object.
    EntityKill,         // Request the host to destroy a game object.
    EntitySetState,     // Notify the host that an entity changed state machines.
    EntityQuery,        // Request information from the host.
    TriggerCutscene,    // Hand control to the cinematic system.
    SetCheckpoint,      // Record a save/respawn point.
    LevelLoad,          // Request a level streaming operation.
    LevelUnload,
    PlayerControlEnabled,
    PlayerControlDisabled,
    KernelShutdown
}
