namespace IronGOAL;

public enum GoalErrorCode
{
    None = 0,

    // Construction
    InvalidConfig,
    HeapAllocationFailed,
    SchemeBootFailed,
    KernelRegistrationFailed,

    // LoadScript
    ScriptNotFound,
    ScriptReadFailed,
    ScriptEvalFailed,

    // Tick
    TickFailed,
    SchedulerCorrupted,

    // Evaluate
    EvalFailed,

    // Channel / Disposal
    RuntimeDisposed,

    // Programming Error - Value accessed on failure result
    InvalidAccess,

    // General
    Unknown
}
