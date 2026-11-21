using System.Numerics;
using Engine;

/// <summary>
/// A tracer round: an <see cref="Entity"/> that flies in a straight line at <see cref="Speed"/> and
/// destroys itself after <see cref="Lifetime"/> seconds. It doesn't hit anything yet; collision
/// arrives in roadmap step 5. It's drawn as pure light (glow 1), so it brightens whatever it passes over.
/// </summary>
sealed class Bullet(Art art, Vector2 position, Vector2 direction) : Entity(position)
{
    public const float Speed = 30f;    // world units per second: half a unit per fixed step
    public const float Lifetime = 1.2f; // seconds: 36 units, more than the arena is wide

    private readonly Vector2 _velocity = direction.LengthSquared() > 1e-12f
        ? Vector2.Normalize(direction) * Speed
        : throw new ArgumentException("A bullet needs a direction to fly in, not a zero vector.", nameof(direction));
    private readonly float _angle = MathF.Atan2(direction.Y, direction.X);
    private float _age;

    /// <summary>
    /// Moves the bullet <paramref name="seconds"/> along its path and ages it, leaving
    /// <see cref="Entity.PreviousPosition"/> alone, so the move is blended like any other. Used right
    /// after spawning, so the bullet already flies during the step it was fired in.
    /// </summary>
    public void Advance(float seconds)
    {
        Position += _velocity * seconds;

        _age += seconds;
        if (_age >= Lifetime)
            Destroy();
    }

    protected override void FixedUpdate(float step) => Advance(step);

    protected override void Draw(SpriteBatch sprites)
    {
        // The art points right with its hot tip at the front, so it's turned to the direction of flight.
        sprites.Draw(art.Bullet, DrawPosition, Art.WorldSize(art.Bullet), Color.White, _angle,
            layer: Layers.Effects, glow: 1f);
    }
}
