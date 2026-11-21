namespace Engine;

/// <summary>
/// Owns a game's entities and runs them: a game creates one in <c>Load</c>, spawns entities into
/// it, and calls <see cref="FixedUpdate"/>, <see cref="Update"/> and <see cref="Draw"/> from its own
/// methods of the same names. Entities run in the order they were spawned.
///
/// <b>Changes during a pass.</b> An entity may spawn others (a bullet) or destroy itself or others
/// while the scene is running them. Removing from or adding to the list being walked would skip or
/// repeat entities, so during a pass new entities wait in a queue and destroyed ones are only
/// flagged; both are applied when the pass ends. A destroyed entity is skipped at once: it isn't
/// updated, drawn or found again, even in the rest of the same pass. Outside a pass (in the game's
/// <c>Load</c>, say) changes apply immediately.
///
/// Nothing here allocates while the game runs, apart from the lists growing to a new largest size.
/// </summary>
public sealed class Scene(Game game)
{
    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _spawnQueue = [];
    private bool _inPass;
    private bool _anyDestroyed; // some entity in _entities is flagged and waiting to be removed

    /// <summary>The game this scene belongs to. Entities reach it through their own <c>Game</c>.</summary>
    public Game Game { get; } = game;

    /// <summary>How many entities the scene runs. Spawns and destroys made during a pass count once it ends.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Adds <paramref name="entity"/> to the scene and returns it. Its <see cref="Entity.PreviousPosition"/>
    /// is set to its <see cref="Entity.Position"/>, so it appears where it is instead of sliding in.
    /// During a pass it joins when the pass ends; until then the finds already see it, but it isn't run.
    /// A destroyed entity can't be spawned again (code still holding it relies on it staying dead):
    /// create a new one instead.
    /// </summary>
    public T Spawn<T>(T entity) where T : Entity
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.IsDestroyed)
            throw new InvalidOperationException($"This {entity.GetType().Name} was destroyed; spawn a new one instead.");
        if (entity.Scene is not null)
            throw new InvalidOperationException($"This {entity.GetType().Name} is already in a scene.");

        entity.Scene = this;
        entity.PreviousPosition = entity.Position;
        _spawnQueue.Add(entity);
        if (!_inPass)
            ApplyChanges();
        return entity;
    }

    /// <summary>
    /// Destroys <paramref name="entity"/>: it's skipped from now on and removed when the current pass
    /// ends (at once outside a pass). Destroying it again, or after it's gone, does nothing.
    /// </summary>
    public void Destroy(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Scene is null || entity.IsDestroyed)
            return;
        if (entity.Scene != this)
            throw new InvalidOperationException($"This {entity.GetType().Name} belongs to another scene.");

        entity.IsDestroyed = true;
        _anyDestroyed = true;
        if (!_inPass)
            ApplyChanges();
    }

    /// <summary>
    /// One fixed step: first every entity's <see cref="Entity.PreviousPosition"/> becomes its
    /// <see cref="Entity.Position"/>, then each runs its <see cref="Entity.FixedUpdate"/>. Anything
    /// the game moves after this call, in the same fixed step, is still part of the smooth blend.
    /// </summary>
    public void FixedUpdate(float step)
    {
        foreach (var entity in _entities)
            entity.PreviousPosition = entity.Position;

        BeginPass();
        try
        {
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].IsDestroyed)
                    _entities[i].FixedUpdate(step);
        }
        finally { EndPass(); }
    }

    /// <summary>Runs every entity's <see cref="Entity.Update"/>.</summary>
    public void Update(float deltaTime)
    {
        BeginPass();
        try
        {
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].IsDestroyed)
                    _entities[i].Update(deltaTime);
        }
        finally { EndPass(); }
    }

    /// <summary>Runs every entity's <see cref="Entity.Draw"/>. Call it between <c>sprites.Begin</c> and <c>End</c>.</summary>
    public void Draw(SpriteBatch sprites)
    {
        BeginPass();
        try
        {
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].IsDestroyed)
                    _entities[i].Draw(sprites);
        }
        finally { EndPass(); }
    }

    /// <summary>The first entity of type <typeparamref name="T"/> (or derived from it), or null.</summary>
    public T? FindFirst<T>() where T : Entity
    {
        foreach (var entity in _entities)
            if (entity is T match && !entity.IsDestroyed)
                return match;
        foreach (var entity in _spawnQueue)
            if (entity is T match && !entity.IsDestroyed)
                return match;
        return null;
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with every entity of type <typeparamref name="T"/>.
    /// Pass in a list kept between frames, so finding doesn't allocate.
    /// </summary>
    public void FindAll<T>(List<T> results) where T : Entity
    {
        results.Clear();
        foreach (var entity in _entities)
            if (entity is T match && !entity.IsDestroyed)
                results.Add(match);
        foreach (var entity in _spawnQueue)
            if (entity is T match && !entity.IsDestroyed)
                results.Add(match);
    }

    /// <summary>The first entity whose <see cref="Entity.Tag"/> is <paramref name="tag"/> (case-sensitive), or null.</summary>
    public Entity? FindFirstWithTag(string tag)
    {
        foreach (var entity in _entities)
            if (entity.Tag == tag && !entity.IsDestroyed)
                return entity;
        foreach (var entity in _spawnQueue)
            if (entity.Tag == tag && !entity.IsDestroyed)
                return entity;
        return null;
    }

    /// <summary>Fills <paramref name="results"/> (cleared first) with every entity tagged <paramref name="tag"/>.</summary>
    public void FindAllWithTag(string tag, List<Entity> results)
    {
        results.Clear();
        foreach (var entity in _entities)
            if (entity.Tag == tag && !entity.IsDestroyed)
                results.Add(entity);
        foreach (var entity in _spawnQueue)
            if (entity.Tag == tag && !entity.IsDestroyed)
                results.Add(entity);
    }

    private void BeginPass()
    {
        if (_inPass)
            throw new InvalidOperationException("A scene pass started inside another one (an entity called its own scene's FixedUpdate, Update or Draw).");
        _inPass = true;
    }

    private void EndPass()
    {
        _inPass = false;
        ApplyChanges();
    }

    // Removes destroyed entities, then adds queued ones that weren't destroyed while waiting.
    // RemoveAll keeps the order of the rest, so entities keep running in spawn order.
    private void ApplyChanges()
    {
        if (_anyDestroyed)
        {
            _entities.RemoveAll(static entity =>
            {
                if (!entity.IsDestroyed)
                    return false;
                entity.Scene = null;
                return true;
            });
            _anyDestroyed = false;
        }

        foreach (var entity in _spawnQueue)
        {
            if (entity.IsDestroyed)
                entity.Scene = null;
            else
                _entities.Add(entity);
        }
        _spawnQueue.Clear();
        Count = _entities.Count;
    }
}
