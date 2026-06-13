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
    
    // ScriptLoader (form-chunked evaluator) - IronScheme's reader (`read`)
    // could not parse the source at all: an unmatched closing bracket, or
    // an unterminated string / list / block comment anywhere in the file.
    // Distinguished from ScriptEvalFailed, which means every form was read
    // successfully but `eval` raised a condition for one of them.
    ScriptSyntaxError,

    // General
    Unknown
}
