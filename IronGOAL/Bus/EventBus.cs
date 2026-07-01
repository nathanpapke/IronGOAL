using System.Threading.Channels;

namespace IronGOAL.Bus;

public sealed class EventBus
{
    // =======================================================================
    // PRIVATE CHANNEL STORAGE
    // =======================================================================
    // Each field is the full Channel<T> (both reader and writer ends).
    // Only the writer end is used inside this class; the reader end is
    // returned through the public properties below.
    
    private readonly Channel<TransformCommand>           _transformChannel;
    private readonly Channel<AudioCommand>               _audioChannel;
    private readonly Channel<GameEvent>                  _gameEventChannel;
    private readonly Channel<Timestamped<DebugCommand>>  _debugChannel;
    private readonly Channel<MemoryEvent>                _memoryChannel;
    
    // =======================================================================
    // DROP COUNTERS
    // =======================================================================
    // Incremented (via Interlocked) whenever a Publish* call on a Wait-mode
    // channel drops a command because no ScriptProcess context was present.
    //
    // Audio, GameEvent, and Debug channels use DropOldest/DropNewest and
    // never reach this path - their drops are intentional by design and
    // counted by the channel itself.
    //
    // The host reads these via the public properties below to surface
    // backpressure diagnostics without polling the channels themselves.
    // Counters are monotonically increasing and never reset.
    
    private static long _transformDropCount;
    private static long _memoryDropCount;
    
    /// <summary>
    /// Total transform commands dropped because the publish call had no
    /// ScriptProcess context.  Monotonically increasing; never resets.
    /// </summary>
    public static long TransformDropCount  => Interlocked.Read(ref _transformDropCount);
    
    /// <summary>
    /// Total memory events dropped because the publish call had no
    /// ScriptProcess context.  Monotonically increasing; never resets.
    /// </summary>
    public static long MemoryDropCount  => Interlocked.Read(ref _memoryDropCount);
    
    // =======================================================================
    // CONSTRUCTION
    // =======================================================================
    // Capacities are tunable at startup via GOAL runtime config so the host
    // can adjust for its own frame budget without recompiling the library.
    // The defaults here match the architectural spec.
    
    public EventBus(
        int transformCapacity = 4096,
        int audioCapacity     = 1024,
        int gameEventCapacity = 512,
        int debugCapacity     = 256,
        int memoryCapacity    = 128)
    {
        // Render - Wait on full.
        // The kernel must not drop draw calls.  When the channel is full,
        // the calling ScriptProcess is suspended via process-suspend until
        // space is available - no .NET thread is ever blocked.  A publish
        // call with no process context (host-originated code) increments
        // RenderDropCount and returns immediately; this is the only path
        // on which render data is lost.
        // SingleReader = false because some engines drain the render channel
        // from a dedicated render thread separate from the main thread.
        _transformChannel = Channel.CreateBounded<TransformCommand>(
            new BoundedChannelOptions(transformCapacity)
            {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = false,
            });
        
        // Audio - DropOldest on full.
        // A SetPosition command from three frames ago is worthless; dropping
        // it is correct. Play/Stop commands at high frequency are unusual
        // enough that the 1024 default capacity prevents meaningful loss.
        _audioChannel = Channel.CreateBounded<AudioCommand>(
            new BoundedChannelOptions(audioCapacity)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
            });
        
        // GameEvent - DropNewest on full.
        // Unlike audio, ordering and completeness of game events matter -
        // an EntitySpawn that arrives out of order with its EntityKill is
        // worse than a missed spawn.  DropNewest lets the existing queue
        // drain in order rather than overwriting with newer events.
        _gameEventChannel = Channel.CreateBounded<GameEvent>(
            new BoundedChannelOptions(gameEventCapacity)
            {
                FullMode     = BoundedChannelFullMode.DropNewest,
                SingleWriter = true,
                SingleReader = true,
            });
        
