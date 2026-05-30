using IronScheme.Runtime;

namespace IronGOAL;

/// <summary>
/// The four Scheme lambdas registered for one (processTypeName, stateName)
/// pair via <c>(define-state ...)</c>.  Any proc may be null; missing
/// handlers are silently skipped at dispatch time.
/// </summary>
internal sealed class StateDefinition
{
    public Callable? EnterProc  { get; init; }
    public Callable? UpdateProc { get; init; }
    public Callable? ExitProc   { get; init; }
    public Callable? EventProc  { get; init; }
}
