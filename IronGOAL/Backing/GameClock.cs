using System.Collections.Concurrent;
using System.Threading;
using IronScheme;
using IronScheme.Runtime;

namespace IronGOAL.Backing;

/// <summary>
/// Frame timing, game clock, and managed one-shot/repeating timers.
/// Equivalent to GOAL's time-related kernel functions and DC's timer utilities.
///
/// All state is driven by the values the <see cref="ProcessScheduler"/> writes
/// each frame - GameClock never reads wall-clock time directly.  The scheduler
/// calls <see cref="Advance"/> once per <c>Kernel.Tick()</c> before any script
/// code runs, keeping every read within a tick consistent.
/// </summary>
public static class GameClock
{
    // =======================================================================
    // FRAME RATE CONSTANT
    // =======================================================================
    
    /// <summary>
    /// Canonical simulation frame rate used for GOAL-compatible
    /// seconds↔frames conversions.  Matches the PS2-era 60 fps target.
    /// </summary>
    public const int SimFrameRate = 60;
    
    // =======================================================================
    // CLOCK STATE  (written by Advance, read by Scheme methods)
    // =======================================================================
    
    private static float _frameTime;
    private static float _totalTime;
    private static long  _frameCount;
    private static float _timeScale = 1f;
    
    // =======================================================================
    // TIMER TABLE
    // =======================================================================
    
    private enum TimerMode { OneShot, Repeat }
    
    private sealed class TimerEntry
    {
        public long      Handle;
        public TimerMode Mode;
        public float     Interval;       // seconds between fires
        public float     NextFireTime;   // absolute game-time at which to fire
        public object?   Callback;       // IronScheme Callable
        public bool      Cancelled;
    }
    
    private static readonly ConcurrentDictionary<long, TimerEntry> _timers = new();
    private static long _nextHandle;
    
    // =======================================================================
    // INTERNAL TICK DRIVER
    // Called by Kernel.Tick() / ProcessScheduler before scripts run.
    // =======================================================================
    
    /// <summary>
    /// Advances the clock by one frame.  Must be called exactly once per
    /// <c>Kernel.Tick()</c> before any Scheme code executes that frame.
    /// </summary>
    internal static void Advance(float deltaTime, long frameId)
    {
        _frameTime  = deltaTime * _timeScale;
        _totalTime += _frameTime;
        _frameCount = frameId;
 
        FireElapsedTimers();
    }
    
    // =======================================================================
    // FRAME TIMING
    // =======================================================================
    
    /// <summary>
    /// Scaled time elapsed since the previous frame, in seconds.
    /// Scheme: <c>(frame-time)</c>
    /// </summary>
    public static object FrameTime(object[] args)
    {
        return _frameTime;
    }
    
    /// <summary>
    /// Total scaled simulation time since runtime boot, in seconds.
    /// Pausing the host and not calling <c>Tick()</c> freezes this value.
    /// Scheme: <c>(total-time)</c>
    /// </summary>
    public static object TotalTime(object[] args)
    {
        return _totalTime;
    }
    
    /// <summary>
    /// Number of frames elapsed since runtime boot.  Monotonically increasing;
    /// wraps at <see cref="long.MaxValue"/> (unreachable in practice).
    /// Scheme: <c>(frame-count)</c>
    /// </summary>
    public static object FrameCount(object[] args)
    {
        return _frameCount;
    }
    
    // =======================================================================
    // TIME SCALE  (slow-motion / fast-forward)
    // =======================================================================
    
    /// <summary>
    /// Current time-scale factor.  1.0 is normal speed; 0.5 is half speed.
    /// Scheme: <c>(time-scale)</c>
    /// </summary>
    public static object TimeScale(object[] args)
    {
        return _timeScale;
    }
    
    /// <summary>
    /// Sets the time-scale factor applied to all subsequent <c>Advance()</c>
    /// calls.  Clamped to [0, 10] to guard against runaway scripts.
    /// Scheme: <c>(set-time-scale! s)</c>
    /// </summary>
    public static object SetTimeScale(object[] args)
    {
        var scale = args.Length > 0 ? args[0] : null;
        
