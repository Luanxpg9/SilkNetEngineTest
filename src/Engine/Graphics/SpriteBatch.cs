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
/// Sprites.Draw(new Rect(center, size), Color.FromHex("#303340"));                 // flat rectangle, layer 0
/// Sprites.Draw(crate, position, size: new(1f, 1f), Color.White, layer: 10);       // whole texture
/// Sprites.Draw(walk.FrameAt(time), position, size, Color.White, angle, layer: 5); // sprite sheet frame
/// Sprites.End();
/// </code>
///
/// <b>Layers.</b> Each sprite has a layer number, and higher layers are drawn on top of lower ones,
/// whatever order the calls came in. Within a layer, sprites keep their call order: later on top.
/// To make that work, <c>Draw</c> only computes the sprite's 4 corners and stores them. At
/// <see cref="End"/> the sprites are sorted by layer and sent to the GPU.
///
/// <b>Batching.</b> After sorting, runs of sprites that use the same texture go to the GPU in one
/// buffer upload and one draw call. A new draw call starts when the texture changes (one draw call
/// samples one texture here) or after 2048 sprites. So the fewer texture switches in layer order,
/// the fewer draw calls: 600 floor tiles in one layer cost one call.
/// </summary>
public sealed class SpriteBatch : IDisposable
{
    private const int MaxSpritesPerDraw = 2048; // size of the GPU buffer; more sprites just mean more draw calls

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

    // The sprites of the current pass, in call order. Index i owns corners 4i..4i+3. The arrays
    // grow (doubling) when a pass has more sprites than ever before, and are reused after that,
    // so a steady scene allocates nothing per frame.
    private Vertex[] _corners = new Vertex[MaxSpritesPerDraw * 4];
    private Texture[] _textures = new Texture[MaxSpritesPerDraw];
    private long[] _sortKeys = new long[MaxSpritesPerDraw];
    private int[] _order = new int[MaxSpritesPerDraw];
    private int _spriteCount;

    // What one draw call sends: sprites copied here in layer order, then uploaded together.
    private readonly Vertex[] _upload = new Vertex[MaxSpritesPerDraw * 4];

    private bool _begun;

