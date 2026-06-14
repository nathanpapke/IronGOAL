namespace IronGOAL.Bus;

public enum AudioCommandType
{
    Play,               // Begin playback of a clip at a world position.
    Play2D,             // Begin playback with no spatial position (UI, music stings).
    
    Stop,               // Halt a playing clip by handle.
    StopAll,            // Panic-stop every active voice.
    
    SetVolume,          // Adjust volume of an in-flight clip.
    SetPitch,           // Adjust pitch of an in-flight clip.
    SetPosition,        // Move a positional sound source (follow an entity).
    SetParam,           // Set a named RTPC / parameter on a clip.
    
    MusicPlay,          // Start a named background music track.
    MusicStop,          // Fade out and stop music; Value = fade-out seconds.
    MusicSetIntensity,  // Adaptive music intensity driver; Value ∈ [0, 1].
    
    SetListenerPos,     // Update the audio listener transform (pos/fwd/up).
    
    SetReverb,          // Apply a named reverb preset at a given wet level.
    
    DialogPlay          // Play a VO line; host sends send-event to ProcessHandle on completion.
}
