using System.Numerics;

namespace IronGOAL.Bus;

public struct PhysicsCommand
{
    // Which operation this command represents - always check this first in your drain switch.
    public PhysicsCommandType Type     { get; init; }
    
    // Engine-owned handle.
    // Core never allocates or touches the rigid body resource itself;
    // it only echoes back the entity ID the host registered.
    public int                EntityId { get; init; }
    
    // Force / impulse / velocity vector.
    // Unused for SetKinematic, but always present in the struct because
    // every slot in the channel ring buffer must be the same size.
    public Vector3            Vector   { get; init; }
    
    // Scalar payload for operations that don't need a full vector.
    // SetKinematic: nonzero = kinematic, zero = dynamic. Unused otherwise.
    public float               Value    { get; init; }
}
