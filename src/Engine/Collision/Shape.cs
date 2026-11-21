using System.Numerics;

namespace Engine;

public enum ShapeKind { Circle, Box }

/// <summary>
/// A collision shape: a circle or an axis-aligned box (one that never turns), in world units.
///
/// On a <see cref="Collider"/>, <see cref="Center"/> is an offset from the collider's position, so
/// the shape moves with its entity. <see cref="At"/> places it in the world, which is what the
/// tests in <see cref="Intersection"/> take. Nothing here knows about gravity or which way is up, so
/// the same shapes work for a top-down game and a side view.
/// </summary>
public readonly struct Shape
{
    private Shape(ShapeKind kind, Vector2 center, Vector2 halfSize)
    {
        Kind = kind;
        Center = center;
        HalfSize = halfSize;
    }

    public ShapeKind Kind { get; }

    /// <summary>The middle of the shape: an offset on a collider, a world point after <see cref="At"/>.</summary>
    public Vector2 Center { get; }

    /// <summary>Half the box's width and height. For a circle, the radius in both.</summary>
    public Vector2 HalfSize { get; }

    /// <summary>A circle's radius (for a box, half its width).</summary>
    public float Radius => HalfSize.X;

    /// <summary>The smallest axis-aligned rectangle around the shape.</summary>
    public Rect Bounds => new(Center, HalfSize * 2f);

    public static Shape Circle(float radius, Vector2 center = default)
    {
        if (!(radius > 0f))
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A circle needs a radius above 0.");
        return new Shape(ShapeKind.Circle, center, new Vector2(radius));
    }

    public static Shape Box(Vector2 size, Vector2 center = default)
    {
        if (!(size.X > 0f && size.Y > 0f))
            throw new ArgumentOutOfRangeException(nameof(size), size, "A box needs a width and height above 0.");
        return new Shape(ShapeKind.Box, center, size / 2f);
    }

    /// <summary>A box covering <paramref name="rect"/>, the same rectangle a wall might be drawn with.</summary>
    public static Shape Box(Rect rect) => Box(rect.Size, rect.Center);

    /// <summary>The same shape moved by <paramref name="position"/>: from a collider's offset to the world.</summary>
    public Shape At(Vector2 position) => new(Kind, Center + position, HalfSize);
}
