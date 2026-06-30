using IronScheme;
using IronScheme.Runtime;
using IronGOAL;
using IronGOAL.Backing;

namespace Tests;

public class GameClockTests
{
    private Host _host;
    
    static readonly GoalRuntimeConfig Config = new GoalRuntimeConfig
    {
        GlobalHeapSize        = 16 * 1024 * 1024,
        StackHeapSize         =  2 * 1024 * 1024,
        TransformChannelCapacity = 64,
        EnableMemoryTracking  = false,
        EnableDebugChannel    = false,
        LogHandler            = (_, _, _) => { }
    };
    
    public GameClockTests()
    {
        var result = Host.Create(Config);
        Assert.True(result.IsSuccess,
            $"Host.Create failed: {result.ErrorMessage}");
        _host = result.Value!;
    }
    
    // Convenience: advance N frames at the canonical 60 fps frame delta.
    private void TickFrames(int count, float deltaPerFrame = 1f / 60f)
    {
        for (int i = 0; i < count; i++)
            Assert.True(_host.Tick(deltaPerFrame).IsSuccess);
    }
    
    // =======================================================================
    // CONSTANTS
    // =======================================================================
    
    [Fact]
    public void SimFrameRate_Is60()
    {
        Assert.Equal(60, GameClock.SimFrameRate);
    }
    
    // =======================================================================
    // FRAME TIME
    // =======================================================================
    
    [Fact]
    public void FrameTime_BeforeFirstTick_IsZero()
    {
        // Reset() sets _frameTime to 0; confirm via Scheme symbol.
        var result = Assert.IsType<float>("(frame-time)".Eval());
        Assert.Equal(0f, result, 6);
    }
    
    [Fact]
    public void FrameTime_AfterOneTick_EqualsScaledDelta()
    {
        float delta = 1f / 60f;
        Assert.True(_host.Tick(delta).IsSuccess);
 
        // TimeScale defaults to 1.0, so frameTime == delta.
        var result = Assert.IsType<float>("(frame-time)".Eval());
        Assert.Equal(delta, result, 5);
    }
    
    [Fact]
    public void FrameTime_WithHalfTimeScale_IsHalfDelta()
    {
        // Set time scale to 0.5 before ticking.
        "(set-time-scale! 0.5)".Eval();
        float delta = 1f / 60f;
        Assert.True(_host.Tick(delta).IsSuccess);
 
        var result = Assert.IsType<float>("(frame-time)".Eval());
        Assert.Equal(delta * 0.5f, result, 5);
    }
    
    [Fact]
    public void FrameTime_DirectCall_NoArgs_ReturnsFloat()
    {
        TickFrames(1);
        var result = GameClock.FrameTime(Array.Empty<object>());
        Assert.IsType<float>(result);
    }
    
    // =======================================================================
    // TOTAL TIME
    // =======================================================================
    
    [Fact]
    public void TotalTime_BeforeAnyTick_IsZero()
    {
        var result = Assert.IsType<float>("(total-time)".Eval());
        Assert.Equal(0f, result, 6);
    }
    
    [Fact]
    public void TotalTime_AfterThreeFrames_AccumulatesCorrectly()
    {
        float delta = 1f / 60f;
        TickFrames(3, delta);
        
        var result = Assert.IsType<float>("(total-time)".Eval());
        Assert.Equal(3f * delta, result, 5);
    }
    
    [Fact]
    public void TotalTime_IsMonotonicallyIncreasing()
    {
        TickFrames(1);
        float t1 = Assert.IsType<float>("(total-time)".Eval());
        TickFrames(1);
        float t2 = Assert.IsType<float>("(total-time)".Eval());
        
        Assert.True(t2 > t1);
    }
    
    [Fact]
    public void TotalTime_DirectCall_NoArgs_ReturnsFloat()
    {
        var result = GameClock.TotalTime(Array.Empty<object>());
        Assert.IsType<float>(result);
    }
    
    // =======================================================================
    // FRAME COUNT
    // =======================================================================
    
    [Fact]
    public void FrameCount_BeforeAnyTick_IsZero()
    {
        var result = Assert.IsType<long>("(frame-count)".Eval());
        Assert.Equal(0L, result);
    }
    
    [Fact]
    public void FrameCount_AfterFiveTicks_IsFive()
    {
        TickFrames(5);
        var result = Assert.IsType<long>("(frame-count)".Eval());
        Assert.Equal(5L, result);
    }
    
    [Fact]
    public void FrameCount_DirectCall_NoArgs_ReturnsLong()
    {
        var result = GameClock.FrameCount(Array.Empty<object>());
        Assert.IsType<long>(result);
    }
    
