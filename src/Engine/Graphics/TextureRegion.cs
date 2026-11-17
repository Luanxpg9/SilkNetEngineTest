using System.Numerics;

namespace Engine;

/// <summary>
/// A rectangle inside a texture: one frame of a sprite sheet, or the whole texture. It's stored as
/// texture coordinates (0..1, v = 0 at the bottom, see <see cref="Texture"/>), which is what the
/// sprite batcher writes into each corner.
///
/// A <see cref="Texture"/> converts to a region covering all of it, so anything that takes a region
/// also takes a whole texture:
/// <code>
/// Sprites.Draw(crate, ...);                                          // the whole texture
/// Sprites.Draw(TextureRegion.FromPixels(sheet, 0, 0, 64, 64), ...);  // its top-left 64x64 pixels
/// TextureRegion[] frames = TextureRegion.Grid(sheet, columns: 8, rows: 1);
/// </code>
/// </summary>
public readonly record struct TextureRegion(Texture Texture, Vector2 UvMin, Vector2 UvMax)
{
    /// <summary>The whole texture.</summary>
    public TextureRegion(Texture texture) : this(texture, Vector2.Zero, Vector2.One) { }

    public static implicit operator TextureRegion(Texture texture) => new(texture);

    /// <summary>Size in texture pixels.</summary>
    public Vector2 PixelSize => (UvMax - UvMin) * Texture.Size;

    /// <summary>
    /// A region given in pixels the way image editors count them: (x, y) is the top-left corner,
    /// with y going down from the top of the image.
    /// </summary>
    public static TextureRegion FromPixels(Texture texture, int x, int y, int width, int height)
    {
        // The texture was flipped on load, so the image's top row is at v = 1: a region's top edge
        // (y pixels from the top) is at v = 1 - y / height.
        var size = texture.Size;
        return new TextureRegion(
            texture,
            UvMin: new Vector2(x / size.X, 1f - (y + height) / size.Y),
            UvMax: new Vector2((x + width) / size.X, 1f - y / size.Y));
    }

    /// <summary>
    /// Cuts a sprite sheet into equal cells, in reading order: left to right, then top to bottom.
    /// Leave transparent space around each drawing inside its cell: linear filtering and mipmaps
    /// average neighboring pixels, so a drawing that touches its cell's edge picks up a sliver of
    /// the next frame.
    /// </summary>
    public static TextureRegion[] Grid(Texture texture, int columns, int rows)
    {
        if (texture.Width % columns != 0 || texture.Height % rows != 0)
            throw new ArgumentException($"A {texture.Width}x{texture.Height} texture doesn't split evenly into {columns}x{rows} cells");

        int cellWidth = texture.Width / columns, cellHeight = texture.Height / rows;
        var regions = new TextureRegion[columns * rows];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                regions[row * columns + column] = FromPixels(texture, column * cellWidth, row * cellHeight, cellWidth, cellHeight);
        return regions;
    }
}
