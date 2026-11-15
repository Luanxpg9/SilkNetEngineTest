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
/// </summary>
static class Toolbar
{
    public const float Width = 64f;
    private const float ButtonSize = 48f;
    private const float Padding = (Width - ButtonSize) / 2f;

    // One button per tool, top to bottom. Tool.None has no button: it's what's left when nothing is selected.
    public static readonly Tool[] Tools = [Tool.Rectangle];

    public static readonly Vector3 PanelColor = new(0.16f, 0.17f, 0.21f);

    public static bool Contains(Vector2 point) => point.X < Width;

    public static Rectangle Panel(float windowHeight) =>
        new(Center: new(Width / 2f, windowHeight / 2f), Size: new(Width, windowHeight), PanelColor);

    private static readonly Vector3 ButtonColor = new(0.22f, 0.23f, 0.28f);
    private static readonly Vector3 HoveredColor = new(0.28f, 0.29f, 0.35f);

    // The square at a position in the column of buttons, counting from the top.
    private static Rectangle Slot(int index, Vector3 color) =>
        new(Center: new(Width / 2f, Padding + ButtonSize / 2f + index * (ButtonSize + Padding)), Size: new(ButtonSize), color);

    public static Rectangle Button(int index, bool selected, bool hovered) =>
        Slot(index, selected ? new(0.30f, 0.45f, 0.75f) : hovered ? HoveredColor : ButtonColor);

    // The tool whose button is under the point, or null if the point isn't on a button.
    public static Tool? ToolAt(Vector2 point)
    {
        for (int i = 0; i < Tools.Length; i++)
            if (Button(i, false, false).Contains(point))
                return Tools[i];
        return null;
    }

    // The gravity option, shown in the slot below the tools while the Rectangle tool is selected.
    // On (green): placed rectangles fall. Off: they stay where they're placed.
    public static Rectangle GravityToggle(bool on, bool hovered) =>
        Slot(Tools.Length, on ? new(0.25f, 0.55f, 0.35f) : hovered ? HoveredColor : ButtonColor);

    public static bool GravityToggleContains(Vector2 point) => GravityToggle(false, false).Contains(point);

    // A downward arrow: a stem, then a head made of bars that narrow toward the tip.
    public static IEnumerable<Rectangle> GravityIcon(Rectangle button, bool on)
    {
        Vector3 color = on ? new(0.92f, 0.95f, 0.92f) : new(0.45f, 0.47f, 0.52f);
        var c = button.Center; // y points down in window pixels
        yield return new(new(c.X, c.Y - 6f), new(6f, 16f), color);
        yield return new(new(c.X, c.Y + 4f), new(18f, 4f), color);
        yield return new(new(c.X, c.Y + 8f), new(12f, 4f), color);
        yield return new(new(c.X, c.Y + 12f), new(6f, 4f), color);
    }

    // What to draw on top of a tool's button. Icons are built from rectangles, so no textures are needed yet.
    public static IEnumerable<Rectangle> Icon(Tool tool, Rectangle button)
    {
        switch (tool)
        {
            case Tool.Rectangle:
                yield return button with { Size = new(28f, 18f), Color = new(0.9f, 0.55f, 0.2f) };
                break;
        }
    }
}
