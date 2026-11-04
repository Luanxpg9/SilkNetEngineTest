using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

const string vertexShaderSource = """
    #version 330 core
    layout (location = 0) in vec2 aPosition;

    uniform mat4 uViewProjection;
    uniform mat4 uModel;

    void main()
    {
        gl_Position = uViewProjection * uModel * vec4(aPosition, 0.0, 1.0);
    }
    """;

const string fragmentShaderSource = """
    #version 330 core
    uniform vec3 uColor;

    out vec4 FragColor;

    void main()
    {
        FragColor = vec4(uColor, 1.0);
    }
    """;

const float CameraSpeed = 1.5f; // world units per second

// A unit quad centered on the origin. Each rectangle's model matrix stretches and moves it.
float[] quadVertices =
[
    //  x      y
    -0.5f, -0.5f, // 0: bottom left
     0.5f, -0.5f, // 1: bottom right
     0.5f,  0.5f, // 2: top right
    -0.5f,  0.5f, // 3: top left
];

// Two triangles that share the 0-2 diagonal, so 4 vertices are enough instead of 6.
uint[] quadIndices =
[
    0, 1, 2,
    2, 3, 0,
];

// The scene, in world units. The camera shows 2 units vertically, so y runs from -1 to 1 at the start.
// Rectangles placed with the toolbar are added to the end.
List<Rectangle> rectangles =
[
    new(Center: new(0f, -0.8f), Size: new(2.4f, 0.15f), Color: new(0.45f, 0.47f, 0.52f)), // platform the cloth falls onto
    new(Center: new(0.5f, -0.6f), Size: new(0.3f, 0.25f), Color: new(0.9f, 0.55f, 0.2f)), // a custom box resting on it
];

// Placed rectangles take the next color from this list, so neighbors are easy to tell apart.
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
    Title = "HavingFunWithEngine",
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
};

var window = Window.Create(options);
GL gl = null!;
uint vao = 0, vbo = 0, ebo = 0, shaderProgram = 0;
int viewProjectionLocation = -1, modelLocation = -1, colorLocation = -1;
IKeyboard? keyboard = null;
IMouse? mouse = null;

var camera = new Camera2D(viewHeight: 2f);

var selectedTool = Tool.None;
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
            // and never reach the scene.
            if (Toolbar.Contains(m.Position))
            {
                if (Toolbar.ToolAt(m.Position) is Tool tool)
                    selectedTool = selectedTool == tool ? Tool.None : tool;
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
            var placed = Rectangle.FromCorners(start, MouseWorldPosition(), palette[placedCount % palette.Length]);
            if (placed.Size.X >= MinPlacedSize && placed.Size.Y >= MinPlacedSize)
            {
                rectangles.Add(placed);
                placedCount++;
                Console.WriteLine($"Placed rectangle at ({placed.Center.X:F2}, {placed.Center.Y:F2}), size {placed.Size.X:F2} x {placed.Size.Y:F2}");
            }
        };
    }

    Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)} on {gl.GetStringS(StringName.Renderer)}");

    // The VAO records how vertex data is laid out, so drawing later only needs to bind it.
    vao = gl.GenVertexArray();
    gl.BindVertexArray(vao);

    // Upload the vertex data to GPU memory.
    vbo = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
    gl.BufferData<float>(BufferTargetARB.ArrayBuffer, quadVertices, BufferUsageARB.StaticDraw);

    // The element buffer holds indices into the vertices. Its binding is stored in the VAO,
    // so it must be bound while the VAO is, and not unbound before the VAO is.
    ebo = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
    gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, quadIndices, BufferUsageARB.StaticDraw);

    // Describe the layout: attribute 0 = position (x, y).
    unsafe
    {
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);
    }

    gl.BindVertexArray(0);

    shaderProgram = CreateShaderProgram(gl, vertexShaderSource, fragmentShaderSource);
    viewProjectionLocation = gl.GetUniformLocation(shaderProgram, "uViewProjection");
    modelLocation = gl.GetUniformLocation(shaderProgram, "uModel");
    colorLocation = gl.GetUniformLocation(shaderProgram, "uColor");

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

    gl.UseProgram(shaderProgram);
    gl.BindVertexArray(vao);

    // The scene, through the camera.
    SetMatrix(viewProjectionLocation, camera.ViewProjection);
    foreach (var rectangle in rectangles)
        DrawRectangle(rectangle);

    if (dragStart is Vector2 start)
        DrawRectangle(Rectangle.FromCorners(start, MouseWorldPosition(), previewColor));

    // The UI, drawn last so it sits on top. Same quad and shader, but with a projection that maps
    // window pixels straight to the screen, top left = (0, 0), so the camera doesn't move it.
    var windowSize = window.Size;
    SetMatrix(viewProjectionLocation, Matrix4x4.CreateOrthographicOffCenter(0f, windowSize.X, windowSize.Y, 0f, -1f, 1f));

    DrawRectangle(Toolbar.Panel(windowSize.Y));
    var mousePosition = mouse?.Position ?? new Vector2(-1f);
    for (int i = 0; i < Toolbar.Tools.Length; i++)
    {
        var tool = Toolbar.Tools[i];
        var button = Toolbar.Button(i, selected: tool == selectedTool, hovered: Toolbar.ToolAt(mousePosition) == tool);
        DrawRectangle(button);
        foreach (var part in Toolbar.Icon(tool, button))
            DrawRectangle(part);
    }
};

