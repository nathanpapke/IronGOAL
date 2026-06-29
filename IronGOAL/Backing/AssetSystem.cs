using System;
using System.Collections.Concurrent;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;
using IronGOAL.Scripting;

namespace IronGOAL.Backing;

public class AssetSystem
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
    
    // Key   = process handle of the suspended ScriptProcess
    // Value = the host-issued asset handle (long) deposited by the host,
    //         or null on load failure.
    //
    // A key being present (even with a null value) is the signal that the
    // answer has arrived.  TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a load result for a suspended process.
    /// Pass the host-issued asset handle as <paramref name="value"/>, or
    /// <c>null</c> on failure.  Writing the key wakes the process on the
    /// next scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // TODO: Check the opcodes.
    // Load queries — 700–709
    private const int OpLoad       = 700;
    private const int OpLoadObject = 701;
    private const int OpLoadBinary = 702;
    private const int OpDgoLoad    = 703;
    
    // Unload command — 710
    private const int OpUnload = 710;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    /// <summary>
    /// Consistent with every other backing class that passes names through
    /// <c>GameEvent</c> integer params.
    /// </summary>
    private static int Hash(string s) => s.GetHashCode(StringComparison.Ordinal);
    
    /// <summary>
    /// Publishes a load <see cref="GameEventType.EntityQuery"/> event and
    /// suspends the calling process until the host deposits an answer via
    /// <see cref="DeliverQueryResponse"/>.
    ///
    /// <para>
    /// <c>Param3</c> carries the requesting process handle automatically
    /// so the host knows which process to answer; callers must not use it
    /// for data.  Must be called from a running <see cref="ScriptProcess"/>
    /// context.  Returns <c>null</c> when called outside a process.
    /// </para>
    /// </summary>
    private static object? QueryLoad(int opcode, string path)
    {
        ScriptProcess? proc = ProcessScheduler.CurrentProcess;
        if (proc is null)
        {
            Console.Error.WriteLine(
                "[AssetSystem] Query called outside a running process — returning null.");
            return null;
        }
        
        long handle = proc.Handle;
        
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntityQuery,
            EntityId = -1,
            Param0   = opcode,
            Param1   = Hash(path),
            Param2   = 0,
            Param3   = (int)(handle & 0x7FFF_FFFF),
        });
        
        proc.SetPredicate(() => _queryResponses.ContainsKey(handle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _queryResponses.TryRemove(handle, out object? value);
        return value;
    }
    
    /// <summary>
    /// Publishes an <see cref="GameEventType.EntitySetState"/> command.
    /// Fire-and-return; no process suspension.
    /// </summary>
    private static void PublishSetState(int opcode, int param1)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntitySetState,
            EntityId = -1,
            Param0   = opcode,
            Param1   = param1,
        });
    
    // =======================================================================
    // ASSET FUNCTIONS
    // =======================================================================
    
    /// <summary>
    /// Loads a GOAL script object from the given path.  Suspends the calling
    /// process until the host responds with an asset handle.
    ///
    /// <para>Scheme: <c>(load path)</c> → asset handle or <c>#f</c></para>
    /// </para>
    /// </summary>
    public static object Load(object[] args)
    {
        if (args.Length < 1 || args[0] is not string path)
            return "#f".Eval();
        
        object? result = QueryLoad(OpLoad, path);
        return result is long handle ? (object)handle : "#f".Eval();
    }
    
    /// <summary>
    /// Loads any GOAL object from the given path.  Semantically equivalent
    /// to <c>load</c> but signals to the host that the target may be any
    /// object type, not specifically a script file.
    ///
    /// <para>Scheme: <c>(loado path)</c> → asset handle or <c>#f</c></para>
    /// </summary>
    public static object LoadObject(object[] args)
    {
        if (args.Length < 1 || args[0] is not string path)
            return "#f".Eval();
 
        object? result = QueryLoad(OpLoadObject, path);
        return result is long handle ? (object)handle : "#f".Eval();
    }
    
    /// <summary>
    /// Loads a raw binary blob from the given path.  The host treats the
    /// payload as opaque bytes with no GOAL object header.
    ///
    /// <para>Scheme: <c>(loadb path)</c> → asset handle or <c>#f</c></para>
    /// </summary>
    public static object LoadBinary(object[] args)
    {
        if (args.Length < 1 || args[0] is not string path)
            return "#f".Eval();
        
        object? result = QueryLoad(OpLoadBinary, path);
        return result is long handle ? (object)handle : "#f".Eval();
    }
    
    /// <summary>
    /// Loads a DGO (Disc Game Object) archive by name.  In the original
    /// GOAL kernel this triggers the OVERLORD I/O subsystem to read a
    /// named <c>.DGO</c> container from disc, link each object file in
    /// load order, and run each Top Level segment.  In IronGOAL the host
    /// is responsible for the equivalent managed archive pipeline.
    ///
    /// <para>Scheme: <c>(dgo-load name)</c> → archive handle or <c>#f</c></para>
    /// </para>
    /// </summary>
    public static object DgoLoad(object[] args)
    {
        if (args.Length < 1 || args[0] is not string name)
            return "#f".Eval();
 
        object? result = QueryLoad(OpDgoLoad, name);
        return result is long handle ? (object)handle : "#f".Eval();
    }
    
    /// <summary>
    /// Releases a previously loaded asset by handle.  Fire-and-return; the
    /// host decrements the reference count or frees the resource on its next
    /// drain cycle.
    ///
    /// <para>Scheme: <c>(unload handle)</c> → <c>#t</c></para>
    /// </summary>
    public static object Unload(object[] args)
    {
        if (args.Length < 1 || args[0] is not long assetHandle)
            return "#f".Eval();
        
        PublishSetState(OpUnload, (int)(assetHandle & 0x7FFF_FFFF));
        return "#t".Eval();
    }
}
