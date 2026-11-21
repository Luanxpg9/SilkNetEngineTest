using System.Numerics;
using Engine;

/// <summary>
/// A first-pass enemy: an <see cref="Entity"/> that walks straight at the player at a fixed speed,
/// whom it finds in the scene by type. Its collider is solid: it's stopped by walls, the crate,
/// other enemies and the player, and slides around them. Bumping into the player shows up as a
/// contact from its move, and then it disappears (there's no damage yet). One bullet kills it.
/// </summary>
sealed class Enemy : Entity
{
    public const string TagName = "enemy";
    public const float Speed = 2f;     // world units per second (the player does 5)
    public const float Radius = 0.45f; // world units: the collider, about as wide as the shoulders
    private const float TouchMargin = 0.01f; // world units: how close to touching still counts as reaching the player

    private static readonly Vector2 BodyCenterPixel = new(256f, 256f); // in enemy.png

    public float Angle; // radians, counter-clockwise from +X; where it faces

    private readonly Art _art;
    private readonly List<Contact> _contacts = []; // filled by each move, kept so moving doesn't allocate
    private float _walkTime; // seconds into the walk animation

    public Enemy(Art art, Vector2 position) : base(position)
    {
        _art = art;
        Tag = TagName;
        // Solid, and stopped by walls, other enemies and the player. The player's mask doesn't include
        // enemies, so enemies never block the player; they only block themselves against it.
        Collider = new Collider(Shape.Circle(Radius), CollisionLayers.Enemy,
            mask: CollisionLayers.Wall | CollisionLayers.Enemy | CollisionLayers.Player);
    }

    /// <summary>Hit by a bullet. Enemies die in one hit for now.</summary>
    public void Kill() => Destroy();

    // Simulation: walk toward where the player really is (Position, not the blended DrawPosition),
    // sliding around whatever is in the way.
    protected override void FixedUpdate(float step)
    {
        var player = Scene!.FindFirst<Player>();
        if (player is null)
            return;

        var toPlayer = player.Position - Position;
        float distance = toPlayer.Length();
        if (distance < 1e-6f)
            return;
        MoveAndSlide(toPlayer / distance * MathF.Min(Speed * step, distance), _contacts);

        // Reached the player? Destroying in the middle of the scene's fixed step is safe (it's removed
        // when the step ends).
        if (TouchedPlayer(player))
            Destroy();
    }

    // Usually the move reports a contact with the player's collider. But each push-out round records
    // only the deepest overlap, so when several enemies crowd the player, a deeper overlap with a
    // neighbor can win every round, and pushing out of it can also lift this one off the player: no
    // contact is reported though they touch. Being within touching distance after the move counts too
    // (a push-out leaves a gap of only 0.0001 units, well inside the margin).
    private bool TouchedPlayer(Player player)
    {
        foreach (var contact in _contacts)
            if (contact.Other?.Owner is Player)
                return true;
        return Vector2.Distance(player.Position, Position) <= Radius + Player.Radius + TouchMargin;
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
