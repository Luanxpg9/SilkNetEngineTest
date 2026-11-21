using System.Numerics;

namespace Engine;

/// <summary>
/// Every collider in a scene, and the questions a game asks about them: what overlaps a point or a
/// shape, what a ray hits first, and "move this body, stopping at solid things". It also reports
/// trigger overlaps once per fixed step. Each <see cref="Scene"/> has one (<see cref="Scene.Collision"/>),
/// which adds and removes entities' colliders by itself; colliders without an entity (walls) are
/// added with <see cref="Add"/>.
///
/// It's collision only, not physics: nothing falls, bounces or gets pushed unless the game moves it.
/// It doesn't know about gravity or which way is up, so it suits a top-down game and a side view
/// alike: a platformer adds its own gravity to the move and reads the contact normals to find the
/// ground.
///
/// <b>Only nearby colliders are tested.</b> The world files every collider in a uniform grid of
/// <see cref="CellSize"/> cells (the broad phase), and a query or move only runs exact shape tests
/// against colliders filed in the cells it touches. Whenever a collider moves or changes shape (with
/// <see cref="Move"/>, by setting <see cref="Entity.Position"/>, <see cref="Entity.Teleport"/> or
/// <see cref="Collider.Shape"/>), it's refiled at once, so queries always see it where it is. That
/// also means a world works on its own, without a scene.
///
/// Queries fill lists the caller passes in, so nothing allocates while the game runs (the grid's
/// cell lists are kept and reused, including ones for cells nothing is in any more, which is fine
/// for a level of bounded size). Colliders of destroyed entities are ignored at once, even before
/// they leave.
/// </summary>
public sealed class CollisionWorld
{
    private const int MaxSubsteps = 16;          // a move longer than this many shape-sizes is cut short rather than slowing the game
    private const int MaxPushIterations = 4;     // per substep: enough to settle into a corner between two walls
    private const float Skin = 1e-4f;            // world units pushed past touching, so rounding doesn't leave a body barely inside a wall
    private const int CircleDebugSegments = 32;

    private static readonly Color DebugSolidColor = Color.FromHex("#4CD964");
    private static readonly Color DebugTriggerColor = Color.FromHex("#FFCC00");
    private static readonly Color DebugStaticColor = Color.FromHex("#3FA9F5");

    // Cell coordinates are kept within this, so a position at infinity or far out can't make a loop
    // over cells run forever (a float cast to int saturates at int.MaxValue).
    private const int MaxCell = 1 << 30;

    private readonly List<Collider> _colliders = [];

    // The broad phase: cell (x, y) → the colliders whose bounds touch it, keyed by PackCell. Lists are
    // emptied, never dropped, on a rebuild, so after the first steps nothing is allocated.
    private readonly Dictionary<long, List<Collider>> _cells = [];
    private bool _gridDirty = true;
    private float _cellSize = 2f;
    private int _queryStamp;
    private readonly List<Collider> _candidates = [];        // for queries and moves
    private readonly List<Collider> _triggerCandidates = []; // for the trigger update, which may run queries from its events

    // Trigger overlaps: last fixed step's and this one's, keyed by the pair's ids, swapped every step.
    private Dictionary<long, (Collider A, Collider B)> _touching = [];
    private Dictionary<long, (Collider A, Collider B)> _touchingNow = [];
    private readonly List<(Collider A, Collider B, bool Began)> _triggerEvents = [];

    /// <summary>How many colliders are in the world.</summary>
    public int Count => _colliders.Count;

    /// <summary>The colliders, in no particular order.</summary>
    public IReadOnlyList<Collider> Colliders => _colliders;

    /// <summary>
    /// The width of the broad phase's square cells, in world units (2 by default). About the size of
    /// the common moving things, or a bit larger, works best: much smaller and big colliders are filed
    /// in many cells, much larger and each cell holds many colliders. Changing it refiles every collider
    /// before the next query. It must be above 0.
    /// </summary>
    public float CellSize
    {
        get => _cellSize;
        set
        {
            if (!(value > 0f))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Cells need a size above 0.");
            _cellSize = value;
            _gridDirty = true;
        }
    }

