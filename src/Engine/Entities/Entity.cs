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

    /// <summary>Simulation at a constant rate, run by <see cref="Scene.FixedUpdate"/>. Move here.</summary>
    protected internal virtual void FixedUpdate(float step) { }

    /// <summary>Once per frame, run by <see cref="Scene.Update"/>, with the real time since the last frame.</summary>
    protected internal virtual void Update(float deltaTime) { }

    /// <summary>Once per frame, run by <see cref="Scene.Draw"/>, between the batch's Begin and End. Draw at <see cref="DrawPosition"/>.</summary>
    protected internal virtual void Draw(SpriteBatch sprites) { }
}
