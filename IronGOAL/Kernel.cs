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
        
        // Register all C# kernel functions as Scheme symbols.
        //var regResult = RegisterAll(_scheme, EventBus, config);
        //if (regResult.IsFailure)
        {
            //_log(GoalLogSeverity.Warning, GoalErrorCode.KernelRegistrationFailed,
                //$"Some kernel symbols failed to register: {regResult.ErrorMessage}");
        }
        
        _log(GoalLogSeverity.Info, GoalErrorCode.None, "Kernel booted successfully.");
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
        // GAMEMATH
        // ===================================================================
        
        DefineFunction("vec3", GameMath.Vec3);
        DefineFunction("vector+", GameMath.Vector3Add);
        DefineFunction("vector-", GameMath.Vector3Sub);
        DefineFunction("vector-scale", GameMath.Vector3Scale);
        DefineFunction("vector-dot", GameMath.Vector3Dot);
        DefineFunction("vector-cross", GameMath.Vector3Cross);
        DefineFunction("vector-length", GameMath.Vector3Length);
        DefineFunction("vector-normalize", GameMath.Vector3Normalize);
        DefineFunction("vector-distance", GameMath.Vector3Distance);
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
