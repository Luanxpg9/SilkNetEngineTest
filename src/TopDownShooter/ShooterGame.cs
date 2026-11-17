using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The top-down shooter. So far: a soldier who walks around a checkered arena with WASD, aims an
/// M16A4 at the mouse with a laser showing where it points, and is followed by the camera. A crate
/// and an enemy (who only turns to watch) stand in the arena. F3 logs how many sprites and draw
/// calls the last frame took.
/// </summary>
sealed class ShooterGame() : Game("HavingFunWithEngine - Top-Down Shooter")
{
    // The arena is a grid of 1x1 tiles centered on the origin, with a wall around it.
    private const int ArenaWidth = 30, ArenaHeight = 20; // tiles
    private const float WallThickness = 0.5f;
    private static readonly Color TileColorA = Color.FromHex("#292B36");
    private static readonly Color TileColorB = Color.FromHex("#303340");
    private static readonly Color WallColor = Color.FromHex("#737885");
    private static readonly Vector2 ArenaHalfSize = new(ArenaWidth / 2f, ArenaHeight / 2f);

    // The crate image has a transparent margin with its shadow in it, so it's drawn a bit larger
    // than the 1.2 units the box itself should cover.
    private static readonly Vector2 CratePosition = new(3f, 2f);
    private static readonly Vector2 CrateSize = new(1.5f);

    // Shows 8 world units vertically: room to see what's coming, with the soldier large enough
    // for the rifle's details to read.
    private readonly Camera2D _camera = new(viewHeight: 8f);
    private readonly Player _player = new(position: Vector2.Zero);
    private readonly Enemy _enemy = new(position: new Vector2(-5f, 3f));
    private Art _art = null!;     // loaded in Load, once the GL context exists
    private Vector2 _aimPoint;    // the mouse in world units, this frame

    protected override void Load() => _art = new Art(GL);

    protected override void Unload() => _art.Dispose();

    protected override void Resized(Vector2 framebufferSize) => _camera.AspectRatio = AspectRatio;

    // Simulation: movement belongs here, so it's identical at any frame rate.
    protected override void FixedUpdate(float step)
    {
        // Remember where this step starts, before anything moves the player (see Player.DrawPosition).
        _player.BeginFixedStep();

        // Held keys (IsDown) drive movement. WasPressed works here too (each press reaches exactly one
        // fixed step), which is what firing will use; Esc stays in Update because quitting isn't simulation.
        Vector2 direction = Vector2.Zero;
        if (Input.IsDown(Key.A) || Input.IsDown(Key.Left))  direction.X -= 1;
        if (Input.IsDown(Key.D) || Input.IsDown(Key.Right)) direction.X += 1;
        if (Input.IsDown(Key.S) || Input.IsDown(Key.Down))  direction.Y -= 1;
        if (Input.IsDown(Key.W) || Input.IsDown(Key.Up))    direction.Y += 1;
        _player.Move(direction, step);

        // Keep the player inside the walls until real collision arrives (roadmap step 5).
        var limit = ArenaHalfSize - new Vector2(Player.Size / 2f);
        _player.Position = Vector2.Clamp(_player.Position, -limit, limit);
    }

    // Per frame: things that should follow the screen smoothly, and one-off input.
    protected override void Update(float deltaTime)
    {
        if (Input.WasPressed(Key.Escape))
            Exit();

        // Read during Update, so the numbers are from the whole previous frame.
        if (Input.WasPressed(Key.F3))
            Log.Info($"Last frame: {Sprites.SpritesDrawn} sprites in {Sprites.DrawCalls} draw calls");

        // The camera centers on where the player is drawn this frame, so the player stays still
        // on screen and the arena slides under it.
        var playerAt = _player.DrawPosition(FixedStepAlpha);
        _camera.Position = playerAt;

        // Aim after moving the camera, so the mouse is converted to world units with this frame's view.
        _aimPoint = Input.MouseWorldPosition(_camera);
        _player.FaceTowards(_aimPoint, from: playerAt);
        _player.Animate(deltaTime);

        _enemy.FaceTowards(playerAt);
    }

    protected override void Draw()
    {
        Sprites.Begin(_camera.ViewProjection);

        // The order of these calls doesn't matter: layers decide what covers what. Within a layer,
        // sprites with the same texture batch together (the 604 floor sprites are one draw call).
        DrawArena();
        Sprites.Draw(_art.Crate, CratePosition, CrateSize, Color.White, layer: Layers.Props);
        _enemy.Draw(Sprites, _art);
        _player.Draw(Sprites, _art, _player.DrawPosition(FixedStepAlpha), _aimPoint);

        Sprites.End();
    }

    // A checkerboard floor, so movement is visible while the camera follows the player, and a wall around it.
    // That's 604 flat sprites, batched into a single draw call.
    private void DrawArena()
    {
        for (int x = 0; x < ArenaWidth; x++)
            for (int y = 0; y < ArenaHeight; y++)
            {
                var center = new Vector2(x + 0.5f, y + 0.5f) - ArenaHalfSize;
                Sprites.Draw(new Rect(center, Vector2.One), (x + y) % 2 == 0 ? TileColorA : TileColorB, layer: Layers.Floor);
            }

        var outer = ArenaHalfSize * 2f + new Vector2(WallThickness * 2f);
        float wallY = ArenaHalfSize.Y + WallThickness / 2f;
        float wallX = ArenaHalfSize.X + WallThickness / 2f;
        Sprites.Draw(new Rect(new(0f, wallY), new(outer.X, WallThickness)), WallColor, layer: Layers.Floor);   // top
        Sprites.Draw(new Rect(new(0f, -wallY), new(outer.X, WallThickness)), WallColor, layer: Layers.Floor);  // bottom
        Sprites.Draw(new Rect(new(-wallX, 0f), new(WallThickness, outer.Y)), WallColor, layer: Layers.Floor);  // left
        Sprites.Draw(new Rect(new(wallX, 0f), new(WallThickness, outer.Y)), WallColor, layer: Layers.Floor);   // right
    }
}
