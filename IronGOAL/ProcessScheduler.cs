using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IronScheme.Runtime;

namespace IronGOAL;

/// <summary>
/// This is a cooperative process scheduler.  Each ScriptProcess holds a
/// suspended IronScheme continuation.  The scheduler advances game time and
/// resumes any process whose wakeup time has elapsed, running it until it
/// yields again or exits.
/// </summary>
public sealed class ProcessScheduler : IDisposable
{
    // Thread Static
    
    /// <summary>
    /// Set by <see cref="ScriptProcess.Start"/> on the process thread.
    /// Read by <see cref="SuspendCurrent"/> and its variants so that
    /// <c>(suspend)</c> can reach the owning process without threading
    /// it through Scheme call stacks.
    /// </summary>
    [ThreadStatic]
    internal static ScriptProcess? CurrentProcess;
    
    // Process Table
    
    private readonly ConcurrentDictionary<long, ScriptProcess> _processes = new();
    private long _nextHandle = 1;
    
    // State Registry
    
    // Key: "processTypeName\0stateName"
    private readonly Dictionary<string, StateDefinition> _states = new();
    
    // Deferred Queues
    
    private readonly ConcurrentQueue<ScriptProcess>               _spawnQueue      = new();
    private readonly ConcurrentQueue<(long Handle, string State)> _transitionQueue = new();
    private readonly ConcurrentBag<long>                          _killQueue       = new();
    
    // Cross-process call result table.
    //
    // Key   = handle of the *calling* process that is suspended waiting for
    //         the result of (run-function-in-process target callable args...).
    // Value = the object the target callable returned (may be null).
    //
    // A key present in the dictionary (even with a null value) is the wakeup
    // signal: the calling process's SuspendUntil predicate checks for its own
    // handle here, returning true when the entry exists.  TryRemove retrieves
    // and removes atomically so the value is consumed exactly once.
    private readonly ConcurrentDictionary<long, object?> _callResults = new();
    
    // Clock
    
    private float _gameTime;
    internal float GameTime => _gameTime;
    
    // Disposed Flag
    
    private bool _disposed;
    
    // =======================================================================
    // State Registry
    // =======================================================================
    
    /// <summary>
    /// Stores the four lifecycle procs for a (processTypeName, stateName) pair.
    /// Called from <c>ProcessRuntime.DefineState</c>.
    /// </summary>
    internal void RegisterState(
        string typeName,
        string stateName,
        Callable? enterProc,
        Callable? updateProc,
        Callable? exitProc,
        Callable? eventProc)
    {
        _states[StateKey(typeName, stateName)] = new StateDefinition
        {
            EnterProc  = enterProc,
            UpdateProc = updateProc,
            ExitProc   = exitProc,
            EventProc  = eventProc,
        };
    }
    
    internal Callable? GetEnterProc(string type, string state)  => Lookup(type, state)?.EnterProc;
    internal Callable? GetExitProc(string type, string state)   => Lookup(type, state)?.ExitProc;
    internal Callable? GetUpdateProc(string type, string state) => Lookup(type, state)?.UpdateProc;
    internal Callable? GetEventProc(string type, string state)  => Lookup(type, state)?.EventProc;
    
    private StateDefinition? Lookup(string type, string state)
    {
        _states.TryGetValue(StateKey(type, state), out var def);
        return def;
    }
    
    private static string StateKey(string type, string state) => $"{type}\0{state}";
    
    // =======================================================================
    // Spawn
    // =======================================================================
    
    /// <summary>
    /// Creates a new process and queues it for startup on the next
    /// <see cref="Tick"/>.  Returns the process handle immediately.
    /// </summary>
    internal long Spawn(string name, string initialState, long parentHandle)
    {
        long handle = Interlocked.Increment(ref _nextHandle);
        
        // Type name is everything before ':' if the caller used
        // the "type:instance" naming convention, otherwise the full name.
        string typeName = name.Contains(':')
            ? name[..name.IndexOf(':')]
            : name;
        
        var proc = new ScriptProcess(
            handle, name, typeName, initialState, parentHandle, priority: 0, this);
        
        _processes[handle] = proc;
        
        if (parentHandle != 0 && _processes.TryGetValue(parentHandle, out var parent))
            parent.AddChild(handle);
        
        _spawnQueue.Enqueue(proc);
        return handle;
    }
    
    // =======================================================================
    // Kill
    // =======================================================================
    
    /// <summary>
    /// Queues a process for termination. When <paramref name="killChildren"/>
    /// is true, all descendants are also queued recursively.
    /// Termination is applied at the start of the next <see cref="Tick"/>.
    /// </summary>
    internal void Kill(long handle, bool killChildren)
    {
        _killQueue.Add(handle);
        
        if (killChildren && _processes.TryGetValue(handle, out var proc))
            foreach (long child in proc.Children.ToList())
                Kill(child, true);
    }
    
