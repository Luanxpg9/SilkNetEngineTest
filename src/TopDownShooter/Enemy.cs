using System.Numerics;
using Engine;

/// <summary>
/// A first-pass enemy: an <see cref="Entity"/> that walks straight at the player at a fixed speed,
/// whom it finds in the scene by type, and disappears when it reaches them. Its collider is a
/// trigger that notices the player, so "reaching" is the trigger's begin event. Until damage exists,
/// that's how it leaves the scene, which tests destroying an entity in the middle of the fixed step.
/// It still walks through walls and other enemies: that changes in roadmap step 5 Phase B.
/// </summary>
sealed class Enemy : Entity
{
    public const string TagName = "enemy";
    public const float Speed = 2f;     // world units per second (the player does 5)
    public const float Radius = 0.45f; // world units: the collider, about as wide as the shoulders

    private static readonly Vector2 BodyCenterPixel = new(256f, 256f); // in enemy.png

    public float Angle; // radians, counter-clockwise from +X; where it faces

    private readonly Art _art;
    private float _walkTime; // seconds into the walk animation

    public Enemy(Art art, Vector2 position) : base(position)
    {
        _art = art;
        Tag = TagName;
        Collider = new Collider(Shape.Circle(Radius), CollisionLayers.Enemy, mask: CollisionLayers.Player, isTrigger: true);
    }

    // Simulation: walk toward where the player really is (Position, not the blended DrawPosition).
    protected override void FixedUpdate(float step)
    {
        var player = Scene!.FindFirst<Player>();
        if (player is null)
            return;

        var toPlayer = player.Position - Position;
        float distance = toPlayer.Length();
        if (distance > 1e-6f)
            Position += toPlayer / distance * MathF.Min(Speed * step, distance); // never past the player
    }

    // The trigger touched the player: the enemy has reached them. This comes at the end of the
    // fixed step, still inside the scene's pass, so destroying here is safe.
    protected override void TriggerEnter(Collider other)
    {
        if (other.Owner is Player)
            Destroy();
    }

    // Per frame: face the player as smoothly as the player is drawn, and play the walk cycle while moving.
    protected override void Update(float deltaTime)
    {
        var player = Scene!.FindFirst<Player>();
        if (player is not null)
        {
            var toPlayer = player.DrawPosition - DrawPosition;
            if (toPlayer.LengthSquared() > 1e-6f)
                Angle = MathF.Atan2(toPlayer.Y, toPlayer.X);
        }

        bool moving = (Position - PreviousPosition).LengthSquared() > 1e-10f;
        _walkTime = moving ? _walkTime + deltaTime : 0f; // frame 0 is the standing pose
    }

    protected override void Draw(SpriteBatch sprites)
    {
        var at = DrawPosition;

        // It always walks toward the player, so the legs simply point the same way as the body.
        var legs = _art.Walk.FrameAt(_walkTime);
        sprites.Draw(legs, at, Art.WorldSize(legs), Color.White, Angle, layer: Layers.Legs);

        sprites.Draw(_art.Enemy, at, Art.WorldSize(_art.Enemy), Color.White, Angle,
            Art.Origin(_art.Enemy, BodyCenterPixel.X, BodyCenterPixel.Y), Layers.Bodies);
    }
}
