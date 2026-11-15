using System.Numerics;
using System.Runtime.InteropServices;

namespace Engine;

/// <summary>
/// Rigid-body physics for the scene's boxes: gravity, collisions with rotation, and friction.
/// Fixed bodies never move but still block falling ones, and falling bodies can land on, tip off
/// and push each other.
///
/// Each step, in the style of Box2D Lite (a small teaching version of the Box2D engine):
/// <list type="number">
/// <item>Gravity changes every falling body's velocity.</item>
/// <item><see cref="Collision"/> finds the contact points between every pair of bodies that touch.</item>
/// <item>A sequential impulse solver runs several passes over the contacts. Each contact applies a
/// small push at its point, so it changes both the velocity and the spin of the bodies. Pushes along
/// the normal stop the bodies sinking into each other; pushes along the surface are friction.</item>
/// <item>Bodies move and turn with their corrected velocities.</item>
/// </list>
/// The simulation runs in fixed steps, independent of frame rate, which also keeps fast-falling
/// bodies from skipping through thin platforms.
/// </summary>
public sealed class Physics
{
    public static readonly Vector2 Gravity = new(0f, -9.81f); // world units per second squared; one unit is one meter
    private const float TimeStep = 1f / 240f;    // seconds per simulation step
    private const int MaxStepsPerFrame = 16;     // after a long hitch, drop time instead of trying to catch up
    private const int SolverIterations = 10;     // more passes = stiffer, more accurate contacts
    private const float Friction = 0.5f;         // 0 = ice; higher = grippier, so boxes tip instead of sliding
    private const float CorrectionFactor = 0.2f; // fraction of the overlap removed each step
    private const float AllowedOverlap = 0.005f; // overlap left alone, so resting contacts don't flicker on and off
    private const float MaxCorrectionSpeed = 2f; // caps the push-out when a body is placed deep inside another
    private const float RemoveBelowY = -50f;     // bodies that fall off the world are removed

    private const float SameContactDistance = 0.02f; // a contact this close to last step's, between the same bodies, is the same contact

    private float _accumulator; // frame time not yet simulated, always less than one step
    private List<Contact> _contacts = [];
    private List<Contact> _previousContacts = [];

    public void Update(List<Body> bodies, float deltaTime)
    {
        _accumulator += deltaTime;

        int steps = 0;
        while (_accumulator >= TimeStep && steps < MaxStepsPerFrame)
        {
            Step(bodies);
            _accumulator -= TimeStep;
            steps++;
        }
        if (_accumulator >= TimeStep)
            _accumulator = 0f;

        bodies.RemoveAll(b => b.Falls && b.Position.Y < RemoveBelowY);
    }

    private void Step(List<Body> bodies)
    {
        foreach (var body in bodies)
            if (body.Falls)
                body.Velocity += Gravity * TimeStep;

        (_previousContacts, _contacts) = (_contacts, _previousContacts);
        _contacts.Clear();
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
                if (bodies[i].Falls || bodies[j].Falls)
                    Collision.BoxBox(bodies[i], bodies[j], _contacts);

        // Contacts are structs, so they're updated in place through a span.
        var contacts = CollectionsMarshal.AsSpan(_contacts);
        foreach (ref var contact in contacts)
        {
            WarmStart(ref contact);
            Prepare(ref contact);
        }
        for (int iteration = 0; iteration < SolverIterations; iteration++)
            foreach (ref var contact in contacts)
                Solve(ref contact);

        // Semi-implicit Euler: velocities were updated first, now move with them.
        foreach (var body in bodies)
        {
            if (!body.Falls)
                continue;
            body.Position += body.Velocity * TimeStep;
            body.Angle += body.AngularVelocity * TimeStep;
        }
    }