    private void Terminate(long handle)
    {
        if (!_processes.TryRemove(handle, out var proc)) return;
        
        // Detach from parent.
        if (proc.ParentHandle != 0 &&
            _processes.TryGetValue(proc.ParentHandle, out var parent))
            parent.RemoveChild(handle);
        
        // Fire exit handler best-effort; the thread may already be gone.
        try
        {
            GetExitProc(proc.ProcessTypeName, proc.CurrentState)?.Call(proc);
        }
        catch { /* Ignore exceptions.  We're tearing down. */ }
        
        proc.RequestCancel();
        proc.Dispose();
    }
    
    // =======================================================================
    // Go State
    // =======================================================================
    
    /// <summary>
    /// Queues a state transition for <paramref name="handle"/>.
    /// The exit/enter procs fire at the start of the next <see cref="Tick"/>.
    /// </summary>
    internal void GoState(long handle, string state) =>
        _transitionQueue.Enqueue((handle, state));
    
    // =======================================================================
    // Suspend (called from process threads via ProcessRuntime)
    // =======================================================================
    
    /// <summary>
    /// Yields the currently running process until the next frame.
    /// Logs a warning and returns immediately if called outside a process.
    /// </summary>
    internal void SuspendCurrent()
    {
        if (CurrentProcess is not { } proc)
        {
            Console.Error.WriteLine(
                "[ProcessScheduler] (suspend) called outside a running process — ignored.");
            return;
        }
        proc.YieldToScheduler();
    }
    
    /// <summary>
    /// Yields the current process for exactly <paramref name="frames"/> frames.
    /// </summary>
    internal void SuspendCurrentForFrames(int frames)
    {
        if (CurrentProcess is not { } proc)
        {
            Console.Error.WriteLine(
                "[ProcessScheduler] (suspend-for-frames) called outside a running process — ignored.");
            return;
        }
        proc.SetFrameDelay(frames);
        proc.YieldToScheduler();
    }
    
    /// <summary>
    /// Yields the current process until <paramref name="predicate"/> returns true.
    /// The predicate is evaluated once per frame on the Tick thread before the
    /// process is considered for resumption.
    /// </summary>
    internal void SuspendCurrentUntil(Callable predicate)
    {
        if (CurrentProcess is not { } proc)
        {
            Console.Error.WriteLine(
                "[ProcessScheduler] (suspend-until) called outside a running process — ignored.");
            return;
        }
        
        bool Evaluate() => predicate.Call() is bool result && result;
        
        proc.SetPredicate(Evaluate);
        proc.YieldToScheduler();
        proc.ClearPredicate();
    }
    
    // =======================================================================
    // Run Function In Process
    // =======================================================================
    
    /// <summary>
    /// Enqueues <paramref name="callable"/> (with <paramref name="callArgs"/>)
    /// as a one-shot pending call on <paramref name="targetHandle"/>.  The
    /// callable runs on the target process's thread at the start of its next
    /// wakeup, before event delivery.  The result is deposited in
    /// <see cref="_callResults"/> keyed by the <em>calling</em> process handle,
    /// waking it via its SuspendUntil predicate.
    /// <returns>
    /// Returns <c>false</c> if the target is dead, not found, or the same
    /// process as the caller (deadlock guard).  The caller is responsible for
    /// returning <c>#f</c> to Scheme in that case without suspending.
    /// </returns>
    internal bool EnqueueCallInProcess(
        long targetHandle, long callerHandle, Callable callable, object[] callArgs)
    {
        // Deadlock guard: a process cannot call into itself synchronously.
        if (targetHandle == callerHandle)
        {
            Console.Error.WriteLine(
                $"[ProcessScheduler] (run-function-in-process) target == caller ({callerHandle})" +
                " — self-call would deadlock; returning #f.");
            return false;
        }
        
        if (!_processes.TryGetValue(targetHandle, out var target) ||
            target.Status == ProcessStatus.Dead)
        {
            return false;
        }
        
        target.EnqueuePendingCall(callable, callArgs, callerHandle);
        return true;
    }
    
    /// <summary>
    /// Called by the Tick loop after a pending call completes on the target
    /// process thread.  Deposits <paramref name="result"/> so the waiting
    /// caller process's predicate becomes true and it wakes next frame.
    /// </summary>
    internal void DepositCallResult(long callerHandle, object? result) =>
        _callResults[callerHandle] = result;
 
    /// <summary>
    /// Called by <see cref="RunInProcessAndWait"/> to block the calling
    /// process until its result has been deposited.  Returns the result and
    /// removes the entry atomically.
    /// </summary>
    internal object? WaitForCallResult(long callerHandle)
    {
        if (CurrentProcess is not { } proc)
        {
            Console.Error.WriteLine(
                "[ProcessScheduler] WaitForCallResult called outside a running process — ignored.");
            return null;
        }
        
        // Suspend until the target has run and deposited the result.
        proc.SetPredicate(() => _callResults.ContainsKey(callerHandle));
        proc.YieldToScheduler();
        proc.ClearPredicate();
        
