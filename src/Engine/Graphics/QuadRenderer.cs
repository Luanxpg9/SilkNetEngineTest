using System.Numerics;
using Silk.NET.OpenGL;

namespace Engine;

/// <summary>
/// Draws flat-colored quads. The GPU only ever holds one unit square (-0.5..0.5); each draw
/// stretches, turns and moves it with a model matrix, so a rectangle, a rotated box and a toolbar
/// button are all the same quad. One draw call per quad, which is fine for a handful of them;
/// the sprite batcher in roadmap step 3 replaces this for big scenes.
///
/// Usage per frame: <see cref="Begin"/>, then <see cref="SetViewProjection"/> for a pass
/// (camera for the scene, pixels for the UI), then any number of <see cref="Draw(Matrix4x4, Vector3)"/>.
/// </summary>
public sealed class QuadRenderer : IDisposable
{
    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;

        void main()
        {
            gl_Position = uViewProjection * uModel * vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        uniform vec3 uColor;

        out vec4 FragColor;

        void main()
        {
            FragColor = vec4(uColor, 1.0);
        }
        """;

    // A unit quad centered on the origin.
    private static readonly float[] QuadVertices =
    [
        //  x      y
        -0.5f, -0.5f, // 0: bottom left
         0.5f, -0.5f, // 1: bottom right
         0.5f,  0.5f, // 2: top right
        -0.5f,  0.5f, // 3: top left
    ];

    // Two triangles that share the 0-2 diagonal, so 4 vertices are enough instead of 6.
    private static readonly uint[] QuadIndices =
    [
        0, 1, 2,
        2, 3, 0,
    ];

    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo, _program;
    private readonly int _viewProjectionLocation, _modelLocation, _colorLocation;

    public QuadRenderer(GL gl)
    {
        _gl = gl;

        // The VAO records how vertex data is laid out, so drawing later only needs to bind it.
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Upload the vertex data to GPU memory.
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, QuadVertices, BufferUsageARB.StaticDraw);

        // The element buffer holds indices into the vertices. Its binding is stored in the VAO,
        // so it must be bound while the VAO is, and not unbound before the VAO is.
        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, QuadIndices, BufferUsageARB.StaticDraw);

        // Describe the layout: attribute 0 = position (x, y).
        unsafe
        {
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        }
        gl.EnableVertexAttribArray(0);

        gl.BindVertexArray(0);

        _program = ShaderProgram.Create(gl, VertexShaderSource, FragmentShaderSource);
        _viewProjectionLocation = gl.GetUniformLocation(_program, "uViewProjection");
        _modelLocation = gl.GetUniformLocation(_program, "uModel");
        _colorLocation = gl.GetUniformLocation(_program, "uColor");
    }

    // Binds the shader and the quad. Call once per frame before drawing.
    public void Begin()
    {
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
    }

    // The matrix every following draw goes through: a camera's ViewProjection for the scene,
    // or a pixel projection for the UI.
    public void SetViewProjection(Matrix4x4 viewProjection) => SetMatrix(_viewProjectionLocation, viewProjection);

    public unsafe void Draw(Matrix4x4 model, Vector3 color)
    {
        _gl.Uniform3(_colorLocation, color);
        SetMatrix(_modelLocation, model);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)QuadIndices.Length, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Draw(Rectangle rectangle) => Draw(rectangle.Model, rectangle.Color);

    // System.Numerics stores matrices row-major and GLSL reads column-major. That implicit
    // transpose is exactly what turns its row-vector math into GLSL's `matrix * vector`,
    // so no transpose flag is needed.
    private unsafe void SetMatrix(int location, Matrix4x4 matrix) =>
        _gl.UniformMatrix4(location, 1, false, (float*)&matrix);

    public void Dispose()
    {
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
