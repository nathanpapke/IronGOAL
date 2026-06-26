using System;
using IronScheme;
using IronScheme.Runtime;

using IronGOAL.Bus;

namespace IronGOAL.Backing;

/// <summary>
/// Backing methods for the cooperative process / state-script system.
/// All methods match the <c>Func&lt;object[], object&gt;</c> signature
/// expected by <c>Kernel.DefineFunction</c> and delegate directly into
/// <see cref="ProcessScheduler"/>.
/// <para>
/// <see cref="Install"/> must be called by <c>Kernel</c> before
/// <c>RegisterAll()</c>, passing the <see cref="ProcessScheduler"/>
/// instance that <c>Kernel</c> owns.
/// </para>
/// </summary>
public static class ProcessRuntime
{
    private static ProcessScheduler? _scheduler = new();
    private static EventBus?         _bus;
    
    /// <summary>
    /// Called by <c>Kernel</c> after constructing its
    /// <see cref="ProcessScheduler"/> and before <c>RegisterAll()</c>.
    /// </summary>
    public static void Install(ProcessScheduler scheduler) =>
        _scheduler = scheduler;
    
    /// <summary>
    /// Called by <c>Kernel</c> after constructing its <see cref="EventBus"/>
    /// and before <c>RegisterAll()</c>.  Required for <c>kernel-shutdown</c>
    /// to publish its lifecycle signal.
    /// </summary>
    public static void InstallBus(EventBus bus) =>
        _bus = bus;
    
    private static ProcessScheduler Scheduler => _scheduler;
    
    // =======================================================================
    // Process Lifecycle
    // =======================================================================
    
    /// <summary>
    /// Create a new process and return its handle.
    /// <para>Scheme: <c>(process-spawn name initial-state parent-handle)</c></para>
    /// </summary>
    public static object ProcessSpawn(object[] args)
    {
        string name         = args.Length > 0 ? Convert.ToString(args[0]) ?? "" : "";
        string initialState = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        long   parent       = args.Length > 2 ? Convert.ToInt64(args[2])  : 0L;
        return Scheduler.Spawn(name, initialState, parent);
    }
    
    /// <summary>
    /// Kill a process and optionally its entire child subtree.
    /// <para>Scheme: <c>(process-kill handle kill-children?)</c></para>
    /// </summary>
    public static object ProcessKill(object[] args)
    {
        long handle       = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        bool killChildren = args.Length > 1 && args[1] is bool b && b;
        Scheduler.Kill(handle, killChildren);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Returns <c>#t</c> if the handle refers to a live process.
    /// <para>Scheme: <c>(process-alive? handle)</c></para>
    /// </summary>
    public static object IsProcessAlive(object[] args)
    {
        long handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        return Scheduler.IsAlive(handle);
    }
    
    /// <summary>
    /// Returns the parent handle, or <c>0</c> for root processes.
    /// <para>Scheme: <c>(process-parent handle)</c></para>
    /// </summary>
    public static object GetProcessParent(object[] args)
    {
        long handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        return Scheduler.GetParent(handle);
    }
    
    /// <summary>
    /// Returns child handles as an array.
    /// <para>Scheme: <c>(process-children handle)</c></para>
    /// </summary>
    public static object GetProcessChildren(object[] args)
    {
        long   handle   = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        long[] children = Scheduler.GetChildren(handle);
        return Array.ConvertAll(children, c => (object)c);
    }
    
    // =======================================================================
    // State Transitions
    // =======================================================================
    
    /// <summary>
    /// Queue a state transition, deferred to the next frame boundary.
    /// <para>Scheme: <c>(go handle new-state-name)</c></para>
    /// </summary>
    public static object GoState(object[] args)
    {
        long   handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        string state  = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        Scheduler.GoState(handle, state);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Register the four lifecycle lambdas for a (type, state) pair.
    /// <para>Scheme: <c>(defstate type state enter update exit event)</c></para>
    /// </summary>
    public static object DefineState(object[] args)
    {
        string typeName  = args.Length > 0 ? Convert.ToString(args[0]) ?? "" : "";
        string stateName = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        
        static Callable? ToCallable(object[] a, int i) =>
            a.Length > i && a[i] is Callable c ? c : null;
        
        Scheduler.RegisterState(typeName, stateName,
            ToCallable(args, 2),   // enter
            ToCallable(args, 3),   // update
            ToCallable(args, 4),   // exit
            ToCallable(args, 5));  // event
        
        return "nil".Eval();
    }
    
    // =======================================================================
    // Suspend / Resume
    // =======================================================================
    
    /// <summary>
    /// Yield the current process until the next scheduler frame.
    /// <para>Scheme: <c>(suspend)</c></para>
    /// </summary>
    public static object Suspend(object[] args)
    {
        Scheduler.SuspendCurrent();
        return "nil".Eval();
    }
    
