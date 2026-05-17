namespace IronGOAL.Bus;

public enum DebugCommandType
{
    Log,        // Informational Message  (maps to GOAL's (format #t ...))
    Warning,    // Non-fatal Warning
    Error,      // Script-level Error (not an exception - GOAL continued on errors)
    Inspect,    // Result of (Inspect Entity) - Formatted Field Dump
    Assert,     // Failed Assertion from a Script-level (Assert ...) Call
    ProfileBegin,
    ProfileEnd
}
