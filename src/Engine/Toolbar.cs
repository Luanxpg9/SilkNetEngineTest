using System.Numerics;

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

    public static Rectangle Button(int index, bool selected, bool hovered)
    {
        float centerY = Padding + ButtonSize / 2f + index * (ButtonSize + Padding);
        Vector3 color = selected ? new(0.30f, 0.45f, 0.75f)
                      : hovered  ? new(0.28f, 0.29f, 0.35f)
                      :            new(0.22f, 0.23f, 0.28f);
        return new(Center: new(Width / 2f, centerY), Size: new(ButtonSize), color);
    }

    // The tool whose button is under the point, or null if the point isn't on a button.
    public static Tool? ToolAt(Vector2 point)
    {
        for (int i = 0; i < Tools.Length; i++)
            if (Button(i, false, false).Contains(point))
                return Tools[i];
        return null;
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
