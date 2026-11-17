using System.Numerics;

namespace Engine;

/// <summary>
/// An axis-aligned rectangle described by its center and full size: only a shape, with no color.
/// Used for UI layout and hit testing (in window units), and for previews and walls (in world units).
/// Drawing takes the color separately: <c>Sprites.Draw(rect, color)</c>.
/// </summary>
public readonly record struct Rect(Vector2 Center, Vector2 Size)
{
    public Vector2 Min => Center - Size / 2f;
    public Vector2 Max => Center + Size / 2f;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    // The rectangle spanned by two opposite corners, in any order (dragging up-left works too).
    public static Rect FromCorners(Vector2 a, Vector2 b) => new((a + b) / 2f, Vector2.Abs(b - a));
}
