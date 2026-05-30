using System;
using IronScheme;
using IronScheme.Runtime;

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
    private static ProcessScheduler? _scheduler;
    
    /// <summary>
    /// Called by <c>Kernel</c> after constructing its
    /// <see cref="ProcessScheduler"/> and before <c>RegisterAll()</c>.
    /// </summary>
    public static void Install(ProcessScheduler scheduler) =>
        _scheduler = scheduler;
    
    private static ProcessScheduler Sched =>
        _scheduler ?? throw new InvalidOperationException(
            "[ProcessRuntime] Install() has not been called. " +
            "Kernel must call ProcessRuntime.Install(scheduler) before RegisterAll().");
    
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
        return Sched.Spawn(name, initialState, parent);
    }
    
    /// <summary>
    /// Kill a process and optionally its entire child subtree.
    /// <para>Scheme: <c>(process-kill handle kill-children?)</c></para>
    /// </summary>
    public static object ProcessKill(object[] args)
    {
        long handle       = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        bool killChildren = args.Length > 1 && args[1] is bool b && b;
        Sched.Kill(handle, killChildren);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Returns <c>#t</c> if the handle refers to a live process.
    /// <para>Scheme: <c>(process-alive? handle)</c></para>
    /// </summary>
    public static object IsProcessAlive(object[] args)
    {
        long handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        return Sched.IsAlive(handle);
    }
    
    /// <summary>
    /// Returns the parent handle, or <c>0</c> for root processes.
    /// <para>Scheme: <c>(process-parent handle)</c></para>
    /// </summary>
    public static object GetProcessParent(object[] args)
    {
        long handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        return Sched.GetParent(handle);
    }
    
    /// <summary>
    /// Returns child handles as an array.
    /// <para>Scheme: <c>(process-children handle)</c></para>
    /// </summary>
    public static object GetProcessChildren(object[] args)
    {
        long   handle   = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        long[] children = Sched.GetChildren(handle);
        return Array.ConvertAll(children, c => (object)c);
    }
    
    // =======================================================================
    // State Transitions
    // =======================================================================
    
    /// <summary>
    /// Queue a state transition, deferred to the next frame boundary.
    /// <para>Scheme: <c>(go-state handle new-state-name)</c></para>
    /// </summary>
    public static object GoState(object[] args)
    {
        long   handle = args.Length > 0 ? Convert.ToInt64(args[0]) : 0L;
        string state  = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        Sched.GoState(handle, state);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Register the four lifecycle lambdas for a (type, state) pair.
    /// <para>Scheme: <c>(define-state type state enter update exit event)</c></para>
    /// </summary>
    public static object DefineState(object[] args)
    {
        string typeName  = args.Length > 0 ? Convert.ToString(args[0]) ?? "" : "";
        string stateName = args.Length > 1 ? Convert.ToString(args[1]) ?? "" : "";
        
        static Callable? ToCallable(object[] a, int i) =>
            a.Length > i && a[i] is Callable c ? c : null;
        
        Sched.RegisterState(typeName, stateName,
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
        Sched.SuspendCurrent();
        return "nil".Eval();
    }
    
    /// <summary>
    /// Yield for exactly <c>n</c> frames before resuming.
    /// <para>Scheme: <c>(suspend-for-frames n)</c></para>
    /// </summary>
    public static object SuspendForFrames(object[] args)
    {
        int frames = args.Length > 0 ? Math.Max(1, Convert.ToInt32(args[0])) : 1;
        Sched.SuspendCurrentForFrames(frames);
        return "nil".Eval();
    }
    
    /// <summary>
    /// Yield until the predicate lambda returns <c>#t</c>.
    /// <para>Scheme: <c>(suspend-until (lambda () ...))</c></para>
    /// </summary>
    public static object SuspendUntil(object[] args)
    {
        if (args.Length > 0 && args[0] is Callable predicate)
            Sched.SuspendCurrentUntil(predicate);
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
        Sched.SendEvent(handle, eventType, data);
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
        Sched.BroadcastEvent(eventType, data);
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
        Sched.SetPriority(handle, priority);
        return "nil".Eval();
    }
}