    // =======================================================================
    // TIME SCALE — read
    // =======================================================================
    
    [Fact]
    public void TimeScale_Default_IsOne()
    {
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(1f, result, 6);
    }
    
    [Fact]
    public void TimeScale_DirectCall_NoArgs_ReturnsFloat()
    {
        var result = GameClock.TimeScale(Array.Empty<object>());
        Assert.IsType<float>(result);
    }
    
    // =======================================================================
    // SET TIME SCALE — write, clamp, wrong-type
    // =======================================================================
    
    [Fact]
    public void SetTimeScale_HalfSpeed_UpdatesTimeScale()
    {
        "(set-time-scale! 0.5)".Eval();
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(0.5f, result, 5);
    }
    
    [Fact]
    public void SetTimeScale_Zero_IsPermitted()
    {
        "(set-time-scale! 0.0)".Eval();
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(0f, result, 6);
    }
    
    [Fact]
    public void SetTimeScale_AtUpperBound_IsTen()
    {
        "(set-time-scale! 10.0)".Eval();
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(10f, result, 5);
    }
    
    [Fact]
    public void SetTimeScale_AboveUpperBound_ClampedToTen()
    {
        // Value beyond the [0, 10] clamp must be clamped, not stored raw.
        "(set-time-scale! 99.0)".Eval();
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(10f, result, 5);
    }
    
    [Fact]
    public void SetTimeScale_BelowZero_ClampedToZero()
    {
        "(set-time-scale! -1.0)".Eval();
        var result = Assert.IsType<float>("(time-scale)".Eval());
        Assert.Equal(0f, result, 6);
    }
    
    [Fact]
    public void SetTimeScale_ValidInput_ReturnsTrueSchemeValue()
    {
        // SetTimeScale returns #t on success.
        var result = "(set-time-scale! 2.0)".Eval();
        Assert.Equal(true, result);
    }
    
    [Fact]
    public void SetTimeScale_WrongType_ReturnsFalse()
    {
        // Pass a string argument to exercise the wrong-type guard.
        // Use the C# surface with a string box (IronScheme crash-safe pattern).
        var result = GameClock.SetTimeScale(new object[] { "not-a-float" });
        Assert.IsNotType<float>(result);
        Assert.IsNotType<bool>(result);
    }
    
    // =======================================================================
    // SECONDS TO FRAMES
    // =======================================================================
    
    [Fact]
    public void SecondsToFrames_OneSecond_Returns60()
    {
        var result = Assert.IsType<long>("(seconds->frames 1.0)".Eval());
        Assert.Equal(60L, result);
    }
    
    [Fact]
    public void SecondsToFrames_HalfSecond_Returns30()
    {
        var result = Assert.IsType<long>("(seconds->frames 0.5)".Eval());
        Assert.Equal(30L, result);
    }
    
    [Fact]
    public void SecondsToFrames_Zero_ReturnsZero()
    {
        var result = Assert.IsType<long>("(seconds->frames 0.0)".Eval());
        Assert.Equal(0L, result);
    }
    
    [Fact]
    public void SecondsToFrames_RoundsToNearestFrame()
    {
        // 1/60 + tiny epsilon → rounds to 1 frame.
        var result = Assert.IsType<long>(
            GameClock.SecondsToFrames(new object[] { (float)(1.0 / 60.0 + 0.001) }));
        Assert.Equal(1L, result);
    }
    
    [Fact]
    public void SecondsToFrames_WrongType_ReturnsFalse()
    {
        var result = GameClock.SecondsToFrames(new object[] { "bad" });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void SecondsToFrames_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.SecondsToFrames(Array.Empty<object>());
        Assert.IsNotType<long>(result);
    }
    
    // =======================================================================
    // FRAMES TO SECONDS
    // =======================================================================
    
    [Fact]
    public void FramesToSeconds_SixtyFrames_ReturnsOneSecond()
    {
        // Scheme integer literals box as int in IronScheme.
        var result = Assert.IsType<float>("(frames->seconds 60)".Eval());
        Assert.Equal(1f, result, 5);
    }
    
    [Fact]
    public void FramesToSeconds_ThirtyFrames_ReturnsHalfSecond()
    {
        var result = Assert.IsType<float>("(frames->seconds 30)".Eval());
        Assert.Equal(0.5f, result, 5);
    }
    
    [Fact]
    public void FramesToSeconds_ZeroFrames_ReturnsZero()
    {
        var result = Assert.IsType<float>("(frames->seconds 0)".Eval());
        Assert.Equal(0f, result, 6);
    }
    
