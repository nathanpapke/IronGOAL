using System;
using IronScheme;
using IronGOAL;
using IronGOAL.Backing;
using Xunit;

namespace Tests;

public class AudioSystemTests
{
    // =======================================================================
    // BOOT
    // =======================================================================
    
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        TransformChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    static AudioSystemTests() => Host.Create(Config);
    
    // =======================================================================
    // HELPERS
    // =======================================================================
    
    private static bool IsTrue(object? v)  => v is bool b && b;
    private static bool IsFalse(object? v) => v is bool b && !b;
    
    // =======================================================================
    // PLAYBACK COMMANDS — sound-play
    // =======================================================================
    
    [Fact]
    public void Play_ValidArgs_ReturnsHandle()
    {
        // Positional play: clip name + world position vec3.
        object? result = "(sound-play \"explosion\" (vec3 1.0 2.0 3.0))".Eval();
 
        // Handle is issued locally (Option A) — expect a non-#f result.
        Assert.False(IsFalse(result));
    }
    
    [Fact]
    public void Play_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.Play(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Play_MissingPosition_ReturnsFalse()
    {
        // Clip name supplied but no position — guard fires.
        object result = AudioSystem.Play(new object[] { "explosion" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Play_WrongClipNameType_ReturnsFalse()
    {
        // Long where string is required.
        object result = AudioSystem.Play(new object[] { 42L, "ignored" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — sound-play-2d
    // =======================================================================
    
    [Fact]
    public void Play2D_ValidArgs_ReturnsHandle()
    {
        object? result = "(sound-play-2d \"ui-confirm\")".Eval();
        Assert.False(IsFalse(result));
    }
    
    [Fact]
    public void Play2D_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.Play2D(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Play2D_WrongArgType_ReturnsFalse()
    {
        // Long where string is required.
        object result = AudioSystem.Play2D(new object[] { 99L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — sound-stop
    // =======================================================================
    
    [Fact]
    public void Stop_ValidHandle_ReturnsTrue()
    {
        // Fire-and-forget command publishes to the audio channel.
        object? result = "(sound-stop 1)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void Stop_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.Stop(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void Stop_WrongArgType_ReturnsFalse()
    {
        // String where a long handle is required.
        object result = AudioSystem.Stop(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PLAYBACK COMMANDS — sound-stop-all
    // =======================================================================
    
    [Fact]
    public void StopAll_NoArgs_ReturnsTrue()
    {
        object? result = "(sound-stop-all)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void StopAll_IgnoresExtraArgs_ReturnsTrue()
    {
        // Panic stop takes no parameters; extras should be tolerated or ignored.
        object result = AudioSystem.StopAll(new object[] { 1L });
        Assert.True(IsTrue(result));
    }
    
    // =======================================================================
    // PARAMETERS — sound-set-volume!
    // =======================================================================
    
    [Fact]
    public void SetVolume_ValidArgs_ReturnsTrue()
    {
        object? result = "(sound-set-volume! 1 0.5)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetVolume_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.SetVolume(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVolume_MissingVolume_ReturnsFalse()
    {
        // Handle supplied but no volume value — guard fires.
        object result = AudioSystem.SetVolume(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetVolume_WrongHandleType_ReturnsFalse()
    {
        // String where long handle is required.
        object result = AudioSystem.SetVolume(new object[] { "not-a-handle", "0.5" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PARAMETERS — sound-set-pitch!
    // =======================================================================
    
    [Fact]
    public void SetPitch_ValidArgs_ReturnsTrue()
    {
        object? result = "(sound-set-pitch! 1 1.5)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetPitch_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.SetPitch(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetPitch_MissingPitch_ReturnsFalse()
    {
        object result = AudioSystem.SetPitch(new object[] { 1L });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetPitch_WrongHandleType_ReturnsFalse()
    {
        object result = AudioSystem.SetPitch(new object[] { "not-a-handle", "1.0" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // PARAMETERS — sound-set-param!
    // =======================================================================
    
    [Fact]
    public void SetParam_ValidArgs_ReturnsTrue()
    {
        // Named RTPC parameter.
        object? result = "(sound-set-param! 1 \"engine-rpm\" 0.75)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetParam_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.SetParam(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetParam_MissingValue_ReturnsFalse()
    {
        // Handle and param name supplied but no value — guard fires.
        object result = AudioSystem.SetParam(new object[] { 1L, "engine-rpm" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetParam_WrongParamNameType_ReturnsFalse()
    {
        // Long where string param name is required.
        object result = AudioSystem.SetParam(new object[] { 1L, 42L, "0.75" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // MUSIC — music-play
    // =======================================================================
    
    [Fact]
    public void MusicPlay_ValidArgs_ReturnsTrue()
    {
        object? result = "(music-play \"theme-main\")".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MusicPlay_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.MusicPlay(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MusicPlay_WrongArgType_ReturnsFalse()
    {
        // Long where string track name is required.
        object result = AudioSystem.MusicPlay(new object[] { 7L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // MUSIC — music-stop
    // =======================================================================
    
    [Fact]
    public void MusicStop_ValidArgs_ReturnsTrue()
    {
        // Fade-out duration in seconds.
        object? result = "(music-stop 2.0)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MusicStop_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.MusicStop(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MusicStop_WrongArgType_ReturnsFalse()
    {
        // String where float fade time is required.
        object result = AudioSystem.MusicStop(new object[] { "two-seconds" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // MUSIC — music-set-intensity!
    // =======================================================================
    
    [Fact]
    public void MusicSetIntensity_ValidArgs_ReturnsTrue()
    {
        // Adaptive music intensity in [0, 1].
        object? result = "(music-set-intensity! 0.8)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void MusicSetIntensity_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.MusicSetIntensity(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void MusicSetIntensity_WrongArgType_ReturnsFalse()
    {
        object result = AudioSystem.MusicSetIntensity(new object[] { "loud" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // AMBIENT — set-listener-pos!
    // =======================================================================
    
    [Fact]
    public void SetListenerPos_ValidArgs_ReturnsTrue()
    {
        // position, forward, up — all vec3.
        object? result =
            "(set-listener-pos! (vec3 0.0 0.0 0.0) (vec3 0.0 0.0 1.0) (vec3 0.0 1.0 0.0))"
            .Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetListenerPos_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.SetListenerPos(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetListenerPos_MissingUpVector_ReturnsFalse()
    {
        // Position and forward supplied but up vector absent — guard fires.
        var pos     = "(vec3 0.0 0.0 0.0)".Eval()!;
        var forward = "(vec3 0.0 0.0 1.0)".Eval()!;
 
        object result = AudioSystem.SetListenerPos(new object[] { pos, forward });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetListenerPos_WrongArgType_ReturnsFalse()
    {
        // String where vec3 is required.
        object result = AudioSystem.SetListenerPos(
            new object[] { "not-a-vec3", "not-a-vec3", "not-a-vec3" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // AMBIENT — set-reverb!
    // =======================================================================
    
    [Fact]
    public void SetReverb_ValidArgs_ReturnsTrue()
    {
        // Named reverb preset + wet level.
        object? result = "(set-reverb! \"cave-large\" 0.4)".Eval();
        Assert.True(IsTrue(result));
    }
    
    [Fact]
    public void SetReverb_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.SetReverb(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetReverb_MissingWetLevel_ReturnsFalse()
    {
        object result = AudioSystem.SetReverb(new object[] { "cave-large" });
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void SetReverb_WrongPresetNameType_ReturnsFalse()
    {
        // Long where string preset name is required.
        object result = AudioSystem.SetReverb(new object[] { 5L, "0.4" });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VOICE / DIALOG — dialog-play
    // =======================================================================
    
    [Fact]
    public void DialogPlay_ValidArgsNoCallback_ReturnsHandle()
    {
        object? result = "(dialog-play \"ellie-warning\")".Eval();
        Assert.False(IsFalse(result));
    }
    
    [Fact]
    public void DialogPlay_ValidArgsWithCallback_ReturnsHandle()
    {
        // Optional on-complete callback as a thunk.
        object? result = "(dialog-play \"ellie-warning\" (lambda () #t))".Eval();
        Assert.False(IsFalse(result));
    }
    
    [Fact]
    public void DialogPlay_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.DialogPlay(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void DialogPlay_WrongDialogIdType_ReturnsFalse()
    {
        // Long where string dialog id is required.
        object result = AudioSystem.DialogPlay(new object[] { 13L });
        Assert.True(IsFalse(result));
    }
    
    // =======================================================================
    // VOICE / DIALOG — dialog-playing? (opcode 210, EntityQuery)
    // =======================================================================
    
    [Fact]
    public void DialogIsPlaying_CalledOutsideProcess_ReturnsFalse()
    {
        // No suspending process context to receive the query response.
        object? result = "(dialog-playing? 1)".Eval();
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsDialogPlaying_EmptyArgs_ReturnsFalse()
    {
        object result = AudioSystem.IsDialogPlaying(Array.Empty<object>());
        Assert.True(IsFalse(result));
    }
    
    [Fact]
    public void IsDialogPlaying_WrongArgType_ReturnsFalse()
    {
        // String where long ClipId handle is required.
        object result = AudioSystem.IsDialogPlaying(new object[] { "not-a-handle" });
        Assert.True(IsFalse(result));
    }
}
