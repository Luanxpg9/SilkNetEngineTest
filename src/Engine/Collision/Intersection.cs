using System.Numerics;

namespace Engine;

/// <summary>
/// How two shapes touch. <see cref="Normal"/> is a unit vector pointing away from
/// <see cref="Other"/>, toward the shape being tested, and <see cref="Depth"/> is how far they
/// overlap along it, so moving by <c>Normal × Depth</c> separates them. With y up, standing on a floor
/// gives a normal close to (0, 1) and touching a wall on the left gives one close to (1, 0); a game
/// that cares (a platformer checking for ground) reads that, and one that doesn't ignores it.
/// <see cref="Other"/> is null when two bare shapes are tested.
/// </summary>
public readonly record struct Contact(Collider? Other, Vector2 Normal, float Depth);

/// <summary>
/// Where a ray hit: what it hit, the world point, the surface normal there (pointing back toward the
/// ray), and how far along the ray. A ray that starts inside a shape hits it at distance 0, with the
/// normal pointing straight back along the ray.
/// </summary>
public readonly record struct RayHit(Collider? Collider, Vector2 Point, Vector2 Normal, float Distance);

/// <summary>
/// Tests between world-placed shapes (see <see cref="Shape.At"/>): overlap with a contact, point
/// inside, and raycasts. <see cref="CollisionWorld"/> builds on these; a game can also call them
/// directly. All are exact for circles and axis-aligned boxes, and none allocates.
/// </summary>
public static class Intersection
{
    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> overlap. If they do, <paramref name="contact"/>
    /// says how to push <paramref name="a"/> out of <paramref name="b"/>. Shapes that only touch (depth 0)
    /// don't count as overlapping.
    /// </summary>
    public static bool Overlap(in Shape a, in Shape b, out Contact contact)
    {
        switch (a.Kind, b.Kind)
        {
            case (ShapeKind.Circle, ShapeKind.Circle):
                return CircleCircle(a, b, out contact);
            case (ShapeKind.Box, ShapeKind.Box):
                return BoxBox(a, b, out contact);
            case (ShapeKind.Circle, ShapeKind.Box):
                return CircleBox(a, b, out contact);
            default: // box against circle: the circle's answer, turned around
                bool hit = CircleBox(b, a, out var flipped);
                contact = flipped with { Normal = -flipped.Normal };
                return hit;
        }
    }

    /// <summary>Whether <paramref name="point"/> is inside <paramref name="shape"/> (on the edge counts).</summary>
    public static bool Contains(in Shape shape, Vector2 point)
    {
        var offset = point - shape.Center;
        return shape.Kind == ShapeKind.Circle
            ? offset.LengthSquared() <= shape.Radius * shape.Radius
            : MathF.Abs(offset.X) <= shape.HalfSize.X && MathF.Abs(offset.Y) <= shape.HalfSize.Y;
    }

    /// <summary>
    /// Casts a ray from <paramref name="origin"/> along <paramref name="direction"/> (a unit vector) for
    /// up to <paramref name="maxDistance"/>. On a hit, gives the distance to the shape's surface and the
    /// surface normal there. Starting inside the shape is a hit at distance 0, with the normal
    /// pointing back along the ray.
    /// </summary>
    public static bool Raycast(in Shape shape, Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out Vector2 normal)
    {
        return shape.Kind == ShapeKind.Circle
            ? RayCircle(shape, origin, direction, maxDistance, out distance, out normal)
            : RayBox(shape, origin, direction, maxDistance, out distance, out normal);
    }

    private static bool CircleCircle(in Shape a, in Shape b, out Contact contact)
    {
        contact = default;
        var offset = a.Center - b.Center;
        float radii = a.Radius + b.Radius;
        float distanceSquared = offset.LengthSquared();
        if (distanceSquared >= radii * radii)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        // Exactly on top of each other there's no "away": pick a fixed direction so the result is stable.
        var normal = distance > 1e-6f ? offset / distance : Vector2.UnitY;
        contact = new Contact(null, normal, radii - distance);
        return true;
    }

