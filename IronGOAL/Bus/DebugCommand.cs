namespace IronGOAL.Bus;

public readonly struct DebugCommand
{
    public DebugCommandType Type         { get; init; }
    
    // The Scheme symbol name of the Kernel method or script location
    // that produced this command - used to correlate log output to source.
    public string           SourceSymbol { get; init; }
    
    // Formatted Message
    // For Inspect, this is the full field dump string.
    // For Assert, this is the expression that failed.
    public string           Message      { get; init; }
}
