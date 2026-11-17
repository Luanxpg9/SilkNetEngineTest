using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Engine;

/// <summary>
/// Draws many textured, tinted, rotated quads ("sprites") with few draw calls. Flat-colored
/// rectangles are sprites too: they use <see cref="WhitePixel"/>, a 1×1 white texture, tinted.
///
/// Usage per pass (the scene through a camera, then the UI in window units):
/// <code>
/// Sprites.Begin(_camera.ViewProjection);
/// Sprites.Draw(new Rect(center, size), Color.FromHex("#303340"));        // flat rectangle
/// Sprites.Draw(crate, position, size: new(1f, 1f), Color.White);         // texture
/// Sprites.End();
/// </code>
///
/// How it batches: each <c>Draw</c> only computes the sprite's 4 corners on the CPU and appends them
/// to an array. Nothing reaches the GPU until the batch is flushed, and then all the sprites go in one
/// buffer upload and one draw call. A flush happens at <see cref="End"/>, when the next sprite uses a
/// different texture (one draw call can only sample one texture here), and when the array is full.
/// So draw sprites that share a texture one after another: 600 floor tiles in a row cost one call.
/// </summary>
public sealed class SpriteBatch : IDisposable
{
    private const int MaxSprites = 2048; // per flush; more sprites just mean more flushes

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition; // already in world (or window) units
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;    // tint, premultiplied

        uniform mat4 uViewProjection;

        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            vTexCoord = aTexCoord;
            vColor = aColor;
            gl_Position = uViewProjection * vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord;
        in vec4 vColor;

        uniform sampler2D uTexture;

        out vec4 FragColor;

