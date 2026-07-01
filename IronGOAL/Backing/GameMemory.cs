using System.Collections.Concurrent;
using IronScheme;
 
using IronGOAL.Bus;

namespace IronGOAL.Backing;

public class GameMemory
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
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Standard suspend/wake table - identical pattern to PhysicsSystem,
    // AnimationSystem, EntitySystem, etc.
    // Key   = process handle of the suspended ScriptProcess.
    // Value = the answer the host deposited via DeliverQueryResponse.
    // A key being present (even with a null value) signals answer arrival.
    // TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a heap-stats query answer for a
    /// suspended process.  Writing the key wakes the process on the next
    /// scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // =======================================================================
    // KMEMOPEN SCOPE STACK
    // =======================================================================
    
    // Tracks open kmemopen arena scopes.  Each entry is (arenaName, scopeTag)
    // matching what the host receives in the paired MemoryEvents.
    // Stack discipline is enforced: kmemclose pops the most recent open scope.
    //
    // Thread-safety: Lock on _memOpenStack itself - contention is negligible
    // because kmemopen/kmemclose are called at level-load cadence, not per
    // frame.
 
    private static readonly Stack<(string Arena, string Tag)> _memOpenStack = new();
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    /// <summary>
    /// Converts an IronScheme numeric value to <c>float</c>.
    /// IronScheme boxes float literals as <see cref="double"/>, so both
    /// cases must be handled - see the IronScheme boxing note in the
    /// architecture doc.
    /// </summary>
    private static float AsFloat(object o) => o switch
    {
        double d => (float)d,
        float  f => f,
        _        => 0f,
    };
    
    /// <summary>
    /// Maps a heap-name string from GOAL script to a
    /// <see cref="MemoryArenaId"/> enum value.
    /// Unrecognized names fall back to <see cref="MemoryArenaId.Global"/>.
    /// </summary>
    private static MemoryArenaId ParseArena(string name) =>
        name.ToLowerInvariant() switch
        {
            "global" => MemoryArenaId.Global,
            "stack"  => MemoryArenaId.Stack,
            "level"  => MemoryArenaId.Level,
            "debug"  => MemoryArenaId.Debug,
            _        => MemoryArenaId.Global,
        };
    
    /// <summary>
    /// Publishes a <see cref="MemoryEvent"/> via the Wait-mode memory channel,
    /// suspending the calling <see cref="ScriptProcess"/> if the channel is
    /// full, or dropping and counting when no process context is present.
    /// </summary>
    private static void PublishMemory(MemoryEvent evt)
    {
        if (_bus is null) return;
        
        // Fast path - channel has room; overwhelmingly common.
        if (_bus.PublishMemory(evt)) return;
        
        // Slow path - channel is full.
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            // No process context: drop and record, consistent with the
            // no-context policy for all Wait-mode channels.
            EventBus.RecordMemoryDrop();
            return;
        }
        
        // Suspend the script process (not the .NET thread) until a slot opens.
        proc.SetPredicate(() => _bus.PublishMemory(evt));
        proc.YieldToScheduler();
        proc.ClearPredicate();
    }
    
    /// <summary>
    /// Publishes a <see cref="GameEvent"/> query to the bus and suspends the
    /// calling process until the host deposits an answer via
    /// <see cref="DeliverQueryResponse"/>.  Returns the deposited value, or
    /// <c>null</c> if called outside a <see cref="ScriptProcess"/>.
    /// </summary>
    private static object? Query(Opcode op, int param1 = 0, int param2 = 0)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[GameMemory] Query called outside a running process - returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = -1,
            Param0   = (int)op,
            Param1   = param1,
            Param2   = param2,
            Param3   = (int)(handle & 0x7FFF_FFFF),
        });
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    // =======================================================================
    // MEMORY ALLOCATION
    // =======================================================================
    
    /// <summary>
    /// Allocates a block of memory on the named heap arena and returns a
    /// typed handle (<c>long</c>), or <c>#f</c> on failure (arena exhausted).
    ///
    /// <para>Scheme: <c>(kmalloc heap size flags name)</c></para>
    /// </summary>
    public static object Alloc(object[] args)
    {
        if (args.Length < 4
            || args[0] is not string arenaName
            || args[3] is not string label)
            return "#f".Eval();
 
        int size  = args[1] is long l1 ? (int)l1
            : args[1] is int  i1 ? i1
            : 0;
        int flags = args[2] is long l2 ? (int)l2
            : args[2] is int  i2 ? i2
            : 0;
        
        if (size <= 0) return "#f".Eval();
        
        MemoryArenaId arena = ParseArena(arenaName);
        
        // Publish the alloc intent so the host can observe it even if the
        // process is not yet wired up.
        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.Alloc,
            Arena   = arena,
            Address = 0,   // host fills in the real address
            Size    = size,
        });
        
        // Suspend until host deposits the handle (or null for failure).
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null) return "#f".Eval();
        
        long procHandle = proc.Handle;
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = -1,
            Param0   = (int)Opcode.Alloc,
            Param1   = size,
            Param2   = flags,
            Param3   = (int)(procHandle & 0x7FFF_FFFF),
        });
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(procHandle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(procHandle, out object? value);
        
        // null or missing value -> AllocFailed path; return #f.
        if (value is null) return "#f".Eval();
        return value;
    }
    
    /// <summary>
    /// Alias of <c>kmalloc</c> registered as the original GOAL <c>malloc</c>
    /// symbol.  Same four-argument signature; delegates directly to
    /// <see cref="Alloc"/>.
    ///
    /// <para>Scheme: <c>(malloc heap size flags name)</c></para>
    /// </summary>
    public static object ManagedAlloc(object[] args) => Alloc(args);
    
    /// <summary>
    /// Releases a handle back to its originating arena.
    ///
    /// <para>Scheme: <c>(kfree handle)</c></para>
    /// </summary>
    public static object Free(object[] args)
    {
        if (args.Length < 1 || args[0] is not long handle)
            return "#f".Eval();
        
        // Notify the host - fire-and-return; no suspension needed.
        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.Free,
            Arena   = MemoryArenaId.Global, // host resolves real arena from handle
            Address = (int)(handle & 0x7FFF_FFFF),
            Size    = 0,
        });
        
        return "#t".Eval();
    }
    
    
    /// <summary>
    /// Opens a scoped allocation context on the named arena.  All subsequent
    /// allocations on that arena until the matching <c>kmemclose</c> are
    /// grouped under this scope; the host can rewind the arena to the
    /// pre-open watermark when the scope closes.
    ///
    /// <para>Scheme: <c>(kmemopen heap name)</c></para>
    /// </summary>
    public static object MemOpen(object[] args)
    {
        if (args.Length < 2
            || args[0] is not string arenaName
            || args[1] is not string scopeTag)
            return "#f".Eval();
        
        MemoryArenaId arena = ParseArena(arenaName);
        
        lock (_memOpenStack)
        {
            _memOpenStack.Push((arenaName, scopeTag));
        }
        
        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.Alloc, // Alloc with Size=0 signals open
            Arena   = arena,
            Address = 0,
            Size    = 0,
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Closes the most recently opened <c>kmemopen</c> scope.
    ///
    /// <para>Scheme: <c>(kmemclose)</c></para>
    /// </summary>
    public static object MemClose(object[] args)
    {
        (string arenaName, string _) scope;
        
        lock (_memOpenStack)
        {
            if (_memOpenStack.Count == 0) return "#f".Eval();
            scope = _memOpenStack.Pop();
        }
        
        MemoryArenaId arena = ParseArena(scope.arenaName);
        
        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.ArenaReset,
            Arena   = arena,
            Address = 0,
            Size    = 0,
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Signals the host that a DMA transfer to the IOP has been requested.
    /// This is a fire-and-forget notification; no allocation handle is returned.
    /// The host may treat the event as a data-transfer signal or no-op it.
    /// 
    /// <para>Scheme: <c>(dma-to-iop dest src size)</c></para>
    /// </summary>
    public static object DmaToIop(object[] args)
    {
        // Require at least src and size; dest is IOP-side and unused in managed code.
        if (args.Length < 3) return "#f".Eval();

        int src  = args[1] is long ls ? (int)ls : args[1] is int is_ ? is_ : 0;
        int size = args[2] is long lz ? (int)lz : args[2] is int iz  ? iz  : 0;

        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.DmaTransfer,
            Arena   = MemoryArenaId.Global,   // Not arena-scoped; sentinel value.
            Address = src,
            Size    = size,
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Allocates a typed object on the named arena with an explicit byte size.
    /// This is the general-purpose typed allocator; most GOAL
    /// <c>(new 'global ...)</c> expressions compile down to this.
    ///
    /// <para>Scheme: <c>(new-dynamic-structure heap type size)</c></para>
    /// </summary>
    public static object NewDynamicStructure(object[] args)
    {
        if (args.Length < 3
            || args[0] is not string arenaName
            || args[1] is not string typeName)
            return "#f".Eval();
        
        int size = args[2] is long l ? (int)l
            : args[2] is int  i ? i
            : 0;
        
        if (size <= 0) return "#f".Eval();
        
        // Reuse Alloc: pack the type name hash into the flags slot.
        // The host reads Param2 as a type hint when it allocates the object.
        int typeHash = typeName.GetHashCode(StringComparison.Ordinal);
        
        return Alloc(new object[]
        {
            arenaName,
            (long)size,
            (long)typeHash,
            typeName,   // debug label = type name
        });
    }
    
    // =======================================================================
    // HEAP
    // =======================================================================
    
    /// <summary>
    /// Queries the host for the current number of bytes allocated on the
    /// named arena.  Suspends the calling process until the host answers.
    ///
    /// <para>Scheme: <c>(heap-bytes-used heap)</c></para>
    /// </summary>
    public static object HeapBytesUsed(object[] args)
    {
        if (args.Length < 1 || args[0] is not string arenaName)
            return "#f".Eval();
        
        MemoryArenaId arena = ParseArena(arenaName);
        
        object? result = Query(Opcode.HeapBytesUsed, (int)arena);
        if (result is null) return "#f".Eval();
        return result;
    }
    
    /// <summary>
    /// Queries the host for the total capacity in bytes of the named arena.
    /// Suspends the calling process until the host answers.
    ///
    /// <para>Scheme: <c>(heap-bytes-total heap)</c></para>
    /// </summary>
    public static object HeapBytesTotal(object[] args)
    {
        if (args.Length < 1 || args[0] is not string arenaName)
            return "#f".Eval();
        
        MemoryArenaId arena = ParseArena(arenaName);
        
        object? result = Query(Opcode.HeapBytesTotal, (int)arena);
        if (result is null) return "#f".Eval();
        return result;
    }
    
    /// <summary>
    /// Resets the named arena to its empty state, reclaiming all allocated
    /// bytes.  This is the GOAL level-heap rewind used when a level unloads.
    ///
    /// <para>Scheme: <c>(heap-reset! heap)</c></para>
    /// </summary>
    public static object HeapReset(object[] args)
    {
        if (args.Length < 1 || args[0] is not string arenaName)
            return "#f".Eval();
        
        MemoryArenaId arena = ParseArena(arenaName);
        
        // Also clear any orphaned kmemopen scopes on this arena.
        lock (_memOpenStack)
        {
            // Rebuild the stack without scopes for this arena.
            var temp = new Stack<(string Arena, string Tag)>(_memOpenStack);
            _memOpenStack.Clear();
            foreach (var entry in temp)
            {
                if (!string.Equals(entry.Arena, arenaName,
                        StringComparison.OrdinalIgnoreCase))
                    _memOpenStack.Push(entry);
            }
        }
        
        PublishMemory(new MemoryEvent
        {
            Type    = MemoryEventType.ArenaReset,
            Arena   = arena,
            Address = 0,
            Size    = 0,
        });
        
        return "#t".Eval();
    }
    
    // =======================================================================
    // SERIALIZE / DESERIALIZE
    // =======================================================================
    
    /// <summary>
    /// Requests that the host serialize the object identified by
    /// <c>handle</c> to a binary blob.  Fire-and-return; the host writes the
    /// result back through its own channel (not a query suspension).
    ///
    /// <para>Scheme: <c>(obj-serialize handle)</c></para>
    /// </summary>
    public static object Serialize(object[] args)
    {
        if (args.Length < 1 || args[0] is not long handle)
            return "#f".Eval();
        
        // Serialize is a host-side operation - fire a GameEvent and return.
        // The host writes the result to its own output mechanism (file, buffer)
        // without needing to suspend the script.
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntitySetState,
            EntityId = (int)(handle & 0x7FFF_FFFF),
            Param0   = (int)Opcode.Serialize,
            Param1   = 0,
            Param2   = 0,
            Param3   = 0,
        });
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests that the host deserialize a binary blob identified by
    /// <c>blobHandle</c> into a new object of <c>typeName</c>.
    /// Fire-and-return; returns a new allocation handle or <c>#f</c>.
    ///
    /// <para>Scheme: <c>(obj-deserialize blob-handle type-name)</c></para>
    /// </summary>
    public static object Deserialize(object[] args)
    {
        if (args.Length < 2
            || args[0] is not long blobHandle
            || args[1] is not string typeName)
            return "#f".Eval();
        
        int typeHash = typeName.GetHashCode(StringComparison.Ordinal);
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntitySetState,
            EntityId = (int)(blobHandle & 0x7FFF_FFFF),
            Param0   = (int)Opcode.Deserialize,
            Param1   = typeHash,
            Param2   = 0,
            Param3   = 0,
        });
        
        return "#t".Eval();
    }
}
