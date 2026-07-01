using System.Threading.Channels;

namespace IronGOAL.Bus;

public static class ChannelDrain
{
    // =======================================================================
    // SYNCHRONOUS DRAIN
    // =======================================================================
    // TryRead returns false immediately when the channel is momentarily
    // empty, so this never blocks.  Call it from your render pass or tick
    // update where you cannot yield the thread.
    
    /// Drains all currently available commands and invokes the handler for
    /// each.  Returns the number of commands processed this call.
    public static int DrainSync<TCommand>(
        ChannelReader<TCommand> reader,
        Action<TCommand>        handler)
        where TCommand : struct
    {
        int count = 0;
        while (reader.TryRead(out TCommand cmd))
        {
            handler(cmd);
            count++;
        }
        return count;
    }
    
    /// Drains up to maxCommands commands per call.  Use this on the render
    /// channel when you want to enforce a per-frame processing budget and
    /// let Wait-mode backpressure signal the kernel to slow down if the
    /// host consistently cannot drain fast enough.
    public static int DrainSync<TCommand>(
        ChannelReader<TCommand> reader,
        Action<TCommand>        handler,
        int                     maxCommands)
        where TCommand : struct
    {
        int count = 0;
        while (count < maxCommands && reader.TryRead(out TCommand cmd))
        {
            handler(cmd);
            count++;
        }
        return count;
    }
    
    // =======================================================================
    // ASYNCH ONE-SHOT DRAIN
    // =======================================================================
    // Drains everything currently in the channel without awaiting new items.
    // Useful when your consumer runs on an async path (e.g. a task-based
    // update loop) but you don't want a long-running consumer task.
    
    /// Drains all commands currently available, awaiting each handler
    /// invocation.  Returns when the channel is momentarily empty.
    public static async ValueTask<int> DrainAsync<TCommand>(
        ChannelReader<TCommand>   reader,
        Func<TCommand, ValueTask> handler,
        CancellationToken         ct = default)
        where TCommand : struct
    {
        int count = 0;
        while (reader.TryRead(out TCommand cmd))
        {
            ct.ThrowIfCancellationRequested();
            await handler(cmd).ConfigureAwait(false);
            count++;
        }
        return count;
    }
    
    // =======================================================================
    // LONG-RUNNING CONSUMER
    // =======================================================================
    // ReadAllAsync yields the thread back to the scheduler between items,
    // making this safe to run on a dedicated Task without spinning a core.
    // The loop exits cleanly when EventBus.Complete() is called on shutdown,
    // because TryComplete() causes ReadAllAsync to stop enumerating.
    
    /// Continuously drains commands as they arrive, awaiting each handler.
    /// Runs until the channel is completed (on shutdown) or the token is
    /// canceled.  This is the correct pattern for the audio consumer thread
    /// and the IronGOAL.Console stdout debug sink.
    public static async Task ConsumeAsync<TCommand>(
        ChannelReader<TCommand>   reader,
        Func<TCommand, ValueTask> handler,
        CancellationToken         ct = default)
        where TCommand : struct
    {
        await foreach (TCommand cmd in reader.ReadAllAsync(ct).ConfigureAwait(false))
            await handler(cmd).ConfigureAwait(false);
    }
    
    // =======================================================================
    // DIAGNOSTIC HELPER
    // =======================================================================
    // Not used in production drain loops - useful in tests and the dev host
    // to assert channel state without consuming items.
    
    /// Returns the number of items currently waiting in the channel.
    /// Available in .NET 8 via CanCount / Count on the reader.
    public static int PeekCount<TCommand>(ChannelReader<TCommand> reader)
        where TCommand : struct
    {
        return reader.CanCount ? reader.Count : -1;
    }
}
