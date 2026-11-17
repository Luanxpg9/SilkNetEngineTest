using System.Numerics;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Engine;

/// <summary>How a texture is sampled when it's drawn bigger or smaller than its pixel size.</summary>
public enum TextureFilter
{
    Nearest, // each screen pixel takes the closest texel: crisp, blocky (pixel art, debug grids)
    Linear,  // blends the 4 closest texels: smooth (detailed art, scaled sprites)
}

/// <summary>
/// An image on the GPU, ready to be drawn by <see cref="SpriteBatch"/>.
/// <code>
/// var crate = Texture.Load(GL, "Assets/crate.png");
/// ...
/// crate.Dispose(); // in Unload
/// </code>
///
/// Pixels are stored with <b>premultiplied alpha</b>: red, green and blue are already multiplied by
/// alpha when the image loads. That's what keeps transparent edges clean. With plain alpha, a fully
/// transparent pixel still has a color (often black), and linear filtering blends that color into the
/// visible pixels next to it, which shows as a dark outline. Premultiplied, a transparent pixel is
/// (0, 0, 0, 0) and contributes nothing. <see cref="SpriteBatch"/> blends to match.
/// </summary>
public sealed class Texture : IDisposable
{
    private readonly GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public Vector2 Size => new(Width, Height);

    /// <summary>
    /// Creates a texture from raw RGBA pixels (4 bytes per pixel, rows from the bottom up, the order
    /// OpenGL expects). Pass <paramref name="premultiplied"/> false for ordinary image data, and the
    /// pixels are premultiplied here; <paramref name="rgba"/> itself isn't changed.
    /// </summary>
    public Texture(GL gl, int width, int height, ReadOnlySpan<byte> rgba, TextureFilter filter = TextureFilter.Linear, bool premultiplied = false)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for a {width}x{height} RGBA image, got {rgba.Length}", nameof(rgba));

        _gl = gl;
        Width = width;
        Height = height;

        byte[]? converted = null;
        if (!premultiplied)
        {
            converted = rgba.ToArray();
            Premultiply(converted);
            rgba = converted;
        }

        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        // Rows of RGBA bytes are always a multiple of 4 bytes long, which matches OpenGL's default
        // unpack alignment, so no PixelStore call is needed.
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, rgba);

        var glFilter = filter == TextureFilter.Nearest ? GLEnum.Nearest : GLEnum.Linear;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)glFilter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)glFilter);

        // Clamp instead of repeat: with linear filtering, repeat would blend the opposite edge into
        // a sprite's border.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Loads a PNG (or JPG, BMP, TGA). A relative path is taken from the folder the game runs from,
    /// where each game's Assets folder is copied, so it works whatever the working directory is.
    /// </summary>
    public static Texture Load(GL gl, string path, TextureFilter filter = TextureFilter.Linear)
    {
        string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

        // Image files store the top row first, but OpenGL's texture coordinates start at the bottom
        // (v = 0 is the bottom row). Flipping on load makes them agree, so v = 1 is the image's top.
        StbImage.stbi_set_flip_vertically_on_load(1);

        using var stream = File.OpenRead(fullPath);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        Premultiply(image.Data);
        var texture = new Texture(gl, image.Width, image.Height, image.Data, filter, premultiplied: true);
        Log.Info($"Loaded {path} ({image.Width}x{image.Height})");
        return texture;
    }

    // RGB *= A, rounded, for every pixel.
    private static void Premultiply(Span<byte> rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            if (a == 255)
                continue;
            rgba[i] = (byte)((rgba[i] * a + 127) / 255);
            rgba[i + 1] = (byte)((rgba[i + 1] * a + 127) / 255);
            rgba[i + 2] = (byte)((rgba[i + 2] * a + 127) / 255);
        }
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
