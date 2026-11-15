using System.Numerics;
using Engine;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Rectangle = Engine.Rectangle; // Silk.NET.Maths has a Rectangle too

const float CameraSpeed = 1.5f; // world units per second

// The scene, in world units. The camera shows 2 units vertically, so y runs from -1 to 1 at the start.
// Bodies placed with the toolbar are added to the end.
List<Body> bodies =
[
    new(position: new(0f, -0.8f), size: new(2.4f, 0.15f), color: new(0.45f, 0.47f, 0.52f), falls: false), // a platform
    new(position: new(0.5f, -0.6f), size: new(0.3f, 0.25f), color: new(0.9f, 0.55f, 0.2f), falls: false), // a custom box resting on it
];

// Falling rectangles take the next color from this list, so neighbors are easy to tell apart.
// Fixed ones are grey like the platform, so it's clear at a glance what will move.
var fixedColor = new Vector3(0.45f, 0.47f, 0.52f);
Vector3[] palette =
[
    new(0.9f, 0.55f, 0.2f),  // orange
    new(0.35f, 0.75f, 0.45f), // green
    new(0.85f, 0.35f, 0.4f),  // red
    new(0.75f, 0.6f, 0.9f),   // lilac
    new(0.95f, 0.85f, 0.35f), // yellow
];
var previewColor = new Vector3(0.4f, 0.7f, 1f);
const float MinPlacedSize = 0.01f; // world units; smaller drags are treated as accidental clicks

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(1280, 720),
    Title = "HavingFunWithEngine - Sandbox",
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
};

var window = Window.Create(options);
GL gl = null!;
QuadRenderer quads = null!;
IKeyboard? keyboard = null;
IMouse? mouse = null;

var camera = new Camera2D(viewHeight: 2f);

var physics = new Physics();

var selectedTool = Tool.None;
bool placeWithGravity = true; // the Rectangle tool's option: do placed rectangles fall?
Vector2? dragStart = null; // world position where the current drag began, or null when not dragging
int placedCount = 0;

window.Load += () =>
{
    gl = window.CreateOpenGL();

    var input = window.CreateInput();
    keyboard = input.Keyboards.FirstOrDefault();
    foreach (var kb in input.Keyboards)
        kb.KeyDown += (_, key, _) =>
        {
            if (key == Key.Escape)
                window.Close();
            else if (key == Key.G)
                placeWithGravity = !placeWithGravity;
        };

    mouse = input.Mice.FirstOrDefault();
    if (mouse is not null)
    {
        mouse.MouseDown += (m, button) =>
        {
            if (button == MouseButton.Right)
            {
                dragStart = null; // right click cancels a drag in progress
                return;
            }
            if (button != MouseButton.Left)
                return;

            // Clicks on the panel pick a tool (clicking the selected one again deselects it)
            // or flip the selected tool's option, and never reach the scene.
            if (Toolbar.Contains(m.Position))
            {
                if (Toolbar.ToolAt(m.Position) is Tool tool)
                    selectedTool = selectedTool == tool ? Tool.None : tool;
                else if (selectedTool == Tool.Rectangle && Toolbar.GravityToggleContains(m.Position))
                    placeWithGravity = !placeWithGravity;
                return;
            }

            if (selectedTool == Tool.Rectangle)
                dragStart = MouseWorldPosition();
        };

        mouse.MouseUp += (_, button) =>
        {
            if (button != MouseButton.Left || dragStart is not Vector2 start)
                return;

            dragStart = null;
            var color = placeWithGravity ? palette[placedCount % palette.Length] : fixedColor;
            var area = Rectangle.FromCorners(start, MouseWorldPosition(), color);
            if (area.Size.X >= MinPlacedSize && area.Size.Y >= MinPlacedSize)
            {
                bodies.Add(new Body(area.Center, area.Size, color, placeWithGravity));
                if (placeWithGravity)
                    placedCount++;
                Console.WriteLine($"Placed {(placeWithGravity ? "falling" : "fixed")} rectangle at ({area.Center.X:F2}, {area.Center.Y:F2}), size {area.Size.X:F2} x {area.Size.Y:F2}");
            }
        };
    }

    Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)} on {gl.GetStringS(StringName.Renderer)}");

    quads = new QuadRenderer(gl);

    var size = window.FramebufferSize;
    camera.AspectRatio = (float)size.X / size.Y;
};

