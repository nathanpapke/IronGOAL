namespace IronGOAL;

public static class GoalResultLogger
{
    private static GoalLogHandler? _handler;

    internal static void Seed(GoalLogHandler handler)
    {
        _handler = handler;
    }

    internal static void Log(GoalLogSeverity severity, GoalErrorCode code, string message)
    {
        // If no handler has been seeded, silently drop - never throw.
        _handler?.Invoke(severity, code, message);
    }
}
