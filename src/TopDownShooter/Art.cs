using System.Numerics;
using Engine;
// Only GL from Silk.NET.OpenGL: the whole namespace would bring its own Texture, clashing with Engine.Texture.
using GL = Silk.NET.OpenGL.GL;

/// <summary>
/// Draw order, bottom to top. Sprites in a higher layer always cover those in a lower one,
/// whatever order the game draws them in.
/// </summary>
static class Layers
{
    public const int Floor = 0;     // tiles and walls
    public const int Props = 10;    // crates and other things lying on the floor
    public const int Legs = 20;     // walking legs, under the bodies
    public const int Bodies = 30;   // torsos and arms
    public const int Weapons = 40;  // the player's rifle, over the arms (so it can recoil on its own later)
    public const int BodyTops = 45; // helmet and gloves, over the rifle: cheek on the stock, fingers around the grip
    public const int Effects = 50;  // laser beam, later muzzle flashes and tracers
}

/// <summary>
/// The game's textures, loaded once, plus the conversions between art pixels and world units.
///
/// All character art is drawn at <see cref="PixelsPerUnit"/> pixels per world unit, so a sprite's
/// size in the world is its pixel size divided by that. Points in the art (where the rifle butt goes,
/// where the laser comes out) are written in pixels, as read from the SVG files, with y going down
/// like in any image editor; <see cref="Offset"/> and <see cref="Origin"/> turn them into world units.
/// </summary>
sealed class Art : IDisposable
{
    public const float PixelsPerUnit = 400f;

    public Texture Crate { get; }
    public Texture SoldierBase { get; }  // torso and arms
    public Texture SoldierTop { get; }   // helmet and gloves
    public Texture Rifle { get; }
    public Texture Legs { get; }         // walk cycle sheet: 8 frames of 768x512 side by side
    public Texture Enemy { get; }
    public Texture Bullet { get; }       // not used until bullets exist (roadmap step 4)
    public Texture LaserDot { get; }

    public Animation Walk { get; }

    public Art(GL gl)
    {
        // Linear filtering with mipmaps: the art is drawn far smaller than its pixel size.
        Crate = Texture.Load(gl, "Assets/crate.png");
        SoldierBase = Texture.Load(gl, "Assets/soldier_base.png");
        SoldierTop = Texture.Load(gl, "Assets/soldier_top.png");
        Rifle = Texture.Load(gl, "Assets/rifle.png");
        Legs = Texture.Load(gl, "Assets/legs_walk.png");
        Enemy = Texture.Load(gl, "Assets/enemy.png");
        Bullet = Texture.Load(gl, "Assets/bullet.png");
        LaserDot = Texture.Load(gl, "Assets/laser_dot.png");

        // 8 frames at 0.08 s: a full stride (both feet) about 1.5 times per second, a jog.
        Walk = new Animation(TextureRegion.Grid(Legs, columns: 8, rows: 1), frameDuration: 0.08f);
    }

    /// <summary>World size of a texture, or of one frame of a sheet.</summary>
    public static Vector2 WorldSize(TextureRegion region) => region.PixelSize / PixelsPerUnit;

    /// <summary>
    /// The world-unit offset from one point in the art to another, both in pixels with y going down.
    /// The result has y going up, like the world: a point below the anchor gives a negative y.
    /// </summary>
    public static Vector2 Offset(float x, float y, float anchorX, float anchorY) =>
        new((x - anchorX) / PixelsPerUnit, -(y - anchorY) / PixelsPerUnit);

    /// <summary>A pixel of a texture as a <see cref="SpriteBatch"/> origin (0..1, from the bottom left).</summary>
    public static Vector2 Origin(Texture texture, float x, float y) => new(x / texture.Width, 1f - y / texture.Height);

    /// <summary>Turns a local offset (art facing +X) by <paramref name="angle"/>, so it follows a sprite drawn at that angle.</summary>
    public static Vector2 Rotate(Vector2 offset, float angle)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
    }

    public void Dispose()
    {
        Crate.Dispose();
        SoldierBase.Dispose();
        SoldierTop.Dispose();
        Rifle.Dispose();
        Legs.Dispose();
        Enemy.Dispose();
        Bullet.Dispose();
        LaserDot.Dispose();
    }
}
