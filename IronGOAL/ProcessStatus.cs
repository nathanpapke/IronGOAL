namespace IronGOAL;

internal enum ProcessStatus
{
    /// <summary>Spawned but not yet started; waiting for first Tick.</summary>
    Pending,
    /// <summary>Thread is executing the update proc.</summary>
    Running,
    /// <summary>Thread is parked, ready to be resumed by the scheduler.</summary>
    Suspended,
    /// <summary>Update proc returned or faulted, pending reap.</summary>
    Dead
}
