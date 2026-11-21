using System.Numerics;

namespace Engine;

/// <summary>
/// A shape in a <see cref="CollisionWorld"/>: either attached to an <see cref="Entity"/> (set its
/// <see cref="Entity.Collider"/>; the scene adds it to its world when the entity spawns and removes
/// it when the entity goes), or standing on its own, like a wall: set its <see cref="Position"/> and
/// <see cref="CollisionWorld.Add"/> it.
///
/// <b>Solid or trigger.</b> Solid colliders block <see cref="CollisionWorld.Move"/>. Triggers never
/// block; they report overlaps as events instead (see <see cref="Entity.TriggerEnter"/>).
///
/// <b>Layers and masks.</b> <see cref="Layer"/> says what the collider is and <see cref="Mask"/> what
/// it notices, both as bits, so a game can have up to 32 kinds of thing and names them itself
/// (<c>const uint Player = 1 &lt;&lt; 0, Wall = 1 &lt;&lt; 1</c>). A collider notices another when its
/// mask has a bit of the other's layer. That's one-way: a moving player whose mask includes walls is
/// stopped by them, while the walls (mask 0) notice nothing.
/// </summary>
public sealed class Collider(Shape shape, uint layer, uint mask = 0u, bool isTrigger = false)
{
    private static int _nextId;
    private Vector2 _position;

    /// <summary>
    /// The shape, with its <see cref="Shape.Center"/> as an offset from <see cref="Position"/>. A new
    /// shape is seen by the world's queries at once.
    /// </summary>
    public Shape Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            World?.Moved(this);
        }
    }

    private Shape _shape = shape;

    public uint Layer { get; set; } = layer;
    public uint Mask { get; set; } = mask;
    public bool IsTrigger { get; set; } = isTrigger;

    /// <summary>The entity it's attached to, or null for one standing on its own.</summary>
    public Entity? Owner { get; internal set; }

    /// <summary>The world it's in, or null.</summary>
    public CollisionWorld? World { get; internal set; }

    /// <summary>
    /// Where the collider is: its owner's <see cref="Entity.Position"/>, or its own for a collider
    /// without an owner. Setting it on an attached collider moves the entity. Either way its world's
    /// queries find it at the new place at once. Setting it is a jump; <see cref="CollisionWorld.Move"/>
    /// is the move that stops at solid things.
    /// </summary>
    public Vector2 Position
    {
        get => Owner?.Position ?? _position;
        set
        {
            if (Owner is not null)
            {
                Owner.Position = value; // the entity tells the world
            }
            else
            {
                _position = value;
                World?.Moved(this);
            }
        }
    }

    /// <summary>The shape placed in the world, ready for <see cref="Intersection"/>.</summary>
    public Shape WorldShape => Shape.At(Position);

    /// <summary>Whether this collider's mask includes <paramref name="other"/>'s layer.</summary>
    public bool Notices(Collider other) => (Mask & other.Layer) != 0;

    // Gone from the game: its entity was destroyed (it leaves the world when the scene's pass ends).
    internal bool IsDead => Owner is { IsDestroyed: true };

    internal int Id { get; } = Interlocked.Increment(ref _nextId);
    internal int Index = -1; // where it sits in its world's list, for removing without a search

    // The world's grid bookkeeping: the range of cells it was filed under, and the last query
    // that already collected it (so one spanning several cells is tested once).
    internal bool InGrid;
    internal int CellMinX, CellMinY, CellMaxX, CellMaxY;
    internal int QueryStamp;
}