    /// <summary>
    /// Exact shape tests run since <see cref="ResetStats"/>: what the broad phase saves shows up here.
    /// Read it once per frame and reset it, the way <see cref="SpriteBatch.DrawCalls"/> is read.
    /// </summary>
    public int ShapeTests { get; private set; }

    /// <summary>
    /// Sets <see cref="ShapeTests"/> back to 0. Nothing calls it for you: call it once per frame, after
    /// reading the count, so the next reading covers one frame.
    /// </summary>
    public void ResetStats() => ShapeTests = 0;

    /// <summary>
    /// Adds a collider. The scene does this for entities' colliders, so call it for ones that stand on
    /// their own, like walls (set their <see cref="Collider.Position"/> first or after; either works).
    /// </summary>
    public void Add(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (collider.World is not null)
            throw new InvalidOperationException("This collider is already in a collision world.");
        collider.World = this;
        collider.Index = _colliders.Count;
        _colliders.Add(collider);
        if (!_gridDirty)
            File(collider); // otherwise the coming rebuild files it
    }

    /// <summary>Removes a collider. Removing one that isn't in this world does nothing. No trigger exit is reported for it.</summary>
    public void Remove(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (collider.World != this)
            return;
        Unfile(collider);

        // Swap with the last one, so removing doesn't shift the rest of the list.
        int index = collider.Index;
        var last = _colliders[^1];
        _colliders[index] = last;
        last.Index = index;
        _colliders.RemoveAt(_colliders.Count - 1);

        collider.World = null;
        collider.Index = -1;
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with the colliders on <paramref name="mask"/>'s
    /// layers that contain <paramref name="point"/>. Triggers count only with <paramref name="includeTriggers"/>.
    /// </summary>
    public void OverlapPoint(Vector2 point, uint mask, List<Collider> results, bool includeTriggers = false)
    {
        results.Clear();
        Gather(new Rect(point, Vector2.Zero), _candidates);
        foreach (var collider in _candidates)
        {
            if (!Passes(collider, mask, includeTriggers, ignore: null))
                continue;
            ShapeTests++;
            if (Intersection.Contains(collider.WorldShape, point))
                results.Add(collider);
        }
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with the colliders on <paramref name="mask"/>'s
    /// layers that overlap <paramref name="shape"/>, a shape already placed in the world
    /// (<c>Shape.Circle(2f, center: explosionAt)</c>, say).
    /// </summary>
    public void OverlapShape(in Shape shape, uint mask, List<Collider> results, bool includeTriggers = false)
    {
        results.Clear();
        Gather(shape.Bounds, _candidates);
        foreach (var collider in _candidates)
        {
            if (!Passes(collider, mask, includeTriggers, ignore: null))
                continue;
            ShapeTests++;
            if (Intersection.Overlap(shape, collider.WorldShape, out _))
                results.Add(collider);
        }
    }

    /// <summary>
    /// Casts a ray from <paramref name="origin"/> along <paramref name="direction"/> (any length) for up to
    /// <paramref name="maxDistance"/>, and finds the first collider on <paramref name="mask"/>'s layers
    /// it hits. <paramref name="ignore"/> skips one collider, such as the caster's own.
    /// </summary>
    public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, uint mask, out RayHit hit,
        bool includeTriggers = false, Collider? ignore = null)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f)
            return false;
        direction = Vector2.Normalize(direction);

        // The candidates are the colliders in the cells under the ray's bounding box. A diagonal ray
        // collects some cells it doesn't cross, which only costs a few extra tests; a very long one
        // falls back to every collider (see Gather).
        Gather(Rect.FromCorners(origin, origin + direction * maxDistance), _candidates);

