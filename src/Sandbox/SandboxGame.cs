using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The physics playground: a toolbar for placing falling and fixed rectangles, and a camera that
/// pans with WASD. The engine's <see cref="Game"/> runs the window and the loop; the rigid-body
/// physics (<see cref="Physics"/>, <see cref="Body"/>) belongs to Sandbox.
/// </summary>
sealed class SandboxGame() : Game("HavingFunWithEngine - Sandbox")
{
    private const float CameraSpeed = 1.5f;    // world units per second
    private const float MinPlacedSize = 0.01f; // world units; smaller drags are treated as accidental clicks

    // Falling rectangles take the next color from this list, so neighbors are easy to tell apart.
    // Fixed ones are grey like the platform, so it's clear at a glance what will move.
    private static readonly Color FixedColor = new(0.45f, 0.47f, 0.52f);
    private static readonly Color PreviewColor = new(0.4f, 0.7f, 1f);
    private static readonly Color[] Palette =
    [
        new(0.9f, 0.55f, 0.2f),   // orange
        new(0.35f, 0.75f, 0.45f), // green
        new(0.85f, 0.35f, 0.4f),  // red
        new(0.75f, 0.6f, 0.9f),   // lilac
        new(0.95f, 0.85f, 0.35f), // yellow
    ];

    // The scene, in world units. The camera shows 2 units vertically, so y runs from -1 to 1 at the start.
    // Bodies placed with the toolbar are added to the end.
    private readonly List<Body> _bodies =
    [
        new(position: new(0f, -0.8f), size: new(2.4f, 0.15f), color: FixedColor, falls: false),        // a platform
        new(position: new(0.5f, -0.6f), size: new(0.3f, 0.25f), color: Palette[0], falls: false),      // a custom box resting on it
    ];

    private readonly Camera2D _camera = new(viewHeight: 2f);
    private readonly Physics _physics = new();

    private Tool _selectedTool = Tool.None;
    private bool _placeWithGravity = true; // the Rectangle tool's option: do placed rectangles fall?
    private Vector2? _dragStart;           // world position where the current drag began, or null when not dragging
    private int _placedCount;

    protected override void Resized(Vector2 framebufferSize) => _camera.AspectRatio = AspectRatio;

    protected override void Update(float deltaTime)
    {
        // Physics keeps its own 1/240 s fixed step, so it's fed the real frame time.
        _physics.Update(_bodies, deltaTime);

        if (Input.WasPressed(Key.Escape))
            Exit();
        if (Input.WasPressed(Key.G))
            _placeWithGravity = !_placeWithGravity;

        HandleMouse();

        // A crosshair over the scene shows that clicking will place something.
        Input.Cursor = _selectedTool != Tool.None && !Toolbar.Contains(Input.MousePosition)
            ? StandardCursor.Crosshair
            : StandardCursor.Default;

        // WASD or arrow keys pan the camera along X and Y.
        Vector2 direction = Vector2.Zero;
        if (Input.IsDown(Key.A) || Input.IsDown(Key.Left))  direction.X -= 1;
        if (Input.IsDown(Key.D) || Input.IsDown(Key.Right)) direction.X += 1;
        if (Input.IsDown(Key.S) || Input.IsDown(Key.Down))  direction.Y -= 1;
        if (Input.IsDown(Key.W) || Input.IsDown(Key.Up))    direction.Y += 1;

        // Normalize so diagonal movement isn't faster than straight movement.
        if (direction != Vector2.Zero)
            _camera.Position += Vector2.Normalize(direction) * CameraSpeed * deltaTime;
    }

    private void HandleMouse()
    {
        var mouse = Input.MousePosition;

        if (Input.WasPressed(MouseButton.Right))
            _dragStart = null; // right click cancels a drag in progress

        if (Input.WasPressed(MouseButton.Left))
        {
            // Clicks on the panel pick a tool (clicking the selected one again deselects it)
            // or flip the selected tool's option, and never reach the scene.
            if (Toolbar.Contains(mouse))
            {
                if (Toolbar.ToolAt(mouse) is Tool tool)
                    _selectedTool = _selectedTool == tool ? Tool.None : tool;
                else if (_selectedTool == Tool.Rectangle && Toolbar.GravityToggleContains(mouse))
                    _placeWithGravity = !_placeWithGravity;
            }
            else if (_selectedTool == Tool.Rectangle)
            {
                _dragStart = Input.MouseWorldPosition(_camera);
            }
        }

        // Checked after the press, so a click that goes down and up within one frame still places.
        if (Input.WasReleased(MouseButton.Left) && _dragStart is Vector2 start)
        {
            _dragStart = null;
            var area = Rect.FromCorners(start, Input.MouseWorldPosition(_camera));
            if (area.Size.X >= MinPlacedSize && area.Size.Y >= MinPlacedSize)
            {
                var color = _placeWithGravity ? Palette[_placedCount % Palette.Length] : FixedColor;
                _bodies.Add(new Body(area.Center, area.Size, color, _placeWithGravity));
                if (_placeWithGravity)
                    _placedCount++;
                Log.Info($"Placed {(_placeWithGravity ? "falling" : "fixed")} rectangle at ({area.Center.X:F2}, {area.Center.Y:F2}), size {area.Size.X:F2} x {area.Size.Y:F2}");
            }
        }
    }

    protected override void Draw()
    {
        // The scene, through the camera.
        Sprites.Begin(_camera.ViewProjection);
        foreach (var body in _bodies)
            Sprites.Draw(new Rect(body.Position, body.Size), body.Color, body.Angle);

        if (_dragStart is Vector2 start)
            Sprites.Draw(Rect.FromCorners(start, Input.MouseWorldPosition(_camera)), PreviewColor);
        Sprites.End();

        // The UI, drawn last so it sits on top, in window units so the camera doesn't move it.
        Sprites.Begin(ScreenProjection);
        Toolbar.Draw(Sprites, WindowSize.Y, Input.MousePosition, _selectedTool, _placeWithGravity);
        Sprites.End();
    }
}
