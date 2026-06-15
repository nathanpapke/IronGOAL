using System;
using System.Collections.Generic;
using System.Numerics;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class InputSystemTests
{
    // =======================================================================
    // BOOT
    // =======================================================================
    
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        RenderChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    static InputSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    /// <summary>
    /// Pushes a fresh InputFrame snapshot. Call twice in sequence (prev, then
    /// next) to exercise Pressed/Released edge detection, or once for simple
    /// Held/Analog/stick/trigger/mouse reads.
    /// </summary>
    private static void SeedFrame(
        IEnumerable<string>? down = null,
        IDictionary<string, float>? analogAxes = null,
        Vector2 leftStick = default,
        Vector2 rightStick = default,
        float leftTrigger = 0f,
        float rightTrigger = 0f,
        Vector2 mousePosition = default,
        Vector2 mouseDelta = default,
        IReadOnlyList<bool>? mouseButtons = null)
    {
        InputFrame.Update(
            new HashSet<string>(down ?? Array.Empty<string>(), StringComparer.Ordinal),
            new Dictionary<string, float>(analogAxes ?? new Dictionary<string, float>(), StringComparer.Ordinal),
            leftStick,
            rightStick,
            leftTrigger,
            rightTrigger,
            mousePosition,
            mouseDelta,
            mouseButtons ?? Array.Empty<bool>());
    }
    
    /// <summary>
    /// Clears InputFrame back to its empty baseline (nothing down, no axes,
    /// zero vectors) so the next SeedFrame's Pressed/Released diff starts
    /// from a known state.
    /// </summary>
    private static void ResetFrame() => InputFrame.Reset();
    
    // =======================================================================
    // BUTTONS — input-pressed? (rising edge)
    // =======================================================================
    
    [Fact]
    public void Pressed_ButtonNewlyHeld_ReturnsTrue()
    {
        ResetFrame();                                  // frame N:   cross up
        SeedFrame(down: new[] { "cross" });     // frame N+1: cross down
        
        object? result = "(input-pressed? 'cross)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Pressed_ButtonHeldAcrossFrames_ReturnsFalse()
    {
        SeedFrame(down: new[] { "cross" });     // frame N:   cross down
        SeedFrame(down: new[] { "cross" });     // frame N+1: still down
        
        object? result = "(input-pressed? 'cross)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Pressed_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.Pressed(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Pressed_WrongButtonNameType_ReturnsFalse()
    {
        // Button name must be a symbol/string; long should fail the guard.
        object result = InputSystem.Pressed(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // BUTTONS — input-released? (falling edge)
    // =======================================================================
    
    [Fact]
    public void Released_ButtonNewlyUp_ReturnsTrue()
    {
        SeedFrame(down: new[] { "cross" });     // frame N:   cross down
        ResetFrame();                                  // frame N+1: cross up
        
        object? result = "(input-released? 'cross)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Released_ButtonNeverHeld_ReturnsFalse()
    {
        ResetFrame();
        ResetFrame();
        
        object? result = "(input-released? 'cross)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Released_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.Released(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Released_WrongButtonNameType_ReturnsFalse()
    {
        object result = InputSystem.Released(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // BUTTONS — input-held?
    // =======================================================================
    
    [Fact]
    public void Held_ButtonCurrentlyDown_ReturnsTrue()
    {
        SeedFrame(down: new[] { "square" });
        
        object? result = "(input-held? 'square)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Held_ButtonNotDown_ReturnsFalse()
    {
        ResetFrame();
        
        object? result = "(input-held? 'square)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Held_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.Held(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Held_WrongButtonNameType_ReturnsFalse()
    {
        object result = InputSystem.Held(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // ANALOG — input-analog
    // =======================================================================
    
    [Fact]
    public void Analog_NamedAxis_ReturnsCurrentValue()
    {
        SeedFrame(analogAxes: new Dictionary<string, float> { ["left-x"] = 0.75f });
        
        var result = Assert.IsType<float>("(input-analog 'left-x)".Eval());
        Assert.Equal(0.75f, result, 5);
    }
    
    [Fact]
    public void Analog_UnknownAxis_ReturnsZero()
    {
        ResetFrame();
 
        var result = Assert.IsType<float>("(input-analog 'left-x)".Eval());
        Assert.Equal(0f, result, 5);
    }
    
    [Fact]
    public void Analog_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.Analog(Array.Empty<object>());
        Assert.IsNotType<float>(result);
    }
    
    [Fact]
    public void Analog_WrongAxisNameType_ReturnsFalse()
    {
        object result = InputSystem.Analog(new object[] { 1L });
        Assert.IsNotType<float>(result);
    }
    
    // =======================================================================
    // STICKS — input-left-stick / input-right-stick
    // =======================================================================
    
    [Fact]
    public void LeftStick_ReturnsCurrentVector2()
    {
        SeedFrame(leftStick: new Vector2(0.5f, -0.25f));
        
        var result = Assert.IsType<Vector2>(InputSystem.LeftStick(Array.Empty<object>()));
        Assert.Equal(0.5f, result.X, 5);
        Assert.Equal(-0.25f, result.Y, 5);
    }
    
    [Fact]
    public void LeftStick_NoInput_ReturnsZeroVector()
    {
        ResetFrame();
        
        var result = Assert.IsType<Vector2>(InputSystem.LeftStick(Array.Empty<object>()));
        Assert.Equal(Vector2.Zero, result);
    }
    
    [Fact]
    public void RightStick_ReturnsCurrentVector2()
    {
        SeedFrame(rightStick: new Vector2(-1f, 1f));
        
        var result = Assert.IsType<Vector2>(InputSystem.RightStick(Array.Empty<object>()));
        Assert.Equal(-1f, result.X, 5);
        Assert.Equal(1f, result.Y, 5);
    }
    
    [Fact]
    public void RightStick_NoInput_ReturnsZeroVector()
    {
        ResetFrame();
        
        var result = Assert.IsType<Vector2>(InputSystem.RightStick(Array.Empty<object>()));
        Assert.Equal(Vector2.Zero, result);
    }
    
    [Fact]
    public void SchemeSymbol_LeftStick_IsRegistered()
    {
        SeedFrame(leftStick: new Vector2(0.1f, 0.2f));
        var ex = Record.Exception(() => "(input-left-stick)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_RightStick_IsRegistered()
    {
        SeedFrame(rightStick: new Vector2(0.1f, 0.2f));
        var ex = Record.Exception(() => "(input-right-stick)".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // TRIGGERS — input-left-trigger / input-right-trigger
    // =======================================================================
    
    [Fact]
    public void LeftTrigger_ReturnsCurrentValue()
    {
        SeedFrame(leftTrigger: 0.6f);
        
        var result = Assert.IsType<float>(InputSystem.LeftTrigger(Array.Empty<object>()));
        Assert.Equal(0.6f, result, 5);
    }
    
    [Fact]
    public void LeftTrigger_AtRest_ReturnsZero()
    {
        ResetFrame();
        
        var result = Assert.IsType<float>(InputSystem.LeftTrigger(Array.Empty<object>()));
        Assert.Equal(0f, result, 5);
    }
    
    [Fact]
    public void RightTrigger_ReturnsCurrentValue()
    {
        SeedFrame(rightTrigger: 0.9f);
        
        var result = Assert.IsType<float>(InputSystem.RightTrigger(Array.Empty<object>()));
        Assert.Equal(0.9f, result, 5);
    }
    
    [Fact]
    public void RightTrigger_AtRest_ReturnsZero()
    {
        ResetFrame();
        
        var result = Assert.IsType<float>(InputSystem.RightTrigger(Array.Empty<object>()));
        Assert.Equal(0f, result, 5);
    }
    
    [Fact]
    public void SchemeSymbol_LeftTrigger_IsRegistered()
    {
        var ex = Record.Exception(() => "(input-left-trigger)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_RightTrigger_IsRegistered()
    {
        var ex = Record.Exception(() => "(input-right-trigger)".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // MOUSE — input-mouse-pos / input-mouse-delta
    // =======================================================================
    
    [Fact]
    public void MousePosition_ReturnsCurrentVector2()
    {
        SeedFrame(mousePosition: new Vector2(640f, 360f));
        
        var result = Assert.IsType<Vector2>(InputSystem.MousePosition(Array.Empty<object>()));
        Assert.Equal(640f, result.X, 3);
        Assert.Equal(360f, result.Y, 3);
    }
    
    [Fact]
    public void MouseDelta_ReturnsCurrentVector2()
    {
        SeedFrame(mouseDelta: new Vector2(3f, -2f));
        
        var result = Assert.IsType<Vector2>(InputSystem.MouseDelta(Array.Empty<object>()));
        Assert.Equal(3f, result.X, 3);
        Assert.Equal(-2f, result.Y, 3);
    }
    
    [Fact]
    public void MouseDelta_NoMovement_ReturnsZeroVector()
    {
        ResetFrame();
        
        var result = Assert.IsType<Vector2>(InputSystem.MouseDelta(Array.Empty<object>()));
        Assert.Equal(Vector2.Zero, result);
    }
    
    [Fact]
    public void SchemeSymbol_MousePosition_IsRegistered()
    {
        var ex = Record.Exception(() => "(input-mouse-pos)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_MouseDelta_IsRegistered()
    {
        var ex = Record.Exception(() => "(input-mouse-delta)".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // MOUSE — input-mouse-button?
    // =======================================================================
    
    [Fact]
    public void MouseButton_Held_ReturnsTrue()
    {
        // Index 0 (left button) held down this frame.
        SeedFrame(mouseButtons: new[] { true });
        
        object? result = "(input-mouse-button? 0)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MouseButton_NotHeld_ReturnsFalse()
    {
        ResetFrame();
        
        object? result = "(input-mouse-button? 0)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MouseButton_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.MouseButton(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MouseButton_WrongButtonIndexType_ReturnsFalse()
    {
        // Button index must be an integer; string should fail the guard.
        object result = InputSystem.MouseButton(new object[] { "left" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VIBRATION — input-set-vibration! (opcode 411, fire-and-forget command)
    // =======================================================================
    
    [Fact]
    public void SetVibration_ValidArgs_ReturnsTrue()
    {
        // Command publishes GameEventType.EntitySetState with Param1/Param2
        // packed motor strengths; succeeds regardless of host presence.
        object? result = "(input-set-vibration! 0.5 0.75)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetVibration_EmptyArgs_ReturnsFalse()
    {
        object result = InputSystem.SetVibration(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVibration_MissingRightMotor_ReturnsFalse()
    {
        // Left motor strength supplied; right motor strength is absent.
        object result = InputSystem.SetVibration(new object[] { 0.5f });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVibration_WrongMotorValueType_ReturnsFalse()
    {
        // Motor strengths must be float-compatible; string should fail the guard.
        object result = InputSystem.SetVibration(new object[] { "left", "right" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVibration_ZeroBoth_ReturnsTrue()
    {
        // Zeroing vibration is a valid command.
        object? result = "(input-set-vibration! 0.0 0.0)".Eval();
        Assert.True(IsTrue(result));
    }
}