window.Closing += () =>
{
    gl.DeleteBuffer(ebo);
    gl.DeleteBuffer(vbo);
    gl.DeleteVertexArray(vao);
    gl.DeleteProgram(shaderProgram);
    gl.Dispose();
};

window.Run();
window.Dispose();

Vector2 MouseWorldPosition()
{
    var size = window.Size;
    return camera.ScreenToWorld(mouse?.Position ?? Vector2.Zero, new Vector2(size.X, size.Y));
}

// System.Numerics stores matrices row-major and GLSL reads column-major. That implicit
// transpose is exactly what turns its row-vector math into GLSL's `matrix * vector`,
// so no transpose flag is needed.
unsafe void SetMatrix(int location, Matrix4x4 matrix) =>
    gl.UniformMatrix4(location, 1, false, (float*)&matrix);

// Same quad for every rectangle; only the model matrix and color change between draws.
unsafe void DrawRectangle(Rectangle rectangle)
{
    gl.Uniform3(colorLocation, rectangle.Color);
    SetMatrix(modelLocation, rectangle.Model);
    gl.DrawElements(PrimitiveType.Triangles, (uint)quadIndices.Length, DrawElementsType.UnsignedInt, (void*)0);
}

static uint CreateShaderProgram(GL gl, string vertexSource, string fragmentSource)
{
    uint vertexShader = CompileShader(gl, ShaderType.VertexShader, vertexSource);
    uint fragmentShader = CompileShader(gl, ShaderType.FragmentShader, fragmentSource);

    uint program = gl.CreateProgram();
    gl.AttachShader(program, vertexShader);
    gl.AttachShader(program, fragmentShader);
    gl.LinkProgram(program);

    gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
    if (status == 0)
        throw new Exception($"Shader program failed to link: {gl.GetProgramInfoLog(program)}");

    // Once linked, the program keeps its own copy, so the individual shaders can go.
    gl.DetachShader(program, vertexShader);
    gl.DetachShader(program, fragmentShader);
    gl.DeleteShader(vertexShader);
    gl.DeleteShader(fragmentShader);

    return program;
}

static uint CompileShader(GL gl, ShaderType type, string source)
{
    uint shader = gl.CreateShader(type);
    gl.ShaderSource(shader, source);
    gl.CompileShader(shader);

    gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
    if (status == 0)
        throw new Exception($"{type} failed to compile: {gl.GetShaderInfoLog(shader)}");

    return shader;
}