        _callResults.TryRemove(callerHandle, out var result);
        return result;
    }
    
    // =======================================================================
    // Set To Run Function
    // =======================================================================
    
    /// <summary>
    /// Sets a one-shot pre-update callable on <paramref name="targetHandle"/>.
    /// At the start of that process's next wakeup the callable fires before
    /// the normal update proc.  If called again before the tick fires, the
    /// second call overwrites the first (single-slot, matching GOAL semantics).
    ///
    /// <para>Fire-and-forget from the caller's perspective; returns immediately.</para>
    /// </summary>
    internal void SetPreUpdate(long targetHandle, Callable callable, object[] callArgs)
    {
        if (!_processes.TryGetValue(targetHandle, out var target) ||
            target.Status == ProcessStatus.Dead)
        {
            Console.Error.WriteLine(
                $"[ProcessScheduler] (set-to-run-function) target {targetHandle} not found or dead — ignored.");
            return;
        }
        
        target.SetPendingPreUpdate(callable, callArgs);
    }
    
    // =======================================================================
    // Events
    // =======================================================================
    
    /// <summary>
    /// Posts a typed event to a specific process. Delivered before its update
    /// proc is next resumed.
    /// </summary>
    internal void SendEvent(long handle, string eventType, object? data)
    {
        if (_processes.TryGetValue(handle, out var proc))
            proc.EnqueueEvent(eventType, data);
    }
    
    /// <summary>
    /// Broadcasts a typed event to every live process.
    /// </summary>
    internal void BroadcastEvent(string eventType, object? data)
    {
        foreach (var proc in _processes.Values)
            proc.EnqueueEvent(eventType, data);
    }
    
    // =======================================================================
    // Priority
    // =======================================================================
    
    internal void SetPriority(long handle, int priority)
    {
        if (_processes.TryGetValue(handle, out var proc))
            proc.Priority = priority;
    }
    
    // =======================================================================
    // Queries
    // =======================================================================
    
    internal bool   IsAlive(long handle)   => _processes.TryGetValue(handle, out var p) && p.Status != ProcessStatus.Dead;
    internal string GetState(long handle)  => _processes.TryGetValue(handle, out var p) ? p.CurrentState : string.Empty;
    internal long   GetParent(long handle) => _processes.TryGetValue(handle, out var p) ? p.ParentHandle : 0L;
    
    internal long[] GetChildren(long handle) =>
        _processes.TryGetValue(handle, out var p)
            ? p.Children.ToArray()
            : Array.Empty<long>();
    
    // =======================================================================
    // Tick
    // =======================================================================
    
    /// <summary>
    /// Advances all processes by one frame. Called exclusively from
    /// <c>Kernel.Tick()</c> on the host thread.
    /// </summary>
    /// <param name="deltaTime"></param>
    internal void Tick(float deltaTime)
    {
        if (_disposed) return;
        
        _gameTime += deltaTime;
        
        // 1. Terminate processes killed during the previous frame.
        while (_killQueue.TryTake(out long h))
            Terminate(h);
        
        // 2. Start newly spawned processes: fire enter proc, launch thread.
        while (_spawnQueue.TryDequeue(out var proc))
        {
            Callable? update = GetUpdateProc(proc.ProcessTypeName, proc.CurrentState);
            GetEnterProc(proc.ProcessTypeName, proc.CurrentState)?.Call(proc);
            proc.Start(() => update?.Call(proc));
        }
        
        // 3. Apply state transitions requested during the previous frame.
        while (_transitionQueue.TryDequeue(out var tx))
            if (_processes.TryGetValue(tx.Handle, out var p))
                p.TransitionTo(tx.State);
        
        // 4. Build the runnable list: suspended processes that are ready
        //    this frame, ordered by priority (ascending = runs first).
        var runnable = _processes.Values
            .Where(p  => p.ReadyThisFrame())
            .OrderBy(p => p.Priority)
            .ToList();
        
        // 5. Run each process: drain its events then resume until it yields.
        foreach (var p in runnable)
        {
            if (p.Status == ProcessStatus.Dead) continue;
            
            // a. Pre-update one-shot (set-to-run-function).
            p.FirePendingPreUpdate();
 
            // b. Pending calls from other processes (run-function-in-process).
            p.DrainPendingCalls(this);
 
            // c. Events.
            p.DrainEvents();
 
            // d. Normal update.
            p.ResumeAndWait();
        }
        
        // 6. Reap processes that exited naturally this frame.
        foreach (long dead in _processes.Values
                     .Where(p  => p.Status == ProcessStatus.Dead)
                     .Select(p => p.Handle)
                     .ToList())
            Terminate(dead);
    }
    
    
    // =======================================================================
    // IDisposable
    // =======================================================================
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        foreach (var proc in _processes.Values)
        {
            proc.RequestCancel();
            proc.Dispose();
        }
        _processes.Clear();
    }
}