    // Warm starting: a resting contact needs nearly the same push every step, so start from last
    // step's totals instead of zero. Without this, the solver rebuilds the push from scratch each
    // step, always visiting contacts in the same order, and that small bias makes stacked boxes
    // slowly creep and turn.
    private void WarmStart(ref Contact c)
    {
        foreach (var previous in _previousContacts)
        {
            if (previous.A == c.A && previous.B == c.B &&
                Vector2.DistanceSquared(previous.Point, c.Point) < SameContactDistance * SameContactDistance)
            {
                c.NormalImpulse = previous.NormalImpulse;
                c.TangentImpulse = previous.TangentImpulse;
                return;
            }
        }
    }

    // Values that stay the same across all solver passes this step.
    private static void Prepare(ref Contact c)
    {
        c.OffsetA = c.Point - c.A.Position;
        c.OffsetB = c.Point - c.B.Position;

        // A push at an offset from the center both moves and spins a body. These "effective masses"
        // say how much relative speed at the contact point a unit push removes, including the spin.
        Vector2 tangent = Tangent(c.Normal);
        c.NormalMass = 1f / EffectiveInverseMass(c, c.Normal);
        c.TangentMass = 1f / EffectiveInverseMass(c, tangent);

        // Remove overlap gradually by asking for a little extra separating speed (Baumgarte stabilization).
        c.Bias = MathF.Min(CorrectionFactor / TimeStep * MathF.Max(0f, c.Depth - AllowedOverlap), MaxCorrectionSpeed);

        // Re-apply the warm-start totals (zero for a new contact), so the passes below only correct them.
        Apply(c, c.Normal * c.NormalImpulse + tangent * c.TangentImpulse);
    }

    private static void Solve(ref Contact c)
    {
        Vector2 tangent = Tangent(c.Normal);

        // Normal: push the bodies apart just enough that they stop approaching at this point.
        // The running total can only push, never pull (>= 0), so each pass adjusts by the difference.
        float approachSpeed = Vector2.Dot(RelativeVelocity(c), c.Normal);
        float impulse = c.NormalMass * (-approachSpeed + c.Bias);
        float total = MathF.Max(c.NormalImpulse + impulse, 0f);
        Apply(c, c.Normal * (total - c.NormalImpulse));
        c.NormalImpulse = total;

        // Friction: resist sliding along the surface, up to Friction times how hard the bodies press together.
        float slideSpeed = Vector2.Dot(RelativeVelocity(c), tangent);
        float maxFriction = Friction * c.NormalImpulse;
        total = Math.Clamp(c.TangentImpulse - c.TangentMass * slideSpeed, -maxFriction, maxFriction);
        Apply(c, tangent * (total - c.TangentImpulse));
        c.TangentImpulse = total;
    }

    // Velocity of B's contact point relative to A's. A spinning body's point moves at Velocity + ω × offset.
    private static Vector2 RelativeVelocity(in Contact c) =>
        c.B.Velocity + Cross(c.B.AngularVelocity, c.OffsetB) - c.A.Velocity - Cross(c.A.AngularVelocity, c.OffsetA);

    // Equal and opposite: impulse pushes B along it and A against it, at the contact point.
    private static void Apply(in Contact c, Vector2 impulse)
    {
        c.A.Velocity -= impulse * c.A.InverseMass;
        c.A.AngularVelocity -= Cross(c.OffsetA, impulse) * c.A.InverseInertia;
        c.B.Velocity += impulse * c.B.InverseMass;
        c.B.AngularVelocity += Cross(c.OffsetB, impulse) * c.B.InverseInertia;
    }

    private static float EffectiveInverseMass(in Contact c, Vector2 direction)
    {
        float armA = Cross(c.OffsetA, direction);
        float armB = Cross(c.OffsetB, direction);
        return c.A.InverseMass + c.B.InverseMass + c.A.InverseInertia * armA * armA + c.B.InverseInertia * armB * armB;
    }

    private static Vector2 Tangent(Vector2 normal) => new(normal.Y, -normal.X);

    // 2D cross products: vector × vector gives the turning amount (a scalar);
    // scalar × vector gives the velocity of a point spinning at that rate.
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static Vector2 Cross(float w, Vector2 r) => new(-w * r.Y, w * r.X);
}
