using System;
using System.Drawing;
using System.IO;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>
/// Loads <see cref="InstallerBranding"/> from a sibling <c>branding.json</c> file
/// (next to the running .exe) and provides helpers to convert hex color strings
/// to <see cref="Color"/>.
/// </summary>
public static class ThemeLoader
{
    public static InstallerBranding LoadBranding()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "branding.json");
            if (!File.Exists(path)) return new InstallerBranding();
            var json = File.ReadAllText(path);
            var b = System.Text.Json.JsonSerializer.Deserialize<InstallerBranding>(json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });
            return b ?? new InstallerBranding();
        }
        catch
        {
            return new InstallerBranding();
        }
    }

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
