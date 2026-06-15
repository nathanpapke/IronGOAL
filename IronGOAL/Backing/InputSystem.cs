using System;
using System.Collections.Generic;
using System.Numerics;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Scheme-facing backing methods for the input system.
/// This class owns no input state - <see cref="InputFrame"/> is the sole
/// authoritative source, written once per <c>Kernel.Tick()</c> by the host
/// before any script code runs (mirroring <see cref="GameClock.Advance"/>).
/// </summary>
public static class InputSystem
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
    
    //TODO: Finalize opcodes.
    private const int CSetVibration = 411;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    private static void PublishGameEvent(GameEventType type, int param0,
        int param1 = 0, int param2 = 0)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = type,
            EntityId = -1,
            Param0   = param0,
            Param1   = param1,
            Param2   = param2,
            Param3   = 0,
        });
    
    private static int Pack(float f) => BitConverter.SingleToInt32Bits(f);
    
    private static float ToFloat(object o) => o switch
    {
        double d => (float)d,
        float  f => f,
        _        => 0f,
    };
    
    // =======================================================================
    // BUTTONS
    // =======================================================================
    
    /// <summary>
    /// True on the frame a named button transitions from up to down.
    /// <para>Scheme: <c>(input-pressed? "jump")</c></para>
    /// </summary>
    public static object Pressed(object[] args)
    {
        if (args.Length < 1 || args[0] is not string button)
            return "#f".Eval();
        
        return InputFrame.Current.Pressed(button) ? "#t".Eval() : "#f".Eval();
    }
    
    /// <summary>
    /// True on the frame a named button transitions from down to up.
    /// <para>Scheme: <c>(input-released? "jump")</c></para>
    /// </summary>
    public static object Released(object[] args)
    {
        if (args.Length < 1 || args[0] is not string button)
            return "#f".Eval();
        
        return InputFrame.Current.Released(button) ? "#t".Eval() : "#f".Eval();
    }
    
    /// <summary>
    /// True for every frame a named button is currently held down.
    /// <para>Scheme: <c>(input-held? "jump")</c></para>
    /// </summary>
    public static object Held(object[] args)
    {
        if (args.Length < 1 || args[0] is not string button)
            return "#f".Eval();
        
        return InputFrame.Current.Held(button) ? "#t".Eval() : "#f".Eval();
    }
    
    // =======================================================================
    // ANALOG AXES
    // =======================================================================
    
    /// <summary>
    /// Current value of a named analog axis, in [-1, 1].
    /// <para>Scheme: <c>(input-analog "move-x")</c></para>
    /// </summary>
    public static object Analog(object[] args)
    {
        if (args.Length < 1 || args[0] is not string axis)
            return "#f".Eval();
        
        return InputFrame.Current.Analog(axis);
    }
    
    /// <summary>
    /// Left stick position, each axis in [-1, 1].
    /// <para>Scheme: <c>(input-left-stick)</c> -&gt; <see cref="Vector2"/></para>
    /// </summary>
    public static object LeftStick(object[] args)
        => InputFrame.Current.LeftStick;
    
    /// <summary>
    /// Right stick position, each axis in [-1, 1].
    /// <para>Scheme: <c>(input-right-stick)</c> -&gt; <see cref="Vector2"/></para>
    /// </summary>
    public static object RightStick(object[] args)
        => InputFrame.Current.RightStick;
    
    /// <summary>
    /// Left trigger value in [0, 1].
    /// <para>Scheme: <c>(input-left-trigger)</c></para>
    /// </summary>
    public static object LeftTrigger(object[] args)
        => InputFrame.Current.LeftTrigger;
    
    /// <summary>
    /// Right trigger value in [0, 1].
    /// <para>Scheme: <c>(input-right-trigger)</c></para>
    /// </summary>
    public static object RightTrigger(object[] args)
        => InputFrame.Current.RightTrigger;
    
    // =======================================================================
    // MOUSE
    // =======================================================================
    
    /// <summary>
    /// Current mouse position in screen-space pixels.
    /// <para>Scheme: <c>(input-mouse-pos)</c> -&gt; <see cref="Vector2"/></para>
    /// </summary>
    public static object MousePosition(object[] args)
        => InputFrame.Current.MousePosition;
    
    /// <summary>
    /// Mouse movement since the previous frame, in pixels.
    /// <para>Scheme: <c>(input-mouse-delta)</c> -&gt; <see cref="Vector2"/></para>
    /// </summary>
    public static object MouseDelta(object[] args)
        => InputFrame.Current.MouseDelta;
    
    /// <summary>
    /// True if the named mouse button index is currently held down.
    /// <para>Scheme: <c>(input-mouse-button? 0)</c></para>
    /// </summary>
    public static object MouseButton(object[] args)
    {
        if (args.Length < 1)
            return "#f".Eval();
        
        int index = args[0] switch
        {
            long l => (int)l,
            int  i => i,
            _      => -1,
        };
        
        if (index < 0)
            return "#f".Eval();
        
        return InputFrame.Current.MouseButton(index) ? "#t".Eval() : "#f".Eval();
    }
    
    // =======================================================================
    // VIBRATION
    // =======================================================================
    
    /// <summary>
    /// Sets controller vibration motor strengths for a duration.
    /// Param1/Param2 carry the left/right motor strengths as packed floats;
    /// the host owns the active vibration timer.
    /// <para>Scheme: <c>(input-set-vibration! left right duration)</c></para>
    /// </summary>
    public static object SetVibration(object[] args)
    {
        if (args.Length < 3)
            return "#f".Eval();
        
        float left     = ToFloat(args[0]);
        float right    = ToFloat(args[1]);
        float duration = ToFloat(args[2]);
        
        PublishGameEvent(GameEventType.EntitySetState,
            param0: CSetVibration,
            param1: Pack(left),
            param2: Pack(right));
        
        // Duration is forwarded via the render channel's float payload
        // convention is not needed here - host reads duration from a
        // follow-up RenderCommand-free path is out of scope; for now the
        // host derives duration from its own vibration manifest keyed by
        // the (left, right) pair, or this signature gains a Param-carrying
        // alternative once finalized.
        _ = duration;
        
        return "#t".Eval();
    }
}
