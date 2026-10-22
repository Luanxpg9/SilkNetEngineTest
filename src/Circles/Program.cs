using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

// Circle renderer experiment: every circle is a 2-triangle quad, and the fragment
// shader decides per pixel what is inside the circle. All circles are drawn with
// one instanced draw call.
//
// Controls: 1 = flat circles, 2 = sphere impostors, Up/Down = double/halve count,
//           C = toggle collisions, Space = pause, V = toggle vsync, Esc = quit.

const int MaxCircles = 262_144;
const float MinRadius = 4f, MaxRadius = 24f;
const float DepthRange = 1000f; // circles live in a pixel-sized box, this deep in z

const string vertexShaderSource = """
    #version 330 core
    layout (location = 0) in vec2 aCorner;   // per vertex: quad corner in [-1, 1]
    layout (location = 1) in vec3 aCenter;   // per instance: x, y in pixels, z = depth
    layout (location = 2) in float aRadius;  // per instance: radius in pixels
    layout (location = 3) in vec3 aColor;    // per instance

    uniform vec2 uResolution;

    out vec2 vLocal;            // position inside the circle, 1.0 = on the edge
    out vec3 vColor;
    flat out float vRadius;
    flat out float vDepth;

    void main()
    {
        // Grow the quad by one pixel so the anti-aliased edge isn't clipped.
        float scale = (aRadius + 1.0) / aRadius;
        vLocal = aCorner * scale;
        vColor = aColor;
        vRadius = aRadius;
        vDepth = aCenter.z;

        vec2 pixel = aCenter.xy + aCorner * (aRadius + 1.0);
        gl_Position = vec4(pixel / uResolution * 2.0 - 1.0, 0.0, 1.0);
    }
    """;

const string fragmentShaderSource = """
    #version 330 core
    in vec2 vLocal;
    in vec3 vColor;
    flat in float vRadius;
    flat in float vDepth;

    uniform int uMode;
    uniform float uDepthRange;

    out vec4 FragColor;

    void main()
    {
        float d = length(vLocal);

        if (uMode == 0)
        {
            // Flat circle: fade out over about one pixel at the edge.
            float aa = fwidth(d);
            float alpha = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, d);
            if (alpha <= 0.0)
                discard;
            FragColor = vec4(vColor, alpha);
            gl_FragDepth = gl_FragCoord.z;
            return;
        }

        // Sphere impostor: treat the circle as the silhouette of a ball and
        // reconstruct the ball's surface height at this pixel.
        if (d > 1.0)
            discard;
        float h = sqrt(1.0 - d * d);
        vec3 normal = vec3(vLocal, h);

        vec3 lightDir = normalize(vec3(-0.5, 0.6, 0.8));
        float diffuse = max(dot(normal, lightDir), 0.0);
        vec3 halfway = normalize(lightDir + vec3(0.0, 0.0, 1.0));
        float specular = pow(max(dot(normal, halfway), 0.0), 48.0);

        FragColor = vec4(vColor * (0.15 + 0.85 * diffuse) + vec3(0.6 * specular), 1.0);

        // Write the depth of the ball's surface, not the flat quad, so
        // overlapping spheres intersect like real ones.
        gl_FragDepth = (vDepth - h * vRadius) / uDepthRange;
    }
    """;

var random = new Random(54463416);
var circles = new CircleInstance[MaxCircles];
var velocities = new Vector2[MaxCircles];

for (int i = 0; i < MaxCircles; i++)
{
    float radius = MinRadius + random.NextSingle() * (MaxRadius - MinRadius);
    circles[i] = new CircleInstance
    {
        Center = new Vector3(
            radius + random.NextSingle() * (1280 - 2 * radius),
            radius + random.NextSingle() * (720 - 2 * radius),
            radius + random.NextSingle() * (DepthRange - 2 * radius)),
        Radius = radius,
        Color = HsvToRgb(random.NextSingle(), 0.65f, 0.95f),
    };

    float angle = random.NextSingle() * MathF.Tau;
    float speed = random.NextSingle() * 200f + 50f;
    velocities[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
}

int count = 512;
int mode = 0;
bool paused = false;
bool collisions = true;
var collisionGrid = new CollisionGrid(cellSize: 2 * MaxRadius);

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(1280, 720),
    Title = "Circles",
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
};

var window = Window.Create(options);
GL gl = null!;
uint vao = 0, quadVbo = 0, quadEbo = 0, instanceVbo = 0, shaderProgram = 0;
int resolutionLocation = 0, modeLocation = 0;

double fpsTimer = 0;
int fpsFrames = 0;

