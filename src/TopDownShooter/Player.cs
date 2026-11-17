using System.Numerics;
using Engine;

/// <summary>
/// The player: a square that moves with WASD and turns to face the mouse.
///
/// Movement runs in the game's fixed step, so it's the same on every screen. Drawing happens more
/// often than that on a fast screen, so the player keeps its position from the previous step too,
/// and <see cref="DrawPosition"/> blends between the two. Without that blend, a 144 Hz screen would
/// show the square moving in 60 Hz jumps.
/// </summary>
sealed class Player(Vector2 position)
{
    public const float Speed = 5f;     // world units per second
    public const float Size = 0.8f;    // world units, side of the square
    public static readonly Color BodyColor = Color.FromHex("#59BF73");
    public static readonly Color NoseColor = Color.FromHex("#EBF2EB");

    public Vector2 Position = position;          // center, after the latest fixed step
    public Vector2 PreviousPosition = position;  // center, after the step before it
    public float Angle;                          // radians, counter-clockwise from +X; where the player faces

    /// <summary>One fixed step of movement. <paramref name="direction"/> doesn't need to be normalized.</summary>
    public void Move(Vector2 direction, float step)
    {
        PreviousPosition = Position;

        // Normalize so diagonal movement isn't faster than straight movement.
        if (direction != Vector2.Zero)
            Position += Vector2.Normalize(direction) * Speed * step;
    }

    /// <summary>Where to draw the player: <paramref name="alpha"/> of the way from the previous step to the latest.</summary>
    public Vector2 DrawPosition(float alpha) => Vector2.Lerp(PreviousPosition, Position, alpha);

    /// <summary>Turns to face <paramref name="target"/>, a point in world units.</summary>
    public void FaceTowards(Vector2 target, Vector2 from)
    {
        var toTarget = target - from;
        if (toTarget.LengthSquared() > 1e-6f) // with the mouse right on the player, keep the last angle
            Angle = MathF.Atan2(toTarget.Y, toTarget.X);
    }

    /// <summary>
    /// The body, and a small bar sticking out of the front edge so you can see which way the player
    /// faces. Both turn by <see cref="Angle"/>. The bar's center sits half a body ahead of the player's
    /// center along the facing direction, which is the offset (Size / 2, 0) turned by the angle.
    /// </summary>
    public void Draw(SpriteBatch sprites, Vector2 at)
    {
        sprites.Draw(new Rect(at, new Vector2(Size)), BodyColor, Angle);

        var facing = new Vector2(MathF.Cos(Angle), MathF.Sin(Angle));
        sprites.Draw(new Rect(at + facing * (Size * 0.5f), new Vector2(Size * 0.45f, Size * 0.22f)), NoseColor, Angle);
    }
}
