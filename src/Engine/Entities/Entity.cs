using System.Numerics;

namespace Engine;

/// <summary>
/// A game object: the player, an enemy, a bullet. A game derives from it, overrides the methods it
/// needs, and adds it to a <see cref="Engine.Scene"/> with <see cref="Scene.Spawn"/>; the scene then
/// runs it every fixed step and frame, and draws it.
///
/// <b>Smooth motion.</b> Movement happens in <see cref="FixedUpdate"/>, 60 times per second, but a
/// fast screen draws more often than that. So an entity keeps two positions: <see cref="Position"/>,
/// after the latest fixed step, and <see cref="PreviousPosition"/>, after the step before it. The
/// scene copies one into the other for every entity at the start of each fixed step, before any of
/// them moves, so everything that moves an entity during the step is part of the same blend.
/// <see cref="DrawPosition"/> blends between the two, so motion looks smooth at any refresh rate.
/// Spawning and <see cref="Teleport"/> set both, so a new entity doesn't slide in from somewhere else.
/// </summary>
public abstract class Entity
{
    protected Entity() { }

    protected Entity(Vector2 position) => Position = PreviousPosition = position;

    /// <summary>Where the entity is after the latest fixed step, in world units.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Where the entity was after the fixed step before the latest one.</summary>
    public Vector2 PreviousPosition { get; set; }

    /// <summary>An optional name for finding entities, such as "enemy" (see <see cref="Scene.FindFirstWithTag"/>).</summary>
    public string? Tag { get; set; }

    /// <summary>The scene the entity is in, or null before it's spawned and after it's removed.</summary>
    public Scene? Scene { get; internal set; }

    private Collider? _collider;

    /// <summary>
    /// The entity's collider, or null for none. It moves with the entity. The scene adds it to its
    /// <see cref="Scene.Collision"/> world when the entity spawns and removes it when the entity is
    /// destroyed; swapping it while the entity is in a scene swaps it in the world too.
    /// </summary>
    public Collider? Collider
    {
        get => _collider;
        set
        {
            if (value == _collider)
                return;
            if (value is not null && (value.Owner is not null || value.World is not null))
                throw new InvalidOperationException("This collider already belongs to an entity or a collision world.");

            if (_collider is not null)
            {
                _collider.World?.Remove(_collider);
                _collider.Owner = null;
            }
            _collider = value;
            if (value is not null)
            {
                value.Owner = this;
                if (Scene is not null && !IsDestroyed)
                    Scene.Collision.Add(value);
            }
        }
    }

    /// <summary>
    /// True once <see cref="Destroy"/> has been called, and for good: a destroyed entity can't be
    /// spawned again, so code holding on to one (a homing target, say) can trust it. The scene stops
    /// updating, drawing and finding the entity at once, and removes it at the end of the current pass.
    /// </summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>
    /// Where to draw the entity this frame: between <see cref="PreviousPosition"/> and
    /// <see cref="Position"/>, by how far the clock is into the next fixed step
    /// (<see cref="Engine.Game.FixedStepAlpha"/>). So it's drawn slightly in the past, at most one step.
    /// </summary>
    public Vector2 DrawPosition => Scene is null
        ? Position
        : Vector2.Lerp(PreviousPosition, Position, Scene.Game.FixedStepAlpha);

    /// <summary>The game the entity's scene belongs to, for <see cref="Engine.Game.Input"/> and the like.</summary>
    protected Game Game => Scene?.Game
        ?? throw new InvalidOperationException($"{GetType().Name} isn't in a scene, so it has no Game.");

    /// <summary>Moves the entity instantly, with no blending from the old spot (a respawn, say).</summary>
    public void Teleport(Vector2 to) => Position = PreviousPosition = to;

    /// <summary>Removes the entity from its scene (see <see cref="Scene.Destroy"/>).</summary>
    public void Destroy() => Scene?.Destroy(this);

    /// <summary>
    /// Moves by <paramref name="displacement"/>, stopping at solid colliders this entity's
    /// <see cref="Collider"/> notices and sliding along them (see <see cref="CollisionWorld.Move"/>).
    /// Returns how far it really moved, and fills <paramref name="contacts"/> if given. Without a
    /// collider in a world, it just moves. Call it in <see cref="FixedUpdate"/>.
    /// </summary>
    protected Vector2 MoveAndSlide(Vector2 displacement, List<Contact>? contacts = null)
    {
        if (_collider?.World is { } world)
            return world.Move(_collider, displacement, contacts);

        contacts?.Clear();
        Position += displacement;
        return displacement;
    }

    /// <summary>Simulation at a constant rate, run by <see cref="Scene.FixedUpdate"/>. Move here.</summary>
    protected internal virtual void FixedUpdate(float step) { }

    /// <summary>Once per frame, run by <see cref="Scene.Update"/>, with the real time since the last frame.</summary>
    protected internal virtual void Update(float deltaTime) { }

    /// <summary>Once per frame, run by <see cref="Scene.Draw"/>, between the batch's Begin and End. Draw at <see cref="DrawPosition"/>.</summary>
    protected internal virtual void Draw(SpriteBatch sprites) { }

    /// <summary>
    /// This entity's collider started overlapping <paramref name="other"/>, and one of the two is a
    /// trigger that notices the other. Both entities of the pair are told. Called at the end of the
    /// fixed step, after every entity has moved; destroying or spawning here is safe.
    /// </summary>
    protected internal virtual void TriggerEnter(Collider other) { }

    /// <summary>The overlap reported by <see cref="TriggerEnter"/> has ended. Not called when one of the two is removed.</summary>
    protected internal virtual void TriggerExit(Collider other) { }
}
