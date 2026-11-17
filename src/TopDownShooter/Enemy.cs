using System.Numerics;
using Engine;

/// <summary>
/// A first-pass enemy, so its art can be seen in the game: it stands still and turns to face the
/// player. It has no behavior yet; entities (roadmap step 4) and collision (step 5) bring that.
/// </summary>
sealed class Enemy(Vector2 position)
{
    private static readonly Vector2 BodyCenterPixel = new(256f, 256f); // in enemy.png

    public Vector2 Position = position;
    public float Angle;

    public void FaceTowards(Vector2 target)
    {
        var toTarget = target - Position;
        if (toTarget.LengthSquared() > 1e-6f)
            Angle = MathF.Atan2(toTarget.Y, toTarget.X);
    }

    public void Draw(SpriteBatch sprites, Art art)
    {
        // Standing: the walk sheet's first frame, feet side by side.
        var legs = art.Walk.Frames[0];
        sprites.Draw(legs, Position, Art.WorldSize(legs), Color.White, Angle, layer: Layers.Legs);

        sprites.Draw(art.Enemy, Position, Art.WorldSize(art.Enemy), Color.White, Angle,
            Art.Origin(art.Enemy, BodyCenterPixel.X, BodyCenterPixel.Y), Layers.Bodies);
    }
}
