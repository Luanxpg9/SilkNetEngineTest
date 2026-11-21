using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The player: a soldier with an M16A4 who moves with WASD and aims at the mouse.
///
/// It's an <see cref="Entity"/> in the game's scene. Movement runs in <see cref="FixedUpdate"/>, so it's
/// the same on every screen, and the soldier is drawn at the entity's blended
/// <see cref="Entity.DrawPosition"/>, so a 144 Hz screen doesn't show it moving in 60 Hz jumps. Aiming
/// and the walk animation run per frame in <see cref="Update"/>, toward <see cref="AimPoint"/>, which
/// the game sets each frame from the mouse.
///
/// The soldier is four sprites stacked in layers, all turned by <see cref="Angle"/>: the legs
/// (animated, turned toward where the soldier walks), the body (torso and arms), the rifle, and the
/// top (helmet and gloves). See Assets/soldier_base.svg for how the pieces line up.
/// </summary>
sealed class Player(Art art, Vector2 position, Vector2 arenaHalfSize) : Entity(position)
{
    public const float Speed = 5f;  // world units per second
    public const float Size = 0.9f; // world units: the square kept inside the walls (about the shoulder width)

    // Points in the art, in pixels (see the comments at the top of the SVG files).
    private static readonly Vector2 BodyCenterPixel = new(256f, 256f);    // in soldier_base/top.png
    private static readonly Vector2 RifleButtPixel = new(16f, 64f);       // in rifle.png
    private static readonly Vector2 LaserAperturePixel = new(604f, 40f);  // in rifle.png
    private static readonly Vector2 MuzzlePixel = new(748f, 64f);         // in rifle.png: the end of the flash hider

    // The same points as offsets from the soldier's center, in world units, with the soldier facing +X.
    // The rifle butt sits in the right shoulder (soldier pixel 320, 304); y is negative because the
    // soldier's right side is below the center line.
    private static readonly Vector2 RifleOffset = Art.Offset(320f, 304f, BodyCenterPixel.X, BodyCenterPixel.Y);
    private static readonly Vector2 LaserOffset = RifleOffset + Art.Offset(LaserAperturePixel.X, LaserAperturePixel.Y, RifleButtPixel.X, RifleButtPixel.Y);
    private static readonly Vector2 MuzzleOffset = RifleOffset + Art.Offset(MuzzlePixel.X, MuzzlePixel.Y, RifleButtPixel.X, RifleButtPixel.Y);

    // Closer than this ahead of the muzzle (the cursor on the rifle itself), bullets fly straight
    // along the rifle instead of toward the aim point.
    private const float MinAimDistance = 0.5f; // world units

    private static readonly Color BeamGlowColor = Color.FromHex("#FF2A1A").WithAlpha(0.12f);
    private static readonly Color BeamCoreColor = Color.FromHex("#FF4A3A").WithAlpha(0.55f);
    private const float BeamGlowWidth = 0.06f, BeamCoreWidth = 0.016f, LaserDotSize = 0.24f; // world units

    private const float LegsTurnRate = 14f; // how quickly the legs swing to a new walking direction (per second)

    public float Angle;      // radians, counter-clockwise from +X; where the rifle points
    public Vector2 AimPoint; // the mouse in world units, set by the game each frame before the scene updates

    private float _walkTime; // seconds into the walk animation; runs backwards when walking backwards
    private float _legsAngle;

    // Held keys (IsDown) drive movement. Firing uses WasPressed, which works here too: each click
    // reaches exactly one fixed step, so one click is one shot, whatever the frame rate.
    protected override void FixedUpdate(float step)
    {
        var input = Game.Input;
        Vector2 direction = Vector2.Zero;
        if (input.IsDown(Key.A) || input.IsDown(Key.Left))  direction.X -= 1;
        if (input.IsDown(Key.D) || input.IsDown(Key.Right)) direction.X += 1;
        if (input.IsDown(Key.S) || input.IsDown(Key.Down))  direction.Y -= 1;
        if (input.IsDown(Key.W) || input.IsDown(Key.Up))    direction.Y += 1;
        Move(direction, step);

        // Keep inside the walls, inset by half the player's size, until real collision arrives
        // (roadmap step 5). Done here, so the enemies that run after the player in this step see
        // where it really ended up.
        var limit = arenaHalfSize - new Vector2(Size / 2f);
        Position = Vector2.Clamp(Position, -limit, limit);

        // After moving, so the bullet leaves from where the rifle is at the end of this step.
        if (input.WasPressed(MouseButton.Left))
            Fire(step);
    }

    /// <summary>
    /// Fires one round (semi-automatic: one per click). <see cref="Angle"/> was last set in
    /// <see cref="Update"/>, from the drawn position and up to a frame ago, so it aims again from the
    /// real <see cref="Entity.Position"/> first. The bullet starts at the muzzle and flies at the aim
    /// point, so it crosses the laser dot: the barrel sits a few centimeters right of the laser, and
    /// flying straight along <see cref="Angle"/> would pass beside the dot.
    ///
    /// The muzzle is worked out from the real position, which is up to one step of walking (about
    /// 0.08 units) ahead of where the soldier is drawn. So while walking, the bullet's first frame can
    /// sit that far ahead of the drawn muzzle, and after that it outruns the rifle at 30 units per
    /// second. That's too small to see.
    /// </summary>
    private void Fire(float step)
    {
        FaceTowards(AimPoint, from: Position);
        var muzzleAt = Position + MathHelpers.Rotate(MuzzleOffset, Angle);

        var facing = MathHelpers.FromAngle(Angle);
        var toAim = AimPoint - muzzleAt;
        var direction = Vector2.Dot(toAim, facing) > MinAimDistance ? toAim : facing;

        // Mid-pass, so the scene queues it: it joins when this fixed step ends and its own
        // FixedUpdate first runs in the next one. Spawning sets both its positions to the muzzle,
        // so on its own it would stand still there until then, up to a whole step. Advancing it by
        // this step now keeps PreviousPosition at the muzzle, so from its first frame it's drawn
        // sliding out of the muzzle at full speed.
        var bullet = Scene!.Spawn(new Bullet(art, muzzleAt, direction));
        bullet.Advance(step);
    }

