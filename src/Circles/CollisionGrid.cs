using System.Numerics;

/// <summary>
/// Ball-to-ball collisions using a uniform grid as the broad phase.
/// Cells are as wide as the largest ball, so two balls can only touch if they are
/// in the same or neighboring cells. Each frame the balls are bucketed into cells
/// with a counting sort, then every ball checks only the 3x3 cells around it.
/// </summary>
sealed class CollisionGrid(float cellSize)
{
    private const float Restitution = 1f; // 1 = perfectly elastic, lower loses energy on impact

    private int[] _cellStart = [];  // ball indices for cell c are _sorted[_cellStart[c] .. _cellStart[c + 1]]
    private int[] _cellCursor = [];
    private int[] _cellOf = [];     // which cell each ball is in
    private int[] _sorted = [];     // ball indices grouped by cell

    public void Resolve(Span<CircleInstance> circles, Span<Vector2> velocities, float width, float height)
    {
        int columns = (int)(width / cellSize) + 1;
        int rows = (int)(height / cellSize) + 1;
        int cellCount = columns * rows;

        EnsureCapacity(ref _cellStart, cellCount + 1);
        EnsureCapacity(ref _cellCursor, cellCount);
        EnsureCapacity(ref _cellOf, circles.Length);
        EnsureCapacity(ref _sorted, circles.Length);

        // Counting sort: count balls per cell, prefix-sum into start offsets, then scatter.
        Array.Clear(_cellStart, 0, cellCount + 1);
        for (int i = 0; i < circles.Length; i++)
        {
            int cell = CellIndex(circles[i].Center, columns, rows);
            _cellOf[i] = cell;
            _cellStart[cell + 1]++;
        }

        for (int c = 0; c < cellCount; c++)
            _cellStart[c + 1] += _cellStart[c];

        Array.Copy(_cellStart, _cellCursor, cellCount);
        for (int i = 0; i < circles.Length; i++)
            _sorted[_cellCursor[_cellOf[i]]++] = i;

        // Narrow phase: test each ball against balls in the surrounding 3x3 cells.
        for (int i = 0; i < circles.Length; i++)
        {
            int cx = _cellOf[i] % columns;
            int cy = _cellOf[i] / columns;

            for (int ny = Math.Max(cy - 1, 0); ny <= Math.Min(cy + 1, rows - 1); ny++)
            for (int nx = Math.Max(cx - 1, 0); nx <= Math.Min(cx + 1, columns - 1); nx++)
            {
                int cell = ny * columns + nx;
                for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    int j = _sorted[k];
                    if (j > i) // handle each pair once
                        Collide(ref circles[i], ref velocities[i], ref circles[j], ref velocities[j]);
                }
            }
        }
    }

    private static void Collide(ref CircleInstance a, ref Vector2 velocityA, ref CircleInstance b, ref Vector2 velocityB)
    {
        var delta = new Vector2(b.Center.X - a.Center.X, b.Center.Y - a.Center.Y);
        float minDistance = a.Radius + b.Radius;
        float distanceSquared = delta.LengthSquared();
        if (distanceSquared >= minDistance * minDistance || distanceSquared == 0f)
            return;

        float distance = MathF.Sqrt(distanceSquared);
        Vector2 normal = delta / distance;

        // Mass grows with area, so big balls shove small ones around.
        float inverseMassA = 1f / (a.Radius * a.Radius);
        float inverseMassB = 1f / (b.Radius * b.Radius);
        float inverseMassSum = inverseMassA + inverseMassB;

        // Push the balls apart so they no longer overlap, the lighter one moving more.
        Vector2 correction = normal * ((minDistance - distance) / inverseMassSum);
        a.Center.X -= correction.X * inverseMassA;
        a.Center.Y -= correction.Y * inverseMassA;
        b.Center.X += correction.X * inverseMassB;
        b.Center.Y += correction.Y * inverseMassB;

        // Bounce: only if they are moving toward each other along the contact normal.
        float approachSpeed = Vector2.Dot(velocityB - velocityA, normal);
        if (approachSpeed >= 0f)
            return;

        float impulse = -(1f + Restitution) * approachSpeed / inverseMassSum;
        velocityA -= normal * (impulse * inverseMassA);
        velocityB += normal * (impulse * inverseMassB);
    }

    private int CellIndex(Vector3 center, int columns, int rows)
    {
        int x = Math.Clamp((int)(center.X / cellSize), 0, columns - 1);
        int y = Math.Clamp((int)(center.Y / cellSize), 0, rows - 1);
        return y * columns + x;
    }

    private static void EnsureCapacity(ref int[] array, int length)
    {
        if (array.Length < length)
            array = new int[Math.Max(length, array.Length * 2)];
    }
}