    public SpriteBatch(GL gl)
    {
        _gl = gl;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // The vertex buffer gets its size now and new contents every draw call, so it's created empty.
        // StreamDraw tells the driver the data is written once and drawn once.
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_upload.Length * sizeof(Vertex)), null, BufferUsageARB.StreamDraw);
        }

        // The indices never change: every sprite is two triangles over its own 4 corners,
        // 0 1 2 and 2 3 0, offset by 4 per sprite. So they're built once for the largest draw.
        // 16-bit indices are enough: 2048 sprites are 8192 corners, well under 65536.
        var indices = new ushort[MaxSpritesPerDraw * 6];
        for (int sprite = 0; sprite < MaxSpritesPerDraw; sprite++)
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

        // The sampler reads texture unit 0, the one DrawRun binds textures to. Set once, it stays.
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
    /// a camera's for the scene, <see cref="Game.ScreenProjection"/> for the UI. Layers only sort
    /// within a pass: everything in a later pass is drawn on top of everything in an earlier one.
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

        // Sprites are flat and drawn in layer order, so these must be off. Face culling would hide
        // quads whose corners come out clockwise: everything under ScreenProjection (y down), and
        // sprites mirrored with a negative size. A depth test would drop sprites drawn at the same depth.
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
    }

    /// <summary>Sorts the pass's sprites by layer, draws them, and ends the pass.</summary>
    public void End()
    {
        if (!_begun)
            throw new InvalidOperationException("SpriteBatch.End was called without Begin");
        _begun = false;

        if (_spriteCount == 0)
        {
            _gl.BindVertexArray(0);
            return;
        }

        // Sort sprite indices by key. Each key holds the layer in its high 32 bits and the call
        // number in its low 32 bits, so sorting by key orders by layer first and keeps call order
        // within a layer. Sorting two arrays together (keys and indices) allocates nothing.
        for (int i = 0; i < _spriteCount; i++)
            _order[i] = i;
        Array.Sort(_sortKeys, _order, 0, _spriteCount);

        // Walk the sprites in layer order, copying their corners into the upload array. A texture
        // change, or a full upload array, sends what's collected so far as one draw call.
        Texture? runTexture = null;
        int runLength = 0;
        for (int i = 0; i < _spriteCount; i++)
        {
            int sprite = _order[i];
            var texture = _textures[sprite];
            if (runLength > 0 && (texture != runTexture || runLength == MaxSpritesPerDraw))
            {
                DrawRun(runTexture!, runLength);
                runLength = 0;
            }
            runTexture = texture;
            Array.Copy(_corners, sprite * 4, _upload, runLength * 4, 4);
            runLength++;
        }
        DrawRun(runTexture!, runLength);

        // Let go of the textures, so a texture disposed later isn't kept alive by this array.
        Array.Clear(_textures, 0, _spriteCount);
        _spriteCount = 0;

        // Unbind the VAO. It records which index buffer is bound, so while it stays bound, any later
        // BindBuffer(ElementArrayBuffer, ...) elsewhere (a new engine class creating its buffers)
        // would silently replace the batch's indices and the next pass would draw garbage.
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Draws <paramref name="region"/> (a whole <see cref="Texture"/> works too) stretched to
    /// <paramref name="size"/>, tinted by <paramref name="tint"/> (<see cref="Color.White"/> leaves
    /// it as it is). <paramref name="origin"/> is the point of the sprite that sits at
    /// <paramref name="position"/> and that it turns around, from (0, 0) at the bottom left to (1, 1)
    /// at the top right; by default the center. <paramref name="rotation"/> is in radians,
    /// counter-clockwise. Higher <paramref name="layer"/>s are drawn on top.
    ///
    /// <paramref name="glow"/>, from 0 to 1, makes the sprite add its light to what's under it instead
    /// of covering it: 0 is a normal sprite, 1 is pure light (a tracer, a muzzle flash, a laser's glow),
    /// which brightens the floor under it and never darkens it. It doesn't change how bright the
    /// sprite is, only how much it covers, and glowing sprites batch with normal ones (see the README).
    /// </summary>
    public void Draw(TextureRegion region, Vector2 position, Vector2 size, Color tint, float rotation = 0f, Vector2? origin = null, int layer = 0, float glow = 0f)
    {
        if (!_begun)
            throw new InvalidOperationException("SpriteBatch.Draw was called outside Begin/End");

        if (_spriteCount == _textures.Length)
            Grow();

        // The corners relative to the origin, before turning.
        var o = origin ?? new Vector2(0.5f);
        float left = -o.X * size.X, right = left + size.X;
        float bottom = -o.Y * size.Y, top = bottom + size.Y;

        // Turn each corner by the rotation, then move it to the position. Same as a model matrix
        // (scale, rotate, translate), done here so the GPU gets finished positions. It's
        // MathHelpers.Rotate written out, so cos and sin are computed once for all 4 corners.
        float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
        Vector2 Corner(float x, float y) => position + new Vector2(x * cos - y * sin, x * sin + y * cos);

        // Premultiply the tint so it blends the same way as the texture. Then glow lowers only the
        // alpha: the blend is source + destination × (1 − source alpha), so with alpha 0 the
        // destination stays whole and the sprite's color is simply added on top.
        var color = new Vector4(tint.R * tint.A, tint.G * tint.A, tint.B * tint.A, tint.A * (1f - Math.Clamp(glow, 0f, 1f)));

        // Corner order matches the index pattern: bottom left, bottom right, top right, top left.
        var uv0 = region.UvMin;
        var uv1 = region.UvMax;
        int v = _spriteCount * 4;
        _corners[v] = new Vertex(Corner(left, bottom), uv0, color);
        _corners[v + 1] = new Vertex(Corner(right, bottom), new(uv1.X, uv0.Y), color);
        _corners[v + 2] = new Vertex(Corner(right, top), uv1, color);
        _corners[v + 3] = new Vertex(Corner(left, top), new(uv0.X, uv1.Y), color);

        _textures[_spriteCount] = region.Texture;
        _sortKeys[_spriteCount] = ((long)layer << 32) | (uint)_spriteCount;
        _spriteCount++;
    }

    /// <summary>A flat-colored rectangle, turned by <paramref name="rotation"/> radians around its center.</summary>
    public void Draw(Rect rect, Color color, float rotation = 0f, int layer = 0, float glow = 0f) =>
        Draw(WhitePixel, rect.Center, rect.Size, color, rotation, layer: layer, glow: glow);

    /// <summary>A flat-colored line from <paramref name="from"/> to <paramref name="to"/>, <paramref name="thickness"/> wide.</summary>
    public void DrawLine(Vector2 from, Vector2 to, float thickness, Color color, int layer = 0, float glow = 0f)
    {
        // A rectangle whose left-middle point sits at `from`, as long as the line, turned toward `to`.
        var delta = to - from;
        Draw(WhitePixel, from, new Vector2(delta.Length(), thickness), color,
            rotation: MathF.Atan2(delta.Y, delta.X), origin: new Vector2(0f, 0.5f), layer: layer, glow: glow);
    }

    private void Grow()
    {
        int capacity = _textures.Length * 2;
        Array.Resize(ref _corners, capacity * 4);
        Array.Resize(ref _textures, capacity);
        Array.Resize(ref _sortKeys, capacity);
        Array.Resize(ref _order, capacity);
    }

    // Sends the first `count` sprites of the upload array to the GPU and draws them with one call.
    private void DrawRun(Texture texture, int count)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, texture.Handle);

        unsafe
        {
            // "Orphaning": asking for fresh storage before writing. If the GPU is still drawing from
            // the previous contents, the driver hands over a new block instead of making the CPU
            // wait for the GPU to finish with the old one.
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_upload.Length * sizeof(Vertex)), null, BufferUsageARB.StreamDraw);
            _gl.BufferSubData<Vertex>(BufferTargetARB.ArrayBuffer, 0, _upload.AsSpan(0, count * 4));
            _gl.DrawElements(PrimitiveType.Triangles, (uint)(count * 6), DrawElementsType.UnsignedShort, (void*)0);
        }

        DrawCalls++;
        SpritesDrawn += count;
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
