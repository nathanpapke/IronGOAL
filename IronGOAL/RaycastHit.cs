using System.Numerics;

namespace IronGOAL;

/// <summary>
/// Result of a single raycast or ground probe hit.
/// </summary>
/// <param name="Point">
/// World-space position where the ray or probe intersected a surface.
/// </param>
/// <param name="Normal">
/// World-space surface normal at the hit point.
/// </param>
/// <param name="Distance">
/// World-space distance, in meters, traveled from the ray/probe origin to
/// <see cref="Point"/>.
/// </param>
/// <param name="EntityHandle">
/// Opaque handle of the entity that was hit, in the same handle space
/// <see cref="Backing.EntitySystem"/> issues elsewhere.
/// </param>
public sealed record RaycastHit(Vector3 Point,
    Vector3 Normal,
    float   Distance,
    long    EntityHandle);
    