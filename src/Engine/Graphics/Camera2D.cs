using System.Numerics;

namespace Engine;

/// <summary>
/// An orthographic camera that looks straight down the -Z axis at the XY plane.
/// <see cref="Position"/> is the world point at the center of the screen, and
/// <see cref="ViewHeight"/> is how many world units fit vertically. The visible width
/// follows from the window's aspect ratio, so shapes keep their proportions.
/// </summary>
public sealed class Camera2D(float viewHeight)
{
    public Vector2 Position;
    public float ViewHeight = viewHeight;
    public float AspectRatio = 1f; // width / height of the framebuffer

    // World -> camera: moving the camera right is the same as moving the world left.
    public Matrix4x4 View => Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f);

    // Camera -> clip space: maps the visible rectangle onto -1..1 in both axes.
    public Matrix4x4 Projection => Matrix4x4.CreateOrthographic(ViewHeight * AspectRatio, ViewHeight, -1f, 1f);

    // System.Numerics uses row vectors, so transforms compose left to right: view first, then projection.
    public Matrix4x4 ViewProjection => View * Projection;

    // Window pixels (origin top left, y down) -> world units (y up). The inverse of ViewProjection
    // followed by the viewport, worked out by hand because an orthographic camera makes it simple.
    public Vector2 ScreenToWorld(Vector2 screen, Vector2 screenSize)
    {
        var ndc = new Vector2(screen.X / screenSize.X * 2f - 1f, 1f - screen.Y / screenSize.Y * 2f);
        return Position + ndc * new Vector2(ViewHeight * AspectRatio, ViewHeight) / 2f;
    }
}
