using System;
using System.Collections.Concurrent;
using System.Numerics;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Scheme-facing backing methods for the graphics system.
/// This class owns no graphics state - the host's renderer is authoritative
/// for the camera, active effects, and post-processing parameters.
/// </summary>
public static class GraphicsSystem
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
    // RESERVED PSEUDO-ENTITY ID
    // =======================================================================
    
    /// <summary>
    /// Reserved <c>EntityId</c> used for camera-targeted
    /// <see cref="GameEvent"/> and <see cref="RenderCommand"/> traffic, so
    /// the host can distinguish camera commands from real entity transforms
    /// without a new <see cref="RenderCommandType"/> or
    /// <see cref="GameEventType"/>.
    /// </summary>
    private const int CameraEntityId = -2;
    
    // =======================================================================
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Key   = process handle of the suspended ScriptProcess
    // Value = the answer the host wrote via DeliverQueryResponse
    //
    // A key being present (even with a null value) is the signal that the
    // answer has arrived. TryRemove retrieves and removes atomically.
 
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
 
    /// <summary>
    /// Called by <see cref="Host.AnswerEntityQuery"/> to deposit the host's
    /// answer for a suspended process. Writing the key makes the
    /// process-thread predicate return true, waking the process on the next
    /// scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // TODO: Finalize against Param0-Opcodes-Proposal.md once host-side
    // manifest is confirmed.
    
    // --- Camera commands - 500-509 ---
    private const int OpCameraSetFOV   = 500;
    private const int OpCameraLookAt   = 501;
    
    // --- FX - 520-529 ---
    private const int OpFxSpawn         = 520;
    private const int OpFxSpawnAttached = 521;
    private const int OpFxStop          = 522;
    private const int OpFxSetParam      = 523;
    
    // --- Post-processing - 530-539 ---
    private const int OpSetBloom      = 530;
    private const int OpSetColorGrade = 531;
    private const int OpSetMotionBlur = 532;
    private const int OpScreenFade    = 533;
    private const int OpScreenShake   = 534;
    
    // --- Visibility - 540-549 ---
    private const int OpSetLevelVisible  = 540;
    private const int OpSetEntityVisible = 541;
    
    private const int QCameraGetPosition  = 510;
    private const int QCameraGetForward   = 511;
    private const int QWorldToScreen      = 512;
    private const int QScreenToWorldRay   = 513;
    private const int QFxSpawn            = 525; // host-issued FX handle
    private const int QFxSpawnAttached    = 526; // host-issued FX handle
    
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
    
    private static float ToFloat(object o) => o switch
    {
        double d => (float)d,
        float  f => f,
        _        => 0f,
    };
    
    /// <summary>
    /// Publishes a <see cref="DebugCommand"/> onto the debug channel, stamped
    /// with the current frame ID and game time via
    /// <see cref="GameClock"/>'s Scheme-callable accessors (no other source
    /// of timing is available to backing classes).
    /// </summary>
    private static void PublishDebug(DebugCommand cmd)
    {
        long  frameId  = GameClock.FrameCount(Array.Empty<object>()) is long fc ? fc : 0L;
        float gameTime = GameClock.TotalTime(Array.Empty<object>()) is float gt ? gt : 0f;
        _bus?.PublishDebug(cmd, frameId, gameTime);
    }
    
    /// <summary>
    /// Publishes a <see cref="GameEventType.EntityQuery"/> event and suspends
    /// the calling process until the host deposits an answer.
    /// </summary>
    private static object? Query(int entityId, int param0,
        int param1 = 0, int param2 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[GraphicsSystem] Query called outside a running process — returning null.");
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
    // CAMERA - TRANSFORM
    // =======================================================================
    
    /// <summary>
    /// Commands the host to set the camera's world position.
    /// Routed via <see cref="RenderCommandType.SetTransform"/> with
    /// <see cref="CameraEntityId"/>.
    /// <para>Scheme: <c>(camera-set-pos! pos)</c></para>
    /// </summary>
    public static object CameraSetPosition(object[] args)
    {
        if (args.Length == 0 || args[0] is not Vector3 pos)
            return "#f".Eval();
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = CameraEntityId,
            Transform = Matrix4x4.CreateTranslation(pos),
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to set the camera's world rotation.
    /// Routed via <see cref="RenderCommandType.SetTransform"/> with
    /// <see cref="CameraEntityId"/>.
    /// <para>Scheme: <c>(camera-set-rot! rot)</c></para>
    /// </summary>
    public static object CameraSetRotation(object[] args)
    {
        if (args.Length == 0 || args[0] is not Quaternion rot)
            return "#f".Eval();
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = CameraEntityId,
            Transform = Matrix4x4.CreateFromQuaternion(rot),
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to set the camera's vertical field of view.
    /// <para>Scheme: <c>(camera-set-fov! 60.0)</c></para>
    /// <para>
    /// Param0 = <see cref="OpCameraSetFOV"/>.
    /// Param1 = FOV in degrees, bit-cast to int via
    ///          <see cref="BitConverter.SingleToInt32Bits"/>.
    /// </para>
    /// </summary>
    public static object CameraSetFOV(object[] args)
    {
        if (args.Length == 0)
            return "#f".Eval();
        
        float fov = ToFloat(args[0]);
        PublishSetState(CameraEntityId, OpCameraSetFOV, Pack(fov));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to orient the camera toward a world-space target.
    ///
    /// <para>
    /// Param0 = <see cref="OpCameraLookAt"/> signals the intent on the
    /// <see cref="GameEvent"/> channel. The target position is delivered
    /// immediately afterward via a translation-only
    /// <see cref="RenderCommandType.SetTransform"/> packet, paired by
    /// <see cref="CameraEntityId"/> and opcode - the same pattern used by
    /// <c>AnimationSystem.SetIKTarget</c>.
    /// </para>
    ///
    /// <para>Scheme: <c>(camera-look-at! target)</c></para>
    /// </summary>
    public static object CameraLookAt(object[] args)
    {
        if (args.Length == 0 || args[0] is not Vector3 target)
            return "#f".Eval();
        
        PublishSetState(CameraEntityId, OpCameraLookAt);
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = CameraEntityId,
            Transform = Matrix4x4.CreateTranslation(target),
        });
        return "#t".Eval();
    }
    
    // =======================================================================
    // CAMERA - QUERIES
    // =======================================================================
    
    /// <summary>
    /// Asks the host for the camera's current world position.
    /// Suspends for one frame; returns a <see cref="Vector3"/> or <c>#f</c>.
    /// <para>Scheme: <c>(camera-get-pos)</c></para>
    /// </summary>
    public static object CameraGetPosition(object[] args)
        => Query(CameraEntityId, QCameraGetPosition) ?? "#f".Eval();
    
    /// <summary>
    /// Asks the host for the camera's current forward vector.
    /// Suspends for one frame; returns a <see cref="Vector3"/> or <c>#f</c>.
    /// <para>Scheme: <c>(camera-get-forward)</c></para>
    /// </summary>
    public static object CameraGetForward(object[] args)
        => Query(CameraEntityId, QCameraGetForward) ?? "#f".Eval();
    
    /// <summary>
    /// Asks the host to project a world-space position into screen space.
    /// Suspends for one frame; returns a <see cref="Vector2"/> or <c>#f</c>.
    ///
    /// <para>
    /// The world position cannot fit into the integer <c>Param</c> slots, so
    /// it is delivered immediately beforehand via a translation-only
    /// <see cref="RenderCommandType.SetTransform"/> packet, paired by
    /// <see cref="CameraEntityId"/> and <see cref="QWorldToScreen"/>. The
    /// host consumes the pending transform when it answers this query.
    /// </para>
    ///
    /// <para>Scheme: <c>(world->screen world-pos)</c></para>
    /// </summary>
    public static object WorldToScreen(object[] args)
    {
        if (args.Length == 0 || args[0] is not Vector3 worldPos)
            return "#f".Eval();
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = CameraEntityId,
            Transform = Matrix4x4.CreateTranslation(worldPos),
        });
        
        return Query(CameraEntityId, QWorldToScreen) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Asks the host to unproject a screen-space position into a world-space
    /// ray. Suspends for one frame; returns a two-element list
    /// <c>(origin direction)</c> of <see cref="Vector3"/> values, or
    /// <c>#f</c>.
    ///
    /// <para>
    /// Param1/Param2 carry the screen-space X/Y coordinates, bit-cast via
    /// <see cref="Pack"/>. The host is expected to deliver the ray as a
    /// two-element list response; the backing class does not decode further.
    /// </para>
    ///
    /// <para>Scheme: <c>(screen->world-ray screen-pos)</c></para>
    /// </summary>
    public static object ScreenToWorldRay(object[] args)
    {
        if (args.Length == 0 || args[0] is not Vector2 screenPos)
            return "#f".Eval();
 
        return Query(CameraEntityId, QScreenToWorldRay,
            Pack(screenPos.X), Pack(screenPos.Y)) ?? "#f".Eval();
    }
    
    // =======================================================================
    // VISUAL EFFECTS
    // =======================================================================
    
    /// <summary>
    /// Requests the host to spawn a visual effect at a world-space position.
    /// Suspends for one frame; the host issues and returns the FX handle
    /// (unlike <c>AudioSystem</c>'s locally-issued voice handles, since the
    /// host is the sole owner of effect lifetime).
    ///
    /// <para>
    /// Param1 = effect name hash. The world position is delivered via a
    /// translation-only <see cref="RenderCommandType.SetTransform"/> packet
    /// immediately beforehand, paired by entity ID <c>-1</c> and
    /// <see cref="QFxSpawn"/>.
    /// </para>
    ///
    /// <para>Scheme: <c>(fx-spawn 'explosion-large position)</c></para>
    /// </summary>
    public static object FxSpawn(object[] args)
    {
        if (args.Length < 2 || args[0] is not string effectName || args[1] is not Vector3 position)
            return "#f".Eval();
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = -1,
            Transform = Matrix4x4.CreateTranslation(position),
        });
        
        return Query(-1, QFxSpawn, Hash(effectName)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Requests the host to spawn a visual effect attached to a named joint
    /// on an entity. Suspends for one frame; returns a host-issued FX handle
    /// or <c>#f</c>.
    ///
    /// <para>Param1 = joint name hash. EntityId = the owning entity handle.</para>
    ///
    /// <para>Scheme: <c>(fx-spawn-attached 'muzzle-flash entity 'barrel-tip)</c></para>
    /// </summary>
    public static object FxSpawnAttached(object[] args)
    {
        if (args.Length < 3
            || args[0] is not string effectName
            || args[1] is not long handle
            || args[2] is not string jointName)
            return "#f".Eval();
        
        return Query((int)handle, QFxSpawnAttached, Hash(effectName), Hash(jointName)) ?? "#f".Eval();
    }
    
    /// <summary>
    /// Commands the host to stop a running visual effect by handle.
    /// <para>Scheme: <c>(fx-stop fx-handle)</c></para>
    /// <para>Param0 = <see cref="OpFxStop"/>. Param1 = FX handle (narrowed).</para>
    /// </summary>
    public static object FxStop(object[] args)
    {
        if (args.Length == 0 || args[0] is not long fxHandle)
            return "#f".Eval();
        
        PublishSetState(-1, OpFxStop, (int)(fxHandle & 0x7FFF_FFFF));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to set a named float parameter on a running visual
    /// effect.
    /// <para>Scheme: <c>(fx-set-param! fx-handle "intensity" 0.8)</c></para>
    /// <para>
    /// Param0 = <see cref="OpFxSetParam"/>. Param1 = FX handle (narrowed).
    /// Param2 = parameter name hash. The value is delivered as a
    /// translation-only <see cref="RenderCommandType.SetTransform"/> packet
    /// (X component carries the float), paired by entity ID <c>-1</c> and
    /// opcode, mirroring the matrix-pairing pattern used for joint overrides.
    /// </para>
    /// </summary>
    public static object FxSetParam(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long fxHandle
            || args[1] is not string paramName)
            return "#f".Eval();
        
        float value = ToFloat(args[2]);
        
        PublishSetState(-1, OpFxSetParam, (int)(fxHandle & 0x7FFF_FFFF), Hash(paramName));
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = -1,
            Transform = Matrix4x4.CreateTranslation(new Vector3(value, 0f, 0f)),
        });
        return "#t".Eval();
    }
    
    // =======================================================================
    // POST-PROCESSING
    // =======================================================================
    
    /// <summary>
    /// Commands the host to set screen-space bloom parameters.
    /// <para>Scheme: <c>(set-bloom! intensity threshold)</c></para>
    /// <para>
    /// Param0 = <see cref="OpSetBloom"/>. Param1/Param2 = intensity/threshold,
    /// bit-cast via <see cref="Pack"/>.
    /// </para>
    /// </summary>
    public static object SetBloom(object[] args)
    {
        if (args.Length < 2)
            return "#f".Eval();
        
        PublishSetState(-1, OpSetBloom, Pack(ToFloat(args[0])), Pack(ToFloat(args[1])));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to apply a named color-grading LUT with a blend
    /// weight.
    /// <para>Scheme: <c>(set-color-grade! "night" 0.5)</c></para>
    /// <para>
    /// Param0 = <see cref="OpSetColorGrade"/>. Param1 = LUT name hash.
    /// Param2 = blend weight, bit-cast via <see cref="Pack"/>.
    /// </para>
    /// </summary>
    public static object SetColorGrade(object[] args)
    {
        if (args.Length < 2 || args[0] is not string lutName)
            return "#f".Eval();
        
        PublishSetState(-1, OpSetColorGrade, Hash(lutName), Pack(ToFloat(args[1])));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to set the screen-space motion blur amount.
    /// <para>Scheme: <c>(set-motion-blur! 0.3)</c></para>
    /// <para>
    /// Param0 = <see cref="OpSetMotionBlur"/>. Param1 = amount, bit-cast via
    /// <see cref="Pack"/>.
    /// </para>
    /// </summary>
    public static object SetMotionBlur(object[] args)
    {
        if (args.Length == 0)
            return "#f".Eval();
        
        PublishSetState(-1, OpSetMotionBlur, Pack(ToFloat(args[0])));
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to fade the screen to/from a color over a duration.
    /// <para>Scheme: <c>(screen-fade! color duration)</c></para>
    /// <para>
    /// Param0 = <see cref="OpScreenFade"/>. Param1 = duration in seconds,
    /// bit-cast via <see cref="Pack"/>. The color is delivered via a
    /// translation-only <see cref="RenderCommandType.SetTransform"/> packet
    /// (RGB packed into the translation components), paired by entity ID
    /// <c>-1</c> and opcode.
    /// </para>
    /// </summary>
    public static object ScreenFade(object[] args)
    {
        if (args.Length < 2 || args[0] is not Vector3 color)
            return "#f".Eval();
        
        float duration = ToFloat(args[1]);
        
        PublishSetState(-1, OpScreenFade, Pack(duration));
        
        _bus?.PublishRender(new RenderCommand
        {
            Type      = RenderCommandType.SetTransform,
            EntityId  = -1,
            Transform = Matrix4x4.CreateTranslation(color),
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to apply a screen shake effect.
    /// <para>Scheme: <c>(screen-shake! intensity duration)</c></para>
    /// <para>
    /// Param0 = <see cref="OpScreenShake"/>. Param1/Param2 =
    /// intensity/duration, bit-cast via <see cref="Pack"/>.
    /// </para>
    /// </summary>
    public static object ScreenShake(object[] args)
    {
        if (args.Length < 2)
            return "#f".Eval();
        
        PublishSetState(-1, OpScreenShake, Pack(ToFloat(args[0])), Pack(ToFloat(args[1])));
        return "#t".Eval();
    }
    
    // =======================================================================
    // VISIBILITY
    // =======================================================================
    
    /// <summary>
    /// Commands the host to show or hide a named level/zone.
    /// <para>Scheme: <c>(set-level-visible! "training-room" #t)</c></para>
    /// <para>
    /// Param0 = <see cref="OpSetLevelVisible"/>. Param1 = level name hash.
    /// Param2 = 1 if visible, 0 if hidden.
    /// </para>
    /// </summary>
    public static object SetLevelVisible(object[] args)
    {
        if (args.Length < 2 || args[0] is not string levelName)
            return "#f".Eval();
        
        int visible = args[1] is bool b && b ? 1 : 0;
        PublishSetState(-1, OpSetLevelVisible, Hash(levelName), visible);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Commands the host to show or hide an entity's rendered representation
    /// without affecting its logical state.
    /// <para>Scheme: <c>(set-entity-visible! handle #f)</c></para>
    /// <para>
    /// Param0 = <see cref="OpSetEntityVisible"/>. Param1 = 1 if visible, 0 if
    /// hidden.
    /// </para>
    /// </summary>
    public static object SetEntityVisible(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle)
            return "#f".Eval();
        
        int visible = args[1] is bool b && b ? 1 : 0;
        PublishSetState((int)handle, OpSetEntityVisible, visible);
        return "#t".Eval();
    }
    
    // =======================================================================
    // DEBUG DRAW (development only)
    // =======================================================================
    
    // DebugCommand carries Type, SourceSymbol, and a formatted Message
    // string - it has no dedicated geometry fields. Geometry is therefore
    // encoded into Message as a compact, host-parseable string.
    
    /// <summary>
    /// Requests the host's debug renderer draw a line between two world-space
    /// points for a duration.
    /// <para>Scheme: <c>(debug-draw-line from to color duration)</c></para>
    /// </summary>
    public static object DebugDrawLine(object[] args)
    {
        if (args.Length < 4
            || args[0] is not Vector3 from
            || args[1] is not Vector3 to
            || args[2] is not Vector3 color)
            return "#f".Eval();
        
        float duration = ToFloat(args[3]);
        
        PublishDebug(new DebugCommand
        {
            Type         = DebugCommandType.Log,
            SourceSymbol = "debug-draw-line",
            Message      = $"line from=({from.X},{from.Y},{from.Z}) " +
                           $"to=({to.X},{to.Y},{to.Z}) " +
                           $"color=({color.X},{color.Y},{color.Z}) " +
                           $"duration={duration}",
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host's debug renderer draw a wireframe sphere at a
    /// world-space point.
    /// <para>Scheme: <c>(debug-draw-sphere center radius color)</c></para>
    /// </summary>
    public static object DebugDrawSphere(object[] args)
    {
        if (args.Length < 3
            || args[0] is not Vector3 center
            || args[2] is not Vector3 color)
            return "#f".Eval();
        
        float radius = ToFloat(args[1]);
        
        PublishDebug(new DebugCommand
        {
            Type         = DebugCommandType.Log,
            SourceSymbol = "debug-draw-sphere",
            Message      = $"sphere center=({center.X},{center.Y},{center.Z}) " +
                           $"radius={radius} " +
                           $"color=({color.X},{color.Y},{color.Z})",
        });
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host's debug renderer draw text anchored to a world-space
    /// point.
    /// <para>Scheme: <c>(debug-draw-text world-pos "text")</c></para>
    /// </summary>
    public static object DebugDrawText(object[] args)
    {
        if (args.Length < 2 || args[0] is not Vector3 worldPos || args[1] is not string text)
            return "#f".Eval();
        
        PublishDebug(new DebugCommand
        {
            Type         = DebugCommandType.Log,
            SourceSymbol = "debug-draw-text",
            Message      = $"text pos=({worldPos.X},{worldPos.Y},{worldPos.Z}) text=\"{text}\"",
        });
        return "#t".Eval();
    }
}
