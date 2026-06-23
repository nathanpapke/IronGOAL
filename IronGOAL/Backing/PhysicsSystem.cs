using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;
using IronGOAL.Scripting;

namespace IronGOAL.Backing;

public static class PhysicsSystem
{
    // =======================================================================
    // BUS REFERENCE
    // =======================================================================
    
    private static EventBus? _bus;
    
    /// <summary>
    /// Called by <c>Kernel</c> after constructing its <see cref="EventBus"/>
    /// and before <c>RegisterAll()</c>.
    /// </summary>
    public static void Install(EventBus bus) => _bus = bus;
    
    // TODO: Plan all project OpCodes.
    // The values below reflect what PhysicsSystem.cs currently uses.
    // They are provisional; a full inventory pass across all backing classes
    // is required before these are locked.
    
    // --- Raycast queries (300–309) ---
    private const int OpRaycast         = 300; // origin+direction query → RaycastHit or #f
    private const int OpRaycastFiltered = 301; // with layer mask filter
    private const int OpRaycastAll      = 302; // returns RaycastHit[]
    
    // --- Overlap queries (310–319) ---
    private const int OpOverlapSphere   = 310; // returns long[] entity handles
    private const int OpOverlapBox      = 311; // returns long[] entity handles
    
    // --- Rigid body queries (320–329) ---
    private const int OpGetVelocity     = 320; // returns Vector3
    
    // --- Ground / navigation queries (330–339) ---
    private const int OpGroundProbe         = 330; // returns RaycastHit or #f
    private const int OpGetGroundHeight     = 331; // returns float Y
    private const int OpProjectOnNavmesh    = 332; // returns Vector3
    private const int OpFindPath            = 333; // returns Vector3[]
    
    // =======================================================================
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Mirrors EntitySystem / AnimationSystem.
    // Key   = process handle of the suspended ScriptProcess.
    // Value = the answer the host deposited via DeliverQueryResponse.
    // A key being present (even with null) signals answer arrival.
    // TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a query answer for a suspended process.
    /// Writing the key wakes the process on the next scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    /// <summary>
    /// Converts a float to its bit-identical int representation so it can
    /// travel in a <see cref="GameEvent"/> Param slot without precision loss.
    /// </summary>
    private static int Pack(float f) => BitConverter.SingleToInt32Bits(f);
    
    /// <summary>
    /// Converts an IronScheme numeric value to <c>float</c>.
    /// IronScheme boxes float literals as <see cref="double"/>, so both
    /// cases must be handled - see the IronScheme boxing note in the
    /// architecture doc.
    /// </summary>
    private static float AsFloat(object o) => o switch
    {
        double d => (float)d,
        float  f => f,
        _        => 0f,
    };
    
    /// <summary>
    /// Hashes a string name to an int for transport in a Param slot,
    /// matching the convention used by AnimationSystem / AudioSystem.
    /// </summary>
    private static int Hash(string s) =>
        s.GetHashCode(StringComparison.Ordinal);
    
    // -----------------------------------------------------------------------
    // PhysicsCommand publish wrapper
    // -----------------------------------------------------------------------
    // EventBus.PublishPhysics returns bool (true = accepted, false = full).
    // Fast path: TryWrite succeeds immediately - no suspension, no overhead.
    // Slow path (channel full): suspend the calling script process via the
    // same predicate/yield idiom Query() uses, so the .NET thread is never
    // blocked.  If there is no process context (host-originated call), drop
    // and record the miss via EventBus.RecordPhysicsDrop().

    private static void PublishPhysics(PhysicsCommand cmd)
    {
        if (_bus is null) return;

        // Fast path — overwhelmingly common; channel has room.
        if (_bus.PublishPhysics(cmd)) return;

        // Slow path — channel is full.
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            // No process context: drop and count, consistent with the
            // no-context policy established for all Wait-mode channels.
            EventBus.RecordPhysicsDrop();
            return;
        }
        
