using System.Globalization;
using System.Numerics;

namespace Engine;

/// <summary>
/// A color with red, green, blue and alpha (opacity), each from 0 to 1. Alpha 1 is fully opaque,
/// 0 is fully transparent. Colors can be written as hex codes, the same way SVG art and most
/// design tools write them:
/// <code>
/// Color.FromHex("#2B2D35")     // opaque
/// Color.FromHex("#FF000080")   // red at about 50% opacity
/// Color.White.WithAlpha(0.3f)  // white at 30% opacity
/// </code>
/// Note: alpha only shows on screen once blending is turned on (roadmap step 3 Phase A).
/// </summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color Black = new(0f, 0f, 0f);
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);

    /// <summary>The same color with a different opacity.</summary>
    public Color WithAlpha(float alpha) => this with { A = alpha };

    /// <summary>From 0-255 values, like a color picker shows them.</summary>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>From "#RRGGBB" or "#RRGGBBAA" (the # is optional). Throws on anything else, so typos show up right away.</summary>
    public static Color FromHex(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length is not (6 or 8))
            throw new FormatException($"Expected a color like #RRGGBB or #RRGGBBAA, got \"{hex}\"");

        byte Channel(int index) => byte.Parse(digits.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return FromBytes(Channel(0), Channel(1), Channel(2), digits.Length == 8 ? Channel(3) : (byte)255);
    }

    /// <summary>As a vector (R, G, B, A), the form shaders take.</summary>
    public Vector4 ToVector4() => new(R, G, B, A);
}
