using System.Numerics;

namespace IronGOAL.Bus;

public readonly struct AudioCommand
{
    public AudioCommandType Type     { get; init; }

    // Engine-owned Clip Handle
    // For Play/Play2D this is the clip to start.
    // For Stop/SetVolume/SetPitch/SetPosition this is the handle returned
    // by a previous Play call, stored by the script in a local variable.
    public int              ClipId   { get; init; }
    
    public string?          Name { get; init; }

    public float            Volume   { get; init; }   // 0.0 – 1.0
    public float            Pitch    { get; init; }   // 1.0 = normal, 2.0 = octave up
    public float            Value { get; init; }
    
    public Vector3          Position { get; init; }   // world-space; ignored for Play2D
    public Vector3          Forward { get; init; }
    public Vector3          Up { get; init; }
    
    public int              ProcessHandle { get; init; }
}
