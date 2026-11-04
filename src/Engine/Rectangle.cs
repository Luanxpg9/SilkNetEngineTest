using System.Numerics;

/// <summary>
/// An axis-aligned rectangle in world units, described by its center and full size.
/// It doesn't move: the cloth will use <see cref="Min"/> and <see cref="Max"/> to collide with it.
/// Every rectangle is drawn with the same unit quad (-0.5..0.5); <see cref="Model"/> stretches
/// that quad to <see cref="Size"/> and moves it to <see cref="Center"/>.
/// </summary>
readonly record struct Rectangle(Vector2 Center, Vector2 Size, Vector3 Color)
{
    public Vector2 Min => Center - Size / 2f;
    public Vector2 Max => Center + Size / 2f;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    // The rectangle spanned by two opposite corners, in any order (dragging up-left works too).
    public static Rectangle FromCorners(Vector2 a, Vector2 b, Vector3 color) =>
        new((a + b) / 2f, Vector2.Abs(b - a), color);

    // Row-vector order: scale the unit quad first, then move it into place.
    public Matrix4x4 Model => Matrix4x4.CreateScale(Size.X, Size.Y, 1f) * Matrix4x4.CreateTranslation(Center.X, Center.Y, 0f);
}
