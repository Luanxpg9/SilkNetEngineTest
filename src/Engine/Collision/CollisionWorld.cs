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
/// Every query tests every collider for now; a broad phase that only tests nearby ones comes later
/// (roadmap step 5 Phase B). Queries fill lists the caller passes in, so nothing allocates while
/// the game runs. Colliders of destroyed entities are ignored at once, even before they leave.
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

    private readonly List<Collider> _colliders = [];

    // Trigger overlaps: last fixed step's and this one's, keyed by the pair's ids, swapped every step.
    private Dictionary<long, (Collider A, Collider B)> _touching = [];
    private Dictionary<long, (Collider A, Collider B)> _touchingNow = [];
    private readonly List<(Collider A, Collider B, bool Began)> _triggerEvents = [];

    /// <summary>How many colliders are in the world.</summary>
    public int Count => _colliders.Count;

    /// <summary>The colliders, in no particular order.</summary>
    public IReadOnlyList<Collider> Colliders => _colliders;

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
    }

    /// <summary>Removes a collider. Removing one that isn't in this world does nothing. No trigger exit is reported for it.</summary>
    public void Remove(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (collider.World != this)
            return;

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
        foreach (var collider in _colliders)
            if (Passes(collider, mask, includeTriggers, ignore: null) && Intersection.Contains(collider.WorldShape, point))
                results.Add(collider);
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with the colliders on <paramref name="mask"/>'s
    /// layers that overlap <paramref name="shape"/>, a shape already placed in the world
    /// (<c>Shape.Circle(2f, center: explosionAt)</c>, say).
    /// </summary>
    public void OverlapShape(in Shape shape, uint mask, List<Collider> results, bool includeTriggers = false)
    {
        results.Clear();
        foreach (var collider in _colliders)
            if (Passes(collider, mask, includeTriggers, ignore: null) && Intersection.Overlap(shape, collider.WorldShape, out _))
                results.Add(collider);
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

        bool found = false;
        float nearest = maxDistance;
        foreach (var collider in _colliders)
        {
            if (!Passes(collider, mask, includeTriggers, ignore))
                continue;
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
            position = PushOut(collider, position, contacts);
        }

        collider.Position = position;
        return position - start;
    }

    // Pushes the shape out of the solids it overlaps at `position`, deepest first. Pushing straight
    // out along the contact normal removes only the part of the move that went into the wall, and
    // the part along it stays: that's the slide.
    private Vector2 PushOut(Collider mover, Vector2 position, List<Contact>? contacts)
    {
        for (int iteration = 0; iteration < MaxPushIterations; iteration++)
        {
            var shape = mover.Shape.At(position);
            Contact deepest = default;
            bool any = false;

            foreach (var other in _colliders)
            {
                if (other == mover || other.IsTrigger || other.IsDead || !mover.Notices(other))
                    continue;
                if (Intersection.Overlap(shape, other.WorldShape, out var contact) && (!any || contact.Depth > deepest.Depth))
                {
                    any = true;
                    deepest = contact with { Other = other };
                }
            }

            if (!any)
                break;
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

        // Every pair once. Colliders spawned by the events below join the list but wait for the next step.
        int count = _colliders.Count;
        for (int i = 0; i < count; i++)
        {
            var a = _colliders[i];
            if (a.IsDead)
                continue;
            for (int j = i + 1; j < count; j++)
            {
                var b = _colliders[j];
                if (b.IsDead || !((a.IsTrigger && a.Notices(b)) || (b.IsTrigger && b.Notices(a))))
                    continue;
                if (!Intersection.Overlap(a.WorldShape, b.WorldShape, out _))
                    continue;

                long key = PairKey(a, b);
                _touchingNow[key] = (a, b);
                if (!_touching.ContainsKey(key))
                    _triggerEvents.Add((a, b, true));
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

    private static bool Passes(Collider collider, uint mask, bool includeTriggers, Collider? ignore) =>
        collider != ignore && !collider.IsDead && (mask & collider.Layer) != 0 && (includeTriggers || !collider.IsTrigger);
}