        // Debug - DropOldest on full.
        // Debug output is best-effort.  The kernel must never stall waiting
        // for a log line to be consumed; stale messages are less useful than
        // recent ones, so dropping the oldest is correct.
        _debugChannel = Channel.CreateBounded<Timestamped<DebugCommand>>(
            new BoundedChannelOptions(debugCapacity)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
            });
        
        // Memory - Wait on full.
        // Memory events must not be dropped; a missed kfree makes heap
        // accounting drift.  The 128 default capacity is generous relative
        // to how often scripts allocate - if this blocks in practice the
        // host profiler is not draining fast enough.
        _memoryChannel = Channel.CreateBounded<MemoryEvent>(
            new BoundedChannelOptions(memoryCapacity)
            {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true,
            });
    }
    
    // =======================================================================
    // PUBLIC READ SURFACE (forwarded through GoalRuntime to the host)
    // =======================================================================

    public ChannelReader<TransformCommand>          TransformCommands => _transformChannel.Reader;
    public ChannelReader<AudioCommand>              AudioCommands  => _audioChannel.Reader;
    public ChannelReader<GameEvent>                 GameEvents     => _gameEventChannel.Reader;
    public ChannelReader<Timestamped<DebugCommand>> DebugCommands  => _debugChannel.Reader;
    public ChannelReader<MemoryEvent>               MemoryEvents   => _memoryChannel.Reader;
    
    // =======================================================================
    // INTERNAL WRITE SURFACE (called only by Kernel)
    // =======================================================================
    // DropOldest/DropNewest channels use void TryWrite - their drops are
    // intentional and callers do not need to observe them.
    //
    // Wait-mode channels (Render, Memory, Physics) return bool so their
    // per-backing-class publish wrappers can implement the process-suspend
    // retry loop.  Callers must NOT call these directly - use the wrappers
    // in GraphicsSystem, MemoryArena, and PhysicsSystem respectively, which
    // own the suspend/retry/drop-counter logic.
    //
    // The process-suspend retry pattern in those wrappers:
    //   if (!bus.PublishX(cmd)) {
    //       proc.SetPredicate(() => bus.PublishX(cmd));
    //       proc.YieldToScheduler();
    //       proc.ClearPredicate();
    //   }
    // This suspends only the script process, never a .NET thread.
    
    /// <summary>
    /// Single non-blocking enqueue attempt.  Returns <c>true</c> if
    /// accepted, <c>false</c> if the channel is currently full.
    /// Callers must retry via process suspension - see wrapper in
    /// <c>GraphicsSystem</c>.
    /// </summary>
    internal bool PublishTransform(TransformCommand cmd) =>
        _transformChannel.Writer.TryWrite(cmd);
    
    internal void PublishAudio(AudioCommand cmd) =>
        _audioChannel.Writer.TryWrite(cmd);
    
    internal void PublishGameEvent(GameEvent evt) =>
        _gameEventChannel.Writer.TryWrite(evt);
    
    internal void PublishDebug(DebugCommand cmd, long frameId, float gameTime) =>
        _debugChannel.Writer.TryWrite(new Timestamped<DebugCommand>
        {
            Command  = cmd,
            FrameId  = frameId,
            GameTime = gameTime,
        });
    
    /// <summary>
    /// Single non-blocking enqueue attempt.  Returns <c>true</c> if
    /// accepted, <c>false</c> if the channel is currently full.
    /// Callers must retry via process suspension - see wrapper in
    /// <c>MemoryArena</c>.
    /// </summary>
    internal bool PublishMemory(MemoryEvent evt) =>
        _memoryChannel.Writer.TryWrite(evt);
    
    // =======================================================================
    // NO-CONTEXT DROP HELPERS
    // =======================================================================
    // Called by per-backing-class publish wrappers when TryWrite fails and
    // no ScriptProcess context is present to suspend. Records the drop so
    // the host can observe it via the public drop-count properties above.
    
    internal static void RecordTransformDrop()   => Interlocked.Increment(ref _transformDropCount);
    internal static void RecordMemoryDrop()      => Interlocked.Increment(ref _memoryDropCount);
    
    // =======================================================================
    // SHUTDOWN
    // =======================================================================
    // Called by Kernel.Dispose().  Completing a channel signals to any
    // awaiting ConsumeAsync loops that no further items will arrive,
    // allowing them to exit cleanly without cancellation tokens.
    
    internal void Complete()
    {
        _transformChannel.Writer.TryComplete();
        _audioChannel.Writer.TryComplete();
        _gameEventChannel.Writer.TryComplete();
        _debugChannel.Writer.TryComplete();
        _memoryChannel.Writer.TryComplete();
    }
}
