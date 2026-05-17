namespace IronGOAL;

public enum GoalLogSeverity
{
    Info,       // Normal Operational Event
    Warning,    // Degraded but Recoverable Condition
    Error,      // Operation Failed - result carries error code
    Fatal,      // Programming Error - invalid access on a failure result
}
/// <summary>
/// The delegate the host passes in. Must never throw - IronGOAL calls it
/// from inside catch blocks and from Result.Value's guard path.
/// </summary>
public delegate void GoalLogHandler(
    GoalLogSeverity severity,
    GoalErrorCode   code,
    string          message);
    