window.FramebufferResize += size =>
{
    gl.Viewport(size);
    if (size.Y > 0) // minimizing reports a 0 height
        camera.AspectRatio = (float)size.X / size.Y;
};

var currentCursor = StandardCursor.Default;

window.Update += deltaTime =>
{
    physics.Update(bodies, (float)deltaTime);

    // A crosshair over the scene shows that clicking will place something.
    if (mouse is not null)
    {
        var wanted = selectedTool != Tool.None && !Toolbar.Contains(mouse.Position)
            ? StandardCursor.Crosshair
            : StandardCursor.Default;
        if (wanted != currentCursor)
            mouse.Cursor.StandardCursor = currentCursor = wanted;
    }

    if (keyboard is null)
        return;

    // WASD or arrow keys pan the camera along X and Y.
    Vector2 direction = Vector2.Zero;
    if (keyboard.IsKeyPressed(Key.A) || keyboard.IsKeyPressed(Key.Left))  direction.X -= 1;
    if (keyboard.IsKeyPressed(Key.D) || keyboard.IsKeyPressed(Key.Right)) direction.X += 1;
    if (keyboard.IsKeyPressed(Key.S) || keyboard.IsKeyPressed(Key.Down))  direction.Y -= 1;
    if (keyboard.IsKeyPressed(Key.W) || keyboard.IsKeyPressed(Key.Up))    direction.Y += 1;

    // Normalize so diagonal movement isn't faster than straight movement.
    if (direction != Vector2.Zero)
        camera.Position += Vector2.Normalize(direction) * CameraSpeed * (float)deltaTime;
};

window.Render += _ =>
{
    gl.ClearColor(0.1f, 0.1f, 0.15f, 1f);
    gl.Clear(ClearBufferMask.ColorBufferBit);

    quads.Begin();

    // The scene, through the camera.
    quads.SetViewProjection(camera.ViewProjection);
    foreach (var body in bodies)
        quads.Draw(body.Model, body.Color);

    if (dragStart is Vector2 start)
        quads.Draw(Rectangle.FromCorners(start, MouseWorldPosition(), previewColor));

    // The UI, drawn last so it sits on top. Same quads, but with a projection that maps window
    // pixels straight to the screen, top left = (0, 0), so the camera doesn't move it.
    var windowSize = window.Size;
    quads.SetViewProjection(Matrix4x4.CreateOrthographicOffCenter(0f, windowSize.X, windowSize.Y, 0f, -1f, 1f));

    quads.Draw(Toolbar.Panel(windowSize.Y));
    var mousePosition = mouse?.Position ?? new Vector2(-1f);
    for (int i = 0; i < Toolbar.Tools.Length; i++)
    {
        var tool = Toolbar.Tools[i];
        var button = Toolbar.Button(i, selected: tool == selectedTool, hovered: Toolbar.ToolAt(mousePosition) == tool);
        quads.Draw(button);
        foreach (var part in Toolbar.Icon(tool, button))
            quads.Draw(part);
    }

    if (selectedTool == Tool.Rectangle)
    {
        var toggle = Toolbar.GravityToggle(placeWithGravity, hovered: Toolbar.GravityToggleContains(mousePosition));
        quads.Draw(toggle);
        foreach (var part in Toolbar.GravityIcon(toggle, placeWithGravity))
            quads.Draw(part);
    }
};

window.Closing += () =>
{
    quads.Dispose();
    gl.Dispose();
};

window.Run();
window.Dispose();

Vector2 MouseWorldPosition()
{
    var size = window.Size;
    return camera.ScreenToWorld(mouse?.Position ?? Vector2.Zero, new Vector2(size.X, size.Y));
}
