using System.Numerics;

namespace IronGOAL.Bus;

public readonly struct RenderCommand
{
    // Which operation this command represents - always check this first in your drain switch.
    public RenderCommandType Type       { get; init; }

    // Engine-owned Handles
    // Core never allocates or touches these resources;
    // it only echoes back integers the host registered at load time.
    public int               MeshId     { get; init; }
    public int               MaterialId { get; init; }
    public int               EntityId   { get; init; }

    // Full 4x4 World Transform
    // Unused for Clear; always present in the struct
    // because every slot in the channel ring buffer must be the same size.
    public Matrix4x4         Transform  { get; init; }
}
