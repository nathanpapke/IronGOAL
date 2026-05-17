namespace IronGOAL.Bus;

public readonly struct Timestamped<TCommand> where TCommand : struct
{
    // Actual Command Being Wrapped
    // Stored by value - no boxing.
    public TCommand Command  { get; init; }

    // Monotonic tick counter incremented by Kernel.Tick() each frame.
    // Lets the host correlate a debug message to a specific simulation step
    // without relying on wall-clock time, which drifts under a debugger.
    public long     FrameId  { get; init; }

    // Simulation seconds elapsed since runtime was started.
    // Distinct from wall-clock time - pausing the game pauses this clock.
    public float    GameTime { get; init; }
}
