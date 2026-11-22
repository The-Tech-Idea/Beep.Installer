using System.Drawing;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Ui;

/// <summary>
/// The single source of colour and font tokens for the installer's UI.
///
/// There were previously three uncoordinated palettes — the wizard hardcoded its own greys,
/// <c>PackageBuilderForm</c> held a private set of ARGB constants, and every dialog declared
/// colours inline. Nothing followed the active Beep theme, so a dark theme left the builder
/// and dialogs stubbornly light.
///
/// Tokens resolve from <see cref="BeepThemesManager"/> when it supplies a usable value and
/// fall back to the previous hardcoded constants, so the default light appearance is
/// unchanged while a themed or high-contrast environment is finally honoured.
/// </summary>
public static class InstallerTheme
{
    // ── Surfaces ──

    /// <summary>Window/shell background, behind panels.</summary>
    public static Color Shell => SystemInformation.HighContrast ? SystemColors.Control : Resolve(t => t?.BackColor, Color.FromArgb(246, 248, 251));

    /// <summary>Content panel background.</summary>
    public static Color Panel => SystemInformation.HighContrast ? SystemColors.Window : Resolve(t => t?.PanelBackColor, Color.White);

    /// <summary>Sidebar / secondary surface.</summary>
    public static Color Sidebar => SystemInformation.HighContrast ? SystemColors.Control : Resolve(t => t?.SideMenuBackColor, Color.FromArgb(245, 246, 250));

    /// <summary>Hairline separators and control borders.</summary>
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Resolve(t => t?.BorderColor, Color.FromArgb(220, 225, 232));

    // ── Text ──

    /// <summary>Primary body and heading text.</summary>
    public static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : AccessibleForeground(Resolve(t => t?.ForeColor, Color.FromArgb(32, 38, 46)), Panel);

    /// <summary>
    /// Secondary text. Kept deliberately dark: the previous value (98,107,119 on white) sits
    /// around 4.4:1, which is under the WCAG AA 4.5:1 threshold for normal-size text.
    /// </summary>
    public static Color MutedText => SystemInformation.HighContrast ? SystemColors.WindowText : AccessibleForeground(Color.FromArgb(88, 96, 108), Panel);

    /// <summary>Primary action / selection accent.</summary>
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Resolve(t => t?.AccentColor, Color.FromArgb(37, 99, 235));
    public static Color AccentText => SystemInformation.HighContrast ? SystemColors.HighlightText : AccessibleForeground(Color.White, Accent);

    // ── Status ──

    public static Color Success => SystemInformation.HighContrast ? SystemColors.WindowText : AccessibleForeground(Color.FromArgb(30, 122, 58), Panel);
    public static Color Warning => SystemInformation.HighContrast ? SystemColors.WindowText : AccessibleForeground(Color.FromArgb(154, 96, 0), Panel);
    public static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : AccessibleForeground(Color.FromArgb(176, 42, 42), Panel);

    public static Padding SectionPadding => new(12);
    public const int FieldGap = 8;
    public const int ActionMinHeight = 32;

    internal static Color AccessibleForeground(Color preferred, Color background)
    {
        if (Contrast(preferred, background) >= 4.5) return preferred;
        return Contrast(Color.White, background) >= Contrast(Color.Black, background) ? Color.White : Color.Black;
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255.0;
                return normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    // ── Type ──

    public static Font Body => new("Segoe UI", 9F);
    public static Font BodyBold => new("Segoe UI", 9F, FontStyle.Bold);
    public static Font SectionTitle => new("Segoe UI", 13F, FontStyle.Bold);
    public static Font PageTitle => new("Segoe UI", 14F, FontStyle.Bold);

    /// <summary>
    /// True when the active theme is dark, so callers can pick an appropriate asset or
    /// contrast treatment.
    /// </summary>
    public static bool IsDark => Shell.GetBrightness() < 0.5;

    /// <summary>
    /// Reads a token from the current Beep theme, falling back when the theme is absent or
    /// supplies an empty colour. Theme lookups must never throw into UI construction.
    /// </summary>
    private static Color Resolve(System.Func<dynamic?, object?> selector, Color fallback)
    {
        try
        {
            var theme = BeepThemesManager.CurrentTheme;
            if (theme == null) return fallback;

            if (selector(theme) is Color color && color.A != 0) return color;
        }
        catch
        {
            // A theme that does not expose the property is expected — fall back quietly.
        }
        return fallback;
    }
}
