using System.Numerics;

/// <summary>
/// One point where two bodies touch. <see cref="Normal"/> points from <see cref="A"/> to
/// <see cref="B"/>, and <see cref="Depth"/> is how far they overlap along it. The remaining fields
/// belong to the solver in <see cref="Physics"/>.
/// </summary>
struct Contact
{
    public Body A, B;
    public Vector2 Point, Normal;
    public float Depth;

    public Vector2 OffsetA, OffsetB;        // from each body's center to the contact point
    public float NormalMass, TangentMass;   // how much an impulse changes the relative speed, inverted
    public float Bias;                      // extra separating speed that removes overlap
    public float NormalImpulse, TangentImpulse; // totals applied so far this step
}

/// <summary>
/// Contacts between two rotated boxes, found with the separating axis theorem (SAT) and clipping.
/// <list type="number">
/// <item>Two convex shapes don't overlap if there's an axis where their projections don't overlap.
/// For boxes, only the 4 face directions need testing (2 per box). If every axis overlaps,
/// the one with the least overlap is the collision normal.</item>
/// <item>The box owning that axis is the reference; its face pointing at the other box is the
/// reference face. On the other box, the face most opposed to the normal is the incident face.</item>
/// <item>The incident face is clipped to the reference face's width, and the clipped ends that
/// are behind the reference face become the contacts (0, 1 or 2 of them).</item>
/// </list>
/// Two contacts for a box lying flat is what lets it rest without rocking. One contact off to the
/// side of the center is what makes it tip.
/// </summary>
static class Collision
{
    public static void BoxBox(Body a, Body b, List<Contact> contacts)
    {
        // 1. Least-overlap axis on each box. A positive separation means a gap: no contact.
        var (separationA, axisA) = LeastOverlap(a, b);
        if (separationA > 0f)
            return;
        var (separationB, axisB) = LeastOverlap(b, a);
        if (separationB > 0f)
            return;

        // Prefer a's face unless b's is clearly better. Without this tolerance, two nearly equal
        // choices can swap every step and make resting boxes jitter.
        bool referenceIsA = separationB <= 0.95f * separationA + 0.001f;
        var (reference, incident, axis) = referenceIsA ? (a, b, axisA) : (b, a, axisB);

        // 2. Reference face: the face of the reference box that points toward the incident box.
        Vector2 normal = Vector2.Dot(incident.Position - reference.Position, axis) >= 0f ? axis : -axis;
        Vector2 referenceHalf = reference.Size / 2f;
        bool alongX = MathF.Abs(Vector2.Dot(axis, reference.AxisX)) > 0.5f;
        float faceDistance = alongX ? referenceHalf.X : referenceHalf.Y; // from center to the face
        Vector2 side = alongX ? reference.AxisY : reference.AxisX;       // along the face
        float sideHalf = alongX ? referenceHalf.Y : referenceHalf.X;     // half the face's length
        Vector2 faceCenter = reference.Position + normal * faceDistance;

        //    Incident face: the face of the other box whose outward normal is most opposed to ours.
        Vector2 incidentHalf = incident.Size / 2f;
        float dotX = Vector2.Dot(normal, incident.AxisX);
        float dotY = Vector2.Dot(normal, incident.AxisY);
        Vector2 incidentNormal, incidentSide;
        float incidentDistance, incidentSideHalf;
        if (MathF.Abs(dotX) > MathF.Abs(dotY))
        {
            incidentNormal = dotX > 0f ? -incident.AxisX : incident.AxisX;
            (incidentDistance, incidentSide, incidentSideHalf) = (incidentHalf.X, incident.AxisY, incidentHalf.Y);
        }
        else
        {
            incidentNormal = dotY > 0f ? -incident.AxisY : incident.AxisY;
            (incidentDistance, incidentSide, incidentSideHalf) = (incidentHalf.Y, incident.AxisX, incidentHalf.X);
        }
        Vector2 incidentCenter = incident.Position + incidentNormal * incidentDistance;
        Vector2 v1 = incidentCenter + incidentSide * incidentSideHalf;
        Vector2 v2 = incidentCenter - incidentSide * incidentSideHalf;

        // 3. Clip the incident face to the two ends of the reference face.
        float sideOffset = Vector2.Dot(side, faceCenter);
        if (!ClipSegment(ref v1, ref v2, side, sideOffset + sideHalf) ||
            !ClipSegment(ref v1, ref v2, -side, -sideOffset + sideHalf))
            return;

        //    Keep the ends that are behind the reference face. The contact point sits halfway
        //    between the incident box's surface and the reference face.
        foreach (var v in (ReadOnlySpan<Vector2>)[v1, v2])
        {
            float depth = -Vector2.Dot(v - faceCenter, normal);
            if (depth >= 0f)
                contacts.Add(new Contact
                {
                    A = reference,
                    B = incident,
                    Point = v + normal * (depth / 2f),
                    Normal = normal,
                    Depth = depth,
                });
        }
    }

    // Tests box's two face directions against other. Returns the larger separation
    // (negative = overlap, so larger means less overlap) and the axis it was found on.
    private static (float Separation, Vector2 Axis) LeastOverlap(Body box, Body other)
    {
        Vector2 offset = other.Position - box.Position;
        Vector2 half = box.Size / 2f;

        float separationX = MathF.Abs(Vector2.Dot(offset, box.AxisX)) - half.X - ProjectedRadius(other, box.AxisX);
        float separationY = MathF.Abs(Vector2.Dot(offset, box.AxisY)) - half.Y - ProjectedRadius(other, box.AxisY);
        return separationX > separationY ? (separationX, box.AxisX) : (separationY, box.AxisY);
    }

    // Half the length of a box's shadow on an axis.
    private static float ProjectedRadius(Body box, Vector2 axis)
    {
        Vector2 half = box.Size / 2f;
        return half.X * MathF.Abs(Vector2.Dot(axis, box.AxisX)) + half.Y * MathF.Abs(Vector2.Dot(axis, box.AxisY));
    }

    // Cuts the segment v1-v2 to the side of a line where dot(normal, v) <= offset.
    // Returns false if nothing is left.
    private static bool ClipSegment(ref Vector2 v1, ref Vector2 v2, Vector2 normal, float offset)
    {
        float d1 = Vector2.Dot(normal, v1) - offset;
        float d2 = Vector2.Dot(normal, v2) - offset;
        if (d1 > 0f && d2 > 0f)
            return false;
        if (d1 > 0f)
            v1 += (v2 - v1) * (d1 / (d1 - d2));
        else if (d2 > 0f)
            v2 += (v1 - v2) * (d2 / (d2 - d1));
        return true;
    }
}
