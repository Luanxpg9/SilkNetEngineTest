using System.Numerics;

namespace Engine;

/// <summary>
/// An axis-aligned rectangle described by its center and full size: only a shape, with no color.
/// Used for UI layout and hit testing (in window units), and for previews and walls (in world units).
/// Drawing takes the color separately: <c>Quads.Draw(rect, color)</c>.
/// <see cref="Model"/> stretches the renderer's unit quad (-0.5..0.5) to <see cref="Size"/> and
/// moves it to <see cref="Center"/>.
/// </summary>
public readonly record struct Rect(Vector2 Center, Vector2 Size)
{
    public Vector2 Min => Center - Size / 2f;
    public Vector2 Max => Center + Size / 2f;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    // The rectangle spanned by two opposite corners, in any order (dragging up-left works too).
    public static Rect FromCorners(Vector2 a, Vector2 b) => new((a + b) / 2f, Vector2.Abs(b - a));

    // Row-vector order: scale the unit quad first, then move it into place.
    public Matrix4x4 Model => Matrix4x4.CreateScale(Size.X, Size.Y, 1f) * Matrix4x4.CreateTranslation(Center.X, Center.Y, 0f);
}
