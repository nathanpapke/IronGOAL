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
    
    private readonly Channel<RenderCommand>              _renderChannel;
    private readonly Channel<AudioCommand>               _audioChannel;
    private readonly Channel<GameEvent>                  _gameEventChannel;
    private readonly Channel<Timestamped<DebugCommand>>  _debugChannel;
    private readonly Channel<MemoryEvent>                _memoryChannel;
    
    // =======================================================================
    // CONSTRUCTION
    // =======================================================================
    // Capacities are tunable at startup via GOAL runtime config so the host
    // can adjust for its own frame budget without recompiling the library.
    // The defaults here match the architectural spec.
    
    public EventBus(
        int renderCapacity    = 4096,
        int audioCapacity     = 1024,
        int gameEventCapacity = 512,
        int debugCapacity     = 256,
        int memoryCapacity    = 128)
    {
        // Render - Wait on full.
        // The kernel must not drop draw calls; if the host render pass falls
        // behind, backpressure propagates into Tick() via the Wait mode.
        // SingleReader = false because some engines drain the render channel
        // from a dedicated render thread separate from the main thread.
        _renderChannel = Channel.CreateBounded<RenderCommand>(
            new BoundedChannelOptions(renderCapacity)
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
        // Unlike audio, ordering and completeness of game events matters -
        // an EntitySpawn that arrives out of order with its EntityKill is
        // worse than a missed spawn. DropNewest lets the existing queue
        // drain in order rather than overwriting with newer events.
        _gameEventChannel = Channel.CreateBounded<GameEvent>(
            new BoundedChannelOptions(gameEventCapacity)
            {
                FullMode     = BoundedChannelFullMode.DropNewest,
                SingleWriter = true,
                SingleReader = true,
            });
        
        // Debug - DropOldest on full.
        // Debug output is best-effort. The kernel must never stall waiting
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
        // accounting drift. The 128 default capacity is generous relative
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

    public ChannelReader<RenderCommand>             RenderCommands => _renderChannel.Reader;
    public ChannelReader<AudioCommand>              AudioCommands  => _audioChannel.Reader;
    public ChannelReader<GameEvent>                 GameEvents     => _gameEventChannel.Reader;
    public ChannelReader<Timestamped<DebugCommand>> DebugCommands  => _debugChannel.Reader;
    public ChannelReader<MemoryEvent>               MemoryEvents   => _memoryChannel.Reader;
    
    // =======================================================================
    // INTERNAL WRITE SURFACE (called only by Kernel)
    // =======================================================================
    // TryWrite is used on DropOldest/DropNewest channels - it never blocks
    // and returns false only if the item was dropped, which is intentional.
    // WriteAsync is used on Wait channels - it yields the calling coroutine
    // until space is available rather than blocking a thread.
    //
    // All five are marked internal so nothing outside IronGOAL.dll can
    // publish to a channel directly.
    
    internal void PublishRender(RenderCommand cmd) =>
        _renderChannel.Writer.TryWrite(cmd);
    
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
    
    internal void PublishMemory(MemoryEvent evt) =>
        _memoryChannel.Writer.TryWrite(evt);
    
    // =======================================================================
    // SHUTDOWN
    // =======================================================================
    // Called by Kernel.Dispose(). Completing a channel signals to any
    // awaiting ConsumeAsync loops that no further items will arrive,
    // allowing them to exit cleanly without cancellation tokens.
    
    internal void Complete()
    {
        _renderChannel.Writer.TryComplete();
        _audioChannel.Writer.TryComplete();
        _gameEventChannel.Writer.TryComplete();
        _debugChannel.Writer.TryComplete();
        _memoryChannel.Writer.TryComplete();
    }
}
