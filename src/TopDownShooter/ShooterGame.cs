using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// The top-down shooter. So far: a soldier who walks around a checkered arena with WASD, aims an
/// M16A4 at the mouse with a laser showing where it points, fires glowing tracers with a click, and
/// is followed by the camera. The walls and a crate are solid, and the soldier slides along them.
/// Enemies spawn along the walls and walk at the player, disappearing when they reach them; K
/// removes them all. F2 shows the colliders, and F3 logs how many sprites, draw calls, entities and
/// colliders the last frame had.
///
/// The player, the enemies, the spawner and the bullets are entities in a <see cref="Scene"/>, which
/// runs them. The game keeps a handle to the player for what only the game knows about: the camera
/// and the mouse in world units.
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

    // The four walls just outside the floor: drawn, and solid. They overlap at the corners.
    private static readonly Rect[] Walls = BuildWalls();

    // The crate image has a transparent margin with its shadow in it, so it's drawn a bit larger
    // than the 1.2 units the box itself covers, which is the size of its collider.
    private static readonly Vector2 CratePosition = new(3f, 2f);
    private static readonly Vector2 CrateSize = new(1.5f);
    private static readonly Vector2 CrateBoxSize = new(1.2f);

    private const float ColliderLineWidth = 0.025f; // world units: about 2 pixels at this zoom

    // Shows 8 world units vertically: room to see what's coming, with the soldier large enough
    // for the rifle's details to read.
    private readonly Camera2D _camera = new(viewHeight: 8f);
    private Art _art = null!;       // loaded in Load, once the GL context exists
    private Scene _scene = null!;   // created in Load: the entities need the art
    private Player _player = null!; // also in _scene, which owns it
    private bool _showColliders;    // F2

    protected override void Load()
    {
        _art = new Art(GL);
        _scene = new Scene(this);

        // The level's solid parts are colliders without an entity: they never move or think. A wall's
        // box is centered on its rectangle and the collider stays at the origin.
        foreach (var wall in Walls)
            _scene.Collision.Add(new Collider(Shape.Box(wall), CollisionLayers.Wall));
        _scene.Collision.Add(new Collider(Shape.Box(CrateBoxSize), CollisionLayers.Wall) { Position = CratePosition });

        _player = _scene.Spawn(new Player(_art, position: Vector2.Zero));
        _scene.Spawn(new Enemy(_art, position: new Vector2(-5f, 3f)));
        _scene.Spawn(new EnemySpawner(_art, ArenaHalfSize)); // more enemies along the walls, K removes them
    }

    protected override void Unload() => _art.Dispose();

    protected override void Resized(Vector2 framebufferSize) => _camera.AspectRatio = AspectRatio;

    // Simulation: movement belongs here, so it's identical at any frame rate.
    protected override void FixedUpdate(float step)
    {
        // Every entity's step starts where the last one ended, then each runs: the player walks
        // (sliding along the walls and the crate), the enemies chase it, the spawner adds more.
        // Then enemies that reached the player find out through their triggers.
        _scene.FixedUpdate(step);
    }

    // Per frame: things that should follow the screen smoothly, and one-off input.
    protected override void Update(float deltaTime)
    {
        if (Input.WasPressed(Key.Escape))
            Exit();

        // Read during Update, so the numbers are from the whole previous frame.
        if (Input.WasPressed(Key.F3))
            Log.Info($"Last frame: {Sprites.SpritesDrawn} sprites in {Sprites.DrawCalls} draw calls, {_scene.Count} entities, {_scene.Collision.Count} colliders");

        if (Input.WasPressed(Key.F2))
            _showColliders = !_showColliders;

        // The camera centers on where the player is drawn this frame, so the player stays still
        // on screen and the arena slides under it.
        _camera.Position = _player.DrawPosition;

        // After moving the camera, so the mouse is converted to world units with this frame's view.
        // The player aims at it in its own Update.
        _player.AimPoint = Input.MouseWorldPosition(_camera);
        _scene.Update(deltaTime);
    }

    protected override void Draw()
    {
        Sprites.Begin(_camera.ViewProjection);

        // The order of these calls doesn't matter: layers decide what covers what. Within a layer,
        // sprites with the same texture batch together (the 604 floor sprites are one draw call).
        DrawArena();
        Sprites.Draw(_art.Crate, CratePosition, CrateSize, Color.White, layer: Layers.Props);
        _scene.Draw(Sprites);
        if (_showColliders)
            _scene.Collision.DrawDebug(Sprites, ColliderLineWidth, Layers.Debug);

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

        foreach (var wall in Walls)
            Sprites.Draw(wall, WallColor, layer: Layers.Floor);
    }

    private static Rect[] BuildWalls()
    {
        var outer = ArenaHalfSize * 2f + new Vector2(WallThickness * 2f);
        float wallY = ArenaHalfSize.Y + WallThickness / 2f;
        float wallX = ArenaHalfSize.X + WallThickness / 2f;
        return
        [
            new Rect(new(0f, wallY), new(outer.X, WallThickness)),  // top
            new Rect(new(0f, -wallY), new(outer.X, WallThickness)), // bottom
            new Rect(new(-wallX, 0f), new(WallThickness, outer.Y)), // left
            new Rect(new(wallX, 0f), new(WallThickness, outer.Y)),  // right
        ];
    }
}