        bool found = false;
        float nearest = maxDistance;
        foreach (var collider in _candidates)
        {
            if (!Passes(collider, mask, includeTriggers, ignore))
                continue;
            ShapeTests++;
            if (Intersection.Raycast(collider.WorldShape, origin, direction, nearest, out float distance, out var normal)
                && (!found || distance < nearest))
            {
                found = true;
                nearest = distance;
                hit = new RayHit(collider, origin + direction * distance, normal, distance);
            }
        }
        return found;
    }

    /// <summary>
    /// Moves <paramref name="collider"/> (and its entity) by <paramref name="displacement"/>, stopping at
    /// the solid colliders its <see cref="Collider.Mask"/> notices and sliding along them with the rest
    /// of the move. Returns how far it really moved. <paramref name="contacts"/>, if given, is cleared
    /// and filled with one <see cref="Contact"/> per collider it touched, normals pointing away from
    /// them, toward the mover.
    ///
    /// It works the same with any displacement: walking in a top-down game, or falling and jumping in
    /// a side view, where the game can look for a contact with an upward normal to know it's on the ground.
    /// </summary>
    public Vector2 Move(Collider collider, Vector2 displacement, List<Contact>? contacts = null)
    {
        ArgumentNullException.ThrowIfNull(collider);
        contacts?.Clear();
        var start = collider.Position;
        var position = start;

        // Long moves go in substeps no longer than the shape's smaller half-size, so a fast body
        // doesn't jump past a wall between one test and the next.
        float maxStep = MathF.Min(collider.Shape.HalfSize.X, collider.Shape.HalfSize.Y);
        int substeps = Math.Clamp((int)MathF.Ceiling(displacement.Length() / maxStep), 1, MaxSubsteps);
        var stepMove = displacement / substeps;

        for (int s = 0; s < substeps; s++)
        {
            position += stepMove;
            position = PushOut(collider, position, contacts, staticsOnly: false, out bool settled);

            // Crowded in by moving bodies and a wall, the pushes can take turns and run out of
            // iterations on a push away from a neighbor, leaving the body inside the wall. Walls
            // then win: one more round against colliders without an entity, which never move.
            if (!settled)
                position = PushOut(collider, position, contacts, staticsOnly: true, out _);
        }

        // Setting the position refiles the collider in the grid, so the queries and moves after
        // this one find it where it now is.
        collider.Position = position;
        return position - start;
    }

    // Pushes the shape out of the solids it overlaps at `position`, deepest first. Pushing straight
    // out along the contact normal removes only the part of the move that went into the wall, and
    // the part along it stays: that's the slide. `settled` says whether it ended free of overlaps.
    private Vector2 PushOut(Collider mover, Vector2 position, List<Contact>? contacts, bool staticsOnly, out bool settled)
    {
        settled = false;
        for (int iteration = 0; iteration < MaxPushIterations; iteration++)
        {
            var shape = mover.Shape.At(position);
            Contact deepest = default;
            bool any = false;

            Gather(shape.Bounds, _candidates);
            foreach (var other in _candidates)
            {
                if (other == mover || other.IsTrigger || other.IsDead || !mover.Notices(other)
                    || (staticsOnly && other.Owner is not null))
                    continue;
                ShapeTests++;
                if (Intersection.Overlap(shape, other.WorldShape, out var contact) && (!any || contact.Depth > deepest.Depth))
                {
                    any = true;
                    deepest = contact with { Other = other };
                }
            }

            if (!any)
            {
                settled = true;
                break;
            }
            position += deepest.Normal * (deepest.Depth + Skin);
            if (contacts is not null && !HasContactWith(contacts, deepest.Other!))
                contacts.Add(deepest);
        }
        return position;
    }

    private static bool HasContactWith(List<Contact> contacts, Collider other)
    {
        foreach (var contact in contacts)
            if (contact.Other == other)
                return true;
        return false;
    }

    /// <summary>
    /// Finds which trigger pairs started and stopped overlapping since the last call, and tells both
    /// entities of each pair (<see cref="Entity.TriggerEnter"/>, <see cref="Entity.TriggerExit"/>).
    /// A pair counts when one of them is a trigger whose mask notices the other. The scene calls this
    /// at the end of every fixed step, after all entities have moved.
    /// </summary>
    internal void UpdateTriggers()
    {
        _touchingNow.Clear();
        _triggerEvents.Clear();

        // Every pair needs a trigger in it, so start from each trigger and test only the colliders
        // near it. A pair of two triggers is met from both sides; the key check keeps it to one.
        // Colliders spawned by the events below join the list but wait for the next step.
        int count = _colliders.Count;
        for (int i = 0; i < count; i++)
        {
            var trigger = _colliders[i];
            if (!trigger.IsTrigger || trigger.IsDead)
                continue;

            var shape = trigger.WorldShape;
            Gather(shape.Bounds, _triggerCandidates);
            foreach (var other in _triggerCandidates)
            {
                if (other == trigger || other.IsDead || !(trigger.Notices(other) || (other.IsTrigger && other.Notices(trigger))))
                    continue;
                long key = PairKey(trigger, other);
                if (_touchingNow.ContainsKey(key))
                    continue;
                ShapeTests++;
                if (!Intersection.Overlap(shape, other.WorldShape, out _))
                    continue;

                _touchingNow[key] = (trigger, other);
                if (!_touching.ContainsKey(key))
                    _triggerEvents.Add((trigger, other, true));
            }
        }

        // Pairs that touched last step and don't now have ended, unless one of them has left the game.
        foreach (var (key, pair) in _touching)
            if (!_touchingNow.ContainsKey(key) && pair.A.World == this && pair.B.World == this && !pair.A.IsDead && !pair.B.IsDead)
                _triggerEvents.Add((pair.A, pair.B, false));

        (_touching, _touchingNow) = (_touchingNow, _touching);

        // Events go out after the bookkeeping, so whatever the entities do in them (destroy
        // themselves, spawn more) can't disturb it.
        foreach (var (a, b, began) in _triggerEvents)
        {
            Notify(a.Owner, b, began);
            Notify(b.Owner, a, began);
        }
    }

    private static void Notify(Entity? entity, Collider other, bool began)
    {
        if (entity is null || entity.IsDestroyed)
            return;
        if (began)
            entity.TriggerEnter(other);
        else
            entity.TriggerExit(other);
    }

    private static long PairKey(Collider a, Collider b)
    {
        int low = Math.Min(a.Id, b.Id), high = Math.Max(a.Id, b.Id);
        return ((long)low << 32) | (uint)high;
    }

    /// <summary>
    /// Draws every collider's outline, <paramref name="thickness"/> wide (in world units), for checking
    /// that colliders line up with sprites: solid ones attached to entities in green, triggers in
    /// yellow, and ones standing on their own in blue. Entities' colliders are drawn at the entity's
    /// <see cref="Entity.DrawPosition"/>, so they stay on their sprites while moving. Call it between
    /// <c>sprites.Begin</c> and <c>End</c>, with a <paramref name="layer"/> above everything else.
    /// </summary>
    public void DrawDebug(SpriteBatch sprites, float thickness, int layer = int.MaxValue)
    {
        foreach (var collider in _colliders)
        {
            if (collider.IsDead)
                continue;
            var color = collider.IsTrigger ? DebugTriggerColor
                : collider.Owner is null ? DebugStaticColor
                : DebugSolidColor;
            var shape = collider.Shape.At(collider.Owner?.DrawPosition ?? collider.Position);

            if (shape.Kind == ShapeKind.Circle)
            {
                var previous = shape.Center + new Vector2(shape.Radius, 0f);
                for (int i = 1; i <= CircleDebugSegments; i++)
                {
                    var next = shape.Center + MathHelpers.FromAngle(i * MathF.Tau / CircleDebugSegments) * shape.Radius;
                    sprites.DrawLine(previous, next, thickness, color, layer);
                    previous = next;
                }
            }
            else
            {
                var min = shape.Center - shape.HalfSize;
                var max = shape.Center + shape.HalfSize;
                sprites.DrawLine(new(min.X, min.Y), new(max.X, min.Y), thickness, color, layer);
                sprites.DrawLine(new(max.X, min.Y), new(max.X, max.Y), thickness, color, layer);
                sprites.DrawLine(new(max.X, max.Y), new(min.X, max.Y), thickness, color, layer);
                sprites.DrawLine(new(min.X, max.Y), new(min.X, min.Y), thickness, color, layer);
            }
        }
    }

    // Called by Entity.Position, Collider.Position and Collider.Shape whenever a collider in this world
    // moves or changes shape: refile it, so queries find it where it now is. Before the first build
    // (or after CellSize changes) the coming rebuild files everything anyway.
    internal void Moved(Collider collider)
    {
        if (collider.World == this && !_gridDirty && collider.InGrid)
            Refile(collider);
    }

    // Fills `into` with each collider filed in a cell that `bounds` touches, once each. A collider
    // spanning several cells is in several lists; the stamp marks it as already collected.
    private void Gather(Rect bounds, List<Collider> into)
    {
        into.Clear();
        if (_gridDirty)
            Rebuild();

        var (minX, minY) = CellOf(bounds.Min);
        var (maxX, maxY) = CellOf(bounds.Max);

        // An area covering more cells than there are colliders (a very long ray, a huge overlap
        // shape) is cheaper to answer by taking every collider than by looking up each cell.
        if ((maxX - minX + 1L) * (maxY - minY + 1L) > _colliders.Count)
        {
            GatherAll(into);
            return;
        }

        // New colliders start at stamp 0, so the stamp skips 0 when it wraps around.
        if (++_queryStamp == 0)
            _queryStamp = 1;
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                if (!_cells.TryGetValue(PackCell(x, y), out var cell))
                    continue;
                foreach (var collider in cell)
                {
                    if (collider.QueryStamp == _queryStamp)
                        continue;
                    collider.QueryStamp = _queryStamp;
                    into.Add(collider);
                }
            }
    }

    private void GatherAll(List<Collider> into)
    {
        into.Clear();
        into.AddRange(_colliders);
    }

    // Empties every cell (keeping the lists for reuse) and files every collider again.
    private void Rebuild()
    {
        foreach (var cell in _cells.Values)
            cell.Clear();
        foreach (var collider in _colliders)
        {
            collider.InGrid = false;
            File(collider);
        }
        _gridDirty = false;
    }

    // Adds the collider to every cell its bounds touch, and remembers that range.
    private void File(Collider collider)
    {
        var bounds = collider.WorldShape.Bounds;
        var (minX, minY) = CellOf(bounds.Min);
        var (maxX, maxY) = CellOf(bounds.Max);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                long key = PackCell(x, y);
                if (!_cells.TryGetValue(key, out var cell))
                    _cells[key] = cell = [];
                cell.Add(collider);
            }

        collider.InGrid = true;
        (collider.CellMinX, collider.CellMinY, collider.CellMaxX, collider.CellMaxY) = (minX, minY, maxX, maxY);
    }

    private void Unfile(Collider collider)
    {
        if (!collider.InGrid)
            return;
        for (int x = collider.CellMinX; x <= collider.CellMaxX; x++)
            for (int y = collider.CellMinY; y <= collider.CellMaxY; y++)
                if (_cells.TryGetValue(PackCell(x, y), out var cell))
                    cell.Remove(collider);
        collider.InGrid = false;
    }

    // After a move: only touch the lists if the collider now spans different cells, which for a
    // small body walking a few centimeters per step is rare.
    private void Refile(Collider collider)
    {
        var bounds = collider.WorldShape.Bounds;
        var (minX, minY) = CellOf(bounds.Min);
        var (maxX, maxY) = CellOf(bounds.Max);
        if (collider.InGrid && minX == collider.CellMinX && minY == collider.CellMinY
            && maxX == collider.CellMaxX && maxY == collider.CellMaxY)
            return;
        Unfile(collider);
        File(collider);
    }

    private (int X, int Y) CellOf(Vector2 point) =>
        (Math.Clamp((int)MathF.Floor(point.X / _cellSize), -MaxCell, MaxCell),
         Math.Clamp((int)MathF.Floor(point.Y / _cellSize), -MaxCell, MaxCell));

    private static long PackCell(int x, int y) => ((long)x << 32) | (uint)y;

    private static bool Passes(Collider collider, uint mask, bool includeTriggers, Collider? ignore) =>
        collider != ignore && !collider.IsDead && (mask & collider.Layer) != 0 && (includeTriggers || !collider.IsTrigger);
}
