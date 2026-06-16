using System;
using System.Numerics;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class GraphicsSystemTests
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
    
    static GraphicsSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // CAMERA COMMANDS — camera-set-pos!
    // =======================================================================
    
    [Fact]
    public void CameraSetPosition_ValidArgs_ReturnsTrue()
    {
        object? result = "(camera-set-pos! (vec3 0.0 5.0 -10.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void CameraSetPosition_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.CameraSetPosition(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CameraSetPosition_WrongArgType_ReturnsFalse()
    {
        // String where a Vector3 is required.
        object result = GraphicsSystem.CameraSetPosition(new object[] { "not-a-vec3" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // CAMERA COMMANDS — camera-set-rot!
    // =======================================================================
    
    [Fact]
    public void CameraSetRotation_ValidArgs_ReturnsTrue()
    {
        object? result = "(camera-set-rot! (quat-identity))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void CameraSetRotation_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.CameraSetRotation(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CameraSetRotation_WrongArgType_ReturnsFalse()
    {
        // Vector3 where a Quaternion is required.
        object result = GraphicsSystem.CameraSetRotation(new object[] { Vector3.Zero });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // CAMERA COMMANDS — camera-set-fov!
    // =======================================================================
    
    [Fact]
    public void CameraSetFOV_ValidArgs_ReturnsTrue()
    {
        object? result = "(camera-set-fov! 60.0)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void CameraSetFOV_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.CameraSetFOV(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CameraSetFOV_WrongArgType_ReturnsFalse()
    {
        // String where a packed float is required.
        object result = GraphicsSystem.CameraSetFOV(new object[] { "wide" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // CAMERA COMMANDS — camera-look-at!
    // =======================================================================
    
    [Fact]
    public void CameraLookAt_ValidArgs_ReturnsTrue()
    {
        object? result = "(camera-look-at! (vec3 0.0 0.0 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void CameraLookAt_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.CameraLookAt(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CameraLookAt_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.CameraLookAt(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // CAMERA QUERIES — camera-get-pos
    // =======================================================================
    
    [Fact]
    public void CameraGetPosition_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(camera-get-pos)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CameraGetPosition_IgnoresExtraArgs_StillQueries()
    {
        // No args required; extras should not throw or change the guard outcome.
        object result = GraphicsSystem.CameraGetPosition(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SchemeSymbol_CameraGetPosition_IsRegistered()
    {
        var ex = Record.Exception(() => "(camera-get-pos)".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // CAMERA QUERIES — camera-get-forward
    // =======================================================================
    
    [Fact]
    public void CameraGetForward_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(camera-get-forward)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SchemeSymbol_CameraGetForward_IsRegistered()
    {
        var ex = Record.Exception(() => "(camera-get-forward)".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // CAMERA QUERIES — world->screen
    // =======================================================================
    
    [Fact]
    public void WorldToScreen_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(world->screen (vec3 1.0 2.0 3.0))".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void WorldToScreen_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.WorldToScreen(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void WorldToScreen_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.WorldToScreen(new object[] { "not-a-vec3" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SchemeSymbol_WorldToScreen_IsRegistered()
    {
        var ex = Record.Exception(() => "(world->screen (vec3 0.0 0.0 0.0))".Eval());
        Assert.Null(ex);
    }
    
    // =======================================================================
    // CAMERA QUERIES — screen->world-ray
    // =======================================================================
    
    [Fact]
    public void ScreenToWorldRay_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(screen->world-ray (vec2 640.0 360.0))".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenToWorldRay_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenToWorldRay(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenToWorldRay_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenToWorldRay(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISUAL EFFECTS — fx-spawn
    // =======================================================================
    
    [Fact]
    public void FxSpawn_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(fx-spawn 'explosion-large (vec3 1.0 2.0 3.0))".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawn_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.FxSpawn(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawn_MissingPosition_ReturnsFalse()
    {
        // Effect name supplied but no position — guard fires.
        object result = GraphicsSystem.FxSpawn(new object[] { "explosion-large" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawn_WrongEffectNameType_ReturnsFalse()
    {
        // Long where string effect name is required.
        object result = GraphicsSystem.FxSpawn(new object[] { 7L, Vector3.Zero });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISUAL EFFECTS — fx-spawn-attached
    // =======================================================================
    
    [Fact]
    public void FxSpawnAttached_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(fx-spawn-attached 'blood-spray 1 \"neck\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawnAttached_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.FxSpawnAttached(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawnAttached_MissingJointName_ReturnsFalse()
    {
        // Effect name and entity id supplied but joint name absent.
        object result = GraphicsSystem.FxSpawnAttached(new object[] { "blood-spray", 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSpawnAttached_WrongEntityIdType_ReturnsFalse()
    {
        // String where a long entity handle is required.
        object result = GraphicsSystem.FxSpawnAttached(
            new object[] { "blood-spray", "not-a-handle", "neck" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISUAL EFFECTS — fx-stop
    // =======================================================================
    
    [Fact]
    public void FxStop_ValidHandle_ReturnsTrue()
    {
        // Fire-and-forget command; handle need not be a live FX instance.
        object? result = "(fx-stop 1)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void FxStop_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.FxStop(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxStop_WrongArgType_ReturnsFalse()
    {
        // String where a long FX handle is required.
        object result = GraphicsSystem.FxStop(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISUAL EFFECTS — fx-set-param!
    // =======================================================================
    
    [Fact]
    public void FxSetParam_ValidArgs_ReturnsTrue()
    {
        object? result = "(fx-set-param! 1 \"intensity\" 0.5)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void FxSetParam_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.FxSetParam(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSetParam_MissingValue_ReturnsFalse()
    {
        // Handle and param name supplied but no float value.
        object result = GraphicsSystem.FxSetParam(new object[] { 1L, "intensity" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void FxSetParam_WrongHandleType_ReturnsFalse()
    {
        object result = GraphicsSystem.FxSetParam(new object[] { "not-a-handle", "intensity", 0.5 });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // POST-PROCESSING — set-bloom!
    // =======================================================================
    
    [Fact]
    public void SetBloom_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-bloom! 0.8 0.6)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetBloom_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.SetBloom(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetBloom_MissingThreshold_ReturnsFalse()
    {
        object result = GraphicsSystem.SetBloom(new object[] { 0.8 });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetBloom_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.SetBloom(new object[] { "bright", "low" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // POST-PROCESSING — set-color-grade!
    // =======================================================================
    
    [Fact]
    public void SetColorGrade_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-color-grade! \"sepia-lut\" 0.4)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetColorGrade_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.SetColorGrade(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetColorGrade_MissingBlend_ReturnsFalse()
    {
        object result = GraphicsSystem.SetColorGrade(new object[] { "sepia-lut" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetColorGrade_WrongLutNameType_ReturnsFalse()
    {
        // Long where string LUT name is required.
        object result = GraphicsSystem.SetColorGrade(new object[] { 9L, 0.4 });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // POST-PROCESSING — set-motion-blur!
    // =======================================================================
    
    [Fact]
    public void SetMotionBlur_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-motion-blur! 0.3)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetMotionBlur_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.SetMotionBlur(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetMotionBlur_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.SetMotionBlur(new object[] { "blurry" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // POST-PROCESSING — screen-fade!
    // =======================================================================
    
    [Fact]
    public void ScreenFade_ValidArgs_ReturnsTrue()
    {
        object? result = "(screen-fade! (vec3 0.0 0.0 0.0) 1.5)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ScreenFade_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenFade(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenFade_MissingDuration_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenFade(new object[] { Vector3.Zero });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenFade_WrongColorType_ReturnsFalse()
    {
        // String where a Vector3 color is required.
        object result = GraphicsSystem.ScreenFade(new object[] { "black", 1.5 });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // POST-PROCESSING — screen-shake!
    // =======================================================================
    
    [Fact]
    public void ScreenShake_ValidArgs_ReturnsTrue()
    {
        object? result = "(screen-shake! 0.7 0.5)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ScreenShake_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenShake(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenShake_MissingDuration_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenShake(new object[] { 0.7 });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ScreenShake_WrongArgType_ReturnsFalse()
    {
        object result = GraphicsSystem.ScreenShake(new object[] { "strong", "long" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISIBILITY — set-level-visible!
    // =======================================================================
    
    [Fact]
    public void SetLevelVisible_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-level-visible! \"forest-zone\" #t)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetLevelVisible_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.SetLevelVisible(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetLevelVisible_MissingFlag_ReturnsFalse()
    {
        object result = GraphicsSystem.SetLevelVisible(new object[] { "forest-zone" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetLevelVisible_WrongLevelNameType_ReturnsFalse()
    {
        // Long where string level name is required.
        object result = GraphicsSystem.SetLevelVisible(new object[] { 3L, true });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VISIBILITY — set-entity-visible!
    // =======================================================================
    
    [Fact]
    public void SetEntityVisible_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-entity-visible! 1 #f)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetEntityVisible_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.SetEntityVisible(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetEntityVisible_MissingFlag_ReturnsFalse()
    {
        object result = GraphicsSystem.SetEntityVisible(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetEntityVisible_WrongEntityIdType_ReturnsFalse()
    {
        // String where a long entity handle is required.
        object result = GraphicsSystem.SetEntityVisible(new object[] { "not-a-handle", true });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // DEBUG DRAW — debug-draw-line
    // =======================================================================
    
    [Fact]
    public void DebugDrawLine_ValidArgs_ReturnsTrue()
    {
        object? result =
            "(debug-draw-line (vec3 0.0 0.0 0.0) (vec3 1.0 1.0 1.0) (vec3 1.0 0.0 0.0) 2.0)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void DebugDrawLine_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawLine(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawLine_MissingDuration_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawLine(
            new object[] { Vector3.Zero, Vector3.One, Vector3.UnitX });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawLine_WrongFromType_ReturnsFalse()
    {
        // String where a Vector3 "from" point is required.
        object result = GraphicsSystem.DebugDrawLine(
            new object[] { "origin", Vector3.One, Vector3.UnitX, 2.0 });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // DEBUG DRAW — debug-draw-sphere
    // =======================================================================
    
    [Fact]
    public void DebugDrawSphere_ValidArgs_ReturnsTrue()
    {
        object? result =
            "(debug-draw-sphere (vec3 0.0 1.0 0.0) 0.5 (vec3 0.0 1.0 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void DebugDrawSphere_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawSphere(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawSphere_MissingColor_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawSphere(new object[] { Vector3.Zero, 0.5 });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawSphere_WrongCenterType_ReturnsFalse()
    {
        // String where a Vector3 center is required (args[0] guard).
        object result = GraphicsSystem.DebugDrawSphere(
            new object[] { "origin", 0.5, Vector3.UnitY });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawSphere_WrongColorType_ReturnsFalse()
    {
        // String where a Vector3 color is required (args[2] guard) —
        // regression coverage for the corrected argument-index bug.
        object result = GraphicsSystem.DebugDrawSphere(
            new object[] { Vector3.Zero, 0.5, "green" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawSphere_RadiusAsVector3_StillSucceeds()
    {
        // ToFloat tolerates a CLR double/float at args[1]; this asserts the
        // corrected layout (center, radius, color) is in effect and the
        // method does not mistakenly expect a Vector3 at index 1.
        object? result =
            "(debug-draw-sphere (vec3 2.0 2.0 2.0) 1.25 (vec3 1.0 1.0 0.0))".Eval();
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // DEBUG DRAW — debug-draw-text
    // =======================================================================
    
    [Fact]
    public void DebugDrawText_ValidArgs_ReturnsTrue()
    {
        object? result = "(debug-draw-text (vec3 0.0 2.0 0.0) \"hello\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void DebugDrawText_EmptyArgs_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawText(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawText_MissingText_ReturnsFalse()
    {
        object result = GraphicsSystem.DebugDrawText(new object[] { Vector3.Zero });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DebugDrawText_WrongTextType_ReturnsFalse()
    {
        // Long where a string text payload is required.
        object result = GraphicsSystem.DebugDrawText(new object[] { Vector3.Zero, 42L });
        Assert.True(IsFalse(result));
    }
}