    // Two boxes overlap when they overlap on both axes. They're pushed apart along the axis with the
    // smaller overlap: the shortest way out.
    private static bool BoxBox(in Shape a, in Shape b, out Contact contact)
    {
        contact = default;
        var offset = a.Center - b.Center;
        float overlapX = a.HalfSize.X + b.HalfSize.X - MathF.Abs(offset.X);
        float overlapY = a.HalfSize.Y + b.HalfSize.Y - MathF.Abs(offset.Y);
        if (overlapX <= 0f || overlapY <= 0f)
            return false;

        contact = overlapX < overlapY
            ? new Contact(null, new Vector2(offset.X >= 0f ? 1f : -1f, 0f), overlapX)
            : new Contact(null, new Vector2(0f, offset.Y >= 0f ? 1f : -1f), overlapY);
        return true;
    }

    // The circle is pushed away from the point of the box closest to its center. That point is the
    // center clamped into the box, so near a corner the push points away from the corner, which is
    // what lets a circle slide smoothly around it. A center inside the box has no closest point
    // outside, so it's pushed out through the nearest face instead.
    private static bool CircleBox(in Shape circle, in Shape box, out Contact contact)
    {
        contact = default;
        var local = circle.Center - box.Center;
        var closest = Vector2.Clamp(local, -box.HalfSize, box.HalfSize);
        float radius = circle.Radius;

        if (local != closest)
        {
            var offset = local - closest;
            float distanceSquared = offset.LengthSquared();
            if (distanceSquared >= radius * radius)
                return false;
            float distance = MathF.Sqrt(distanceSquared);
            contact = new Contact(null, offset / distance, radius - distance);
            return true;
        }

        float toFaceX = box.HalfSize.X - MathF.Abs(local.X);
        float toFaceY = box.HalfSize.Y - MathF.Abs(local.Y);
        contact = toFaceX < toFaceY
            ? new Contact(null, new Vector2(local.X >= 0f ? 1f : -1f, 0f), toFaceX + radius)
            : new Contact(null, new Vector2(0f, local.Y >= 0f ? 1f : -1f), toFaceY + radius);
        return true;
    }

    // Solves |origin + t·direction − center| = radius for the smaller t.
    private static bool RayCircle(in Shape circle, Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out Vector2 normal)
    {
        distance = 0f;
        normal = -direction;
        var fromCenter = origin - circle.Center;
        float c = fromCenter.LengthSquared() - circle.Radius * circle.Radius;
        if (c <= 0f)
            return true; // starts inside

        float b = Vector2.Dot(fromCenter, direction);
        if (b > 0f)
            return false; // outside and pointing away
        float discriminant = b * b - c;
        if (discriminant < 0f)
            return false; // passes beside it

        float t = -b - MathF.Sqrt(discriminant);
        if (t > maxDistance)
            return false;
        distance = t;
        normal = Vector2.Normalize(origin + direction * t - circle.Center);
        return true;
    }

    // The slab method: on each axis, find where the ray enters and leaves the box's span. The ray is
    // inside the box between the latest entry and the earliest exit; if the latest entry comes after
    // the earliest exit, it misses. The axis of the latest entry is the face it hits.
    private static bool RayBox(in Shape box, Vector2 origin, Vector2 direction, float maxDistance,
        out float distance, out Vector2 normal)
    {
        distance = 0f;
        normal = -direction;
        if (Contains(box, origin))
            return true;

        var min = box.Center - box.HalfSize;
        var max = box.Center + box.HalfSize;
        float enter = 0f, exit = maxDistance;
        var enterNormal = Vector2.Zero;

        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? origin.X : origin.Y;
            float d = axis == 0 ? direction.X : direction.Y;
            float lo = axis == 0 ? min.X : min.Y;
            float hi = axis == 0 ? max.X : max.Y;

            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < lo || o > hi)
                    return false; // parallel to this axis and outside its span
                continue;
            }

            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            if (t1 > enter)
            {
                enter = t1;
                // It enters through the face it travels toward: moving +X, that's the low face, whose normal is −X.
                float sign = d > 0f ? -1f : 1f;
                enterNormal = axis == 0 ? new Vector2(sign, 0f) : new Vector2(0f, sign);
            }
            exit = MathF.Min(exit, t2);
            if (enter > exit)
                return false;
        }

        if (enterNormal == Vector2.Zero)
            return false; // can't happen with the origin outside, but never report a hit without a face
        distance = enter;
        normal = enterNormal;
        return true;
    }
}
