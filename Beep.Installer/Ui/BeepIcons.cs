using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Beep.Installer.Ui;

/// <summary>
/// The one owner of toolbar and sidebar icons. Every icon comes from the Beep control library's own embedded
/// icon set and the installer's embedded SVG assets, found by file name, and is recoloured to the current
/// theme's text colour so it follows theme changes.
/// </summary>
internal static class BeepIcons
{
    private static readonly Lazy<Dictionary<string, Assembly>> ResourceOwners = new(() =>
        new[] { typeof(BeepIcons).Assembly, typeof(TheTechIdea.Beep.Winform.Controls.BeepButton).Assembly }
            .SelectMany(assembly => assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                .Select(name => new KeyValuePair<string, Assembly>(name, assembly)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    private static readonly Lazy<ILookup<string, string>> Index = new(() =>
        ResourceOwners.Value.Keys
            .ToLookup(FileKey, StringComparer.OrdinalIgnoreCase));
    private static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    private static string FileKey(string resource)
    {
        var parts = resource.Split('.');
        return parts.Length >= 2 ? parts[^2] : resource;
    }

    /// <summary>The first icon whose file name matches one of the candidates, in the order given; null when the Beep set has none.</summary>
    public static string? Find(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var hits = Index.Value[candidate].ToList();
            if (hits.Count == 0) continue;
            return hits.OrderBy(h => h.Contains(".tabler", StringComparison.OrdinalIgnoreCase) ? 0 : h.Contains(".SVG.", StringComparison.Ordinal) ? 1 : 2).First();
        }
        return null;
    }

    public static Bitmap? Render(string? resource, int px, Color color)
    {
        if (resource is null) return null;
        var key = (resource, px, color.ToArgb());
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            if (!ResourceOwners.Value.TryGetValue(resource, out var owner)) return null;
            using var stream = owner.GetManifestResourceStream(resource);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var text = Regex.Replace(reader.ReadToEnd(), "currentColor", hex, RegexOptions.IgnoreCase);
            var svg = Svg.SvgDocument.FromSvg<Svg.SvgDocument>(text);
            var bitmap = new Bitmap(px, px);
            svg.Draw(bitmap);
            Cache[key] = bitmap;
            return bitmap;
        }
    }

    /// <summary>
    /// An icon from the first matching candidate that actually draws something, in the theme's text colour.
    /// A library icon that parses but paints nothing (or fails to parse) is skipped for the next candidate.
    /// </summary>
    public static Bitmap? Render(int px, params string[] candidates)
    {
        var color = InstallerTheme.AccessibleForeground(InstallerTheme.Text, InstallerTheme.Sidebar);
        foreach (var candidate in candidates)
        {
            foreach (var resource in Matches(candidate))
            {
                Bitmap? bitmap;
                try { bitmap = Render(resource, px, color); }
                catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException or FormatException)
                {
                    Engine.Diag.Debug("BeepIcons", $"icon '{resource}' could not be drawn", ex);
                    continue;
                }
                if (bitmap is not null && HasInk(bitmap)) return bitmap;
                Engine.Diag.Debug("BeepIcons", $"icon '{resource}' drew nothing; trying the next candidate", null);
            }
        }
        return null;
    }

    private static IEnumerable<string> Matches(string candidate) =>
        Index.Value[candidate].OrderBy(h => h.Contains(".tabler", StringComparison.OrdinalIgnoreCase) ? 0 : h.Contains(".SVG.", StringComparison.Ordinal) ? 1 : 2);

    private static bool HasInk(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).A > 16) return true;
        return false;
    }
}
