using System.Numerics;
using Engine;

enum Tool
{
    None,      // nothing selected: clicks in the scene do nothing
    Rectangle, // click and drag in the scene to place a rectangle
}

/// <summary>
/// The tool panel on the left edge of the window. Everything here is in window pixels,
/// with (0, 0) at the top left and y pointing down, so it stays put while the camera moves.
/// Layout and hit testing are plain <see cref="Rect"/>s; <see cref="Draw"/> paints them straight
/// into the renderer, so nothing is allocated per frame.
/// </summary>
static class Toolbar
{
    public const float Width = 64f;
    private const float ButtonSize = 48f;
    private const float Padding = (Width - ButtonSize) / 2f;

    // One button per tool, top to bottom. Tool.None has no button: it's what's left when nothing is selected.
    public static readonly Tool[] Tools = [Tool.Rectangle];

    private static readonly Color PanelColor = new(0.16f, 0.17f, 0.21f);
    private static readonly Color ButtonColor = new(0.22f, 0.23f, 0.28f);
    private static readonly Color HoveredColor = new(0.28f, 0.29f, 0.35f);
    private static readonly Color SelectedColor = new(0.30f, 0.45f, 0.75f);
    private static readonly Color GravityOnColor = new(0.25f, 0.55f, 0.35f);
    private static readonly Color RectangleIconColor = new(0.9f, 0.55f, 0.2f);
    private static readonly Color ArrowOnColor = new(0.92f, 0.95f, 0.92f);
    private static readonly Color ArrowOffColor = new(0.45f, 0.47f, 0.52f);

    public static bool Contains(Vector2 point) => point.X < Width;

    // The square at a position in the column of buttons, counting from the top.
    private static Rect Slot(int index) =>
        new(Center: new(Width / 2f, Padding + ButtonSize / 2f + index * (ButtonSize + Padding)), Size: new(ButtonSize));

    // The tool whose button is under the point, or null if the point isn't on a button.
    public static Tool? ToolAt(Vector2 point)
    {
        for (int i = 0; i < Tools.Length; i++)
            if (Slot(i).Contains(point))
                return Tools[i];
        return null;
    }

    // The gravity option sits in the slot below the tools while the Rectangle tool is selected.
    private static Rect GravityToggle => Slot(Tools.Length);

    public static bool GravityToggleContains(Vector2 point) => GravityToggle.Contains(point);

    /// <summary>
    /// The panel, one button per tool with its icon, and the gravity toggle while the Rectangle tool
    /// is selected. Uses whatever view-projection is set, so call it during the UI pass.
    /// </summary>
    public static void Draw(QuadRenderer quads, float windowHeight, Vector2 mouse, Tool selected, bool placeWithGravity)
    {
        quads.Draw(new Rect(Center: new(Width / 2f, windowHeight / 2f), Size: new(Width, windowHeight)), PanelColor);

        for (int i = 0; i < Tools.Length; i++)
        {
            var tool = Tools[i];
            var button = Slot(i);
            quads.Draw(button, tool == selected ? SelectedColor : button.Contains(mouse) ? HoveredColor : ButtonColor);
            DrawIcon(quads, tool, button);
        }

        if (selected == Tool.Rectangle)
        {
            // On (green): placed rectangles fall. Off: they stay where they're placed.
            var toggle = GravityToggle;
            quads.Draw(toggle, placeWithGravity ? GravityOnColor : toggle.Contains(mouse) ? HoveredColor : ButtonColor);
            DrawGravityArrow(quads, toggle, placeWithGravity ? ArrowOnColor : ArrowOffColor);
        }
    }

    // What goes on a tool's button. Icons are built from rectangles, so no textures are needed yet.
    private static void DrawIcon(QuadRenderer quads, Tool tool, Rect button)
    {
        switch (tool)
        {
            case Tool.Rectangle:
                quads.Draw(button with { Size = new(28f, 18f) }, RectangleIconColor);
                break;
        }
    }

    // A downward arrow: a stem, then a head made of bars that narrow toward the tip.
    private static void DrawGravityArrow(QuadRenderer quads, Rect button, Color color)
    {
        var c = button.Center; // y points down in window pixels
        quads.Draw(new Rect(new(c.X, c.Y - 6f), new(6f, 16f)), color);
        quads.Draw(new Rect(new(c.X, c.Y + 4f), new(18f, 4f)), color);
        quads.Draw(new Rect(new(c.X, c.Y + 8f), new(12f, 4f)), color);
        quads.Draw(new Rect(new(c.X, c.Y + 12f), new(6f, 4f)), color);
    }
}
