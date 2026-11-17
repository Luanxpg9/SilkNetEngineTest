using System.Numerics;
using Engine;

/// <summary>
/// A solid box in the scene, in world units. A fixed body never moves; a falling body is moved by
/// <see cref="Physics"/>: gravity pulls it down, and contacts with other bodies push it and spin it,
/// so it can tip over edges and roll off corners.
/// Bodies are drawn as flat sprites: a <see cref="Rect"/> at <see cref="Position"/> with <see cref="Size"/>,
/// turned by <see cref="Angle"/>.
/// </summary>
sealed class Body
{
    public Vector2 Position;       // center
    public float Angle;            // radians, counter-clockwise
    public Vector2 Velocity;       // world units per second
    public float AngularVelocity;  // radians per second, counter-clockwise
    public readonly Vector2 Size;
    public readonly Color Color;

    // The solver works with inverses: 0 means "infinitely heavy", which is exactly a fixed body.
    public readonly float InverseMass;
    public readonly float InverseInertia; // resistance to spinning: how hard it is to change AngularVelocity

    public Body(Vector2 position, Vector2 size, Color color, bool falls, float density = 1f)
    {
        Position = position;
        Size = size;
        Color = color;

        if (falls)
        {
            float mass = density * size.X * size.Y;
            InverseMass = 1f / mass;
            // Moment of inertia of a solid rectangle about its center: m (w² + h²) / 12.
            InverseInertia = 12f / (mass * (size.X * size.X + size.Y * size.Y));
        }
    }

    public bool Falls => InverseMass > 0f;

    // The box's local x and y directions in world space.
    public Vector2 AxisX => new(MathF.Cos(Angle), MathF.Sin(Angle));
    public Vector2 AxisY => new(-MathF.Sin(Angle), MathF.Cos(Angle));
}
