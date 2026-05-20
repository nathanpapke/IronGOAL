using System.Threading.Channels;

using IronGOAL.Bus;

namespace IronGOAL;

public sealed class Host : IDisposable
{
    private readonly Kernel        _kernel;
    private readonly GoalLogHandler _log;
    private bool                   _disposed;
    
    private Host(Kernel kernel, GoalLogHandler log)
    {
        _kernel = kernel;
        _log    = log;
    }
    
    // =======================================================================
    // CONSTRUCTION
    // =======================================================================
    
    public static GoalResult<Host> Create(GoalRuntimeConfig config)
    {
        if (config is null)
            return GoalResult<Host>.Fail(
                GoalErrorCode.InvalidConfig, "Config must not be null.");
        
        // Seed the static logger before anything else so that Value access
        // on any result produced during construction is already safe.
        GoalResultLogger.Seed(config.LogHandler);
        
        var validation = ValidateConfig(config);
        if (validation.IsFailure)
        {
            config.LogHandler(GoalLogSeverity.Error,
                validation.ErrorCode, validation.ErrorMessage);
            return GoalResult<Host>.Fail(
                validation.ErrorCode, validation.ErrorMessage);
        }
        
        try
        {
            var kernel  = new Kernel(config);
            var runtime = new Host(kernel, config.LogHandler);
            config.LogHandler(GoalLogSeverity.Info,
                GoalErrorCode.None, "Host created successfully.");
            return GoalResult<Host>.Okay(runtime);
        }
        catch (OutOfMemoryException ex)
        {
            var msg = $"Heap allocation failed: {ex.Message}";
            config.LogHandler(GoalLogSeverity.Fatal,
                GoalErrorCode.HeapAllocationFailed, msg);
            return GoalResult<Host>.Fail(
                GoalErrorCode.HeapAllocationFailed, msg);
        }
        catch (Exception ex)
        {
            var msg = $"Runtime boot failed: {ex.Message}";
            config.LogHandler(GoalLogSeverity.Fatal,
                GoalErrorCode.SchemeBootFailed, msg);
            return GoalResult<Host>.Fail(
                GoalErrorCode.SchemeBootFailed, msg);
        }
    }
    
    // =======================================================================
    // SCRIPT LOADING
    // =======================================================================
    
    public GoalResult LoadScript(string path)
    {
        if (_disposed)
        {
            var msg = "Cannot load script: runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult.Fail(GoalErrorCode.RuntimeDisposed, msg);
        }
        
        if (string.IsNullOrWhiteSpace(path))
        {
            var msg = "Script path must not be null or empty.";
            _log(GoalLogSeverity.Error, GoalErrorCode.ScriptNotFound, msg);
            return GoalResult.Fail(GoalErrorCode.ScriptNotFound, msg);
        }
        
        string resolved = ResolveScriptPath(path, _kernel.Config.ScriptDirectory);
        
        if (!File.Exists(resolved))
        {
            var msg = $"Script not found: '{resolved}'";
            _log(GoalLogSeverity.Error, GoalErrorCode.ScriptNotFound, msg);
            return GoalResult.Fail(GoalErrorCode.ScriptNotFound, msg);
        }
        
        try
        {
            _kernel.LoadFile(resolved);
            _log(GoalLogSeverity.Info, GoalErrorCode.None,
                $"Loaded script: '{resolved}'");
            return GoalResult.Okay;
        }
        catch (IOException ex)
        {
            var msg = $"Could not read '{resolved}': {ex.Message}";
            _log(GoalLogSeverity.Error, GoalErrorCode.ScriptReadFailed, msg);
            return GoalResult.Fail(GoalErrorCode.ScriptReadFailed, msg);
        }
        catch (Exception ex)
        {
            var msg = $"Script evaluation failed for '{resolved}': {ex.Message}";
            _log(GoalLogSeverity.Error, GoalErrorCode.ScriptEvalFailed, msg);
            return GoalResult.Fail(GoalErrorCode.ScriptEvalFailed, msg);
        }
    }
    
    // =======================================================================
    // FRAME TICK
    // =======================================================================
    
