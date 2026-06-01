using IronScheme;
using IronScheme.Runtime;
using IronScheme.Scripting;

using IronGOAL.Backing;
using IronGOAL.Bus;

namespace IronGOAL;

public class Kernel
{
    // =======================================================================
    // STATE
    // =======================================================================
    
    internal GoalRuntimeConfig Config     { get; }
    internal EventBus          EventBus   { get; }
    internal long              FrameId    { get; private set; }
    
    private readonly ProcessScheduler _scheduler;
    private bool                      _disposed;
    private readonly GoalLogHandler   _log;
    private static int _schemeBooted = 0; // 0 = not booted, 1 = booted
    
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
            config.DebugChannelCapacity,
            config.MemoryChannelCapacity);
        
        _scheduler = new ProcessScheduler();
        
        EnsureSchemeBoot();
        
        // Register all C# kernel functions as Scheme symbols.
        RegisterAll();
        
        _log(GoalLogSeverity.Info, GoalErrorCode.None, "Kernel booted successfully.");
    }
    
    private static void EnsureSchemeBoot()
    {
        if (Interlocked.CompareExchange(ref _schemeBooted, 1, 0) == 0)
        {
            // Boot IronScheme.  SchemeRuntime constructor touches .Eval() once
            // to pay the bootstrap cost eagerly by importing R5RS.
            try
            {
                "(import (rnrs r5rs (6)))".Eval();
            }
            catch (SchemeException e)
            {
                Console.WriteLine(e);
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
            object? value = expression.Eval();
            return GoalResult<object>.Okay(expression.Eval());
        }
        catch (Exception ex)
        {
            return GoalResult<object>.Fail(GoalErrorCode.EvalFailed,
                $"Scheme error evaluating '{TruncateForLog(expression)}': {ex.Message}");
        }
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
        
        try
        {
            string source = File.ReadAllText(path);
            // Wrap in begin so the file is one top-level form.
            string wrapped = $"(begin {source})";
            wrapped.Eval();
            return GoalResult.Okay;
        }
        catch (IOException ex)
        {
            return GoalResult.Fail(GoalErrorCode.ScriptReadFailed,
                $"Cannot read '{path}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GoalResult.Fail(GoalErrorCode.ScriptEvalFailed,
                $"Scheme error in '{path}': {ex.Message}");
        }
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
        
        DefineFunction("vec3", GameMath.Vec3);
        DefineFunction("vector+", GameMath.Vector3Add);
        DefineFunction("vector-", GameMath.Vector3Subtract);
        DefineFunction("vector-scale", GameMath.Vector3Scale);
        DefineFunction("vector-dot", GameMath.Vector3Dot);
        DefineFunction("vector-cross", GameMath.Vector3Cross);
        DefineFunction("vector-length", GameMath.Vector3Length);
        DefineFunction("vector-normalize", GameMath.Vector3Normalize);
        DefineFunction("vector-distance", GameMath.Vector3Distance);
        DefineFunction("vector-lerp", GameMath.Vector3Lerp);
        DefineFunction("vec4", GameMath.Vec4);
        DefineFunction("quat-identity", GameMath.QuatIdentity);
        DefineFunction("quat-from-axis-angle", GameMath.QuatFromAxisAngle);
        DefineFunction("quat-from-euler", GameMath.QuatFromEuler);
        DefineFunction("quat*", GameMath.QuatMultiply);
        DefineFunction("quat-slerp", GameMath.QuatSlerp);
        DefineFunction("quat-to-euler", GameMath.QuatToEuler);
        DefineFunction("quat-rotate-vec3", GameMath.QuatRotateVec3);
        DefineFunction("matrix-identity", GameMath.MatrixIdentity);
        DefineFunction("matrix*", GameMath.MatrixMultiply);
        DefineFunction("matrix-inverse", GameMath.MatrixInverse);
        DefineFunction("matrix-from-quat-trans", GameMath.MatrixFromQuatTrans);
        DefineFunction("matrix-transform-point", GameMath.MatrixTransformPoint);
        DefineFunction("matrix-transform-dir", GameMath.MatrixTransformDirection);
        DefineFunction("matrix-look-at", GameMath.MatrixLookAt);
        DefineFunction("matrix-perspective", GameMath.MatrixPerspective);
        DefineFunction("transform-create", GameMath.TransformCreate);
        DefineFunction("transform-get-pos", GameMath.TransformGetPosition);
        DefineFunction("transform-set-pos!", GameMath.TransformSetPosition);
        DefineFunction("transform-get-rot", GameMath.TransformGetRotation);
        DefineFunction("transform-set-rot!", GameMath.TransformSetRotation);
        DefineFunction("transform-forward", GameMath.TransformForward);
        DefineFunction("transform-destroy!", GameMath.TransformDestroy);
        DefineFunction("bbox-make", GameMath.BBoxMake);
        DefineFunction("bbox-contains?", GameMath.BBoxContains);
        DefineFunction("bbox-intersects?", GameMath.BBoxIntersects);
        DefineFunction("bbox-center", GameMath.BBoxCenter);
        DefineFunction("lerp", GameMath.Lerp);
        DefineFunction("clamp", GameMath.Clamp);
        DefineFunction("smooth-step", GameMath.SmoothStep);
        DefineFunction("smoother-step", GameMath.SmootherStep);
        DefineFunction("deg->rad", GameMath.DegToRad);
        DefineFunction("rad->deg", GameMath.RadToDeg);
        DefineFunction("wrap-angle-180", GameMath.WrapAngle180);
        DefineFunction("angle-delta", GameMath.AngleDelta);
        DefineFunction("units->meters", GameMath.UnitsToMeters);
        DefineFunction("meters->units", GameMath.MetersToUnits);
        DefineFunction("random-float", GameMath.RandomFloat);
        DefineFunction("random-int", GameMath.RandomInt);
        DefineFunction("random-point-in-sphere", GameMath.RandomPointInSphere);
        DefineFunction("random-on-sphere", GameMath.RandomOnSphere);
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
        DefineFunction("timer-start", GameClock.TimerStart);
        DefineFunction("timer-repeat", GameClock.TimerRepeat);
        DefineFunction("timer-cancel", GameClock.TimerCancel);
        DefineFunction("timer-remaining", GameClock.TimerRemaining);
        
        // ===================================================================
        // PROCESS RUNTIME
        // ===================================================================
        
        DefineFunction("process-spawn", ProcessRuntime.ProcessSpawn);
        DefineFunction("process-kill", ProcessRuntime.ProcessKill);
        DefineFunction("process-alive?", ProcessRuntime.IsProcessAlive);
        DefineFunction("process-parent", ProcessRuntime.GetProcessParent);
        DefineFunction("process-children", ProcessRuntime.GetProcessChildren);
        DefineFunction("go-state", ProcessRuntime.GoState);
        DefineFunction("define-state", ProcessRuntime.DefineState);
        DefineFunction("suspend", ProcessRuntime.Suspend);
        DefineFunction("suspend-for-frames", ProcessRuntime.SuspendForFrames);
        DefineFunction("suspend-until", ProcessRuntime.SuspendUntil);
        DefineFunction("send-event", ProcessRuntime.SendEvent);
        DefineFunction("broadcast-event", ProcessRuntime.BroadcastEvent);
        DefineFunction("set-process-priority!", ProcessRuntime.SetProcessPriority);
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
