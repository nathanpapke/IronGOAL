namespace IronGOAL.Bus;

public enum MemoryArenaId
{
    Global,     // Long-lived Game Data - persists across levels
    Stack,      // Per-process Scratch Space - freed when a process exits
    Level,      // Level-specific Data - freed on level unload
    Debug       // Development-only Allocations - stripped in ship builds
}
