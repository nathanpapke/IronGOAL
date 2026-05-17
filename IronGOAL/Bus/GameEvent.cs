namespace IronGOAL.Bus;

public readonly struct GameEvent
{
    public GameEventType Type     { get; init; }

    // The entity this event concerns, if any. -1 when not applicable.
    public int           EntityId { get; init; }

    // Up to four integer parameters whose meaning is event-specific.
    // Kept as plain ints so the struct stays blittable with no heap allocation.
    // Example: for EntitySpawn, Param0 = prefab ID, Param1 = spawn-point ID.
    public int           Param0   { get; init; }
    public int           Param1   { get; init; }
    public int           Param2   { get; init; }
    public int           Param3   { get; init; }
}
