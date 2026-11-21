/// <summary>
/// The shooter's collision layers. The engine only sees bits; the names live here. A collider's
/// layer says what it is, and its mask which layers it notices (see <see cref="Engine.Collider"/>).
/// </summary>
static class CollisionLayers
{
    public const uint Player = 1u << 0;
    public const uint Enemy = 1u << 1;
    public const uint Bullet = 1u << 2; // unused so far: bullets raycast instead of carrying a collider
    public const uint Wall = 1u << 3;   // the arena's walls and the crate: anything solid that's part of the level
}
