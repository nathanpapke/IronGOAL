using System;
using System.Numerics;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class AnimationSystemTests
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
    
    static AnimationSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // PLAYBACK COMMANDS — anim-play
    // =======================================================================
    
    [Fact]
    public void Play_ValidArgs_ReturnsTrue()
    {
        // Command is fire-and-forget; publish to GameEvent channel succeeds.
        object? result = "(anim-play 1 \"idle-breathe\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Play_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.Play(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Play_MissingAnimName_ReturnsFalse()
    {
        // Entity ID supplied but no clip name — guard fires.
        object result = AnimationSystem.Play(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Play_WrongEntityIdType_ReturnsFalse()
    {
        // String where long is required.
        object result = AnimationSystem.Play(new object[] { "not-a-handle", "idle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — anim-play-blend
    // =======================================================================
    
    [Fact]
    public void PlayBlend_ValidArgs_ReturnsTrue()
    {
        object? result = "(anim-play-blend 1 \"run\" 0.25)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void PlayBlend_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.PlayBlend(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void PlayBlend_MissingBlendTime_ReturnsFalse()
    {
        // Entity ID and clip name present but blend time is absent.
        object result = AnimationSystem.PlayBlend(new object[] { 1L, "run" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void PlayBlend_WrongBlendTimeType_ReturnsFalse()
    {
        // Blend time must be float-compatible; string should fail the guard.
        object result = AnimationSystem.PlayBlend(new object[] { 1L, "run", "fast" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — anim-stop
    // =======================================================================
    
    [Fact]
    public void Stop_ValidHandle_ReturnsTrue()
    {
        object? result = "(anim-stop 1)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Stop_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.Stop(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Stop_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.Stop(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — anim-pause
    // =======================================================================
    
    [Fact]
    public void Pause_ValidHandle_ReturnsTrue()
    {
        object? result = "(anim-pause 1)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Pause_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.Pause(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Pause_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.Pause(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // STATE QUERIES — anim-current
    // =======================================================================
    
    [Fact]
    public void Current_CalledOutsideProcess_ReturnsFalse()
    {
        // Query suspends via the response table; no process context → #f.
        object? result = "(anim-current 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Current_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.Current(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Current_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.Current(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // STATE QUERIES — anim-current-frame
    // =======================================================================
    
    [Fact]
    public void CurrentFrame_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(anim-current-frame 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CurrentFrame_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.CurrentFrame(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void CurrentFrame_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.CurrentFrame(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // STATE QUERIES — anim-length
    // =======================================================================
    
    [Fact]
    public void Length_CalledOutsideProcess_ReturnsFalse()
    {
        // Length takes a clip name, not an entity ID; still a query.
        object? result = "(anim-length \"idle-breathe\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Length_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.Length(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Length_WrongArgType_ReturnsFalse()
    {
        // Clip name must be a string; long should fail the guard.
        object result = AnimationSystem.Length(new object[] { 42L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // STATE QUERIES — anim-playing?
    // =======================================================================
    
    [Fact]
    public void IsPlaying_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(anim-playing? 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsPlaying_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.IsPlaying(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsPlaying_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.IsPlaying(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // STATE QUERIES — anim-blending?
    // =======================================================================
    
    [Fact]
    public void IsBlending_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(anim-blending? 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsBlending_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.IsBlending(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsBlending_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.IsBlending(new object[] { "bad" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // BLEND TREES — define-blend-tree
    // =======================================================================
    
    [Fact]
    public void DefineBlendTree_ValidArgs_ReturnsTrue()
    {
        // The backing class forwards the name hash to the host via EntitySetState
        // and returns #t; it retains no state itself.
        object? result = "(define-blend-tree \"locomotion\" '())".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void DefineBlendTree_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.DefineBlendTree(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineBlendTree_MissingTreeSpec_ReturnsFalse()
    {
        // Name supplied but tree spec is absent.
        object result = AnimationSystem.DefineBlendTree(new object[] { "locomotion" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DefineBlendTree_WrongNameType_ReturnsFalse()
    {
        // Name must be a string; long should fail.
        object result = AnimationSystem.DefineBlendTree(new object[] { 99L, new object() });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // BLEND TREES — set-blend-param!
    // =======================================================================
    
    [Fact]
    public void SetBlendTreeParam_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-blend-param! 1 \"speed\" 0.75)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetBlendTreeParam_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.SetBlendTreeParam(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetBlendTreeParam_MissingValue_ReturnsFalse()
    {
        // Entity ID and param name present; float value is absent.
        object result = AnimationSystem.SetBlendTreeParam(new object[] { 1L, "speed" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetBlendTreeParam_WrongParamNameType_ReturnsFalse()
    {
        // Param name must be a string.
        object result = AnimationSystem.SetBlendTreeParam(new object[] { 1L, 42L, 0.5f });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // BLEND TREES — get-blend-param
    // =======================================================================
    
    [Fact]
    public void GetBlendTreeParam_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(get-blend-param 1 \"speed\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetBlendTreeParam_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.GetBlendTreeParam(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetBlendTreeParam_MissingParamName_ReturnsFalse()
    {
        object result = AnimationSystem.GetBlendTreeParam(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // JOINTS / BONES — get-joint-transform
    // =======================================================================
    
    [Fact]
    public void GetJointTransform_CalledOutsideProcess_ReturnsFalse()
    {
        object? result = "(get-joint-transform 1 \"spine\")".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetJointTransform_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.GetJointTransform(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetJointTransform_MissingJointName_ReturnsFalse()
    {
        object result = AnimationSystem.GetJointTransform(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void GetJointTransform_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.GetJointTransform(new object[] { "bad", "spine" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // JOINTS / BONES — set-joint-override!
    // =======================================================================
    
    [Fact]
    public void SetJointOverride_ValidArgs_ReturnsTrue()
    {
        // Pairs an EntitySetState signal with a RenderCommandType.SetTransform packet.
        var matrix = Matrix4x4.Identity;
        object result = AnimationSystem.SetJointOverride(
            new object[] { 1L, "spine", matrix });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetJointOverride_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.SetJointOverride(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetJointOverride_MissingTransform_ReturnsFalse()
    {
        // Entity ID and joint name present; transform matrix is absent.
        object result = AnimationSystem.SetJointOverride(new object[] { 1L, "spine" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetJointOverride_WrongJointNameType_ReturnsFalse()
    {
        object result = AnimationSystem.SetJointOverride(
            new object[] { 1L, 99L, Matrix4x4.Identity });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // JOINTS / BONES — clear-joint-override!
    // =======================================================================
    
    [Fact]
    public void ClearJointOverride_ValidArgs_ReturnsTrue()
    {
        object? result = "(clear-joint-override! 1 \"spine\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void ClearJointOverride_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.ClearJointOverride(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ClearJointOverride_MissingJointName_ReturnsFalse()
    {
        object result = AnimationSystem.ClearJointOverride(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void ClearJointOverride_WrongEntityIdType_ReturnsFalse()
    {
        object result = AnimationSystem.ClearJointOverride(new object[] { "bad", "spine" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // ANIMATION EVENTS — anim-on-event
    // =======================================================================
    
    [Fact]
    public void OnEvent_ValidArgs_ReturnsTrue()
    {
        // Publishes an EntitySetState event registering host-side clip event
        // interest. No C# callback table is retained — the host delivers the
        // fired event as a send-event to the subscribing process.
        object? result = "(anim-on-event 1 \"footstep\" (lambda () #t))".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void OnEvent_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.OnEvent(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OnEvent_MissingCallback_ReturnsFalse()
    {
        // Entity ID and event name supplied; callback is absent.
        object result = AnimationSystem.OnEvent(new object[] { 1L, "footstep" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void OnEvent_WrongEventNameType_ReturnsFalse()
    {
        object result = AnimationSystem.OnEvent(
            new object[] { 1L, 42L, new object() });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // IK — set-ik-target!
    // =======================================================================
    
    [Fact]
    public void SetIKTarget_ValidArgs_ReturnsTrue()
    {
        // Pairs an EntitySetState signal with a RenderCommandType.SetTransform
        // packet; host pairs them by entity ID and preceding opcode.
        var worldPos = new Vector3(1f, 2f, 3f);
        object result = AnimationSystem.SetIKTarget(
            new object[] { 1L, "right-hand", worldPos });
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetIKTarget_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.SetIKTarget(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetIKTarget_MissingPosition_ReturnsFalse()
    {
        // Entity ID and chain name present; world position is absent.
        object result = AnimationSystem.SetIKTarget(new object[] { 1L, "right-hand" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetIKTarget_WrongChainNameType_ReturnsFalse()
    {
        object result = AnimationSystem.SetIKTarget(
            new object[] { 1L, 99L, new Vector3(0f, 0f, 0f) });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // IK — set-ik-weight!
    // =======================================================================
    
    [Fact]
    public void SetIKWeight_ValidArgs_ReturnsTrue()
    {
        object? result = "(set-ik-weight! 1 \"right-hand\" 0.8)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetIKWeight_EmptyArgs_ReturnsFalse()
    {
        object result = AnimationSystem.SetIKWeight(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetIKWeight_MissingWeight_ReturnsFalse()
    {
        // Entity ID and chain name present; weight float is absent.
        object result = AnimationSystem.SetIKWeight(new object[] { 1L, "right-hand" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetIKWeight_WrongWeightType_ReturnsFalse()
    {
        // Weight must be float-compatible; string should fail the guard.
        object result = AnimationSystem.SetIKWeight(
            new object[] { 1L, "right-hand", "heavy" });
        Assert.True(IsFalse(result));
    }
}