    /// <summary>
    /// Yield for exactly <c>n</c> frames before resuming.
    /// <para>Scheme: <c>(suspend-for-frames n)</c></para>
    /// </summary>
    public static object SuspendForFrames(object[] args)
    {
        int frames = args.Length > 0 ? Math.Max(1, Convert.ToInt32(args[0])) : 1;
        Scheduler.SuspendCurrentForFrames(frames);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Yield until the predicate lambda returns <c>#t</c>.
    /// <para>Scheme: <c>(suspend-until (lambda () ...))</c></para>
    /// </summary>
    public static object SuspendUntil(object[] args)
    {
        if (args.Length > 0 && args[0] is Callable predicate)
            Scheduler.SuspendCurrentUntil(predicate);
        return "nil".Eval();
    }
    
    // =======================================================================
    // Cross-Process Calls
    // =======================================================================
    
    /// <summary>
    /// Run a callable in the context of another process and return its result.
    ///
    /// <para>Scheme: <c>(run-function-in-process handle callable . args)</c></para>
    /// </summary>
    public static object RunInProcess(object[] args)
    {
        if (args.Length < 2 || args[1] is not Callable callable)
            return "#f".Eval();
        
        long targetHandle = Convert.ToInt64(args[0]);
        
        // Must be called from a running process - we need the caller handle
        // to key the result table and to detect self-call deadlocks.
        if (ProcessScheduler.CurrentProcess is not { } caller)
        {
            Console.Error.WriteLine(
                "[ProcessRuntime] (run-function-in-process) called outside a running " +
                "process — returning #f.");
            return "#f".Eval();
        }
        
        // Pack any trailing arguments for the callable.
        object[] callArgs = args.Length > 2
            ? args[2..]
            : Array.Empty<object>();
        
        bool enqueued = Scheduler.EnqueueCallInProcess(
            targetHandle, caller.Handle, callable, callArgs);
        
        if (!enqueued)
            return "#f".Eval();
        
        // Suspend the calling process until the target deposits the result.
        return Scheduler.WaitForCallResult(caller.Handle) ?? "#f".Eval();
    }
 
    /// <summary>
    /// Set a one-shot pre-update callable on another process.
    ///
    /// <para>Scheme: <c>(set-to-run-function handle callable . args)</c></para>
    /// </summary>
    public static object SetToRun(object[] args)
    {
        if (args.Length < 2 || args[1] is not Callable callable)
            return "nil".Eval();
        
        long targetHandle = Convert.ToInt64(args[0]);
        
        object[] callArgs = args.Length > 2
            ? args[2..]
            : Array.Empty<object>();
        
        Scheduler.SetPreUpdate(targetHandle, callable, callArgs);
        return "nil".Eval();
    }
    
    // =======================================================================
    // Process Communication
    // =======================================================================
    
    /// <summary>
    /// Post a typed event to a specific process.
    /// <para>Scheme: <c>(send-event handle event-type event-data)</c></para>
    /// </summary>
    public static object SendEvent(object[] args)
    {
        long    handle    = args.Length > 0 ? Convert.ToInt64(args[0])   : 0L;
        string  eventType = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        object? data      = args.Length > 2 ? args[2] : null;
        Scheduler.SendEvent(handle, eventType, data);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Broadcast a typed event to every live process.
    /// <para>Scheme: <c>(broadcast-event event-type event-data)</c></para>
    /// </summary>
    public static object BroadcastEvent(object[] args)
    {
        string  eventType = args.Length > 0 ? Convert.ToString(args[0]) ?? "" : "";
        object? data      = args.Length > 1 ? args[1] : null;
        Scheduler.BroadcastEvent(eventType, data);
        return "nil".Eval();
    }
    
    // =========================================================================
    // Scheduler Ordering
    // =========================================================================
    
    /// <summary>
    /// Set scheduling priority. Lower values run first within each frame.
    /// <para>Scheme: <c>(set-process-priority! handle priority)</c></para>
    /// </summary>
    public static object SetProcessPriority(object[] args)
    {
        long handle   = args.Length > 0 ? Convert.ToInt64(args[0])  : 0L;
        int  priority = args.Length > 1 ? Convert.ToInt32(args[1]) : 0;
        Scheduler.SetPriority(handle, priority);
        return "nil".Eval();
    }
    
    // =======================================================================
    // Kernel Shutdown
    // =======================================================================
    
    /// <summary>
    /// Publishes a <see cref="GameEventType.KernelShutdown"/> signal to the
    /// host via the <see cref="EventBus"/>.  IronGOAL itself does not stop
    /// ticking - the host reads this event during its drain loop and decides
    /// whether to call <c>Kernel.Dispose()</c> or otherwise shut down.
    ///
    /// <para>Scheme: <c>(kernel-shutdown)</c></para>
    /// </summary>
    public static object KernelShutdown(object[] args)
    {
        _bus?.PublishGameEvent(new GameEvent
        {
            Type     = GameEventType.KernelShutdown,
            EntityId = -1,
            Param0   = 0,
            Param1   = 0,
            Param2   = 0,
            Param3   = 0,
        });
        return "nil".Eval();
    }
}
