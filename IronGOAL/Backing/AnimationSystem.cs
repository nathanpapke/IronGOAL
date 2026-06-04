using System.Collections.Concurrent;
using System.Numerics;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Scheme-facing backing methods for the animation system.
/// This class owns no animation state - the host engine is authoritative.
///
/// <para>
/// Commands are routed through existing channels with no new event types or
/// command types added:
/// <list type="bullet">
///   <item><description>
///     <b>Playback mutations</b> (anim-play, anim-play-blend, anim-stop,
///     anim-pause, set-blend-param!, set-joint-override!, clear-joint-override!,
///     set-ik-target!, set-ik-weight!) —&gt; <see cref="GameEvent"/> channel via
///     <see cref="GameEventType.EntitySetState"/>.  These are logical state
///     changes on an entity's animation layer, not coarse lifecycle signals;
///     <c>EntitySetState</c> is the correct existing value for mutations that
///     do not fit the render command buffer.  <c>Param0</c> carries an opcode
///     that the host resolves from its animation manifest.
///   </description></item>
///   <item><description>
///     <b>Queries</b> (anim-current, anim-current-frame, anim-length,
///     anim-playing?, anim-blending?, get-blend-param, get-joint-transform) —&gt;
///     suspend/respond via <see cref="GameEventType.EntityQuery"/>, using the
///     same process-handle response table as <see cref="EntitySystem"/>.
///   </description></item>
///   <item><description>
///     <b>Blend tree registration</b> (define-blend-tree) —&gt; stored locally
///     in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by name
///     hash.  The definition record is also forwarded as an
///     <c>EntitySetState</c> event so the host can mirror it.
///   </description></item>
///   <item><description>
///     <b>Event callbacks</b> (anim-on-event) —&gt; stored locally keyed by
///     (entityId, eventName) and invoked by
///     <see cref="FireEvent"/> when the host reports an animation event.
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
public class AnimationSystem
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
    
    // Mirrors the pattern from EntitySystem.
    // Key   = process handle of the suspended ScriptProcess.
    // Value = the answer the host deposited via DeliverQueryResponse.
    // A key being present (even with a null value) signals answer arrival.
    // TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a query answer for a suspended process.
    /// Writing the key makes the process-thread predicate return true, waking
    /// the process on the next scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // =======================================================================
    // Param0 OPCODES (EntitySetState mutations)
    // =======================================================================
    
    // These integer constants are part of the IronGOAL–host contract.
    // The host's EntitySetState handler switches on Param0 to determine
    // which animation operation to perform.
    
    // TODO: Plan all project OpCodes.
    private const int OpPlay            = 30;
    private const int OpPlayBlend       = 31;
    private const int OpStop            = 32;
    private const int OpPause           = 33;
    private const int OpSetBlendParam   = 34;
    private const int OpSetJointOverride = 35;
    private const int OpClearJointOverride = 36;
    private const int OpSetIKTarget     = 37;
    private const int OpSetIKWeight     = 38;
    private const int OpDefineBlendTree = 39;
    private const int OpRegisterAnimEvent  = 40;
    
    // =======================================================================
    // Param0 OPCODES (EntityQuery reads)
    // =======================================================================
    
    private const int QCurrentAnim      = 50;
    private const int QCurrentFrame     = 51;
    private const int QAnimLength       = 52;
    private const int QIsPlaying        = 53;
    private const int QIsBlending       = 54;
    private const int QGetBlendParam    = 55;
    private const int QGetJointTransform = 56;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    private static void PublishSetState(int entityId,
        int param0 = 0, int param1 = 0, int param2 = 0, int param3 = 0)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntitySetState,
            EntityId = entityId,
            Param0   = param0,
            Param1   = param1,
            Param2   = param2,
            Param3   = param3,
        });
    
    private static int Hash(string s) => s.GetHashCode(StringComparison.Ordinal);
    
    private static int Pack(float f) => BitConverter.SingleToInt32Bits(f);
    
    /// <summary>
    /// Publishes an <see cref="GameEventType.EntityQuery"/> event and suspends
    /// the calling process until the host deposits an answer.
    ///
    /// <para>
    /// <c>Param3</c> is reserved for the process handle and must not be used
    /// by callers for data; use <c>param1</c> or <c>param2</c> instead.
    /// </para>
    ///
    /// <para>
    /// Must be called from a running <see cref="ScriptProcess"/> context.
    /// Returns <c>null</c> when called outside a process (scheduler not active).
    /// </para>
    /// </summary>
    private static object? Query(int entityId, int param0,
        int param1 = 0, int param2 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[AnimationSystem] Query called outside a running process — returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = entityId,
            Param0   = param0,
            Param1   = param1,
            Param2   = param2,
            Param3   = (int)(handle & 0x7FFF_FFFF),
        });
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    // =======================================================================
    // PLAYBACK
    // =======================================================================
    
    /// <summary>
    /// Requests the host to immediately play a named clip on an entity, with
    /// no blend.  Replaces any currently playing clip.
    /// <para>Scheme: <c>(anim-play entity 'idle-breathe)</c></para>
    /// <para>
    /// Param0 = <see cref="OpPlay"/>.
    /// Param1 = clip name hash.
    /// </para>
    /// </summary>
    public static object Play(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle || args[1] is not string clipName)
            return "#f".Eval();
        
        PublishSetState((int)handle, OpPlay, Hash(clipName));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to cross-fade to a named clip over a blend interval.
    /// <para>Scheme: <c>(anim-play-blend entity 'run 0.2)</c></para>
    /// <para>
    /// Param0 = <see cref="OpPlayBlend"/>.
    /// Param1 = clip name hash.
    /// Param2 = blend time in seconds, bit-cast to int via
    ///          <see cref="BitConverter.SingleToInt32Bits"/>.
    /// </para>
    /// </summary>
    public static object PlayBlend(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string clipName)
            return "#f".Eval();
        
        float blendTime = args[2] switch
        {
            double d  => (float)d,
            float  f  => f,
            _         => 0f,
        };
        
        PublishSetState((int)handle, OpPlayBlend, Hash(clipName), Pack(blendTime));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to halt all animation on an entity.
    /// <para>Scheme: <c>(anim-stop entity)</c></para>
    /// <para>Param0 = <see cref="OpStop"/>.</para>
    /// </summary>
    public static object Stop(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        PublishSetState((int)handle, OpStop);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to freeze the animation at its current frame.
    /// <para>Scheme: <c>(anim-pause entity)</c></para>
    /// <para>Param0 = <see cref="OpPause"/>.</para>
    /// </summary>
    public static object Pause(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        PublishSetState((int)handle, OpPause);
        return "#t".Eval();
    }
    
    // =======================================================================
    // QUERIES
    // =======================================================================
    
    /// <summary>
    /// Asks the host for the name of the currently playing clip on an entity.
    /// Suspends for one frame; returns the clip name string or <c>#f</c>.
    /// <para>Scheme: <c>(anim-current entity)</c></para>
    /// </summary>
    public static object CurrentAnim(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query((int)handle, QCurrentAnim) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for the current playback frame number (float) on an entity.
    /// Suspends for one frame; returns the frame number or <c>#f</c>.
    /// <para>Scheme: <c>(anim-current-frame entity)</c></para>
    /// </summary>
    public static object CurrentFrame(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        return Query((int)handle, QCurrentFrame) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host for the total duration (in frames) of a named clip.
    /// Suspends for one frame; returns the duration float or <c>#f</c>.
    /// <para>Scheme: <c>(anim-length 'run-cycle)</c></para>
    /// <para>Param1 = clip name hash.</para>
    /// </summary>
    public static object AnimLength(object[] args)
    {
        if (args.Length == 0 || args[0] is not string clipName)
            return "#f".Eval();
        
        // No entity context for a length query; use entityId = -1.
        return Query(-1, QAnimLength, Hash(clipName)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host whether the entity currently has animation playing.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(anim-playing? entity)</c></para>
    /// </summary>
    public static object IsPlaying(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        object? result = Query((int)handle, QIsPlaying);
        return result is bool b ? b : "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host whether the entity is currently mid-blend.
    /// Suspends for one frame; returns <c>#t</c> or <c>#f</c>.
    /// <para>Scheme: <c>(anim-blending? entity)</c></para>
    /// </summary>
    public static object IsBlending(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        object? result = Query((int)handle, QIsBlending);
        return result is bool b ? b : "#f".Eval();
    }
    
    // =======================================================================
    // BLEND TREES
    // =======================================================================
    
    /// <summary>
    /// Notifies the host to register a named blend tree definition.
    ///
    /// <para>
    /// The tree spec is an opaque Scheme object (typically a quoted list) that
    /// the host interprets when building its animation runtime representation.
    /// The backing class does not store the definition — the host is the sole
    /// owner.  Param1 = name hash; the host resolves it against its manifest.
    /// </para>
    ///
    /// <para>Scheme:</para>
    /// <code>
    /// (define-blend-tree 'locomotion
    ///   (blend-1d 'speed
    ///     (0.0 'idle)
    ///     (0.5 'walk)
    ///     (1.0 'run)))
    /// </code>
    ///
    /// <para>Returns the name hash as a long, or <c>#f</c> on bad args.</para>
    /// </summary>
    public static object DefineBlendTree(object[] args)
    {
        if (args.Length < 2 || args[0] is not string treeName || args[1] is null)
            return "#f".Eval();
        
        int nameHash = Hash(treeName);
        PublishSetState(-1, OpDefineBlendTree, nameHash);
        return (long)nameHash;
    }
    
    /// <summary>
    /// Writes a named float driver parameter on the blend tree active for
    /// an entity.
    ///
    /// <para>
    /// Param0 = <see cref="OpSetBlendParam"/>.
    /// Param1 = param name hash.
    /// Param2 = float value, bit-cast to int.
    /// </para>
    ///
    /// <para>Scheme: <c>(set-blend-param! entity 'speed 0.8)</c></para>
    /// </summary>
    public static object SetBlendTreeParam(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string paramName)
            return "#f".Eval();
        
        float value = args[2] switch
        {
            double d  => (float)d,
            float  f  => f,
            _         => 0f,
        };
        
        PublishSetState((int)handle, OpSetBlendParam, Hash(paramName), Pack(value));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Reads a named float driver parameter from the blend tree active for
    /// an entity.  Suspends for one frame; returns the float or <c>#f</c>.
    ///
    /// <para>Scheme: <c>(get-blend-param entity 'speed)</c></para>
    /// <para>Param1 = param name hash.</para>
    /// </summary>
    public static object GetBlendTreeParam(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not string paramName)
            return "#f".Eval();
        
        return Query((int)handle, QGetBlendParam, Hash(paramName)) ?? "#f".Eval();
    }
    
    // =======================================================================
    // JOINTS
    // =======================================================================
    
    /// <summary>
    /// Asks the host for the world-space transform of a named bone on an entity.
    /// Suspends for one frame; returns a <see cref="Matrix4x4"/> or <c>#f</c>.
    ///
    /// <para>Scheme: <c>(get-joint-transform entity 'hand-r)</c></para>
    /// <para>Param1 = joint name hash.</para>
    /// </summary>
    public static object GetJointTransform(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not string jointName)
            return "#f".Eval();
        
        return Query((int)handle, QGetJointTransform, Hash(jointName)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to apply a procedural bone override (IK result or
    /// driven transform) on a named joint.
    ///
    /// <para>
    /// Param0 = <see cref="OpSetJointOverride"/>.
    /// Param1 = joint name hash.
    /// Param2 = transform matrix encoded as a <see cref="GameEventType.EntitySetState"/>
    /// sub-payload.  Because <c>GameEvent</c> carries only four int params and
    /// a 4x4 matrix is 64 bytes, the matrix is routed as a separate
    /// <see cref="RenderCommandType.SetTransform"/> command immediately after
    /// the state-change event.  The host pairs them by entity ID and opcode.
    /// </para>
    ///
    /// <para>Scheme: <c>(set-joint-override! entity 'hand-r matrix)</c></para>
    /// </summary>
    public static object SetJointOverride(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string jointName)
            return "#f".Eval();
        
        Matrix4x4 matrix = args[2] is Matrix4x4 m ? m : Matrix4x4.Identity;
        
        // Signal the intent first so the host can expect the transform packet.
        PublishSetState((int)handle, OpSetJointOverride, Hash(jointName));
        
        // Deliver the matrix via the render channel, reusing SetTransform.
        // The host differentiates a joint override SetTransform from an entity
        // transform SetTransform by the preceding EntitySetState opcode it saw.
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)handle,
            Transform = matrix,
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Removes any procedural bone override from a named joint, returning it
    /// to the animation pose.
    ///
    /// <para>Scheme: <c>(clear-joint-override! entity 'hand-r)</c></para>
    /// <para>
    /// Param0 = <see cref="OpClearJointOverride"/>.
    /// Param1 = joint name hash.
    /// </para>
    /// </summary>
    public static object ClearJointOverride(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not string jointName)
            return "#f".Eval();
        
        PublishSetState((int)handle, OpClearJointOverride, Hash(jointName));
        return "#t".Eval();
    }
    
    // =======================================================================
    // ANIMATION EVENTS
    // =======================================================================
    
    /// <summary>
    /// Registers interest in a named animation event on an entity.
    ///
    /// <para>
    /// The backing class publishes an <see cref="GameEventType.EntitySetState"/>
    /// event (opcode <see cref="OpRegisterAnimEvent"/>, Param1 = entity handle,
    /// Param2 = event name hash) so the host knows to watch for that event in
    /// the clip's event track.
    /// </para>
    ///
    /// <para>
    /// When the host detects the animation event it delivers it to the
    /// subscribing process via <c>send-event</c> through the scheduler.  The
    /// process handles it in its normal <see cref="StateDefinition.EventProc"/>
    /// — no C# callback table or additional host entry point is required.
    /// </para>
    ///
    /// <para>Scheme:</para>
    /// <code>
    /// ;; Inside a state's event proc:
    /// (anim-on-event entity 'footstep)
    ///
    /// ;; The host calls (send-event process-handle "footstep" #f) when the
    /// ;; clip event fires; the state's event lambda receives it normally.
    /// </code>
    ///
    /// <para>Returns <c>#t</c> on success or <c>#f</c> on bad args.</para>
    /// </summary>
    public static object OnEvent(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long handle
            || args[1] is not string eventName)
            return "#f".Eval();
        
        // Param1 = entity handle (narrowed), Param2 = event name hash.
        PublishSetState(-1,
            OpRegisterAnimEvent,
            (int)(handle & 0x7FFF_FFFF),
            Hash(eventName));
 
        return "#t".Eval();
    }
    
    // =======================================================================
    // INVERSE KINEMATICS
    // =======================================================================
    
    /// <summary>
    /// Commands the host to drive an IK chain toward a world-space goal.
    ///
    /// <para>
    /// Param0 = <see cref="OpSetIKTarget"/>.
    /// Param1 = chain name hash.
    /// The world-space position is forwarded as a <see cref="RenderCommand"/>
    /// carrying a translation-only <see cref="Matrix4x4"/>, paired to this
    /// event by entity ID and opcode.
    /// </para>
    ///
    /// <para>Scheme: <c>(set-ik-target! entity 'arm-r (vec3 1.0 1.5 0.3))</c></para>
    /// </summary>
    public static object SetIKTarget(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string chainName)
            return "#f".Eval();
        
        Vector3 target = args[2] is Vector3 v ? v : Vector3.Zero;
        
        PublishSetState((int)handle, OpSetIKTarget, Hash(chainName));
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = (int)handle,
            Transform = Matrix4x4.CreateTranslation(target),
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Sets the blend weight [0, 1] of an IK chain, controlling how much
    /// the IK result overrides the base animation pose.
    ///
    /// <para>
    /// Param0 = <see cref="OpSetIKWeight"/>.
    /// Param1 = chain name hash.
    /// Param2 = weight, bit-cast to int.
    /// </para>
    ///
    /// <para>Scheme: <c>(set-ik-weight! entity 'arm-r 0.75)</c></para>
    /// </summary>
    public static object SetIKWeight(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string chainName)
            return "#f".Eval();
        
        float weight = args[2] switch
        {
            double d  => Math.Clamp((float)d, 0f, 1f),
            float  f  => Math.Clamp(f, 0f, 1f),
            _         => 0f,
        };
        
        PublishSetState((int)handle, OpSetIKWeight, Hash(chainName), Pack(weight));
        return "#t".Eval();
    }
}