    [Fact]
    public void FramesToSeconds_AcceptsLongBoxed()
    {
        // Implementation accepts both int and long boxes (IronScheme boxing).
        var result = GameClock.FramesToSeconds(new object[] { 60L });
        Assert.Equal(1f, Assert.IsType<float>(result), 5);
    }
    
    [Fact]
    public void FramesToSeconds_AcceptsIntBoxed()
    {
        var result = GameClock.FramesToSeconds(new object[] { 30 });
        Assert.Equal(0.5f, Assert.IsType<float>(result), 5);
    }
    
    [Fact]
    public void FramesToSeconds_WrongType_ReturnsFalse()
    {
        var result = GameClock.FramesToSeconds(new object[] { "nope" });
        Assert.IsNotType<float>(result);
    }
    
    [Fact]
    public void FramesToSeconds_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.FramesToSeconds(Array.Empty<object>());
        Assert.IsNotType<float>(result);
    }
    
    // Round-trip invariant: converting to frames and back must be lossless
    // for values that are exact multiples of the frame interval.
    [Fact]
    public void SecondsFrames_RoundTrip_ExactMultiples()
    {
        float[] samples = { 0f, 0.5f, 1f, 2f, 10f };
        foreach (float s in samples)
        {
            long frames  = Assert.IsType<long>(
                GameClock.SecondsToFrames(new object[] { s }));
            float back   = Assert.IsType<float>(
                GameClock.FramesToSeconds(new object[] { frames }));
            Assert.Equal(s, back, 5);
        }
    }
    
    // =======================================================================
    // TIMER START — one-shot
    // =======================================================================
    
    [Fact]
    public void TimerStart_ValidArgs_ReturnsLongHandle()
    {
        // Scheme: pass delay and a no-op lambda.
        var handle = "(timer-start 1.0 (lambda () #t))".Eval();
        Assert.IsType<long>(handle);
    }
    
    [Fact]
    public void TimerStart_HandleIsPositive()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 2.0 (lambda () #t))".Eval());
        Assert.True(handle > 0L);
    }
    
    [Fact]
    public void TimerStart_TwoTimers_ReturnDistinctHandles()
    {
        long h1 = Assert.IsType<long>("(timer-start 1.0 (lambda () #t))".Eval());
        long h2 = Assert.IsType<long>("(timer-start 1.0 (lambda () #t))".Eval());
        Assert.NotEqual(h1, h2);
    }
    
    [Fact]
    public void TimerStart_MissingCallback_ReturnsFalse()
    {
        // Only one argument supplied — callback is missing.
        var result = GameClock.TimerStart(new object[] { 1.0f });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerStart_WrongDelayType_ReturnsFalse()
    {
        var result = GameClock.TimerStart(new object[] { "bad", new object() });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerStart_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.TimerStart(Array.Empty<object>());
        Assert.IsNotType<long>(result);
    }
    
    // Callback invocation: advance past the delay and confirm the Scheme side
    // ran (we observe a side-effect via a define'd counter).
    [Fact]
    public void TimerStart_FiresAfterDelay()
    {
        // Set up a Scheme counter and a timer that increments it.
        "(define timer-fired-count 0)".Eval();
        "(timer-start 0.1 (lambda () (set! timer-fired-count (+ timer-fired-count 1))))".Eval();
        
        // Advance 10 frames at 1/60s each ≈ 0.167 s, past the 0.1 s delay.
        TickFrames(10);
        
        var count = Assert.IsType<int>("timer-fired-count".Eval());
        Assert.Equal(1, count);
    }
    
    [Fact]
    public void TimerStart_DoesNotFireBeforeDelay()
    {
        "(define one-shot-early 0)".Eval();
        "(timer-start 10.0 (lambda () (set! one-shot-early (+ one-shot-early 1))))".Eval();
        
        // Only 1 frame — nowhere near 10 seconds.
        TickFrames(1);
        
        var count = Assert.IsType<int>("one-shot-early".Eval());
        Assert.Equal(0, count);
    }
    
    [Fact]
    public void TimerStart_FiresOnlyOnce()
    {
        "(define one-shot-count 0)".Eval();
        "(timer-start 0.05 (lambda () (set! one-shot-count (+ one-shot-count 1))))".Eval();
 
        // Advance well past the delay (120 frames ≈ 2 seconds).
        TickFrames(120);
 
        // One-shot: must fire exactly once, not repeatedly.
        var count = Assert.IsType<int>("one-shot-count".Eval());
        Assert.Equal(1, count);
    }
    
    // =======================================================================
    // TIMER REPEAT — repeating
    // =======================================================================
    
    [Fact]
    public void TimerRepeat_ValidArgs_ReturnsLongHandle()
    {
        var handle = "(timer-repeat 1.0 (lambda () #t))".Eval();
        Assert.IsType<long>(handle);
    }
    
    [Fact]
    public void TimerRepeat_ZeroInterval_ReturnsFalse()
    {
        // Interval must be > 0 (documented guard in implementation).
        var result = GameClock.TimerRepeat(new object[] { 0.0f, new object() });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerRepeat_NegativeInterval_ReturnsFalse()
    {
        var result = GameClock.TimerRepeat(new object[] { -1.0f, new object() });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerRepeat_WrongIntervalType_ReturnsFalse()
    {
        var result = GameClock.TimerRepeat(new object[] { "bad", new object() });
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerRepeat_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.TimerRepeat(Array.Empty<object>());
        Assert.IsNotType<long>(result);
    }
    
    [Fact]
    public void TimerRepeat_FiresMultipleTimes()
    {
        "(define repeat-count 0)".Eval();
        "(timer-repeat 0.1 (lambda () (set! repeat-count (+ repeat-count 1))))".Eval();
        
        // Advance 18 frames ≈ 0.3 s → should fire ~3 times at 0.1 s intervals.
        TickFrames(18);
        
        var count = Assert.IsType<int>("repeat-count".Eval());
        Assert.True(count >= 2,
            $"Expected at least 2 repeat fires, got {count}");
    }
    
    [Fact]
    public void TimerRepeat_StopsAfterCancel()
    {
        "(define rep-cancel-count 0)".Eval();
        "(define rep-handle (timer-repeat 0.05 (lambda () (set! rep-cancel-count (+ rep-cancel-count 1)))))".Eval();
        
        // Allow it to fire once.
        TickFrames(5);
        int beforeCancel = Assert.IsType<int>("rep-cancel-count".Eval());
        
        // Cancel it.
        "(timer-cancel rep-handle)".Eval();
        
        // Advance further; count must not increase.
        TickFrames(60);
        int afterCancel = Assert.IsType<int>("rep-cancel-count".Eval());
        
        Assert.Equal(beforeCancel, afterCancel);
    }
    
    // =======================================================================
    // TIMER CANCEL
    // =======================================================================
    
    [Fact]
    public void TimerCancel_LiveHandle_ReturnsTrueSchemeValue()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 5.0 (lambda () #t))".Eval());
        var result = GameClock.TimerCancel(new object[] { handle });
        Assert.Equal(true, result);
    }
    
    [Fact]
    public void TimerCancel_UnknownHandle_ReturnsFalse()
    {
        // Handle 999999 was never issued — must not throw, must return #f.
        var result = GameClock.TimerCancel(new object[] { 999999L });
        Assert.IsNotType<bool>(result);   // IsNotType<bool> — it's the #f sentinel
    }
    
    [Fact]
    public void TimerCancel_AlreadyCancelledHandle_ReturnsFalse()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 5.0 (lambda () #t))".Eval());
        GameClock.TimerCancel(new object[] { handle });
        
        // Second cancel on the same handle — timer is gone, should return #f.
        var result = GameClock.TimerCancel(new object[] { handle });
        Assert.IsNotType<bool>(result);
    }
    
    [Fact]
    public void TimerCancel_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.TimerCancel(Array.Empty<object>());
        Assert.IsNotType<bool>(result);
    }
    
    [Fact]
    public void TimerCancel_PreventsFire()
    {
        "(define cancel-fire-count 0)".Eval();
        "(define h (timer-start 0.1 (lambda () (set! cancel-fire-count (+ cancel-fire-count 1)))))".Eval();
        
        // Cancel before the delay expires.
        "(timer-cancel h)".Eval();
        TickFrames(20);
        
        var count = Assert.IsType<int>("cancel-fire-count".Eval());
        Assert.Equal(0, count);
    }
    
    // =======================================================================
    // TIMER REMAINING
    // =======================================================================
    
    [Fact]
    public void TimerRemaining_LiveHandle_ReturnsPositiveFloat()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 5.0 (lambda () #t))".Eval());
        
        var remaining = GameClock.TimerRemaining(new object[] { handle });
        float r = Assert.IsType<float>(remaining);
        Assert.True(r > 0f, $"Expected remaining > 0, got {r}");
    }
    
    [Fact]
    public void TimerRemaining_UnknownHandle_ReturnsFalse()
    {
        var result = GameClock.TimerRemaining(new object[] { 999998L });
        Assert.IsNotType<float>(result);
    }
    
    [Fact]
    public void TimerRemaining_EmptyArgs_ReturnsFalse()
    {
        var result = GameClock.TimerRemaining(Array.Empty<object>());
        Assert.IsNotType<float>(result);
    }
    
    [Fact]
    public void TimerRemaining_DecreasesAsClockAdvances()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 2.0 (lambda () #t))".Eval());
        
        float r1 = Assert.IsType<float>(
            GameClock.TimerRemaining(new object[] { handle }));
        
        TickFrames(30); // ~0.5 s
        
        float r2 = Assert.IsType<float>(
            GameClock.TimerRemaining(new object[] { handle }));
        
        Assert.True(r2 < r1,
            $"Expected remaining to decrease: r1={r1} r2={r2}");
    }
    
    [Fact]
    public void TimerRemaining_AfterFired_ReturnsFalse()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 0.05 (lambda () #t))".Eval());
        
        // Advance past the delay so the timer fires and is removed.
        TickFrames(10);
        
        // One-shot timer is gone — remaining must return #f.
        var result = GameClock.TimerRemaining(new object[] { handle });
        Assert.IsNotType<float>(result);
    }
    
    [Fact]
    public void TimerRemaining_NeverGoesNegative()
    {
        var handle = Assert.IsType<long>(
            "(timer-start 0.2 (lambda () #t))".Eval());
        
        // Advance a bit but not past the delay.
        TickFrames(5);
        
        float r = Assert.IsType<float>(
            GameClock.TimerRemaining(new object[] { handle }));
        Assert.True(r >= 0f,
            $"TimerRemaining must be >= 0, got {r}");
    }
    
    // =======================================================================
    // INTEGRATION — time-scale affects timer fire timing
    // =======================================================================
    
    [Fact]
    public void TimerStart_WithHalfTimeScale_FiresAtDoubleWallFrames()
    {
        // At 0.5x scale, 0.1 scaled-seconds = 0.2 wall-seconds = 12 frames.
        "(set-time-scale! 0.5)".Eval();
        "(define ts-fired 0)".Eval();
        "(timer-start 0.1 (lambda () (set! ts-fired (+ ts-fired 1))))".Eval();
        
        // 7 frames ≈ 0.117 wall-sec → 0.058 scaled-sec — should NOT have fired.
        TickFrames(7);
        Assert.Equal(0, Assert.IsType<int>("ts-fired".Eval()));
        
        // 13 more frames (total 20 ≈ 0.333 wall-sec → 0.167 scaled-sec) — fired.
        TickFrames(13);
        Assert.Equal(1, Assert.IsType<int>("ts-fired".Eval()));
    }
    
    // =======================================================================
    // SCHEME SYMBOL REGISTRATION — verify all symbols are reachable
    // =======================================================================
    
    [Fact]
    public void SchemeSymbol_FrameTime_IsRegistered()
    {
        var ex = Record.Exception(() => "(frame-time)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TotalTime_IsRegistered()
    {
        var ex = Record.Exception(() => "(total-time)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_FrameCount_IsRegistered()
    {
        var ex = Record.Exception(() => "(frame-count)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TimeScale_IsRegistered()
    {
        var ex = Record.Exception(() => "(time-scale)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_SetTimeScale_IsRegistered()
    {
        var ex = Record.Exception(() => "(set-time-scale! 1.0)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_SecondsToFrames_IsRegistered()
    {
        var ex = Record.Exception(() => "(seconds->frames 1.0)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_FramesToSeconds_IsRegistered()
    {
        var ex = Record.Exception(() => "(frames->seconds 60)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TimerStart_IsRegistered()
    {
        var ex = Record.Exception(() => "(timer-start 1.0 (lambda () #t))".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TimerRepeat_IsRegistered()
    {
        var ex = Record.Exception(() => "(timer-repeat 1.0 (lambda () #t))".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TimerCancel_IsRegistered()
    {
        "(define reg-h (timer-start 9.0 (lambda () #t)))".Eval();
        var ex = Record.Exception(() => "(timer-cancel reg-h)".Eval());
        Assert.Null(ex);
    }
    
    [Fact]
    public void SchemeSymbol_TimerRemaining_IsRegistered()
    {
        "(define rem-h (timer-start 9.0 (lambda () #t)))".Eval();
        var ex = Record.Exception(() => "(timer-remaining rem-h)".Eval());
        Assert.Null(ex);
    }
}
