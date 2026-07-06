using System.Drawing;

namespace Beep.Installer.Engine;

/// <summary>
/// Color parsing helpers for the wizard theme.
/// </summary>
public static class ThemeLoader
{
    /// <summary>Parses "#RRGGBB" or "RRGGBB" into a <see cref="Color"/>; returns fallback on failure.</summary>
    public static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6 &&
            byte.TryParse(h.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(h.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(h.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(r, g, b);
        }
        return fallback;
    }
}