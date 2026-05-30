using System.Collections.Concurrent;
using IronScheme.Runtime;

namespace IronGOAL;

/// <summary>
/// A single cooperative coroutine.  Runs its Scheme update proc on a
/// dedicated background thread that parks on <see cref="_resumeGate"/>
/// between frames and signals <see cref="_suspendGate"/> when it yields.
/// </summary>
internal sealed class ScriptProcess : IDisposable
{
    // =======================================================================
    // Identity
    // =======================================================================
    
    public long   Handle          { get; }
    public string Name            { get; }
    public string ProcessTypeName { get; }
    public long   ParentHandle    { get; }
    
    private readonly List<long> _children = new();
    public IReadOnlyList<long> Children => _children;
    
    // =======================================================================
    // State Machine
    // =======================================================================
    
    public string        CurrentState { get; set; }
    public ProcessStatus Status       { get; set; } = ProcessStatus.Pending;
    public int           Priority     { get; set; }
    
    // =======================================================================
    // Coroutine Gates
    // =======================================================================
    
    // Scheduler → process: "your turn, run"
    private readonly ManualResetEventSlim _resumeGate  = new(initialState: false);
    // Process → scheduler: "I've yielded, your turn"
    private readonly ManualResetEventSlim _suspendGate = new(initialState: false);
    
    private Thread? _thread;
    private readonly CancellationTokenSource _cts = new();
    
    // =======================================================================
    // Suspend Conditions
    // =======================================================================
    
    private int         _framesRemaining; // >0 while inside SuspendForFrames
    private Func<bool>? _predicate;       // non-null while inside SuspendUntil
    
    // =======================================================================
    // Event Queue
    // =======================================================================
    
    private readonly ConcurrentQueue<(string Type, object? Data)> _events = new();
    
    // =======================================================================
    // Scheduler Back-Reference (for state lookups in DrainEvents)
    // =======================================================================
    
    private readonly ProcessScheduler _scheduler;
    
    // =======================================================================
    // Deadlock Guard
    // =======================================================================
    
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);
    
    // =======================================================================
    // Constructor
    // =======================================================================
    
    internal ScriptProcess(
        long handle,
        string name,
        string processTypeName,
        string initialState,
        long parentHandle,
        int priority,
        ProcessScheduler scheduler)
    {
        Handle          = handle;
        Name            = name;
        ProcessTypeName = processTypeName;
        CurrentState    = initialState;
        ParentHandle    = parentHandle;
        Priority        = priority;
        _scheduler      = scheduler;
    }
    
    // =======================================================================
    // Child Variables
    // =======================================================================
    
    internal void AddChild(long h)    => _children.Add(h);
    internal void RemoveChild(long h) => _children.Remove(h);
    
    // =======================================================================
    // Thread Launch
    // =======================================================================
    
    /// <summary>
    /// Starts the background thread running <paramref name="body"/>.
    /// <paramref name="body"/> is the Scheme update proc wrapped as an Action.
    /// Called by the scheduler after the initial enter proc has fired.
    /// </summary>
    internal void Start(Action body)
    {
        Status  = ProcessStatus.Suspended; // first Tick will wake it
        _thread = new Thread(() =>
        {
            try
            {
                ProcessScheduler.CurrentProcess = this;
                Status = ProcessStatus.Running;
                body();
            }
            catch (OperationCanceledException)
            {
                // Normal path when Kill() cancels the token while the
                // process is parked in YieldToScheduler().
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ProcessScheduler] Process '{Name}' ({Handle}) faulted: {ex.Message}");
            }
            finally
            {
                ProcessScheduler.CurrentProcess = null;
                Status = ProcessStatus.Dead;
                _suspendGate.Set(); // unblock scheduler so it can reap us
            }
        })
        {
            IsBackground = true,
            Name         = $"Goal-{Name}-{Handle}"
        };
        _thread.Start();
    }
    
    // =======================================================================
    // Yield (called from process thread)
    // =======================================================================
    
    /// <summary>
    /// Blocks the calling thread (the process thread) until the scheduler
    /// resumes it next frame.  Throws <see cref="OperationCanceledException"/>
    /// when the process has been killed.
    /// </summary>
    internal void YieldToScheduler()
    {
        _cts.Token.ThrowIfCancellationRequested();
        Status = ProcessStatus.Suspended;
        _suspendGate.Set();           // signal: "I'm done for this frame"
        _resumeGate.Wait(_cts.Token); // park until scheduler wakes us
        _resumeGate.Reset();
        _cts.Token.ThrowIfCancellationRequested();
        Status = ProcessStatus.Running;
    }
    
    // =======================================================================
    // Frame Readiness (called from scheduler thread)
    // =======================================================================
    
    /// <summary>
    /// Returns true when this process should be woken this frame.
    /// Decrements frame counters and evaluates predicates as a side effect.
    /// </summary>
    internal bool ReadyThisFrame()
    {
        if (Status != ProcessStatus.Suspended) return false;
        
        if (_framesRemaining > 0)
        {
            _framesRemaining--;
            return _framesRemaining == 0;
        }
        
        if (_predicate is not null)
            return _predicate();
        
        return true;
    }
    
    internal void SetFrameDelay(int frames)  => _framesRemaining = frames;
    internal void SetPredicate(Func<bool> p) => _predicate = p;
    internal void ClearPredicate()           => _predicate = null;
    
    // =======================================================================
    // Resume (called from scheduler thread)
    // =======================================================================
    
    /// <summary>
    /// Signals the process thread to run, then blocks the scheduler thread
    /// until the process yields or exits.  Must only be called from Tick().
    /// </summary>
    internal void ResumeAndWait()
    {
        _suspendGate.Reset();
        _resumeGate.Set();
        
        if (!_suspendGate.Wait(WaitTimeout))
            Console.Error.WriteLine(
                $"[ProcessScheduler] Timeout waiting for '{Name}' ({Handle}) to suspend. " +
                "Ensure every update proc eventually calls (suspend).");
    }
    
    // =======================================================================
    // Events
    // =======================================================================
    
    internal void EnqueueEvent(string type, object? data) =>
        _events.Enqueue((type, data));
    
    /// <summary>
    /// Drains pending events and invokes the current state's event proc for
    /// each.  Called by the scheduler before resuming the update proc.
    /// </summary>
    internal void DrainEvents()
    {
        if (_events.IsEmpty) return;
 
        Callable? eventProc = _scheduler.GetEventProc(ProcessTypeName, CurrentState);
        while (_events.TryDequeue(out var ev))
            eventProc?.Call(this, ev.Type, ev.Data);
    }
    
    // =======================================================================
    // State Transition (called from scheduler thread)
    // =======================================================================
    
    /// <summary>
    /// Fires the exit proc for the current state, switches state, then fires
    /// the enter proc for the new state.  Always runs on the Tick thread.
    /// </summary>
    internal void TransitionTo(string newState)
    {
        _scheduler.GetExitProc(ProcessTypeName, CurrentState)?.Call(this);
        CurrentState = newState;
        _scheduler.GetEnterProc(ProcessTypeName, CurrentState)?.Call(this);
    }
    
    // =======================================================================
    // Kill
    // =======================================================================
    
    internal void RequestCancel()
    {
        _cts.Cancel();
        _resumeGate.Set(); // unblock thread so it can observe cancellation
    }
    
    // =======================================================================
    // IDisposable
    // =======================================================================
    
    public void Dispose()
    {
        _cts.Cancel();
        _resumeGate.Set();
        _thread?.Join(millisecondsTimeout: 200);
        _resumeGate.Dispose();
        _suspendGate.Dispose();
        _cts.Dispose();
    }
}