        // Suspend the script process (not the .NET thread) until a slot opens.
        proc.SetPredicate(() => _bus.PublishPhysics(cmd));
        proc.YieldToScheduler();
        proc.ClearPredicate();
    }
    
    // -----------------------------------------------------------------------
    // GameEvent publish helper
    // -----------------------------------------------------------------------
    
    private static void PublishQuery(int entityId, int param0,
        int param1 = 0, int param2 = 0, long processHandle = 0)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = entityId,
            Param0   = param0,
            Param1   = param1,
            Param2   = param2,
            // Param3 carries the process handle so the host knows who to
            // answer. Narrowed to int; handle overflow is only a concern
            // after ~2 billion spawns — safe for any practical session.
            Param3   = (int)(processHandle & 0x7FFF_FFFF),
        });
    
    // -----------------------------------------------------------------------
    // Suspend/query/resume
    // -----------------------------------------------------------------------
    // Publishes a query GameEvent, suspends the calling process until the
    // host deposits an answer, then retrieves and returns it.
    // Must be called from a running ScriptProcess; returns null otherwise.
    
    private static object? Query(int entityId, int param0,
        int param1 = 0, int param2 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[PhysicsSystem] Query called outside a running process - returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        PublishQuery(entityId, param0, param1, param2, handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    // =======================================================================
    // RAYCASTING
    // =======================================================================
    
    
    /// <summary>
    /// Casts a ray from <c>origin</c> in <c>direction</c> up to
    /// <c>maxDist</c> metres and returns a <see cref="RaycastHit"/> on hit
    /// or <c>#f</c> on miss.
    ///
    /// <para>
    /// The host deposits a <see cref="RaycastHit"/> (or <c>null</c> for miss)
    /// via <c>Host.AnswerEntityQuery</c>; this method returns <c>#f</c> when
    /// the deposited value is null.
    /// </para>
    ///
    /// <para>Scheme: <c>(raycast origin direction max-dist)</c></para>
    /// </summary>
    public static object Raycast(object[] args)
    {
        if (args.Length < 3
            || args[0] is not Vector3 origin
            || args[1] is not Vector3 direction
            || args[2] is not { } distArg)
            return "#f".Eval();
        
        float maxDist = AsFloat(distArg);
        
        // Signal the host that a raycast query is incoming.
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        // Deliver origin and direction as a paired SetTransform.
        // Column 0 = origin, Column 1 = direction; remaining columns unused.
        var matrix = new Matrix4x4(
            origin.X,    origin.Y,    origin.Z,    0,
            direction.X, direction.Y, direction.Z, 0,
            0, 0, 0, 0,
            0, 0, 0, 1);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = matrix,
        });
        
        PublishQuery(-1, OpRaycast,
            param1: Pack(maxDist),
            processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is RaycastHit hit ? (object)hit : "#f".Eval();
    }
    
    /// <summary>
    /// Casts a ray filtered to a named layer mask. Returns a
    /// <see cref="RaycastHit"/> on hit or <c>#f</c> on miss.
    ///
    /// <para>Scheme: <c>(raycast-filtered origin direction max-dist layer-mask)</c></para>
    /// </summary>
    public static object RaycastFiltered(object[] args)
    {
        if (args.Length < 4
            || args[0] is not Vector3 origin
            || args[1] is not Vector3 direction
            || args[2] is not { } distArg
            || args[3] is not string layerMask)
            return "#f".Eval();
        
        float maxDist = AsFloat(distArg);
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        var matrix = new Matrix4x4(
            origin.X,    origin.Y,    origin.Z,    0,
            direction.X, direction.Y, direction.Z, 0,
            0, 0, 0, 0,
            0, 0, 0, 1);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = matrix,
        });
        
        PublishQuery(-1, OpRaycastFiltered,
            param1: Pack(maxDist),
            param2: Hash(layerMask),
            processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is RaycastHit hit ? (object)hit : "#f".Eval();
    }
    
    /// <summary>
    /// Casts a ray and returns all hits as a <c>RaycastHit[]</c>, or an
    /// empty array on no hits.
    ///
    /// <para>Scheme: <c>(raycast-all origin direction max-dist layer-mask)</c></para>
    /// </summary>
    public static object RaycastAll(object[] args)
    {
        if (args.Length < 4
            || args[0] is not Vector3 origin
            || args[1] is not Vector3 direction
            || args[2] is not { } distArg
            || args[3] is not string layerMask)
            return "#f".Eval();
        
        float maxDist = AsFloat(distArg);
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        var matrix = new Matrix4x4(
            origin.X,    origin.Y,    origin.Z,    0,
            direction.X, direction.Y, direction.Z, 0,
            0, 0, 0, 0,
            0, 0, 0, 1);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = matrix,
        });
        
        PublishQuery(-1, OpRaycastAll,
            param1: Pack(maxDist),
            param2: Hash(layerMask),
            processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is RaycastHit[] hits ? (object)hits : Array.Empty<RaycastHit>();
    }
    
    // =======================================================================
    // OVERLAP QUERIES
    // =======================================================================
    
    /// <summary>
    /// Returns the entity handles of all colliders overlapping a sphere.
    ///
    /// <para>Scheme: <c>(overlap-sphere center radius layer-mask)</c></para>
    /// </summary>
    public static object OverlapSphere(object[] args)
    {
        if (args.Length < 3
            || args[0] is not Vector3 center
            || args[1] is not { } radiusArg
            || args[2] is not string layerMask)
            return "#f".Eval();
        
        float radius = AsFloat(radiusArg);
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = Matrix4x4.CreateTranslation(center),
        });
        
        PublishQuery(-1, OpOverlapSphere,
            param1: Pack(radius),
            param2: Hash(layerMask),
            processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is long[] handles ? (object)handles : Array.Empty<long>();
    }
    
    /// <summary>
    /// Returns the entity handles of all colliders overlapping an axis-aligned
    /// box defined by a center and half-extents.
    ///
    /// <para>Scheme: <c>(overlap-box center half-extents layer-mask)</c></para>
    /// </summary>
    public static object OverlapBox(object[] args)
    {
        if (args.Length < 3
            || args[0] is not Vector3 center
            || args[1] is not Vector3 halfExtents
            || args[2] is not string layerMask)
            return "#f".Eval();
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        var matrix = new Matrix4x4(
            center.X,      center.Y,      center.Z,      0,
            halfExtents.X, halfExtents.Y, halfExtents.Z, 0,
            0, 0, 0, 0,
            0, 0, 0, 1);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = matrix,
        });
        
        PublishQuery(-1, OpOverlapBox,
            param2: Hash(layerMask),
            processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is long[] handles ? (object)handles : Array.Empty<long>();
    }
    
    // =======================================================================
    // RIGID BODY COMMANDS
    // =======================================================================
    // These are fire-and-forget mutations.  They publish a PhysicsCommand to
    // the physics channel via PublishPhysics.
    // No process suspension for the command itself - only the channel-full
    // slow path may suspend the process briefly if the channel is saturated.
    
    /// <summary>
    /// Applies a continuous force to a rigid body for the current physics
    /// tick.
    ///
    /// <para>Scheme: <c>(apply-force! entity force)</c></para>
    /// </summary>
    public static object ApplyForce(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not Vector3 force)
            return "#f".Eval();
        
        PublishPhysics(new PhysicsCommand
        {
            Type     = PhysicsCommandType.ApplyForce,
            EntityId = (int)handle,
            Vector   = force,
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Applies an instantaneous impulse to a rigid body.
    ///
    /// <para>Scheme: <c>(apply-impulse! entity impulse)</c></para>
    /// </summary>
    public static object ApplyImpulse(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not Vector3 impulse)
            return "#f".Eval();
        
        PublishPhysics(new PhysicsCommand
        {
            Type     = PhysicsCommandType.ApplyImpulse,
            EntityId = (int)handle,
            Vector   = impulse,
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Overrides a rigid body's linear velocity directly.
    ///
    /// <para>Scheme: <c>(set-velocity! entity velocity)</c></para>
    /// </summary>
    public static object SetVelocity(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not Vector3 velocity)
            return "#f".Eval();
        
        PublishPhysics(new PhysicsCommand
        {
            Type     = PhysicsCommandType.SetVelocity,
            EntityId = (int)handle,
            Vector   = velocity,
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Toggles a rigid body between kinematic and dynamic simulation modes.
    ///
    /// <para>Scheme: <c>(set-kinematic! entity #t/#f)</c></para>
    /// <para>
    /// <c>Value</c> = 1.0 for kinematic, 0.0 for dynamic.
    /// </para>
    /// </summary>
    public static object SetKinematic(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle)
            return "#f".Eval();
        
        // Accept both bool and IronScheme boolean objects.
        bool kinematic = args[1] switch
        {
            bool b => b,
            _      => !Equals(args[1], "#f".Eval()),
        };
        
        PublishPhysics(new PhysicsCommand
        {
            Type     = PhysicsCommandType.SetKinematic,
            EntityId = (int)handle,
            Value    = kinematic ? 1f : 0f,
        });
        return "#t".Eval();
    }
    
    // =======================================================================
    // RIGID BODY QUERIES
    // =======================================================================
    
    /// <summary>
    /// Returns the current linear velocity of a rigid body as a
    /// <see cref="Vector3"/>, or <c>#f</c> on failure.
    ///
    /// <para>Scheme: <c>(get-velocity entity)</c></para>
    /// <para>
    /// Param0 = <see cref="OpGetVelocity"/>.
    /// </para>
    /// </summary>
    public static object GetVelocity(object[] args)
    {
        if (args.Length < 1 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query((int)handle, OpGetVelocity) ?? "#f".Eval();
    }
    
    // =======================================================================
    // GROUND / NAVIGATION
    // =======================================================================
    
    /// <summary>
    /// Fires a downward probe from <c>position</c> and returns a
    /// <see cref="RaycastHit"/> describing the first surface hit, or
    /// <c>#f</c> if nothing is below.  Implemented host-side as a downward
    /// raycast; <see cref="RaycastHit"/> is reused because a ground probe
    /// returns the same four fields (point, normal, distance, entity handle).
    ///
    /// <para>Scheme: <c>(ground-probe position)</c></para>
    /// </summary>
    public static object GroundProbe(object[] args)
    {
        if (args.Length < 1 || args[0] is not Vector3 position)
            return "#f".Eval();
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = Matrix4x4.CreateTranslation(position),
        });
        
        PublishQuery(-1, OpGroundProbe, processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is RaycastHit hit ? (object)hit : "#f".Eval();
    }
    
    /// <summary>
    /// Returns the world-space Y coordinate of the first surface directly
    /// below <c>position</c>, or <c>#f</c> if nothing is below.
    ///
    /// <para>Scheme: <c>(get-ground-height position)</c></para>
    /// </summary>
    public static object GetGroundHeight(object[] args)
    {
        if (args.Length < 1 || args[0] is not Vector3 position)
            return "#f".Eval();
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = Matrix4x4.CreateTranslation(position),
        });
        
        PublishQuery(-1, OpGetGroundHeight, processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is float f ? (object)f
            : value is double d ? (float)d
            : "#f".Eval();
    }
    
    /// <summary>
    /// Snaps a world-space point to the nearest position on the navmesh.
    /// Returns a <see cref="Vector3"/> or <c>#f</c> if the point is too
    /// far from any navmesh surface.
    ///
    /// <para>Scheme: <c>(project-on-navmesh point)</c></para>
    /// </summary>
    public static object ProjectOnNavmesh(object[] args)
    {
        if (args.Length < 1 || args[0] is not Vector3 point)
            return "#f".Eval();
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = Matrix4x4.CreateTranslation(point),
        });
        
        PublishQuery(-1, OpProjectOnNavmesh, processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value ?? "#f".Eval();
    }
    
    /// <summary>
    /// Requests a navmesh path from <c>start</c> to <c>end</c>.  Returns a
    /// <see cref="Vector3"/> array of waypoints (including start and end),
    /// or an empty array if no path exists.
    ///
    /// <para>Scheme: <c>(find-path start end)</c></para>
    /// </summary>
    public static object FindPath(object[] args)
    {
        if (args.Length < 2
            || args[0] is not Vector3 start
            || args[1] is not Vector3 end)
            return "#f".Eval();
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long handle = proc.Handle;
        
        var matrix = new Matrix4x4(
            start.X, start.Y, start.Z, 0,
            end.X,   end.Y,   end.Z,   0,
            0, 0, 0, 0,
            0, 0, 0, 1);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)(handle & 0x7FFF_FFFF),
            Transform = matrix,
        });
        
        PublishQuery(-1, OpFindPath, processHandle: handle);
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value is Vector3[] waypoints ? (object)waypoints : Array.Empty<Vector3>();
    }
}
