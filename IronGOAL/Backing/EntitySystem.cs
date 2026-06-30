using System;
using System.Collections.Concurrent;
using System.Numerics;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Scheme-facing backing methods for the entity system.
/// This class owns no entity state - the host engine is authoritative.
///
/// <para>
/// Commands are routed to the appropriate existing channel:
/// <list type="bullet">
///   <item><description>
///     <b>Lifecycle</b> (spawn, destroy) -> <see cref="GameEvent"/> channel,
///     using the existing <see cref="GameEventType.EntitySpawn"/> and
///     <see cref="GameEventType.EntityKill"/> values.  No new event types
///     are added; <see cref="GameEventType"/> represents coarse lifecycle
///     signals, not per-frame mutation commands.
///   </description></item>
///   <item><description>
///     <b>Transform Mutations</b> (set-pos, set-rot, set-scale) ->
///     <see cref="RenderCommand"/> channel, using the existing
///     <see cref="RenderCommandType.SetTransform"/> command with a full
///     <see cref="Matrix4x4"/>.  This avoids float-packing, split-event
///     torn-write hazards, and keeps transform state co-located with the
///     renderer's frame loop where the engine already drains it.
///   </description></item>
///   <item><description>
///     <b>Logical State Changes</b> (set-prop, add-tag, bind-process) ->
///     <see cref="GameEvent"/> channel using
///     <see cref="GameEventType.EntitySetState"/>, which already exists
///     to notify the host that something about an entity changed.
///   </description></item>
///   <item><description>
///     <b>Queries</b> (get-pos, exists?, find-by-type, …) -> stub to
///     <c>#f</c>.  The engine owns the entity table; synchronous queries
///     require a future response channel not yet in the architecture.
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="Install"/> must be called by <c>Kernel</c> before
/// <c>RegisterAll()</c>, passing the <see cref="EventBus"/> instance that
/// <c>Kernel</c> owns.
/// </para>
/// </summary>
public class EntitySystem
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
    
    // =======================================================================
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Key   = process handle of the suspended ScriptProcess
    // Value = the answer the host wrote via DeliverQueryResponse
    //
    // A key being present (even with a null value) is the signal that the
    // answer has arrived.  TryRemove retrieves and removes atomically.
 
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by <see cref="Host.AnswerEntityQuery"/> to deposit the host's
    /// answer for a suspended process.  Writing the key makes the
    /// process-thread predicate return true, waking the process on the next
    /// scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    private static void PublishGameEvent(GameEventType type, int entityId = -1,
        int param0 = 0, int param1 = 0,
        int param2 = 0, int param3 = 0)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = type,
            EntityId = entityId,
            Param0   = param0,
            Param1   = param1,
            Param2   = param2,
            Param3   = param3,
        });
    
    private static void PublishTransform(int entityId, Matrix4x4 transform = default)
    {
        if (_bus is not EventBus bus) return;
        
        var cmd = new TransformCommand { EntityId = entityId, Transform = transform };
        
        if (bus.PublishTransform(cmd)) return;          // fast path: channel had room
        
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            EventBus.RecordTransformDrop();              // no context: drop and count
            return;
        }
        
        proc.SetPredicate(() => bus.PublishTransform(cmd));
        proc.YieldToScheduler();
        proc.ClearPredicate();
    }
    
    private static int Pack(float f) => BitConverter.SingleToInt32Bits(f);
    
    /// <summary>
    /// Publishes a query <see cref="GameEvent"/> and suspends the calling
    /// process until the host deposits an answer via
    /// <see cref="DeliverQueryResponse"/>.
    ///
    /// <para>
    /// The calling process handle is stamped into <c>Param3</c> of the event
    /// automatically so the host always knows which process to answer.
    /// Callers must not use <c>param3</c> for data - use <c>param1</c>,
    /// <c>param2</c>, or <c>entityId</c> for additional query arguments.
    /// </para>
    ///
    /// <para>Must be called from a running <see cref="ScriptProcess"/> thread.
    /// Returns <c>null</c> if called outside a process context.</para>
    /// </summary>
    private static object? Query(GameEventType queryEvent, int entityId = -1,
        int param0 = 0, int param1 = 0, int param2 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[EntitySystem] Query called outside a running process — returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        // Param3 carries the process handle so the host knows who to answer.
        // Narrowed to int; handles are issued by Interlocked.Increment from 1
        // so overflow into negative territory is only a concern after ~2 billion
        // spawns - safe for any practical session.
        PublishGameEvent(queryEvent, entityId, param0, param1, param2,
            param3: (int)(handle & 0x7FFF_FFFF));
        
        // Suspend until the host deposits a response for this process handle.
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        // Collect and clear the response slot.
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    // =======================================================================
    // LIFECYCLE  ->  GameEvent channel (EntitySpawn / EntityKill)
    // =======================================================================
    
    /// <summary>
    /// Requests the host to spawn an entity of the given type.
    /// Param0 = type name hash for the host's type manifest.
    /// <para>Scheme: <c>(entity-spawn type-name)</c></para>
    /// </summary>
    public static object Spawn(object[] args)
    {
        if (args.Length == 0 || args[0] is not string typeName)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntitySpawn,
            param0: typeName.GetHashCode(StringComparison.Ordinal));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to destroy an entity.
    /// <para>Scheme: <c>(entity-destroy! handle)</c></para>
    /// </summary>
    public static object Destroy(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntityKill, entityId: (int)handle);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host whether the handle refers to a live entity.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-exists? handle)</c></para>
    /// </summary>
    public static object Exists(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        object? result = Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   0);
        return result is bool b ? b : "#f".Eval();
    }
    
    // =======================================================================
    // TRANSFORM QUERIES    ->  Query channel
    // TRANSFORM MUTATIONS  ->  RenderCommand channel (SetTransform)
    //
    // Position, rotation, and scale are each expressed as a Matrix4x4 using
    // the corresponding System.Numerics factory, then published via the
    // existing RenderCommandType.SetTransform command.  This keeps all
    // transform state on the render channel where the engine already drains
    // it, avoids float-packing hacks, and prevents split-event torn writes.
    // =======================================================================
    
    /// <summary>
    /// Asks the host for the entity's world position.
    /// Suspends for one frame; returns a <see cref="Vector3"/> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-pos handle)</c></para>
    /// </summary>
    public static object GetPosition(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
 
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   10) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to move an entity to the given position.
    /// <para>Scheme: <c>(entity-set-pos! handle pos)</c></para>
    /// </summary>
    public static object SetPosition(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not Vector3 pos)
            return "#f".Eval();
        
        PublishTransform((int)handle, Matrix4x4.CreateTranslation(pos));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host for the entity's world rotation.
    /// Suspends for one frame; returns a <see cref="Quaternion"/> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-rot handle)</c></para>
    /// </summary>
    public static object GetRotation(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   11) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to set an entity's rotation.
    /// <para>Scheme: <c>(entity-set-rot! handle rot)</c></para>
    /// </summary>
    public static object SetRotation(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not Quaternion rot)
            return "#f".Eval();
        
        PublishTransform((int)handle, Matrix4x4.CreateFromQuaternion(rot));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host for the entity's world scale.
    /// Suspends for one frame; returns a <see cref="Vector3"/> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-scale handle)</c></para>
    /// </summary>
    public static object GetScale(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   12) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to set an entity's scale.
    /// <para>Scheme: <c>(entity-set-scale! handle scale)</c></para>
    /// </summary>
    public static object SetScale(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not Vector3 scale)
            return "#f".Eval();
 
        PublishTransform((int)handle, Matrix4x4.CreateScale(scale));
        return "#t".Eval();
    }
    
    // =======================================================================
    // LOGICAL STATE CHANGES  ->  GameEvent channel (EntitySetState)
    //
    // Properties, tags, and process bindings are aspects of an entity's
    // logical state, not discrete lifecycle events.  They publish via the
    // existing EntitySetState value so the host can react to state changes
    // without requiring new GameEventType members.
    // Param0 carries a hash that the host resolves from its manifest;
    // no heap allocation at the event boundary.
    // =======================================================================
    
    /// <summary>
    /// Asks the host for a named property value on an entity.
    /// Param1 = key hash.
    /// Suspends for one frame; returns the value or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-prop handle key)</c></para>
    /// </summary>
    public static object GetProperty(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string key)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   20,
            param1:   key.GetHashCode(StringComparison.Ordinal)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to set a named property on an entity.
    /// Param1 = key hash, Param2 = value hash.
    /// <para>Scheme: <c>(entity-set-prop! handle key value)</c></para>
    /// </summary>
    public static object SetProperty(object[] args)
    {
        if (args.Length < 3 || args[0] is not long handle || args[1] is not string key)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntitySetState,
            entityId: (int)handle,
            param0:   21,
            param1:   key.GetHashCode(StringComparison.Ordinal),
            param2:   args[2]?.GetHashCode() ?? 0);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host whether the entity has a named property.
    /// Param1 = key hash.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-has-prop? handle key)</c></para>
    /// </summary>
    public static object HasProperty(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string key)
            return "#f".Eval();
        
        object? result = Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   22,
            param1:   key.GetHashCode(StringComparison.Ordinal));
        return result is bool b ? b : "#f".Eval();
    }
    
    // =======================================================================
    // COMPONENTS
    // =======================================================================
    
    /// <summary>
    /// Asks the host whether the entity has a named component.
    /// Param1 = component type name hash.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-has-component? handle component-type)</c></para>
    /// </summary>
    public static object HasComponent(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string componentType)
            return "#f".Eval();
        
        object? result = Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   30,
            param1:   componentType.GetHashCode(StringComparison.Ordinal));
        return result is bool b ? b : "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for the handle of a named component on an entity.
    /// Param1 = component type name hash.
    /// Suspends for one frame; returns a <c>long</c> handle or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-component handle component-type)</c></para>
    /// </summary>
    public static object GetComponent(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string componentType)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   31,
            param1:   componentType.GetHashCode(StringComparison.Ordinal)) ?? "#f".Eval();
    }
    
    // =======================================================================
    // SPATIAL QUERIES
    // =======================================================================
    
    /// <summary>
    /// Asks the host for all entity handles of a given type.
    /// Param1 = type name hash.
    /// Suspends for one frame; returns a <c>long[]</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-find-by-type type-name)</c></para>
    /// </summary>
    public static object FindByType(object[] args)
    {
        if (args.Length == 0 || args[0] is not string typeName)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            param0: 40,
            param1: typeName.GetHashCode(StringComparison.Ordinal)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for all entity handles carrying a given tag.
    /// Param1 = tag name hash.
    /// Suspends for one frame; returns a <c>long[]</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-find-by-tag tag)</c></para>
    /// </summary>
    public static object FindByTag(object[] args)
    {
        if (args.Length == 0 || args[0] is not string tag)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            param0: 41,
            param1: tag.GetHashCode(StringComparison.Ordinal)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for all entity handles within <paramref name="radius"/>
    /// of <paramref name="center"/>.
    /// EntityId = packed radius; Param1-2 = packed X/Y of center.
    /// Param3 = process handle (stamped automatically by <see cref="Query"/>).
    /// Point Z is encoded in the upper half of Param2 as a 16-bit fixed-point
    /// offset; full precision is available if the host uses the
    /// <see cref="Vector3"/> returned by the spatial index directly.
    /// Suspends for one frame; returns a <c>long[]</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-find-in-radius center radius)</c></para>
    /// </summary>
    public static object FindInRadius(object[] args)
    {
        if (args.Length < 2 || args[0] is not Vector3 center || args[1] is not float radius)
            return "#f".Eval();
        
        // EntityId repurposed for packed radius; Param1/2 carry X/Y of center.
        // Z is packed into Param2's lower 16 bits as a signed short (÷128 scale).
        // This is a known constraint of the blittable GameEvent struct.
        // Hosts that need full Z precision should use FindByTag + server-side filter.
        int packedXY  = Pack(center.X);
        int packedYZ  = (Pack(center.Y) & 0xFFFF) | (((int)(center.Z * 128f) & 0xFFFF));
        
        return Query(GameEventType.EntityQuery,
            entityId: Pack(radius),
            param0:   42,
            param1:   packedXY,
            param2:   packedYZ) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for the nearest entity of a given type to a point.
    /// EntityId = type name hash; Param1/2 = packed X/Y of point.
    /// Z precision subject to the same constraint as <see cref="FindInRadius"/>.
    /// Suspends for one frame; returns a <c>long</c> handle or <c>#f</c>.
    /// <para>Scheme: <c>(entity-find-nearest point type-name)</c></para>
    /// </summary>
    public static object FindNearest(object[] args)
    {
        if (args.Length < 2 || args[0] is not Vector3 point || args[1] is not string typeName)
            return "#f".Eval();
        
        int packedYZ = (Pack(point.Y) & 0xFFFF) | (((int)(point.Z * 128f) & 0xFFFF)); // first 0x value was 0xFFFF_0000
        
        return Query(GameEventType.EntityQuery,
            entityId: typeName.GetHashCode(StringComparison.Ordinal),
            param0:   43,
            param1:   Pack(point.X),
            param2:   packedYZ) ?? "#f".Eval();
    }
    
    // =======================================================================
    // TAGS
    // =======================================================================
    
    /// <summary>
    /// Commands the host to add a tag to an entity.
    /// Param1 = tag name hash.
    /// <para>Scheme: <c>(entity-add-tag! handle tag)</c></para>
    /// </summary>
    public static object AddTag(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string tag)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntitySetState,
            entityId: (int)handle,
            param0:   50,
            param1:   tag.GetHashCode(StringComparison.Ordinal));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to remove a tag from an entity.
    /// Param1 = tag name hash.
    /// <para>Scheme: <c>(entity-remove-tag! handle tag)</c></para>
    /// </summary>
    public static object RemoveTag(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string tag)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntitySetState,
            entityId: (int)handle,
            param0:   51,
            param1:   tag.GetHashCode(StringComparison.Ordinal));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host whether the entity carries a given tag.
    /// Param1 = tag name hash.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-has-tag? handle tag)</c></para>
    /// </summary>
    public static object HasTag(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string tag)
            return "#f".Eval();
        
        object? result = Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   52,
            param1:   tag.GetHashCode(StringComparison.Ordinal));
        return result is bool b ? b : "#f".Eval();
    }
    
    // =======================================================================
    // PROCESS <-> ENTITY BINDING
    // =======================================================================
    
    /// <summary>
    /// Commands the host to link an entity to a process handle.
    /// Param1 = process handle (int-narrowed).
    /// <para>Scheme: <c>(entity-bind-process! entity-handle process-handle)</c></para>
    /// </summary>
    public static object BindProcess(object[] args)
    {
        if (args.Length < 2 || args[0] is not long entityHandle || args[1] is not long processHandle)
            return "#f".Eval();
        
        PublishGameEvent(GameEventType.EntitySetState,
            entityId: (int)entityHandle,
            param0:   60,
            param1:   (int)(processHandle & 0x7FFF_FFFF));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Asks the host for the process handle bound to an entity.
    /// Suspends for one frame; returns a <c>long</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-process entity-handle)</c></para>
    /// </summary>
    public static object GetProcess(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)handle,
            param0:   61) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for the entity handle bound to a process.
    /// EntityId = process handle (int-narrowed).
    /// Suspends for one frame; returns a <c>long</c> or <c>#f</c>.
    /// <para>Scheme: <c>(entity-get-entity process-handle)</c></para>
    /// </summary>
    public static object GetEntity(object[] args)
    {
        if (args.Length == 0 || args[0] is not long processHandle)
            return "#f".Eval();
        
        return Query(GameEventType.EntityQuery,
            entityId: (int)(processHandle & 0x7FFF_FFFF),
            param0:   62) ?? "#f".Eval();
    }
}