    public GoalResult Tick(float deltaTime)
    {
        if (_disposed)
        {
            var msg = "Cannot tick: runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult.Fail(GoalErrorCode.RuntimeDisposed, msg);
        }
        
        if (deltaTime < 0f)
        {
            var msg = $"deltaTime must be >= 0, got {deltaTime}.";
            _log(GoalLogSeverity.Warning, GoalErrorCode.InvalidConfig, msg);
            return GoalResult.Fail(GoalErrorCode.InvalidConfig, msg);
        }
        
        try
        {
            _kernel.Tick(deltaTime);
            return GoalResult.Okay;
        }
        catch (Exception ex)
        {
            var msg = $"Tick failed at frame {_kernel.FrameId}: {ex.Message}";
            _log(GoalLogSeverity.Error, GoalErrorCode.TickFailed, msg);
            return GoalResult.Fail(GoalErrorCode.TickFailed, msg);
        }
    }
    
    // =======================================================================
    // REPL / TEST SUPPORT
    // =======================================================================
    
    public object? Evaluate(string expression)
    {
        if (_disposed)
        {
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed,
                "Cannot evaluate: Host has been disposed.");
            return null;
        }
        
        if (string.IsNullOrWhiteSpace(expression))
        {
            _log(GoalLogSeverity.Warning, GoalErrorCode.EvalFailed,
                "Cannot evaluate: expression is empty.");
            return null;
        }
        
        var result = _kernel.Evaluate(expression);
        
        if (result.IsFailure)
        {
            _log(GoalLogSeverity.Error, GoalErrorCode.EvalFailed, result.ErrorMessage);
            return null;
        }
        
        return result.Value;
    }
    
    // =======================================================================
    // CHANNEL READERS
    // =======================================================================
    
    public ChannelReader<RenderCommand>              RenderCommands => _kernel.EventBus.RenderCommands;
    public ChannelReader<AudioCommand>               AudioCommands  => _kernel.EventBus.AudioCommands;
    public ChannelReader<GameEvent>                  GameEvents     => _kernel.EventBus.GameEvents;
    public ChannelReader<Timestamped<DebugCommand>>  DebugCommands  => _kernel.EventBus.DebugCommands;
    public ChannelReader<MemoryEvent>                MemoryEvents   => _kernel.EventBus.MemoryEvents;
    
    // =======================================================================
    // DISPOSAL
    // =======================================================================
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _kernel.Dispose();
            _log(GoalLogSeverity.Info, GoalErrorCode.None,
                "GoalRuntime disposed cleanly.");
        }
        catch
        {
            // Swallow unconditionally - Dispose must never propagate.
            // The log call itself is outside the try so a throw inside
            // Kernel.Dispose() is caught before reaching it.
            _log(GoalLogSeverity.Warning, GoalErrorCode.Unknown,
                "Exception swallowed during GoalRuntime disposal.");
        }
    }
    
    // =======================================================================
    // PRIVATE HELPERS
    // =======================================================================
    
    private static GoalResult ValidateConfig(GoalRuntimeConfig config)
    {
        if (config.GlobalHeapSize <= 0)
            return GoalResult.Fail(GoalErrorCode.InvalidConfig,
                $"GlobalHeapSize must be > 0, got {config.GlobalHeapSize}.");
        
        if (config.StackHeapSize <= 0)
            return GoalResult.Fail(GoalErrorCode.InvalidConfig,
                $"StackHeapSize must be > 0, got {config.StackHeapSize}.");
        
        if (config.StackHeapSize >= config.GlobalHeapSize)
            return GoalResult.Fail(GoalErrorCode.InvalidConfig,
                "StackHeapSize must be smaller than GlobalHeapSize.");
        
        if (config.RenderChannelCapacity <= 0)
            return GoalResult.Fail(GoalErrorCode.InvalidConfig,
                "RenderChannelCapacity must be > 0.");
        
        if (config.LogHandler is null)
            return GoalResult.Fail(GoalErrorCode.InvalidConfig,
                "LogHandler must not be null.");
        
        return GoalResult.Okay;
    }
    
    private static string ResolveScriptPath(string path, string? scriptDirectory)
    {
        if (Path.IsPathRooted(path) || scriptDirectory is null)
            return path;
        return Path.Combine(scriptDirectory, path);
    }
}
