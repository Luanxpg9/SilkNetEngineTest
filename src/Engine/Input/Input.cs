using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Engine;

/// <summary>
/// Keyboard and mouse state for the current frame. Silk.NET reports input as events (key down,
/// key up), but game code usually wants to ask questions instead: is this key held right now?
/// Was it pressed this frame? This class records the events as they arrive and answers those
/// questions, so an <c>Update</c> can just check <c>Input.WasPressed(Key.Space)</c>.
///
/// <see cref="Game"/> calls <see cref="BeginFrame"/> once per frame, before <c>Update</c>.
/// "This frame" means: since the previous call.
/// </summary>
public sealed class Input
{
    private readonly IWindow _window;
    private readonly IMouse? _mouse;
    private readonly ButtonStates _keys = new(512);   // Silk.NET's Key values go up to 348
    private readonly ButtonStates _buttons = new(16); // MouseButton values go up to 11
    private StandardCursor _cursor = StandardCursor.Default;

    internal Input(IInputContext context, IWindow window)
    {
        _window = window;

        foreach (var keyboard in context.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => _keys.Down((int)key);
            keyboard.KeyUp += (_, key, _) => _keys.Up((int)key);
        }

        foreach (var mouse in context.Mice)
        {
            mouse.MouseDown += (_, button) => _buttons.Down((int)button);
            mouse.MouseUp += (_, button) => _buttons.Up((int)button);
        }

        // Position and cursor come from the first mouse. Several mice still move one pointer.
        _mouse = context.Mice.FirstOrDefault();
        MousePosition = _mouse?.Position ?? new Vector2(-1f);
    }

    /// <summary>Mouse position in window pixels, (0, 0) at the top left, y down. Read at the start of the frame.</summary>
    public Vector2 MousePosition { get; private set; }

    /// <summary>The mouse position in world units, as seen through <paramref name="camera"/>.</summary>
    public Vector2 MouseWorldPosition(Camera2D camera) =>
        camera.ScreenToWorld(MousePosition, new Vector2(_window.Size.X, _window.Size.Y));

    // Held: down right now, or tapped so quickly this frame that it was pressed and released
    // between two frames. Counting the tap as held keeps it from being lost.
    public bool IsDown(Key key) => _keys.IsDown((int)key);
    public bool WasPressed(Key key) => _keys.WasPressed((int)key);
    public bool WasReleased(Key key) => _keys.WasReleased((int)key);

    public bool IsDown(MouseButton button) => _buttons.IsDown((int)button);
    public bool WasPressed(MouseButton button) => _buttons.WasPressed((int)button);
    public bool WasReleased(MouseButton button) => _buttons.WasReleased((int)button);

    /// <summary>The mouse cursor's shape. Setting the same value again does nothing, so it's fine to set every frame.</summary>
    public StandardCursor Cursor
    {
        get => _cursor;
        set
        {
            if (value == _cursor)
                return;
            _cursor = value;
            if (_mouse is not null)
                _mouse.Cursor.StandardCursor = value;
        }
    }

    internal void BeginFrame()
    {
        _keys.BeginFrame();
        _buttons.BeginFrame();
        if (_mouse is not null)
            MousePosition = _mouse.Position;
    }

    /// <summary>
    /// Down/pressed/released flags for a set of buttons, indexed by the enum value.
    /// Events fill the "since last frame" arrays; <see cref="BeginFrame"/> moves them to
    /// the "this frame" arrays that the questions read, so answers stay the same for a whole frame.
    /// Plain bool arrays, so nothing allocates once the game is running.
    /// </summary>
    private sealed class ButtonStates(int count)
    {
        private readonly bool[] _down = new bool[count];
        private readonly bool[] _pressedSinceLastFrame = new bool[count];
        private readonly bool[] _releasedSinceLastFrame = new bool[count];
        private readonly bool[] _pressedThisFrame = new bool[count];
        private readonly bool[] _releasedThisFrame = new bool[count];

        // Key.Unknown is -1, and a future Silk.NET could add values past the array.
        private bool Valid(int i) => (uint)i < (uint)count;

        public void Down(int i)
        {
            if (!Valid(i))
                return;
            _down[i] = true;
            _pressedSinceLastFrame[i] = true;
        }

        public void Up(int i)
        {
            if (!Valid(i))
                return;
            _down[i] = false;
            _releasedSinceLastFrame[i] = true;
        }

        public void BeginFrame()
        {
            Array.Copy(_pressedSinceLastFrame, _pressedThisFrame, count);
            Array.Copy(_releasedSinceLastFrame, _releasedThisFrame, count);
            Array.Clear(_pressedSinceLastFrame);
            Array.Clear(_releasedSinceLastFrame);
        }

        public bool IsDown(int i) => Valid(i) && (_down[i] || _pressedThisFrame[i]);
        public bool WasPressed(int i) => Valid(i) && _pressedThisFrame[i];
        public bool WasReleased(int i) => Valid(i) && _releasedThisFrame[i];
    }
}
