using IronGOAL.Backing;

namespace IronGOAL;

public sealed class GoalRuntimeConfig
{
    // =======================================================================
    // LOG HANDLING
    // =======================================================================

    /// <summary>
    /// The host must provide a log handler. IronGOAL never decides where
    /// log output goes - it only calls this. Must never throw.
    /// </summary>
    public required GoalLogHandler LogHandler { get; init; }
    
    // =======================================================================
    // HEAP SIZES
    // =======================================================================
    
    /// <summary>
    /// Size in bytes of the global heap arena. Long-lived game data -
    /// entities, level resources, type tables - is allocated here.
    /// Default: 64 MB, matching the IronGOAL.Console dev configuration.
    /// </summary>
    public int GlobalHeapSize { get; init; } = 64 * 1024 * 1024;
    
    /// <summary>
    /// Size in bytes of the per-process stack heap.  Freed automatically
    /// when a ScriptProcess exits.  Keep this smaller than the global heap.
    /// Default: 8 MB.
    /// </summary>
    public int StackHeapSize { get; init; } = 8 * 1024 * 1024;
    
    // =======================================================================
    // SCRIPT LOADING
    // =======================================================================
    
    /// <summary>
    /// Root directory that LoadScript() resolves relative paths against.
    /// If null, paths passed to LoadScript() must be absolute.
    /// </summary>
    public string? ScriptDirectory { get; init; }
    
    // =======================================================================
    // CHANNEL CAPACITIES
    // =======================================================================
    // Exposed here so the host can tune for its own frame budget without
    // recompiling the library. The defaults match the EventBus spec.
    
    public int TransformChannelCapacity { get; init; } = 4096;
    public int AudioChannelCapacity     { get; init; } = 1024;
    public int GameEventChannelCapacity { get; init; } = 512;
    public int DebugChannelCapacity     { get; init; } = 256;
    public int MemoryChannelCapacity    { get; init; } = 128;
    
    // =======================================================================
    // DEVELOPMENT FLAGS
    // =======================================================================
    
    /// <summary>
    /// When true, the MemoryEvent channel is active and MemoryArena
    /// publishes alloc/free events.  Disable in ship builds to eliminate
    /// the per-allocation overhead.
    /// Default: true (on) - turn off for production.
    /// </summary>
    public bool EnableMemoryTracking { get; init; } = true;
    
    /// <summary>
    /// When true, the Debug channel is active and KernelBacking publishes
    /// log, warn, inspect, and assert commands.  Disable in ship builds.
    /// Default: true (on) - turn off for production.
    /// </summary>
    public bool EnableDebugChannel { get; init; } = true;
    
    // =======================================================================
    // SCHEME ENVIRONMENT
    // =======================================================================
    
    /// <summary>
    /// An existing IronScheme top-level environment object - the value
    /// returned by <c>"(interaction-environment)".Eval()</c> - obtained by
    /// the host program before constructing this <c>Console</c>.
    /// </summary>
    public object? SchemeEnvironment { get; init; } = null;
    
    // =======================================================================
    // DATABASE SYSTEM
    // =======================================================================

    /// <summary>
    /// Console-provided handler for <c>(sql-query ...)</c> calls from scripts.
    /// <para>
    /// The delegate receives the raw SQL string and must return a
    /// <c>string[]</c> where <c>[0]</c> is the content-type name (a symbol
    /// name such as <c>"race-info"</c>) and <c>[1..N]</c> are the flat
    /// field-value strings - mirroring the layout of GOAL's <c>sql-result</c>
    /// type from Jak X.  Return <c>null</c> to signal query failure;
    /// <c>sql-query</c> will return <c>#f</c> to the script.
    /// </para>
    /// <para>
    /// When <c>null</c> (default), all <c>sql-query</c> calls return
    /// <c>#f</c> immediately, matching <c>sqlpipe-query</c>'s behavior when
    /// the named-pipe files are absent.
    /// </para>
    /// </summary>
    public DatabaseSystem.SqlQueryDelegate? SqlQueryHandler { get; init; } = null;
}
