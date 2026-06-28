using IronScheme;
using IronScheme.Runtime;
using IronScheme.Scripting;

using IronGOAL.Backing;
using IronGOAL.Bus;
using IronGOAL.Scripting;

namespace IronGOAL;

public class Kernel
{
    // =======================================================================
    // STATE
    // =======================================================================
    
    internal GoalRuntimeConfig Config     { get; }
    internal EventBus          EventBus   { get; }
    internal long              FrameId    { get; private set; }
    internal object SchemeEnvironment     { get; }
    
    private readonly ProcessScheduler _scheduler;
    private bool                      _disposed;
    private readonly GoalLogHandler   _log;
    private static int _schemeBooted = 0; // 0 = not booted, 1 = booted
    private static TextWriter? _schemeWriter; // GC anchor
    private static int _writerInstalled = 0;
    private readonly ScriptLoader _scriptLoader;
    
    // =======================================================================
    // CONSTRUCTION
    // =======================================================================
    
    internal Kernel(GoalRuntimeConfig config)
    {
        Config   = config;
        _log     = config.LogHandler;
        
        // Build channels first so kernel backing can reference the bus
        // during registration without any ordering dependency.
        EventBus = new EventBus(
            config.RenderChannelCapacity,
            config.AudioChannelCapacity,
            config.GameEventChannelCapacity,
            physicsCapacity: config.PhysicsChannelCapacity,
            debugCapacity: config.DebugChannelCapacity,
            memoryCapacity: config.MemoryChannelCapacity);
        
        _scheduler = new ProcessScheduler();
        
        // Use the host-provided environment if given; otherwise obtain the
        // process's interaction-environment ourselves.
        SchemeEnvironment = config.SchemeEnvironment
                            ?? "(interaction-environment)".Eval();
        
        RunBootSequence(config);
        
        _scriptLoader = new ScriptLoader(SchemeEnvironment);
        
        // Register all C# kernel functions as Scheme symbols.
        ProcessRuntime.Install(_scheduler);
        EntitySystem.Install(EventBus);
        AnimationSystem.Install(EventBus);
        AudioSystem.Install(EventBus);
        InputSystem.Install(EventBus);
        GraphicsSystem.Install(EventBus);
        PhysicsSystem.Install(EventBus);
        GameMemory.Install(EventBus);
        RegisterAll();
        
        _log(GoalLogSeverity.Info, GoalErrorCode.None, "Kernel booted successfully.");
    }
    
    internal static void RunBootSequence(GoalRuntimeConfig config)
    {
        if (Interlocked.CompareExchange(ref _schemeBooted, 1, 0) == 0)
        {
            Console.SetError(new SchemeOutputWriter(Console.Error));
            
            try
            {
                "(import (rnrs r5rs (6)))".Eval();
            }
            catch (Exception e)
            {
                string message = e.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                Console.Error.WriteLine($"[IronGOAL] Scheme boot warning: {message}");
            }
        }
    }
    
    // =======================================================================
    // TICK
    // =======================================================================
    
    internal void Tick(float deltaTime)
    {
        if (_disposed)
        {
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed,
                "Kernel.Tick called after disposal.");
            return;
        }
        
