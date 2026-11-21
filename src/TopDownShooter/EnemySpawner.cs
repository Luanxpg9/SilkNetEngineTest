using System.Numerics;
using Engine;
using Silk.NET.Input;

/// <summary>
/// An entity with nothing to draw: every <see cref="Interval"/> seconds it spawns an enemy at a
/// random spot along the walls, away from the player, until <see cref="MaxEnemies"/> are alive.
/// <c>K</c> removes every enemy at once.
///
/// It's here to exercise the scene before bullets do (roadmap step 4 Phase B): it spawns and destroys
/// other entities in the middle of the scene's fixed step, and uses both ways of finding them, by
/// type (to count enemies) and by tag (to remove them).
/// </summary>
sealed class EnemySpawner(Art art, Vector2 arenaHalfSize) : Entity
{
    private const float Interval = 1.5f;              // seconds between spawns
    private const int MaxEnemies = 8;
    private const float EdgeInset = 1f;               // world units in from the walls
    private const float MinDistanceFromPlayer = 6f;   // world units, so enemies don't appear on top of the player
    private const int SpawnTries = 8;                 // random spots tried before settling for the last one

    // Kept between steps, so finding doesn't allocate.
    private readonly List<Enemy> _enemies = [];
    private readonly List<Entity> _tagged = [];
    private float _timer;

    protected override void FixedUpdate(float step)
    {
        var scene = Scene!;

        // In FixedUpdate, a press reaches exactly one fixed step, so this runs once per press.
        if (Game.Input.WasPressed(Key.K))
        {
            scene.FindAllWithTag(Enemy.TagName, _tagged);
            foreach (var enemy in _tagged)
                enemy.Destroy(); // safe mid-pass: they're skipped at once and removed when the step ends
            Log.Info($"Removed {_tagged.Count} enemies");
        }

        _timer += step;
        if (_timer < Interval)
            return;
        _timer -= Interval;

        scene.FindAll(_enemies);
        if (_enemies.Count >= MaxEnemies)
            return;

        var playerAt = scene.FindFirst<Player>()?.Position ?? Vector2.Zero;
        scene.Spawn(new Enemy(art, PickSpawnPoint(playerAt))); // joins the scene when this step ends
    }

    // A random point on a rectangle just inside the walls, preferring ones far from the player.
    private Vector2 PickSpawnPoint(Vector2 playerAt)
    {
        var half = arenaHalfSize - new Vector2(EdgeInset);
        var point = Vector2.Zero;
        for (int i = 0; i < SpawnTries; i++)
        {
            float along = Random.Shared.NextSingle() * 2f - 1f; // -1..1 along the chosen wall
            point = Random.Shared.Next(4) switch
            {
                0 => new Vector2(along * half.X, half.Y),   // top
                1 => new Vector2(along * half.X, -half.Y),  // bottom
                2 => new Vector2(-half.X, along * half.Y),  // left
                _ => new Vector2(half.X, along * half.Y),   // right
            };
            if (Vector2.Distance(point, playerAt) >= MinDistanceFromPlayer)
                break;
        }
        return point;
    }
}
