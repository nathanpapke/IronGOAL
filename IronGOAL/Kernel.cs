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
            config.DebugChannelCapacity,
            config.MemoryChannelCapacity);
        
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
        
        // ===================================================================
        // ENTITY SYSTEM
        // ===================================================================
        
        DefineFunction("entity-spawn", EntitySystem.Spawn);
        DefineFunction("entity-destroy!", EntitySystem.Destroy);
        DefineFunction("entity-exists?", EntitySystem.Exists);
        DefineFunction("entity-get-pos", EntitySystem.GetPosition);
        DefineFunction("entity-set-pos!", EntitySystem.SetPosition);
        DefineFunction("entity-get-rot", EntitySystem.GetRotation);
        DefineFunction("entity-set-rot!", EntitySystem.SetRotation);
        DefineFunction("entity-get-scale", EntitySystem.GetScale);
        DefineFunction("entity-set-scale!", EntitySystem.SetScale);
        DefineFunction("entity-get-prop", EntitySystem.GetProperty);
        DefineFunction("entity-set-prop!", EntitySystem.SetProperty);
        DefineFunction("entity-has-prop?", EntitySystem.HasProperty);
        DefineFunction("entity-has-component?", EntitySystem.HasComponent);
        DefineFunction("entity-get-component", EntitySystem.GetComponent);
        DefineFunction("entity-find-by-type", EntitySystem.FindByType);
        DefineFunction("entity-find-by-tag", EntitySystem.FindByTag);
        DefineFunction("entity-find-in-radius", EntitySystem.FindInRadius);
        DefineFunction("entity-find-nearest", EntitySystem.FindNearest);
        DefineFunction("entity-add-tag!", EntitySystem.AddTag);
        DefineFunction("entity-remove-tag!", EntitySystem.RemoveTag);
        DefineFunction("entity-has-tag?", EntitySystem.HasTag);
        DefineFunction("entity-bind-process!", EntitySystem.BindProcess);
        DefineFunction("entity-get-process", EntitySystem.GetProcess);
        DefineFunction("entity-get-entity", EntitySystem.GetEntity);
        
        // ===================================================================
        // ANIMATION SYSTEM
        // ===================================================================
        
        DefineFunction("anim-play", AnimationSystem.Play);
        DefineFunction("anim-play-blend", AnimationSystem.PlayBlend);
        DefineFunction("anim-stop", AnimationSystem.Stop);
        DefineFunction("anim-pause", AnimationSystem.Pause);
        DefineFunction("anim-current", AnimationSystem.Current);
        DefineFunction("anim-current-frame", AnimationSystem.CurrentFrame);
        DefineFunction("anim-length", AnimationSystem.Length);
        DefineFunction("anim-playing?", AnimationSystem.IsPlaying);
        DefineFunction("anim-blending?", AnimationSystem.IsBlending);
        DefineFunction("define-blend-tree", AnimationSystem.DefineBlendTree);
        DefineFunction("set-blend-param!", AnimationSystem.SetBlendTreeParam);
        DefineFunction("get-blend-param", AnimationSystem.GetBlendTreeParam);
        DefineFunction("get-joint-transform", AnimationSystem.GetJointTransform);
        DefineFunction("set-joint-override!", AnimationSystem.SetJointOverride);
        DefineFunction("clear-joint-override!", AnimationSystem.ClearJointOverride);
        DefineFunction("anim-on-event", AnimationSystem.OnEvent);
        DefineFunction("set-ik-target!", AnimationSystem.SetIKTarget);
        DefineFunction("set-ik-weight!", AnimationSystem.SetIKWeight);
        
        // ===================================================================
        // AUDIO SYSTEM
        // ===================================================================
        
        
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
