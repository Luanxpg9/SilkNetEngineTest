using System.Numerics;

namespace Engine;

/// <summary>
/// Small angle and vector helpers that games keep needing: aiming, turning toward a target,
/// moving along a direction. Angles are in radians, counter-clockwise from +X, like everywhere
/// in the engine.
/// </summary>
public static class MathHelpers
{
    /// <summary>
    /// The same angle brought into -π..π. Use it on the difference between two angles, so turning
    /// from one to the other takes the short way round (from 170° to -170° is +20°, not -340°).
    /// </summary>
    public static float WrapAngle(float angle) => MathF.IEEERemainder(angle, MathF.Tau);

    /// <summary>The unit vector pointing along <paramref name="angle"/>: (cos, sin).</summary>
    public static Vector2 FromAngle(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    /// <summary>
    /// Turns <paramref name="vector"/> counter-clockwise by <paramref name="angle"/>. Handy for
    /// offsets defined with a sprite facing +X (where the gun's muzzle is, say), which then follow
    /// the sprite when it's drawn turned.
    /// </summary>
    public static Vector2 Rotate(Vector2 vector, float angle)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    /// <summary>
    /// How far to move toward a target this frame for smooth following that looks the same at any
    /// frame rate: <c>value += (target - value) * SmoothingFactor(rate, deltaTime)</c>.
    /// <paramref name="rate"/> is per second; higher follows faster. A plain <c>rate * deltaTime</c>
    /// would overshoot on slow frames and follow faster on fast screens; the exponential form gives
    /// the same curve however the time is sliced.
    /// </summary>
    public static float SmoothingFactor(float rate, float deltaTime) => 1f - MathF.Exp(-rate * deltaTime);
}
