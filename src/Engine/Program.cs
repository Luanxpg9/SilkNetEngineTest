using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

const string vertexShaderSource = """
    #version 330 core
    layout (location = 0) in vec3 aPosition;
    layout (location = 1) in vec3 aColor;

    uniform mat4 uViewProjection;

    out vec3 vColor;

    void main()
    {
        vColor = aColor;
        gl_Position = uViewProjection * vec4(aPosition, 1.0);
    }
    """;

const string fragmentShaderSource = """
    #version 330 core
    in vec3 vColor;

    out vec4 FragColor;

    void main()
    {
        FragColor = vec4(vColor, 1.0);
    }
    """;

const float CameraSpeed = 1.5f; // world units per second

// Each vertex: position (x, y, z) in world units followed by color (r, g, b).
float[] vertices =
[
    //  x      y     z      r     g     b
    -0.5f, -0.5f, 0.0f,  1.0f, 0.0f, 0.0f, // bottom left: red
     0.5f, -0.5f, 0.0f,  0.0f, 1.0f, 0.0f, // bottom right: green
     0.0f,  0.5f, 0.0f,  0.0f, 0.0f, 1.0f, // top: blue
];

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(1280, 720),
    Title = "HavingFunWithEngine",
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
};

var window = Window.Create(options);
GL gl = null!;
uint vao = 0, vbo = 0, shaderProgram = 0;
int viewProjectionLocation = -1;
IKeyboard? keyboard = null;

// 2 world units tall, so the triangle (1 unit across) fills about half the window's height.
var camera = new Camera2D(viewHeight: 2f);

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

    Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)} on {gl.GetStringS(StringName.Renderer)}");

    // The VAO records how vertex data is laid out, so drawing later only needs to bind it.
    vao = gl.GenVertexArray();
    gl.BindVertexArray(vao);

    // Upload the vertex data to GPU memory.
    vbo = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
    gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

    // Describe the layout: attribute 0 = position, attribute 1 = color.
    const uint stride = 6 * sizeof(float);
    unsafe
    {
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);

        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
    }

    gl.BindVertexArray(0);

    shaderProgram = CreateShaderProgram(gl, vertexShaderSource, fragmentShaderSource);
    viewProjectionLocation = gl.GetUniformLocation(shaderProgram, "uViewProjection");

    var size = window.FramebufferSize;
    camera.AspectRatio = (float)size.X / size.Y;
};

window.FramebufferResize += size =>
{
    gl.Viewport(size);
    if (size.Y > 0) // minimizing reports a 0 height
        camera.AspectRatio = (float)size.X / size.Y;
};

window.Update += deltaTime =>
{
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

    // System.Numerics stores matrices row-major and GLSL reads column-major. That implicit
    // transpose is exactly what turns its row-vector math into GLSL's `matrix * vector`,
    // so no transpose flag is needed.
    var viewProjection = camera.ViewProjection;
    unsafe
    {
        gl.UniformMatrix4(viewProjectionLocation, 1, false, (float*)&viewProjection);
    }

    gl.BindVertexArray(vao);
    gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
};

window.Closing += () =>
{
    gl.DeleteBuffer(vbo);
    gl.DeleteVertexArray(vao);
    gl.DeleteProgram(shaderProgram);
    gl.Dispose();
};

window.Run();
window.Dispose();

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
