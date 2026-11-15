using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Engine;

/// <summary>
/// Keyboard and mouse state. Silk.NET reports input as events (key down, key up), but game code
/// usually wants to ask questions instead: is this key held right now? Was it pressed just now?
/// This class records the events as they arrive and answers those questions, so game code can
/// just check <c>Input.WasPressed(Key.Space)</c>.
///
/// "Just now" depends on where you ask, and the answer is right in both places:
/// <list type="bullet">
/// <item>In <c>Update</c> and <c>Draw</c>: since the previous frame.</item>
/// <item>In <c>FixedUpdate</c>: since the previous fixed step. A frame can run 0, 1 or several fixed
/// steps, so presses are kept until a fixed step runs, and each press is seen by exactly one of them:
/// never missed, never counted twice. That's what makes "fire on click" safe in the simulation.</item>
/// </list>
/// <see cref="Game"/> drives this: <see cref="BeginFrame"/> once per frame, and
/// <see cref="BeginFixedStep"/> / <see cref="EndFixedStep"/> around each <c>FixedUpdate</c>.
/// </summary>
public sealed class Input
{
    private readonly IWindow _window;
    private readonly IMouse? _mouse;
    private readonly ButtonStates _keys = new(512);   // Silk.NET's Key values go up to 348
    private readonly ButtonStates _buttons = new(16); // MouseButton values go up to 11
    private StandardCursor _cursor = StandardCursor.Default;
    private bool _inFixedStep; // true while FixedUpdate runs: questions then use the fixed-step edges

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

    // Held: down right now, or tapped so quickly that it was pressed and released before anyone
    // asked. Counting the tap as held for that one frame (or fixed step) keeps it from being lost.
    public bool IsDown(Key key) => _keys.IsDown((int)key, _inFixedStep);
    public bool WasPressed(Key key) => _keys.WasPressed((int)key, _inFixedStep);
    public bool WasReleased(Key key) => _keys.WasReleased((int)key, _inFixedStep);

    public bool IsDown(MouseButton button) => _buttons.IsDown((int)button, _inFixedStep);
    public bool WasPressed(MouseButton button) => _buttons.WasPressed((int)button, _inFixedStep);
    public bool WasReleased(MouseButton button) => _buttons.WasReleased((int)button, _inFixedStep);

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
        _keys.Frame.Latch();
        _buttons.Frame.Latch();
        if (_mouse is not null)
            MousePosition = _mouse.Position;
    }

    internal void BeginFixedStep()
    {
        _keys.Fixed.Latch();
        _buttons.Fixed.Latch();
        _inFixedStep = true;
    }

    internal void EndFixedStep() => _inFixedStep = false;

    /// <summary>
    /// Down/pressed/released state for a set of buttons, indexed by the enum value. The live "down"
    /// flags are shared; pressed and released are tracked twice, once per frame and once per fixed
    /// step, because the two run at different rates. Plain bool arrays, so nothing allocates once the
    /// game is running.
    /// </summary>
    private sealed class ButtonStates(int count)
    {
        private readonly bool[] _down = new bool[count];
        public readonly Edges Frame = new(count);
        public readonly Edges Fixed = new(count);

        // Key.Unknown is -1, and a future Silk.NET could add values past the array.
        private bool Valid(int i) => (uint)i < (uint)count;

        public void Down(int i)
        {
            if (!Valid(i))
                return;
            _down[i] = true;
            Frame.Pressed(i);
            Fixed.Pressed(i);
        }

        public void Up(int i)
        {
            if (!Valid(i))
                return;
            _down[i] = false;
            Frame.Released(i);
            Fixed.Released(i);
        }

        public bool IsDown(int i, bool fixedStep) => Valid(i) && (_down[i] || Edges(fixedStep).WasPressed(i));
        public bool WasPressed(int i, bool fixedStep) => Valid(i) && Edges(fixedStep).WasPressed(i);
        public bool WasReleased(int i, bool fixedStep) => Valid(i) && Edges(fixedStep).WasReleased(i);

        private Edges Edges(bool fixedStep) => fixedStep ? Fixed : Frame;
    }

    /// <summary>
    /// Presses and releases for one consumer (frames or fixed steps). Events fill the "pending" flags.
    /// <see cref="Latch"/> moves them to the "current" flags that questions read, and clears pending,
    /// so the answers stay the same until the next latch, and each event is latched exactly once.
    /// </summary>
    private sealed class Edges(int count)
    {
        private readonly bool[] _pendingPressed = new bool[count];
        private readonly bool[] _pendingReleased = new bool[count];
        private readonly bool[] _pressed = new bool[count];
        private readonly bool[] _released = new bool[count];

        public void Pressed(int i) => _pendingPressed[i] = true;
        public void Released(int i) => _pendingReleased[i] = true;

        public void Latch()
        {
            Array.Copy(_pendingPressed, _pressed, count);
            Array.Copy(_pendingReleased, _released, count);
            Array.Clear(_pendingPressed);
            Array.Clear(_pendingReleased);
        }

        public bool WasPressed(int i) => _pressed[i];
        public bool WasReleased(int i) => _released[i];
    }
}