        if (scale is float s)
        {
            _timeScale = Math.Clamp(s, 0f, 10f);
            return "#t".Eval();
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // UNIT CONVERSION  (GOAL-compatible seconds ↔ frames)
    // =======================================================================
    
    /// <summary>
    /// Converts a duration in seconds to a frame count at
    /// <see cref="SimFrameRate"/> fps, rounded to the nearest frame.
    /// Scheme: <c>(seconds->frames s)</c>
    /// </summary>
    public static object SecondsToFrames(object[] args)
    {
        var seconds = args.Length > 0 ? args[0] : null;
        
        if (seconds is float s)
        {
            return (long)MathF.Round(s * SimFrameRate);
        }
        
        return "#f".Eval();
    }
    
    /// <summary>
    /// Converts a frame count to seconds at <see cref="SimFrameRate"/> fps.
    /// Scheme: <c>(frames->seconds n)</c>
    /// </summary>
    public static object FramesToSeconds(object[] args)
    {
        var frames = args.Length > 0 ? args[0] : null;
        
        // Accept both long (normal) and int (IronScheme may box as int).
        if (frames is long f)
        {
            return (float)f / SimFrameRate;
        }
        if (frames is int fi)
        {
            return (float)fi / SimFrameRate;
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // ONE-SHOT TIMER
    // =======================================================================
    
    /// <summary>
    /// Schedules a one-shot callback to fire after <paramref name="delaySec"/>
    /// scaled seconds.  Returns an opaque timer handle that can be passed to
    /// <c>timer-cancel</c> or <c>timer-remaining</c>.
    ///
    /// The callback receives no arguments.  If the runtime is disposed before
    /// the timer fires it is silently dropped.
    ///
    /// Scheme: <c>(timer-start delay-sec callback)</c>
    /// </summary>
    public static object TimerStart(object[] args)
    {
        var delaySec = args.Length > 0 ? args[0] : null;
        var callback = args.Length > 1 ? args[1] : null;
        
        if (delaySec is float d && callback is not null)
        {
            long handle = Interlocked.Increment(ref _nextHandle);
            _timers[handle] = new TimerEntry
            {
                Handle      = handle,
                Mode        = TimerMode.OneShot,
                Interval    = d,
                NextFireTime = _totalTime + d,
                Callback    = callback,
            };
            return handle;
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // REPEATING TIMER
    // =======================================================================
    
    /// <summary>
    /// Schedules a repeating callback to fire every <paramref name="intervalSec"/>
    /// scaled seconds until canceled.  Returns an opaque timer handle.
    ///
    /// If the scheduler falls behind (e.g. a very long frame), only one
    /// invocation fires per <c>Advance()</c> call - the timer reschedules
    /// relative to its last fire time, so it self-corrects without flooding.
    ///
    /// Scheme: <c>(timer-repeat interval-sec callback)</c>
    /// </summary>
    public static object TimerRepeat(object[] args)
    {
        var intervalSec = args.Length > 0 ? args[0] : null;
        var callback    = args.Length > 1 ? args[1] : null;
        
        if (intervalSec is float i && i > 0f && callback is not null)
        {
            long handle = Interlocked.Increment(ref _nextHandle);
            _timers[handle] = new TimerEntry
            {
                Handle       = handle,
                Mode         = TimerMode.Repeat,
                Interval     = i,
                NextFireTime = _totalTime + i,
                Callback     = callback,
            };
            return handle;
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // CANCEL TIMER
    // =======================================================================
    
    /// <summary>
    /// Cancels a pending timer.  Safe to call on an already-fired one-shot or
    /// an unknown handle - both cases return <c>#f</c> without throwing.
    /// Scheme: <c>(timer-cancel handle)</c>
    /// </summary>
    public static object TimerCancel(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long h && _timers.TryGetValue(h, out var entry))
        {
            entry.Cancelled = true;
            _timers.TryRemove(h, out _);
            return "#t".Eval();
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // TIMER REMAINING
    // =======================================================================
    
    /// <summary>
    /// Returns the scaled seconds remaining until the next fire of the given
    /// timer, or <c>#f</c> if the handle is unknown / already fired.
    ///
    /// For repeating timers this reflects time until the next repetition.
    /// Scheme: <c>(timer-remaining handle)</c>
    /// </summary>
    public static object TimerRemaining(object[] args)
    {
        var handle = args.Length > 0 ? args[0] : null;
        
        if (handle is long h && _timers.TryGetValue(h, out var entry))
        {
            float remaining = entry.NextFireTime - _totalTime;
            return MathF.Max(0f, remaining);
        }
        
        return "#f".Eval();
    }
    
    // =======================================================================
    // INTERNAL TIMER FIRE LOOP
    // =======================================================================
    
    private static void FireElapsedTimers()
    {
        foreach (var kvp in _timers)
        {
            TimerEntry entry = kvp.Value;
 
            if (entry.Cancelled || _totalTime < entry.NextFireTime)
                continue;
 
            // Invoke the Scheme callback.
            if (entry.Callback is Callable callable)
            {
                try
                {
                    callable.Call();
                }
                catch (Exception ex)
                {
                    // A misbehaving timer callback must not crash the scheduler.
                    // Log and continue; host can observe via the debug channel.
                    Console.Error.WriteLine(
                        $"[GameClock] Timer {entry.Handle} callback threw: {ex.Message}");
                }
            }
 
            if (entry.Mode == TimerMode.OneShot)
            {
                _timers.TryRemove(kvp.Key, out _);
            }
            else
            {
                // Advance relative to last fire time so repeats stay rhythmic
                // even when a frame is late.
                entry.NextFireTime += entry.Interval;
            }
        }
    }
    
    // =======================================================================
    // RESET  (called by tests or on runtime disposal)
    // =======================================================================
    
    /// <summary>
    /// Resets all clock state to zero and clears the timer table.
    /// Called internally when the kernel is disposed; exposed for unit tests.
    /// </summary>
    internal static void Reset()
    {
        _frameTime  = 0f;
        _totalTime  = 0f;
        _frameCount = 0;
        _timeScale  = 1f;
        _timers.Clear();
        _nextHandle = 0;
    }
}
