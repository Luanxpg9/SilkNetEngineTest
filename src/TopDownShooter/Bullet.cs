using System.Numerics;
using Engine;

/// <summary>
/// A tracer round: an <see cref="Entity"/> that flies in a straight line at <see cref="Speed"/>, hits
/// the first enemy, wall or crate in its path, and otherwise destroys itself after
/// <see cref="Lifetime"/> seconds. It's drawn as pure light (glow 1), so it brightens whatever it
/// passes over.
///
/// It has no collider of its own. A bullet moves half a unit per fixed step, more than an enemy's
/// radius, so testing only where it lands could skip right over a thin thing. Instead each step
/// casts a ray along the whole move and stops at the first thing it crosses.
/// </summary>
sealed class Bullet(Art art, Vector2 position, Vector2 direction) : Entity(position)
{
    public const float Speed = 30f;    // world units per second: half a unit per fixed step
    public const float Lifetime = 1.2f; // seconds: 36 units, more than the arena is wide

    /// <summary>What bullets stop at.</summary>
    public const uint HitMask = CollisionLayers.Enemy | CollisionLayers.Wall;

    private readonly Vector2 _direction = direction.LengthSquared() > 1e-12f
        ? Vector2.Normalize(direction)
        : throw new ArgumentException("A bullet needs a direction to fly in, not a zero vector.", nameof(direction));
    private readonly float _angle = MathF.Atan2(direction.Y, direction.X);
    private float _age;
    private bool _spent; // it hit something this step: drawn flying into the impact point, then gone

    /// <summary>
    /// Flies <paramref name="seconds"/> along its path and ages, leaving
    /// <see cref="Entity.PreviousPosition"/> alone, so the move is blended like any other. If something
    /// is in the way, the bullet stops on it and hits it at once; it's destroyed at the start of the
    /// next step, so the frames in between draw it reaching the impact point. Used right after
    /// spawning too, so the bullet already flies during the step it was fired in.
    /// </summary>
    public void Advance(float seconds)
    {
        if (_spent)
            return;

        float distance = Speed * seconds;
        if (Scene!.Collision.Raycast(Position, _direction, distance, HitMask, out var hit))
        {
            // Destroying it now would hide it at once (the scene skips destroyed entities), up to
            // half a unit short of what it hit. Kept one more step, it's drawn sliding into it.
            Position = hit.Point;
            Strike(hit.Collider!);
            _spent = true;
            return;
        }
        Position += _direction * distance;

        _age += seconds;
        if (_age >= Lifetime)
            Destroy();
    }

    /// <summary>What a bullet does to what it hits: kills enemies; walls and the crate just stop it.</summary>
    public static void Strike(Collider target)
    {
        if (target.Owner is Enemy enemy)
            enemy.Kill();
    }

    protected override void FixedUpdate(float step)
    {
        if (_spent)
        {
            Destroy(); // it reached the impact point last step
            return;
        }
        Advance(step);
    }

    protected override void Draw(SpriteBatch sprites)
    {
        // The art points right with its hot tip at the front, so it's turned to the direction of flight.
        sprites.Draw(art.Bullet, DrawPosition, Art.WorldSize(art.Bullet), Color.White, _angle,
            layer: Layers.Effects, glow: 1f);
    }
}
