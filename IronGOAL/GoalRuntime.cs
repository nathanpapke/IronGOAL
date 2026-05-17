using System.Threading.Channels;

using IronGOAL.Bus;

namespace IronGOAL;

public sealed class GoalRuntime : IDisposable
{
    private readonly Kernel        _kernel;
    private readonly GoalLogHandler _log;
    private bool                   _disposed;
    
    private GoalRuntime(Kernel kernel, GoalLogHandler log)
    {
        _kernel = kernel;
        _log    = log;
    }
    
    // =======================================================================
    // CONSTRUCTION
    // =======================================================================
    
    public static GoalResult<GoalRuntime> Create(GoalRuntimeConfig config)
    {
        if (config is null)
            return GoalResult<GoalRuntime>.Fail(
                GoalErrorCode.InvalidConfig, "Config must not be null.");
        
        // Seed the static logger before anything else so that Value access
        // on any result produced during construction is already safe.
        GoalResultLogger.Seed(config.LogHandler);
        
        var validation = ValidateConfig(config);
        if (validation.IsFailure)
        {
            config.LogHandler(GoalLogSeverity.Error,
                validation.ErrorCode, validation.ErrorMessage);
            return GoalResult<GoalRuntime>.Fail(
                validation.ErrorCode, validation.ErrorMessage);
        }
        
        try
        {
            var kernel  = new Kernel(config);
            var runtime = new GoalRuntime(kernel, config.LogHandler);
            config.LogHandler(GoalLogSeverity.Info,
                GoalErrorCode.None, "GoalRuntime created successfully.");
            return GoalResult<GoalRuntime>.Ok(runtime);
        }
        catch (OutOfMemoryException ex)
        {
            var msg = $"Heap allocation failed: {ex.Message}";
            config.LogHandler(GoalLogSeverity.Fatal,
                GoalErrorCode.HeapAllocationFailed, msg);
            return GoalResult<GoalRuntime>.Fail(
                GoalErrorCode.HeapAllocationFailed, msg);
        }
        catch (Exception ex)
        {
            var msg = $"Runtime boot failed: {ex.Message}";
            config.LogHandler(GoalLogSeverity.Fatal,
                GoalErrorCode.SchemeBootFailed, msg);
            return GoalResult<GoalRuntime>.Fail(
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
            _kernel.LoadScript(resolved);
            _log(GoalLogSeverity.Info, GoalErrorCode.None,
                $"Loaded script: '{resolved}'");
            return GoalResult.Ok;
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
            return GoalResult.Ok;
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
    
    public GoalResult<string> Evaluate(string expression)
    {
        if (_disposed)
        {
            var msg = "Cannot evaluate: runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<string>.Fail(GoalErrorCode.RuntimeDisposed, msg);
        }
        
        if (string.IsNullOrWhiteSpace(expression))
        {
            var msg = "Expression must not be null or empty.";
            _log(GoalLogSeverity.Warning, GoalErrorCode.EvalFailed, msg);
            return GoalResult<string>.Fail(GoalErrorCode.EvalFailed, msg);
        }
        
        try
        {
            string result = _kernel.Evaluate(expression);
            return GoalResult<string>.Ok(result);
        }
        catch (Exception ex)
        {
            var msg = $"Scheme evaluation error: {ex.Message}";
            _log(GoalLogSeverity.Error, GoalErrorCode.EvalFailed, msg);
            return GoalResult<string>.Fail(GoalErrorCode.EvalFailed, msg);
        }
    }
    
    // =======================================================================
    // CHANNEL READERS
    // =======================================================================
    
    public GoalResult<ChannelReader<RenderCommand>> GetRenderCommands()
    {
        if (_disposed)
        {
            var msg = "Runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<ChannelReader<RenderCommand>>.Fail(
                GoalErrorCode.RuntimeDisposed, msg);
        }
        return GoalResult<ChannelReader<RenderCommand>>.Ok(
            _kernel.EventBus.RenderCommands);
    }
    
    public GoalResult<ChannelReader<AudioCommand>> GetAudioCommands()
    {
        if (_disposed)
        {
            var msg = "Runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<ChannelReader<AudioCommand>>.Fail(
                GoalErrorCode.RuntimeDisposed, msg);
        }
        return GoalResult<ChannelReader<AudioCommand>>.Ok(
            _kernel.EventBus.AudioCommands);
    }
    
    public GoalResult<ChannelReader<GameEvent>> GetGameEvents()
    {
        if (_disposed)
        {
            var msg = "Runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<ChannelReader<GameEvent>>.Fail(
                GoalErrorCode.RuntimeDisposed, msg);
        }
        return GoalResult<ChannelReader<GameEvent>>.Ok(
            _kernel.EventBus.GameEvents);
    }
    
    public GoalResult<ChannelReader<Timestamped<DebugCommand>>> GetDebugCommands()
    {
        if (_disposed)
        {
            var msg = "Runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<ChannelReader<Timestamped<DebugCommand>>>.Fail(
                GoalErrorCode.RuntimeDisposed, msg);
        }
        return GoalResult<ChannelReader<Timestamped<DebugCommand>>>.Ok(
            _kernel.EventBus.DebugCommands);
    }
    
    public GoalResult<ChannelReader<MemoryEvent>> GetMemoryEvents()
    {
        if (_disposed)
        {
            var msg = "Runtime has been disposed.";
            _log(GoalLogSeverity.Error, GoalErrorCode.RuntimeDisposed, msg);
            return GoalResult<ChannelReader<MemoryEvent>>.Fail(
                GoalErrorCode.RuntimeDisposed, msg);
        }
        return GoalResult<ChannelReader<MemoryEvent>>.Ok(
            _kernel.EventBus.MemoryEvents);
    }
    
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
        
        return GoalResult.Ok;
    }
    
    private static string ResolveScriptPath(string path, string? scriptDirectory)
    {
        if (Path.IsPathRooted(path) || scriptDirectory is null)
            return path;
        return Path.Combine(scriptDirectory, path);
    }
}
