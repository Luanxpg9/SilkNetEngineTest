using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The top-down shooter. So far: a player that walks around a checkered arena with WASD,
/// faces the mouse, and is followed by the camera. A crate sits in the arena as the first textured
/// sprite. F3 logs how many sprites and draw calls the last frame took.
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

    // Shows 12 world units vertically: room to see what's coming, with the player still large on screen.
    private readonly Camera2D _camera = new(viewHeight: 12f);
    private readonly Player _player = new(position: Vector2.Zero);
    private Texture _crate = null!; // loaded in Load, once the GL context exists

    protected override void Load() => _crate = Texture.Load(GL, "Assets/crate.png");

    protected override void Unload() => _crate.Dispose();

    protected override void Resized(Vector2 framebufferSize) => _camera.AspectRatio = AspectRatio;

    // Simulation: movement belongs here, so it's identical at any frame rate.
    protected override void FixedUpdate(float step)
    {
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
        _player.FaceTowards(Input.MouseWorldPosition(_camera), from: playerAt);
    }

    protected override void Draw()
    {
        Sprites.Begin(_camera.ViewProjection);

        // Drawn back to front. The floor and walls all use the white pixel, so they're one draw call;
        // the crate switches texture (a second call), and the player switches back (a third).
        DrawArena();
        Sprites.Draw(_crate, CratePosition, CrateSize, Color.White);
        _player.Draw(Sprites, _player.DrawPosition(FixedStepAlpha));

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
                Sprites.Draw(new Rect(center, Vector2.One), (x + y) % 2 == 0 ? TileColorA : TileColorB);
            }

        var outer = ArenaHalfSize * 2f + new Vector2(WallThickness * 2f);
        float wallY = ArenaHalfSize.Y + WallThickness / 2f;
        float wallX = ArenaHalfSize.X + WallThickness / 2f;
        Sprites.Draw(new Rect(new(0f, wallY), new(outer.X, WallThickness)), WallColor);   // top
        Sprites.Draw(new Rect(new(0f, -wallY), new(outer.X, WallThickness)), WallColor);  // bottom
        Sprites.Draw(new Rect(new(-wallX, 0f), new(WallThickness, outer.Y)), WallColor);  // left
        Sprites.Draw(new Rect(new(wallX, 0f), new(WallThickness, outer.Y)), WallColor);   // right
    }
}
