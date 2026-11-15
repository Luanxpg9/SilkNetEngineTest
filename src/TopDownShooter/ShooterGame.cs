using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The top-down shooter. So far: a player that walks around a checkered arena with WASD,
/// faces the mouse, and is followed by the camera.
/// </summary>
sealed class ShooterGame() : Game("HavingFunWithEngine - Top-Down Shooter")
{
    // The arena is a grid of 1x1 tiles centered on the origin, with a wall around it.
    private const int ArenaWidth = 30, ArenaHeight = 20; // tiles
    private const float WallThickness = 0.5f;
    private static readonly Vector3 TileColorA = new(0.16f, 0.17f, 0.21f);
    private static readonly Vector3 TileColorB = new(0.19f, 0.20f, 0.25f);
    private static readonly Vector3 WallColor = new(0.45f, 0.47f, 0.52f);
    private static readonly Vector2 ArenaHalfSize = new(ArenaWidth / 2f, ArenaHeight / 2f);

    // Shows 12 world units vertically: room to see what's coming, with the player still large on screen.
    private readonly Camera2D _camera = new(viewHeight: 12f);
    private readonly Player _player = new(position: Vector2.Zero);

    protected override void Resized(Vector2 framebufferSize) => _camera.AspectRatio = AspectRatio;

    // Simulation: movement belongs here, so it's identical at any frame rate.
    protected override void FixedUpdate(float step)
    {
        // Held keys (IsDown) are safe in the fixed step; one-off presses are handled in Update.
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

        // The camera centers on where the player is drawn this frame, so the player stays still
        // on screen and the arena slides under it.
        var playerAt = _player.DrawPosition(FixedStepAlpha);
        _camera.Position = playerAt;

        // Aim after moving the camera, so the mouse is converted to world units with this frame's view.
        _player.FaceTowards(Input.MouseWorldPosition(_camera), from: playerAt);
    }

    protected override void Draw()
    {
        Quads.Begin();
        Quads.SetViewProjection(_camera.ViewProjection);

        DrawArena();

        var playerAt = _player.DrawPosition(FixedStepAlpha);
        Quads.Draw(_player.BodyModel(playerAt), Player.BodyColor);
        Quads.Draw(_player.NoseModel(playerAt), Player.NoseColor);
    }

    // A checkerboard floor, so movement is visible while the camera follows the player, and a wall around it.
    // That's 600 tiles, one draw call each: fine for now, and the sprite batcher in step 3 will make it one call.
    private void DrawArena()
    {
        for (int x = 0; x < ArenaWidth; x++)
            for (int y = 0; y < ArenaHeight; y++)
            {
                var center = new Vector2(x + 0.5f, y + 0.5f) - ArenaHalfSize;
                Quads.Draw(new Rectangle(center, Vector2.One, (x + y) % 2 == 0 ? TileColorA : TileColorB));
            }

        var outer = ArenaHalfSize * 2f + new Vector2(WallThickness * 2f);
        float wallY = ArenaHalfSize.Y + WallThickness / 2f;
        float wallX = ArenaHalfSize.X + WallThickness / 2f;
        Quads.Draw(new Rectangle(new(0f, wallY), new(outer.X, WallThickness), WallColor));   // top
        Quads.Draw(new Rectangle(new(0f, -wallY), new(outer.X, WallThickness), WallColor));  // bottom
        Quads.Draw(new Rectangle(new(-wallX, 0f), new(WallThickness, outer.Y), WallColor));  // left
        Quads.Draw(new Rectangle(new(wallX, 0f), new(WallThickness, outer.Y), WallColor));   // right
    }
}