        void main()
        {
            // Both are premultiplied, and the product of two premultiplied colors is premultiplied too.
            FragColor = texture(uTexture, vTexCoord) * vColor;
        }
        """;

    // One corner of a sprite, exactly as the vertex shader reads it: 8 floats, 32 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex(Vector2 position, Vector2 texCoord, Vector4 color)
    {
        public Vector2 Position = position;
        public Vector2 TexCoord = texCoord;
        public Vector4 Color = color;
    }

    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo, _program;
    private readonly int _viewProjectionLocation;

    private readonly Vertex[] _vertices = new Vertex[MaxSprites * 4];
    private int _spriteCount;   // sprites waiting in _vertices
    private Texture? _texture;  // the texture they all use
    private bool _begun;

    public SpriteBatch(GL gl)
    {
        _gl = gl;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // The vertex buffer gets its size now and new contents every flush, so it's created empty.
        // StreamDraw tells the driver the data is written once and drawn once.
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(Vertex)), null, BufferUsageARB.StreamDraw);
        }

        // The indices never change: every sprite is two triangles over its own 4 corners,
        // 0 1 2 and 2 3 0, offset by 4 per sprite. So they're built once for the largest batch.
        // 16-bit indices are enough: 2048 sprites are 8192 corners, well under 65536.
        var indices = new ushort[MaxSprites * 6];
        for (int sprite = 0; sprite < MaxSprites; sprite++)
        {
            int i = sprite * 6;
            ushort v = (ushort)(sprite * 4);
            indices[i] = v;
            indices[i + 1] = (ushort)(v + 1);
            indices[i + 2] = (ushort)(v + 2);
            indices[i + 3] = (ushort)(v + 2);
            indices[i + 4] = (ushort)(v + 3);
            indices[i + 5] = v;
        }
        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData<ushort>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);

        // The layout of one Vertex: position at byte 0, texture coordinate at 8, color at 16.
        unsafe
        {
            uint stride = (uint)sizeof(Vertex);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        }
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.EnableVertexAttribArray(2);

        gl.BindVertexArray(0);

        _program = ShaderProgram.Create(gl, VertexShaderSource, FragmentShaderSource);
        _viewProjectionLocation = gl.GetUniformLocation(_program, "uViewProjection");

        // The sampler reads texture unit 0, the one Flush binds textures to. Set once, it stays.
        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture"), 0);

        WhitePixel = new Texture(gl, 1, 1, [255, 255, 255, 255], TextureFilter.Nearest, premultiplied: true);
    }

    /// <summary>
    /// A 1×1 white texture. Tinted, it gives any flat color, which is how rectangles are drawn
    /// without a separate shader.
    /// </summary>
    public Texture WhitePixel { get; }

    /// <summary>Draw calls so far this frame. <see cref="Game"/> resets it before each <see cref="Game"/> Draw.</summary>
    public int DrawCalls { get; private set; }

    /// <summary>Sprites drawn so far this frame, reset with <see cref="DrawCalls"/>.</summary>
    public int SpritesDrawn { get; private set; }

    internal void ResetFrameStats()
    {
        DrawCalls = 0;
        SpritesDrawn = 0;
    }

    /// <summary>
    /// Starts a pass. Every sprite until <see cref="End"/> goes through <paramref name="viewProjection"/>:
    /// a camera's for the scene, <see cref="Game.ScreenProjection"/> for the UI.
    /// </summary>
    public void Begin(Matrix4x4 viewProjection)
    {
        if (_begun)
            throw new InvalidOperationException("SpriteBatch.Begin was called twice without End in between");
        _begun = true;

        // The state is set on every Begin, not once, so it survives a game changing it in between.
        _gl.UseProgram(_program);
        unsafe
        {
            // Uploaded as-is: see the matrix layout note in the engine README.
            _gl.UniformMatrix4(_viewProjectionLocation, 1, false, (float*)&viewProjection);
        }
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // Blending mixes each new pixel with what's already on screen:
        //   result = source + destination × (1 − source alpha)
        // which is the correct "over" operation for premultiplied colors (see Texture).
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
    }

    /// <summary>Draws everything still waiting and ends the pass.</summary>
    public void End()
    {
        if (!_begun)
            throw new InvalidOperationException("SpriteBatch.End was called without Begin");
        Flush();
        _begun = false;
        _texture = null;
    }

    /// <summary>
    /// Draws <paramref name="texture"/> stretched to <paramref name="size"/>, tinted by
    /// <paramref name="tint"/> (<see cref="Color.White"/> leaves it as it is).
    /// <paramref name="origin"/> is the point of the sprite that sits at <paramref name="position"/> and
    /// that it turns around, from (0, 0) at the bottom left to (1, 1) at the top right; by default
    /// the center. <paramref name="rotation"/> is in radians, counter-clockwise.
    /// </summary>
    public void Draw(Texture texture, Vector2 position, Vector2 size, Color tint, float rotation = 0f, Vector2? origin = null)
    {
        if (!_begun)
            throw new InvalidOperationException("SpriteBatch.Draw was called outside Begin/End");

        if (texture != _texture)
        {
            Flush();
            _texture = texture;
        }
        else if (_spriteCount == MaxSprites)
        {
            Flush();
        }

        // The corners relative to the origin, before turning.
        var o = origin ?? new Vector2(0.5f);
        float left = -o.X * size.X, right = left + size.X;
        float bottom = -o.Y * size.Y, top = bottom + size.Y;

        // Turn each corner by the rotation, then move it to the position. Same as a model matrix
        // (scale, rotate, translate), done here so the GPU gets finished positions.
        float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
        Vector2 Corner(float x, float y) => position + new Vector2(x * cos - y * sin, x * sin + y * cos);

        // Premultiply the tint so it blends the same way as the texture.
        var color = new Vector4(tint.R * tint.A, tint.G * tint.A, tint.B * tint.A, tint.A);

        // Corner order matches the index pattern: bottom left, bottom right, top right, top left.
        // Texture coordinates put the image's bottom row at v = 0 (Texture flips images on load).
        int v = _spriteCount * 4;
        _vertices[v] = new Vertex(Corner(left, bottom), new(0f, 0f), color);
        _vertices[v + 1] = new Vertex(Corner(right, bottom), new(1f, 0f), color);
        _vertices[v + 2] = new Vertex(Corner(right, top), new(1f, 1f), color);
        _vertices[v + 3] = new Vertex(Corner(left, top), new(0f, 1f), color);
        _spriteCount++;
    }

    /// <summary>A flat-colored rectangle, turned by <paramref name="rotation"/> radians around its center.</summary>
    public void Draw(Rect rect, Color color, float rotation = 0f) => Draw(WhitePixel, rect.Center, rect.Size, color, rotation);

    // Sends the waiting sprites to the GPU and draws them with one call.
    private void Flush()
    {
        if (_spriteCount == 0 || _texture is null)
            return;

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture.Handle);

        unsafe
        {
            // "Orphaning": asking for fresh storage before writing. If the GPU is still drawing from
            // the previous contents, the driver hands over a new block instead of making the CPU
            // wait for the GPU to finish with the old one.
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(Vertex)), null, BufferUsageARB.StreamDraw);
            _gl.BufferSubData<Vertex>(BufferTargetARB.ArrayBuffer, 0, _vertices.AsSpan(0, _spriteCount * 4));
            _gl.DrawElements(PrimitiveType.Triangles, (uint)(_spriteCount * 6), DrawElementsType.UnsignedShort, (void*)0);
        }

        DrawCalls++;
        SpritesDrawn += _spriteCount;
        _spriteCount = 0;
    }

    public void Dispose()
    {
        WhitePixel.Dispose();
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