    // Per frame, from where the soldier is drawn: aim, then animate.
    protected override void Update(float deltaTime)
    {
        FaceTowards(AimPoint, from: DrawPosition);
        Animate(deltaTime);
    }

    /// <summary>One fixed step of movement. <paramref name="direction"/> doesn't need to be normalized.</summary>
    private void Move(Vector2 direction, float step)
    {
        // Normalize so diagonal movement isn't faster than straight movement.
        if (direction != Vector2.Zero)
            Position += Vector2.Normalize(direction) * Speed * step;
    }

    /// <summary>
    /// Aims at <paramref name="target"/>, a point in world units, from <paramref name="from"/> (where
    /// the player is drawn). The laser sits to the right of the soldier's center, so pointing the
    /// center at the target would leave the beam passing beside it. Instead the soldier turns a bit
    /// further, by asin(offset / distance), until the laser's line runs straight through the target.
    /// </summary>
    private void FaceTowards(Vector2 target, Vector2 from)
    {
        var toTarget = target - from;
        float distance = toTarget.Length();
        if (distance < 1e-3f) // with the mouse right on the player, keep the last angle
            return;

        float towardTarget = MathF.Atan2(toTarget.Y, toTarget.X);
        Angle = towardTarget - MathF.Asin(Math.Clamp(LaserOffset.Y / distance, -1f, 1f));
    }

    /// <summary>
    /// Per frame: plays the walk cycle while moving and turns the legs toward the walking direction.
    /// Walking backwards (away from where the rifle points) turns the legs the other way round and
    /// plays the cycle in reverse, so the legs never point behind the body.
    /// </summary>
    private void Animate(float deltaTime)
    {
        var moved = Position - PreviousPosition;
        float targetAngle = Angle;
        if (moved.LengthSquared() > 1e-10f)
        {
            float walkAngle = MathF.Atan2(moved.Y, moved.X);
            bool backwards = MathF.Abs(MathHelpers.WrapAngle(walkAngle - Angle)) > MathF.PI / 2f;
            targetAngle = backwards ? walkAngle + MathF.PI : walkAngle;
            _walkTime += backwards ? -deltaTime : deltaTime;
        }
        else
        {
            _walkTime = 0f; // frame 0 is the standing pose
        }

        // Swing toward the target by a fraction each frame: fast at first, easing in at the end,
        // with the same curve at any frame rate.
        _legsAngle += MathHelpers.WrapAngle(targetAngle - _legsAngle) * MathHelpers.SmoothingFactor(LegsTurnRate, deltaTime);
    }

    protected override void Draw(SpriteBatch sprites)
    {
        var at = DrawPosition;
        var bodyOrigin = Art.Origin(art.SoldierBase, BodyCenterPixel.X, BodyCenterPixel.Y);
        var bodySize = Art.WorldSize(art.SoldierBase);

        var legs = art.Walk.FrameAt(_walkTime);
        sprites.Draw(legs, at, Art.WorldSize(legs), Color.White, _legsAngle, layer: Layers.Legs);

        sprites.Draw(art.SoldierBase, at, bodySize, Color.White, Angle, bodyOrigin, Layers.Bodies);

        // The rifle turns around its butt, which is placed at the shoulder, turned with the body.
        var rifleAt = at + MathHelpers.Rotate(RifleOffset, Angle);
        sprites.Draw(art.Rifle, rifleAt, Art.WorldSize(art.Rifle), Color.White, Angle,
            Art.Origin(art.Rifle, RifleButtPixel.X, RifleButtPixel.Y), Layers.Weapons);

        sprites.Draw(art.SoldierTop, at, bodySize, Color.White, Angle, bodyOrigin, Layers.BodyTops);

        DrawLaser(sprites, at);
    }

    // A thin beam from the laser module to the aim point, with a soft glow around it and a dot where
    // it lands. When the aim point is closer than the laser itself (mouse over the rifle), there's no beam.
    private void DrawLaser(SpriteBatch sprites, Vector2 at)
    {
        var laserAt = at + MathHelpers.Rotate(LaserOffset, Angle);
        var facing = MathHelpers.FromAngle(Angle);
        if (Vector2.Dot(AimPoint - laserAt, facing) <= 0f)
            return;

        // The glow and the dot are pure light: they brighten what's under them instead of tinting it.
        sprites.DrawLine(laserAt, AimPoint, BeamGlowWidth, BeamGlowColor, Layers.Effects, glow: 1f);
        sprites.DrawLine(laserAt, AimPoint, BeamCoreWidth, BeamCoreColor, Layers.Effects);
        sprites.Draw(art.LaserDot, AimPoint, new Vector2(LaserDotSize), Color.White, layer: Layers.Effects, glow: 1f);
    }
}
