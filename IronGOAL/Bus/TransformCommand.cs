using System.Numerics;

namespace IronGOAL.Bus;

/// <summary>
/// A single entity transform update bound for the host's frame loop.
/// Carries the entity handle (narrowed to int) and the world transform to
/// apply. There is no operation discriminator - every item on the transform
/// channel is, by construction, a transform update.
/// </summary>
public readonly struct TransformCommand
{
    public int       EntityId  { get; init; }
    public Matrix4x4 Transform { get; init; }
}
