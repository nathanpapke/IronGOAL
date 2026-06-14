using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Scheme-facing backing methods for the audio system.
/// </summary>
public static class AudioSystem
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
    // HANDLE COUNTER
    // =======================================================================
    
    // Locally-issued handle counter.  The host maps these ids to its own
    // engine resources.  Starts at 1 so that 0 can serve as a sentinel
    // "no handle" value on the host side if needed.
    //
    // Interlocked.Increment guarantees uniqueness across concurrent Scheme
    // processes without a lock.  Wrap-around at long.MaxValue is acceptable
    // for practical session lifetimes.
    
    private static long _nextHandle = 0;
    
    private static long MintHandle() => Interlocked.Increment(ref _nextHandle);
    
    // =======================================================================
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Mirrors EntitySystem / AnimationSystem.
    // Key   = process handle of the suspended ScriptProcess.
    // Value = the bool answer deposited by the host via DeliverQueryResponse.
    // A key being present (even with null) signals answer arrival.
    // TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a query answer for a suspended process.
    /// Writing the key wakes the process on the next scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    //TODO: Finalize opcodes.
    private const int QDialogIsPlaying = 210;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    private static void PublishAudio(AudioCommand cmd)
        => _bus?.PublishAudio(cmd);
    
    private static float ToFloat(object o) => o switch
    {
        double d => (float)d,
        float  f => f,
        _        => 0f,
    };
    
    /// <summary>
    /// Publishes a <see cref="GameEventType.EntityQuery"/> event and suspends
    /// the calling process until the host deposits an answer.
    /// <c>Param3</c> is reserved for the process handle (stamped automatically).
    /// Returns <c>null</c> when called outside a process context.
    /// </summary>
    private static object? Query(int param0, int param1 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[AudioSystem] Query called outside a running process - returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = -1,
            Param0   = param0,
            Param1   = param1,
            Param2   = 0,
            Param3   = (int)(handle & 0x7FFF_FFFF),
        });
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    // =======================================================================
    // PLAY CONTROL
    // =======================================================================
    
    /// <summary>
    /// Begins positional playback of a named sound asset at a world-space
    /// position.  Mints and returns a clip handle immediately (no suspend).
    /// The handle may be passed to <c>sound-stop</c>, <c>sound-set-volume!</c>,
    /// <c>sound-set-pitch!</c>, <c>sound-set-param!</c>, or discarded.
    ///
    /// <para>Scheme: <c>(sound-play "explosion" (vec3 10.0 0.0 5.0))</c></para>
    /// <para>Args: <c>name:string  position:Vector3</c></para>
    /// </summary>
    public static object Play(object[] args)
    {
        if (args.Length < 2
            || args[0] is not string name
            || args[1] is not Vector3 position)
            return "#f".Eval();
        
        long id = MintHandle();
        PublishAudio(new AudioCommand
        {
            Type     = AudioCommandType.Play,
            ClipId   = (int)id,
            Name     = name,
            Volume   = 1f,
            Pitch    = 1f,
            Position = position,
        });
        return id;
    }
    
    /// <summary>
    /// Begins non-positional (UI / music-sting) playback of a named sound
    /// asset.  Mints and returns a clip handle immediately (no suspend).
    ///
    /// <para>Scheme: <c>(sound-play-2d "menu-select")</c></para>
    /// <para>Args: <c>name:string</c></para>
    /// </summary>
    public static object Play2D(object[] args)
    {
        if (args.Length == 0 || args[0] is not string name)
            return "#f".Eval();
        
        long id = MintHandle();
        PublishAudio(new AudioCommand
        {
            Type   = AudioCommandType.Play2D,
            ClipId = (int)id,
            Name   = name,
            Volume = 1f,
            Pitch  = 1f,
        });
        return id;
    }
    
    /// <summary>
    /// Halts a playing clip identified by a handle returned from
    /// <c>sound-play</c> or <c>sound-play-2d</c>.
    ///
    /// <para>Scheme: <c>(sound-stop handle)</c></para>
    /// <para>Args: <c>handle:long</c></para>
    /// </summary>
    public static object Stop(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type   = AudioCommandType.Stop,
            ClipId = (int)handle,
        });
        return "nil".Eval();
    }
    
    /// <summary>
    /// Panic-stops every active voice.  No arguments.
    ///
    /// <para>Scheme: <c>(sound-stop-all)</c></para>
    /// </summary>
    public static object StopAll(object[] args)
    {
        PublishAudio(new AudioCommand { Type = AudioCommandType.StopAll });
        return "nil".Eval();
    }
    
    // =======================================================================
    // PER-VOICE PARAMETERS
    // =======================================================================
    
    /// <summary>
    /// Adjusts the volume of a playing clip.
    ///
    /// <para>Scheme: <c>(sound-set-volume! handle 0.5)</c></para>
    /// <para>Args: <c>handle:long  volume:float  [0, 1]</c></para>
    /// </summary>
    public static object SetVolume(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle)
            return "#f".Eval();
        
        float volume = ToFloat(args[1]);
        PublishAudio(new AudioCommand
        {
            Type   = AudioCommandType.SetVolume,
            ClipId = (int)handle,
            Volume = Math.Clamp(volume, 0f, 1f),
        });
        return "nil".Eval();
    }
    
    /// <summary>
    /// Adjusts the pitch ratio of a playing clip.
    /// 1.0 = normal speed; 2.0 = one octave up.
    ///
    /// <para>Scheme: <c>(sound-set-pitch! handle 1.5)</c></para>
    /// <para>Args: <c>handle:long  pitch:float</c></para>
    /// </summary>
    public static object SetPitch(object[] args)
    {
        if (args.Length < 2 || args[0] is not long handle)
            return "#f".Eval();
 
        PublishAudio(new AudioCommand
        {
            Type   = AudioCommandType.SetPitch,
            ClipId = (int)handle,
            Pitch  = Math.Max(0f, ToFloat(args[1])),
        });
        return "nil".Eval();
    }
    
    /// <summary>
    /// Sets a named RTPC / parameter on a playing clip.
    ///
    /// <para>Scheme: <c>(sound-set-param! handle "distance-blend" 0.75)</c></para>
    /// <para>Args: <c>handle:long  param-name:string  value:float</c></para>
    /// </summary>
    public static object SetParam(object[] args)
    {
        if (args.Length < 3
            || args[0] is not long handle
            || args[1] is not string paramName)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type   = AudioCommandType.SetParam,
            ClipId = (int)handle,
            Name   = paramName,
            Value  = ToFloat(args[2]),
        });
        return "nil".Eval();
    }
    
    // =======================================================================
    // MUSIC
    // =======================================================================
    
    /// <summary>
    /// Starts a named background music track.
    ///
    /// <para>Scheme: <c>(music-play "overworld-theme")</c></para>
    /// <para>Args: <c>track-name:string</c></para>
    /// </summary>
    public static object MusicPlay(object[] args)
    {
        if (args.Length == 0 || args[0] is not string trackName)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type = AudioCommandType.MusicPlay,
            Name = trackName,
        });
        return "nil".Eval();
    }
    
    /// <summary>
    /// Fades out and stops the current music track.
    ///
    /// <para>Scheme: <c>(music-stop 2.0)</c></para>
    /// <para>Args: <c>fade-out-seconds:float</c></para>
    /// </summary>
    public static object MusicStop(object[] args)
    {
        if (args.Length == 0)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type  = AudioCommandType.MusicStop,
            Value = Math.Max(0f, ToFloat(args[0])),
        });
        return "nil".Eval();
    }
    
    /// <summary>
    /// Sets the adaptive music intensity driver value.
    /// The host maps this scalar to whichever mixing layer controls energy.
    ///
    /// <para>Scheme: <c>(music-set-intensity! 0.8)</c></para>
    /// <para>Args: <c>intensity:float  [0, 1]</c></para>
    /// </summary>
    public static object MusicSetIntensity(object[] args)
    {
        if (args.Length == 0)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type  = AudioCommandType.MusicSetIntensity,
            Value = Math.Clamp(ToFloat(args[0]), 0f, 1f),
        });
        return "nil".Eval();
    }
    
    // =======================================================================
    // LISTENER
    // =======================================================================
    
    /// <summary>
    /// Updates the audio listener's world-space transform.
    /// Typically called once per frame from the camera process.
    ///
    /// <para>Scheme: <c>(set-listener-pos! pos fwd up)</c></para>
    /// <para>Args: <c>position:Vector3  forward:Vector3  up:Vector3</c></para>
    /// </summary>
    public static object SetListenerPos(object[] args)
    {
        if (args.Length < 3
            || args[0] is not Vector3 position
            || args[1] is not Vector3 forward
            || args[2] is not Vector3 up)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type     = AudioCommandType.SetListenerPos,
            Position = position,
            Forward  = forward,
            Up       = up,
        });
        return "nil".Eval();
    }
    
    // =======================================================================
    // AMBIENCE
    // =======================================================================
    
    /// <summary>
    /// Applies a named reverb preset at a given wet level.
    ///
    /// <para>Scheme: <c>(set-reverb! "cave" 0.6)</c></para>
    /// <para>Args: <c>preset-name:string  wet-level:float  [0, 1]</c></para>
    /// </summary>
    public static object SetReverb(object[] args)
    {
        if (args.Length < 2 || args[0] is not string presetName)
            return "#f".Eval();
        
        PublishAudio(new AudioCommand
        {
            Type  = AudioCommandType.SetReverb,
            Name  = presetName,
            Value = Math.Clamp(ToFloat(args[1]), 0f, 1f),
        });
        return "nil".Eval();
    }
    
    // =======================================================================
    // DIALOG / VO
    // =======================================================================
    
    /// <summary>
    /// Plays a VO line identified by a dialog asset id.  Mints and returns a
    /// handle immediately (no suspend).  The calling process's handle is
    /// forwarded to the host so it can dispatch a <c>send-event</c> to that
    /// process when the line finishes, routed via
    /// <c>StateDefinition.EventProc</c>.  No C# callback table is retained.
    ///
    /// <para>Scheme: <c>(dialog-play "ellie-warning")</c></para>
    /// <para>Args: <c>dialog-id:string</c></para>
    /// </summary>
    public static object DialogPlay(object[] args)
    {
        if (args.Length == 0 || args[0] is not string dialogId)
            return "#f".Eval();
        
        long id          = MintHandle();
        int  procHandle  = 0;
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is not null)
            procHandle = (int)(proc.Handle & 0x7FFF_FFFF);
        
        PublishAudio(new AudioCommand
        {
            Type          = AudioCommandType.DialogPlay,
            ClipId        = (int)id,
            Name          = dialogId,
            ProcessHandle = procHandle,
        });
        return id;
    }
    
    /// <summary>
    /// Asks the host whether the VO line identified by a handle is still
    /// playing.  Suspends the calling process for one frame.  Returns
    /// <c>#t</c>, <c>#f</c>, or <c>#f</c> if called outside a process.
    ///
    /// <para>Scheme: <c>(dialog-playing? handle)</c></para>
    /// <para>Args: <c>handle:long</c></para>
    /// </summary>
    public static object IsDialogPlaying(object[] args)
    {
        if (args.Length == 0 || args[0] is not long handle)
            return "#f".Eval();
        
        object? result = Query(QDialogIsPlaying, param1: (int)handle);
        return result is bool b ? b : "#f".Eval();
    }
}
