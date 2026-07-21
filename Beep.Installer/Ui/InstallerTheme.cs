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
    public static Color Shell => Resolve(t => t?.BackColor, Color.FromArgb(246, 248, 251));

    /// <summary>Content panel background.</summary>
    public static Color Panel => Resolve(t => t?.PanelBackColor, Color.White);

    /// <summary>Sidebar / secondary surface.</summary>
    public static Color Sidebar => Resolve(t => t?.SideMenuBackColor, Color.FromArgb(245, 246, 250));

    /// <summary>Hairline separators and control borders.</summary>
    public static Color Border => Resolve(t => t?.BorderColor, Color.FromArgb(220, 225, 232));

    // ── Text ──

    /// <summary>Primary body and heading text.</summary>
    public static Color Text => Resolve(t => t?.ForeColor, Color.FromArgb(32, 38, 46));

    /// <summary>
    /// Secondary text. Kept deliberately dark: the previous value (98,107,119 on white) sits
    /// around 4.4:1, which is under the WCAG AA 4.5:1 threshold for normal-size text.
    /// </summary>
    public static Color MutedText => Color.FromArgb(88, 96, 108);

    /// <summary>Primary action / selection accent.</summary>
    public static Color Accent => Resolve(t => t?.AccentColor, Color.FromArgb(37, 99, 235));

    // ── Status ──

    public static Color Success => Color.FromArgb(30, 122, 58);
    public static Color Warning => Color.FromArgb(154, 96, 0);
    public static Color Danger => Color.FromArgb(176, 42, 42);

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
