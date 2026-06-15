using System.Numerics;

namespace IronGOAL;

public sealed class InputFrame
{
    private static readonly InputFrame Empty = new(
        down:         new HashSet<string>(),
        pressed:      new HashSet<string>(),
        released:     new HashSet<string>(),
        axes:         new Dictionary<string, float>(),
        mouseButtons: new bool[8]);
    
    private static InputFrame _current = Empty;
    
    /// <summary>The most recently published input snapshot.</summary>
    public static InputFrame Current => _current;
    
    private readonly HashSet<string> _down;
    private readonly HashSet<string> _pressedThisFrame;
    private readonly HashSet<string> _releasedThisFrame;
    private readonly Dictionary<string, float> _axes;
    private readonly bool[] _mouseButtons;
    
    public Vector2 LeftStick      { get; private init; }
    public Vector2 RightStick     { get; private init; }
    public float   LeftTrigger    { get; private init; }
    public float   RightTrigger   { get; private init; }
    public Vector2 MousePosition  { get; private init; }
    public Vector2 MouseDelta     { get; private init; }
    
    private InputFrame(
        HashSet<string> down,
        HashSet<string> pressed,
        HashSet<string> released,
        Dictionary<string, float> axes,
        bool[] mouseButtons)
    {
        _down              = down;
        _pressedThisFrame  = pressed;
        _releasedThisFrame = released;
        _axes              = axes;
        _mouseButtons      = mouseButtons;
    }
    
    /// <summary>
    /// Replaces <see cref="Current"/> with a new snapshot built from raw
    /// host input state. Must be called exactly once per
    /// <c>Kernel.Tick()</c>, before any Scheme code runs that frame -
    /// the same contract as <see cref="GameClock.Advance"/>.
    /// </summary>
    /// <param name="down">Set of button names currently held down.</param>
    /// <param name="axes">Named analog axis values, each in [-1, 1].</param>
    /// <param name="leftStick">Left stick position, each axis in [-1, 1].</param>
    /// <param name="rightStick">Right stick position, each axis in [-1, 1].</param>
    /// <param name="leftTrigger">Left trigger value in [0, 1].</param>
    /// <param name="rightTrigger">Right trigger value in [0, 1].</param>
    /// <param name="mousePosition">Mouse position in screen-space pixels.</param>
    /// <param name="mouseDelta">Mouse movement since the previous frame, in pixels.</param>
    /// <param name="mouseButtons">Mouse button held state, indexed by button.</param>
    public static void Update(
        IReadOnlySet<string> down,
        IReadOnlyDictionary<string, float> axes,
        Vector2 leftStick,
        Vector2 rightStick,
        float leftTrigger,
        float rightTrigger,
        Vector2 mousePosition,
        Vector2 mouseDelta,
        IReadOnlyList<bool> mouseButtons)
    {
        var previousDown = _current._down;
        
        var downSet     = new HashSet<string>(down);
        var pressedSet  = new HashSet<string>();
        var releasedSet = new HashSet<string>();
        
        foreach (var button in downSet)
        {
            if (!previousDown.Contains(button))
                pressedSet.Add(button);
        }
        
        foreach (var button in previousDown)
        {
            if (!downSet.Contains(button))
                releasedSet.Add(button);
        }
        
        var axesMap = new Dictionary<string, float>(axes);
        
        var buttons = new bool[Math.Max(8, mouseButtons.Count)];
        for (int i = 0; i < mouseButtons.Count; i++)
            buttons[i] = mouseButtons[i];
        
        _current = new InputFrame(downSet, pressedSet, releasedSet, axesMap, buttons)
        {
            LeftStick     = leftStick,
            RightStick    = rightStick,
            LeftTrigger   = leftTrigger,
            RightTrigger  = rightTrigger,
            MousePosition = mousePosition,
            MouseDelta    = mouseDelta,
        };
    }
    
    /// <summary>
    /// Resets to an empty snapshot. Called internally on runtime disposal;
    /// exposed for unit tests, mirroring <see cref="GameClock.Reset"/>.
    /// </summary>
    internal static void Reset() => _current = Empty;
    
    // =======================================================================
    // READS
    // =======================================================================
    
    /// <summary>True if <paramref name="button"/> transitioned down this frame.</summary>
    public bool Pressed(string button) => _pressedThisFrame.Contains(button);
    
    /// <summary>True if <paramref name="button"/> transitioned up this frame.</summary>
    public bool Released(string button) => _releasedThisFrame.Contains(button);
    
    /// <summary>True if <paramref name="button"/> is currently held down.</summary>
    public bool Held(string button) => _down.Contains(button);
    
    /// <summary>Value of a named analog axis, in [-1, 1]. Returns 0 if unknown.</summary>
    public float Analog(string axis) => _axes.TryGetValue(axis, out var v) ? v : 0f;
    
    /// <summary>True if the mouse button at <paramref name="index"/> is held down.</summary>
    public bool MouseButton(int index)
        => index >= 0 && index < _mouseButtons.Length && _mouseButtons[index];
}
