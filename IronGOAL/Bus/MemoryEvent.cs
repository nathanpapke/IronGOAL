namespace IronGOAL.Bus;

public readonly struct MemoryEvent
{
    public MemoryEventType Type    { get; init; }
    public MemoryArenaId   Arena   { get; init; }

    // Byte Address Within the Arena's Address Space
    // For Alloc: the address of the new allocation
    // For Free: the address being released
    // For ThresholdCrossed/ArenaReset: 0
    public int             Address { get; init; }

    // Size in Bytes of the Allocation or Free
    // For ThresholdCrossed: the current heap-used value
    // For ArenaReset: total bytes cleared
    public int             Size    { get; init; }
}
