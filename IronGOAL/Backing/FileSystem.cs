using System.Collections.Concurrent;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;
using IronGOAL.Scripting;

namespace IronGOAL.Backing;

public class FileSystem
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
    // PENDING OPERATION STATE
    // =======================================================================
    
    // Holds the most recently enqueued mc-save / mc-load parameters.
    // mc-run reads these and publishes the query.  A second enqueue before
    // mc-run completes overwrites the first (GOAL single-slot semantics).
    //
    // -1 signals NO_OP (no pending operation).
    //
    // Thread-safety: lock on _pendingLock.  Contention is negligible because
    // save/load operations occur at user-action cadence, not per-frame.
    
    private static int _pendingOpcode  = -1;
    private static int _pendingCardIdx =  0;
    private static int _pendingFileIdx =  0;
    private static readonly object _pendingLock = new();
    
    // =======================================================================
    // LAST RESULT
    // =======================================================================
    
    // Stores the result code deposited by the host when the most recent
    // mc-run query completes.  mc-check-result reads this field.
    //
    // Initialized to 1 (McStatusCode::OK) so scripts that call mc-check-result
    // before any save/load see a clean state rather than BUSY (0).
    
    private static volatile int _lastResult = 1; // McStatusCode::OK
    
    // =======================================================================
    // QUERY RESPONSE TABLE
    // =======================================================================
    
    // Standard suspend/wake table - identical pattern to GameMemory,
    // PhysicsSystem, AnimationSystem, EntitySystem, etc.
    // Key   = process handle of the suspended ScriptProcess.
    // Value = the answer the host deposited via DeliverQueryResponse.
    // A key being present (even with a null value) signals answer arrival.
    // TryRemove retrieves and removes atomically.
    
    private static readonly ConcurrentDictionary<long, object?> _queryResponses = new();
    
    /// <summary>
    /// Called by the host to deliver a query answer for a suspended process.
    /// Writing the key wakes the process on the next scheduler tick.
    /// </summary>
    internal static void DeliverQueryResponse(long processHandle, object? value)
        => _queryResponses[processHandle] = value;
    
    // =======================================================================
    // INTERNAL HELPERS
    // =======================================================================
    
    private static void PublishCommand(Opcode op, int param1 = 0, int param2 = 0)
        => _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.EntitySetState,
            EntityId = -1,
            Param0   = (int)op,
            Param1   = param1,
            Param2   = param2,
            Param3   = 0,
        });
    
    /// <summary>
    /// Publishes a <see cref="GameEventType.EntityQuery"/> and suspends the
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
                "[FileSystem] Query called outside a running process - returning null.");
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
    
    private static int AsInt(object o) => o switch
    {
        long l => (int)l,
        int  i => i,
        _      => 0,
    };
    
    // =======================================================================
    // FILE OPERATIONS
    // =======================================================================
    
    /// <summary>
    /// Runs one iteration of the memory card state machine.
    /// If a save or load operation is pending (enqueued by
    /// <see cref="McSave"/> or <see cref="McLoad"/>), publishes that
    /// operation to the host as a suspending query and blocks until the
    /// host completes it and deposits a result code.  The result is stored
    /// in <c>_lastResult</c> for subsequent <see cref="McCheckResult"/>
    /// calls.  If no operation is pending, publishes a no-op notification
    /// and returns <c>#t</c> immediately without suspending.
    ///
    /// <para>Scheme: <c>(mc-run)</c></para>
    /// </summary>
    public static object McRun(object[] args)
    {
        int pendingOpcode;
        int cardIdx;
        int fileIdx;
        
        lock (_pendingLock)
        {
            pendingOpcode = _pendingOpcode;
            cardIdx       = _pendingCardIdx;
            fileIdx       = _pendingFileIdx;
            
            // Clear pending slot immediately - the operation is now in-flight.
            if (pendingOpcode != -1)
                _pendingOpcode = -1;
        }
        
        if (pendingOpcode == -1)
        {
            // NO_OP path - notify host and return without suspending, matching
            // GOAL's MC_run behaviour when op.operation == NO_OP.
            PublishCommand(Opcode.McRun);
            return "#t".Eval();
        }
        
        // Suspend until host completes the operation and deposits result code.
        object? result = Query(Opcode.McRun, cardIdx, fileIdx);
        
        // Store the result for mc-check-result.
        if (result is long rl)
            _lastResult = (int)rl;
        else if (result is int ri)
            _lastResult = ri;
        // If result is null or unexpected, leave _lastResult at its prior value.
        
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to format (initialize) the save storage for the
    /// given card slot.  Fire-and-return; the OpenGOAL PC port stubs this
    /// to succeed immediately, and IronGOAL does the same.
    ///
    /// <para>Scheme: <c>(mc-format card-idx)</c></para>
    /// </summary>
    public static object McFormat(object[] args)
    {
        int cardIdx = args.Length >= 1 ? AsInt(args[0]) : 0;
        PublishCommand(Opcode.McFormat, cardIdx);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to unformat (wipe) the save storage for the given
    /// card slot.  Debug use only in GOAL; fire-and-return.
    ///
    /// <para>Scheme: <c>(mc-unformat card-idx)</c></para>
    /// </summary>
    public static object McUnformat(object[] args)
    {
        int cardIdx = args.Length >= 1 ? AsInt(args[0]) : 0;
        PublishCommand(Opcode.McUnformat, cardIdx);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to create the save file directory/entry for the
    /// given slot.  The <c>data</c> pointer argument is a PS2-era scratch
    /// buffer with no managed meaning; it is accepted but ignored.
    /// Fire-and-return.
    ///
    /// <para>Scheme: <c>(mc-createfile param data)</c></para>
    /// </summary>
    public static object McCreateFile(object[] args)
    {
        int param = args.Length >= 1 ? AsInt(args[0]) : 0;
        // args[1] = data ptr - ignored.
        PublishCommand(Opcode.McCreateFile, param);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Enqueues a SAVE operation for the given card and file slot.
    /// The <c>save-data</c> and <c>save-summary-data</c> pointer arguments
    /// are PS2 managed-memory addresses with no managed meaning; they are
    /// accepted but ignored.  The host is responsible for reading its own
    /// in-memory game state when it processes the subsequent
    /// <see cref="McRun"/> query.  Fire-and-return.
    ///
    /// <para>Scheme: <c>(mc-save card-idx file-idx save-data save-summary-data)</c></para>
    /// </summary>
    public static object McSave(object[] args)
    {
        int cardIdx = args.Length >= 1 ? AsInt(args[0]) : 0;
        int fileIdx = args.Length >= 2 ? AsInt(args[1]) : 0;
        // args[2] = save-data ptr, args[3] = save-summary-data ptr - both ignored.
        
        lock (_pendingLock)
        {
            _pendingOpcode  = (int)Opcode.McSave;
            _pendingCardIdx = cardIdx;
            _pendingFileIdx = fileIdx;
        }
        
        PublishCommand(Opcode.McSave, cardIdx, fileIdx);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Enqueues a LOAD operation for the given card and file slot.
    /// The <c>data</c> pointer argument is a PS2 managed-memory destination
    /// with no managed meaning; it is accepted but ignored.  The host is
    /// responsible for depositing loaded data into its own structures when it
    /// processes the subsequent <see cref="McRun"/> query.  Fire-and-return.
    ///
    /// <para>Scheme: <c>(mc-load card-idx file-idx data)</c></para>
    /// </summary>
    public static object McLoad(object[] args)
    {
        int cardIdx = args.Length >= 1 ? AsInt(args[0]) : 0;
        int fileIdx = args.Length >= 2 ? AsInt(args[1]) : 0;
        // args[2] = data ptr - ignored.
        
        lock (_pendingLock)
        {
            _pendingOpcode  = (int)Opcode.McLoad;
            _pendingCardIdx = cardIdx;
            _pendingFileIdx = fileIdx;
        }
        
        PublishCommand(Opcode.McLoad, cardIdx, fileIdx);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Requests the host to allocate raw space on the given port of the
    /// specified size.  This was a debug/test function in GOAL that was
    /// exported as a kernel symbol but never called from <c>.gc</c> scripts.
    /// Fire-and-return.
    ///
    /// <para>Scheme: <c>(mc-makefile port size)</c></para>
    /// </summary>
    public static object McMakeFile(object[] args)
    {
        int port = args.Length >= 1 ? AsInt(args[0]) : 0;
        int size = args.Length >= 2 ? AsInt(args[1]) : 0;
        PublishCommand(Opcode.McMakeFile, port, size);
        return "#t".Eval();
    }
    
    /// <summary>
    /// Queries the host for the current status of the given save slot.
    /// Suspends the calling process until the host deposits a Scheme list
    /// of the form <c>(handle known formatted initiated last-file)</c>
    /// matching the fields of GOAL's <c>mc_slot_info</c> struct, or
    /// <c>#f</c> if no status is available.  The <c>info</c> pointer
    /// argument is a PS2-era output struct; it is accepted but ignored -
    /// the host populates its own representation.
    ///
    /// <para>Scheme: <c>(mc-get-status slot info)</c></para>
    /// </summary>
    public static object McGetStatus(object[] args)
    {
        int slot = args.Length >= 1 ? AsInt(args[0]) : 0;
        // args[1] = info ptr - ignored.
        
        object? result = Query(Opcode.McGetStatus, slot);
        return result ?? "#f".Eval();
    }
    
    /// <summary>
    /// Returns the result code from the most recently completed
    /// <see cref="McRun"/> operation as a Scheme integer.
    /// No <see cref="GameEvent"/> is published; this is a direct read of
    /// the static <c>_lastResult</c> field populated by <see cref="McRun"/>.
    /// Returns <c>1</c> (McStatusCode::OK) if no operation has completed yet.
    ///
    /// <para>Scheme: <c>(mc-check-result)</c></para>
    /// </summary>
    public static object McCheckResult(object[] args)
    {
        // Return as a boxed long so IronScheme treats it as a Scheme integer,
        // consistent with how other backing methods return numeric values.
        return (long)_lastResult;
    }
}
