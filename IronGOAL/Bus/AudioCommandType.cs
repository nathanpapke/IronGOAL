namespace IronGOAL.Bus;

public enum AudioCommandType
{
    Play,           // Begin playback of a clip at a world position.
    Play2D,         // Begin playback with no spatial position (UI, music stings).
    Stop,           // Halt a playing clip by handle.
    SetVolume,      // Adjust volume of an in-flight clip.
    SetPitch,       // Adjust pitch of an in-flight clip.
    SetPosition     // Move a positional sound source (follow an entity).
}
