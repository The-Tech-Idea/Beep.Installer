using System;
using System.Globalization;

namespace Beep.Installer.Engine;

/// <summary>
/// Provides locale-aware formatting helpers for the installer UI.
/// </summary>
public static class LocaleFormatter
{
    /// <summary>Human-readable file size using the current UI culture.</summary>
    public static string FormatSize(long bytes)
    {
        var ci = Lang.LanguageManager.CurrentCulture;
        return bytes switch
        {
            >= 1_073_741_824 => (bytes / 1_073_741_824.0).ToString("F1", ci) + " GB",
            >= 1_048_576 => (bytes / 1_048_576.0).ToString("F1", ci) + " MB",
            >= 1024 => (bytes / 1024.0).ToString("F0", ci) + " KB",
            _ => bytes.ToString("N0", ci) + " B"
        };
    }

    /// <summary>Formats a count with thousands separator per the current UI culture.</summary>
    public static string FormatCount(int count) => count.ToString("N0", Lang.LanguageManager.CurrentCulture);

    /// <summary>Formats a DateTime per the current UI culture.</summary>
    public static string FormatDateTime(DateTime dt) => dt.ToString("g", Lang.LanguageManager.CurrentCulture);

    /// <summary>Formats a TimeSpan per the current UI culture.</summary>
    public static string FormatElapsed(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s" :
        ts.TotalMinutes >= 1 ? $"{ts.Minutes}m {ts.Seconds}s" :
        $"{ts.Seconds}.{ts.Milliseconds / 100}s";
}