        _scheduler.Tick(deltaTime);
        FrameId++;
    }
    
    // =======================================================================
    // EVALUATE (REPL / TEST SURFACE)
    // =======================================================================
    /// <summary>
    /// Evaluate a Scheme expression and return its write-representation.
    /// Returns the error message prefixed with "#error:" on failure so
    /// tests can distinguish error strings from valid results.
    /// Never throws.
    /// </summary>
    internal GoalResult<object> Evaluate(string expression)
    {
        if (_disposed)
            return GoalResult<object>.Fail(GoalErrorCode.RuntimeDisposed,
                "Kernel has been disposed.");
        
        try
        {
            object value = expression.EvalWithEnvironmentInstance(SchemeEnvironment);
            return GoalResult<object>.Okay(value);
        }
        catch (Exception ex)
        {
            string message = ex.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            string trunk = TruncateForLog(expression).Replace("\r\n", "\n").Replace("\n", "\r\n");
            var msg = $"Scheme error evaluating:\r\n{trunk}\r\n{message}";
            return GoalResult<object>.Fail(GoalErrorCode.EvalFailed, msg);
        }
    }
    
    /// <summary>
    /// Read and evaluate exactly one top-level Scheme form - the REPL /
    /// command-window primitive.  See <see cref="ScriptLoader.EvaluateExpression"/>
    /// for the read/eval semantics (only the first form in
    /// <paramref name="expression"/> is evaluated; <see cref="SchemeForm.Index"/>
    /// is always 0).  Never throws.
    /// </summary>
    /// <param name="expression">Expression to evaluate.</param>
    /// <returns>Result of evaluation.</returns>
    internal FormResult EvaluateForm(string expression)
    {
        if (_disposed)
            return FormResult.Failed(new SchemeForm(null, 0, expression),
                "Kernel has been disposed.");

        return _scriptLoader.EvaluateExpression(expression);
    }
    
    /// <summary>
    /// Load and evaluate a .gc script file.
    /// Returns GoalResult.Fail if the file cannot be read or Scheme raises.
    /// </summary>
    internal GoalResult LoadFile(string path)
    {
        if (_disposed)
            return GoalResult.Fail(GoalErrorCode.RuntimeDisposed,
                "SchemeRuntime has been disposed.");
        
        return _scriptLoader.LoadFile(path);
    }
    
    // =======================================================================
    // FUNCTION REGISTRATION
    // =======================================================================
    
    /// <summary>
    /// Registers a variadic function that can be called with 'apply' or
    /// with variable numbers of arguments.  Uses CallTargetN signature.
    /// </summary>
    private void DefineFunction(string schemeName, Func<object[], object> method)
    {
        CallTargetN target = args =>
        {
            return method(args);
        };
        var closure = Closure.Create(target, -1);
        $"(define {schemeName} {{0}})".Eval(closure);
    }

    /// <summary>
    /// Register all GOAL functions with the Scheme interpreter.
    /// Each registration makes a C# method callable from Scheme.
    /// </summary>
    private void RegisterAll()
    {
        // ===================================================================
        // GAME MATH
        // ===================================================================
        
        DefineFunction("vector+", GameMath.Vector3Add);
        DefineFunction("vector-", GameMath.Vector3Subtract);
        DefineFunction("vector-float*", GameMath.Vector3Scale);
        DefineFunction("vector-dot", GameMath.Vector3Dot);
        DefineFunction("vector-cross", GameMath.Vector3Cross);
        DefineFunction("vector-length", GameMath.Vector3Length);
        DefineFunction("vector-normalize!", GameMath.Vector3Normalize);
        DefineFunction("vector-distance", GameMath.Vector3Distance);
        DefineFunction("vector-lerp", GameMath.Vector3Lerp);
        DefineFunction("quat-identity", GameMath.QuatIdentity);
        DefineFunction("quat-from-axis-angle", GameMath.QuatFromAxisAngle);
        DefineFunction("quat*", GameMath.QuatMultiply);
        DefineFunction("quat-slerp", GameMath.QuatSlerp);
        DefineFunction("quat-rotate-vec3", GameMath.QuatRotateVec3);
        DefineFunction("matrix-identity", GameMath.MatrixIdentity);
        DefineFunction("matrix*", GameMath.MatrixMultiply);
        DefineFunction("matrix-inverse", GameMath.MatrixInverse);
        DefineFunction("matrix-from-quat-trans", GameMath.MatrixFromQuatTrans);
        DefineFunction("matrix-transform-point", GameMath.MatrixTransformPoint);
        DefineFunction("matrix-transform-dir", GameMath.MatrixTransformDirection);
        DefineFunction("matrix-look-at", GameMath.MatrixLookAt);
        DefineFunction("matrix-perspective", GameMath.MatrixPerspective);
        DefineFunction("lerp", GameMath.Lerp);
        DefineFunction("clamp", GameMath.Clamp);
        DefineFunction("deg->rad", GameMath.DegToRad);
        DefineFunction("rad->deg", GameMath.RadToDeg);
        DefineFunction("wrap-angle-180", GameMath.WrapAngle180);
        DefineFunction("angle-delta", GameMath.AngleDelta);
        DefineFunction("fabs", GameMath.Fabs);
        DefineFunction("sqrtf", GameMath.Sqrtf);
        DefineFunction("fequal-epsilon?", GameMath.FEqualEpsilon);
        DefineFunction("/-signed-0-guard", GameMath.SignedDiv0Guard);
        DefineFunction("mod-signed-0-guard", GameMath.SignedMod0Guard);
        
        // ===================================================================
        // GAME CLOCK
        // ===================================================================
        
        DefineFunction("frame-time", GameClock.FrameTime);
        DefineFunction("total-time", GameClock.TotalTime);
        DefineFunction("frame-count", GameClock.FrameCount);
        DefineFunction("time-scale", GameClock.TimeScale);
        DefineFunction("set-time-scale!", GameClock.SetTimeScale);
        DefineFunction("seconds->frames", GameClock.SecondsToFrames);
        DefineFunction("frames->seconds", GameClock.FramesToSeconds);
        
        // ===================================================================
        // PROCESS RUNTIME
        // ===================================================================
        
        DefineFunction("run-process-and-function", ProcessRuntime.ProcessSpawn);
        DefineFunction("kill-process", ProcessRuntime.ProcessKill);
        DefineFunction("process-alive?", ProcessRuntime.IsProcessAlive);
        DefineFunction("go", ProcessRuntime.GoState);
        DefineFunction("defstate", ProcessRuntime.DefineState);
        DefineFunction("suspend", ProcessRuntime.Suspend);
        DefineFunction("run-function-in-process", ProcessRuntime.RunInProcess);
        DefineFunction("set-to-run-function", ProcessRuntime.SetToRun);
        DefineFunction("send-event", ProcessRuntime.SendEvent);
        DefineFunction("kernel-shutdown", ProcessRuntime.KernelShutdown);
        
        // ===================================================================
        // ENTITY SYSTEM
        // ===================================================================
        
        DefineFunction("entity-spawn", EntitySystem.Spawn);
        DefineFunction("entity-kill", EntitySystem.Destroy);
        
        // ===================================================================
        // AUDIO SYSTEM
        // ===================================================================
        
        DefineFunction("snd-play", AudioSystem.Play);
        DefineFunction("snd-play-2d", AudioSystem.Play2D);
        DefineFunction("snd-stop", AudioSystem.Stop);
        DefineFunction("snd-stop-all", AudioSystem.StopAll);
        DefineFunction("snd-set-volume!", AudioSystem.SetVolume);
        DefineFunction("snd-set-pitch!", AudioSystem.SetPitch);
        DefineFunction("snd-set-param!", AudioSystem.SetParam);
        
        // ===================================================================
        // GAME MEMORY
        // ===================================================================
        
        DefineFunction("kmalloc", GameMemory.Alloc);
        DefineFunction("malloc", GameMemory.ManagedAlloc);
        DefineFunction("kfree", GameMemory.Free);
        DefineFunction("kmemopen", GameMemory.MemOpen);
        DefineFunction("kmemclose", GameMemory.MemClose);
        DefineFunction("dma-to-iop", GameMemory.DmaToIop);
        DefineFunction("new-dynamic-structure", GameMemory.NewDynamicStructure);
        
        // ===================================================================
        // TYPE SYSTEM
        // ===================================================================
        
        DefineFunction("deftype", TypeSystem.DefineType);
        DefineFunction("method-set!", TypeSystem.SetMethod);
        DefineFunction("method-id", TypeSystem.MethodId);
        DefineFunction("type-type?", TypeSystem.IsType);
        DefineFunction("type-of", TypeSystem.TypeOf);
    }
    
    // =======================================================================
    // DISPOSAL
    // =======================================================================
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        //_scheme.Dispose();
        EventBus.Complete();
        _log(GoalLogSeverity.Info, GoalErrorCode.None, "Kernel disposed.");
    }
    
    // =======================================================================
    // PRIVATE HELPERS
    // =======================================================================
    
    /// <summary>
    /// Convert an IronScheme result object to its Scheme write representation.
    /// write representation differs from display in that strings include quotes
    /// and symbols include their names verbatim.
    /// </summary>
    private static string SchemeWrite(object? value)
    {
        if (value is null)
            return "()";
        
        // IronScheme uses Microsoft.Scripting.SymbolId (or wrapped objects)
        // for symbols. ToString() on most Scheme objects gives the right
        // external representation already. For Scheme's #t / #f:
        if (value is bool b)
            return b ? "#t" : "#f";
        
        // For pairs and vectors IronScheme's ToString() gives Scheme notation.
        return value.ToString() ?? "()";
    }
    
    private static string TruncateForLog(string s, int max = 60)
        => s.Length <= max ? s : s[..max] + "…";
}