window.Load += () =>
{
    gl = window.CreateOpenGL();

    var input = window.CreateInput();
    foreach (var keyboard in input.Keyboards)
        keyboard.KeyDown += (_, key, _) =>
        {
            switch (key)
            {
                case Key.Escape: window.Close(); break;
                case Key.Number1: mode = 0; break;
                case Key.Number2: mode = 1; break;
                case Key.Up: count = Math.Min(count * 2, MaxCircles); break;
                case Key.Down: count = Math.Max(count / 2, 1); break;
                case Key.C: collisions = !collisions; break;
                case Key.Space: paused = !paused; break;
                case Key.V: window.VSync = !window.VSync; break;
            }
        };

    vao = gl.GenVertexArray();
    gl.BindVertexArray(vao);

    // The single quad every circle is drawn with.
    float[] corners = [-1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f];
    uint[] indices = [0, 1, 2, 0, 2, 3];

    quadVbo = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, quadVbo);
    gl.BufferData<float>(BufferTargetARB.ArrayBuffer, corners, BufferUsageARB.StaticDraw);

    quadEbo = gl.GenBuffer();
    gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, quadEbo);
    gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);

    unsafe
    {
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);

        // Per-circle data. Allocated once at max size and refilled every frame.
        uint stride = (uint)Unsafe.SizeOf<CircleInstance>();
        instanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxCircles * stride), null, BufferUsageARB.DynamicDraw);

        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        for (uint location = 1; location <= 3; location++)
        {
            gl.EnableVertexAttribArray(location);
            // Divisor 1: advance this attribute once per instance, not once per vertex.
            gl.VertexAttribDivisor(location, 1);
        }
    }

    gl.BindVertexArray(0);

    shaderProgram = CreateShaderProgram(gl, vertexShaderSource, fragmentShaderSource);
    resolutionLocation = gl.GetUniformLocation(shaderProgram, "uResolution");
    modeLocation = gl.GetUniformLocation(shaderProgram, "uMode");
    gl.UseProgram(shaderProgram);
    gl.Uniform1(gl.GetUniformLocation(shaderProgram, "uDepthRange"), DepthRange);

    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    gl.DepthFunc(DepthFunction.Less);
};

window.FramebufferResize += size => gl.Viewport(size);

window.Update += deltaTime =>
{
    if (paused)
        return;

    float dt = (float)Math.Min(deltaTime, 0.05);
    var size = window.FramebufferSize;

    for (int i = 0; i < count; i++)
    {
        ref var circle = ref circles[i];
        ref var velocity = ref velocities[i];

        circle.Center.X += velocity.X * dt;
        circle.Center.Y += velocity.Y * dt;

        if (circle.Center.X < circle.Radius) { circle.Center.X = circle.Radius; velocity.X = MathF.Abs(velocity.X); }
        else if (circle.Center.X > size.X - circle.Radius) { circle.Center.X = size.X - circle.Radius; velocity.X = -MathF.Abs(velocity.X); }

        if (circle.Center.Y < circle.Radius) { circle.Center.Y = circle.Radius; velocity.Y = MathF.Abs(velocity.Y); }
        else if (circle.Center.Y > size.Y - circle.Radius) { circle.Center.Y = size.Y - circle.Radius; velocity.Y = -MathF.Abs(velocity.Y); }
    }

    if (collisions)
        collisionGrid.Resolve(circles.AsSpan(0, count), velocities.AsSpan(0, count), size.X, size.Y);
};

window.Render += deltaTime =>
{
    gl.ClearColor(0.1f, 0.1f, 0.15f, 1f);
    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

    if (mode == 0)
    {
        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
    }
    else
    {
        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
    }

    gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceVbo);
    gl.BufferSubData<CircleInstance>(BufferTargetARB.ArrayBuffer, 0, circles.AsSpan(0, count));

    var size = window.FramebufferSize;
    gl.UseProgram(shaderProgram);
    gl.Uniform2(resolutionLocation, (float)size.X, (float)size.Y);
    gl.Uniform1(modeLocation, mode);

    gl.BindVertexArray(vao);
    unsafe
    {
        gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0, (uint)count);
    }

    fpsFrames++;
    fpsTimer += deltaTime;
    if (fpsTimer >= 0.5)
    {
        window.Title = $"Circles | {count:N0} {(mode == 0 ? "circles" : "spheres")} | {fpsFrames / fpsTimer:F0} FPS | collisions {(collisions ? "on" : "off")} | vsync {(window.VSync ? "on" : "off")}";
        fpsFrames = 0;
        fpsTimer = 0;
    }
};

window.Closing += () =>
{
    gl.DeleteBuffer(quadVbo);
    gl.DeleteBuffer(quadEbo);
    gl.DeleteBuffer(instanceVbo);
    gl.DeleteVertexArray(vao);
    gl.DeleteProgram(shaderProgram);
    gl.Dispose();
};

window.Run();
window.Dispose();

static Vector3 HsvToRgb(float h, float s, float v)
{
    Vector3 k = new(5f, 3f, 1f);
    Vector3 p = new(
        MathF.Abs((k.X + h * 6f) % 6f - 3f) - 1f,
        MathF.Abs((k.Y + h * 6f) % 6f - 3f) - 1f,
        MathF.Abs((k.Z + h * 6f) % 6f - 3f) - 1f);
    p = Vector3.Clamp(p, Vector3.Zero, Vector3.One);
    return v * Vector3.Lerp(Vector3.One, p, s);
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

// Matches the per-instance vertex attributes: 7 floats, 28 bytes, no padding.
struct CircleInstance
{
    public Vector3 Center;
    public float Radius;
    public Vector3 Color;
}
