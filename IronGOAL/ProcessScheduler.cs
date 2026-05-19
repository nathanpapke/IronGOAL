namespace IronGOAL;

/// <summary>
/// This is a cooperative process scheduler.  Each ScriptProcess holds a
/// suspended IronScheme continuation.  The scheduler advances game time and
/// resumes any process whose wakeup time has elapsed, running it until it
/// yields again or exits.
/// </summary>
public class ProcessScheduler
{
    private float _gameTime;
    
    internal float GameTime => _gameTime;
    
    internal void Tick(float deltaTime)
    {
        _gameTime += deltaTime;
        // TODO: Dequeue ready ScriptProcess entries, resume continuations.
    }
}
