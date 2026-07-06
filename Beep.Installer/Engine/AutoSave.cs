using System;
using System.IO;

namespace Beep.Installer.Engine;

/// <summary>
/// Crash-recovery auto-save (Track X4). The builder periodically writes a dirty script to an
/// autosave next to the script file (<c>&lt;name&gt;.autosave.bsetup</c>); on next open, a newer
/// autosave triggers a recovery prompt. Pure path/timestamp logic so it is unit-testable.
/// </summary>
public static class AutoSave
{
    /// <summary>Autosave interval used by the builder timer.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>The autosave path for a given script file. Untitled scripts use %APPDATA%.</summary>
    public static string AutoSavePath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BeepInstaller", "autosave-untitled.bsetup");

        var dir = Path.GetDirectoryName(projectPath);
        var name = Path.GetFileNameWithoutExtension(projectPath);
        return Path.Combine(string.IsNullOrEmpty(dir) ? "" : dir, name + ".autosave.bsetup");
    }

    /// <summary>
    /// True when an autosave exists and is newer than the script file (so recovering it would
    /// restore unsaved work). A missing script file with an autosave present also counts.
    /// </summary>
    public static bool IsRecoveryAvailable(string? projectPath)
    {
        var autosave = AutoSavePath(projectPath);
        if (!File.Exists(autosave)) return false;
        if (!File.Exists(projectPath)) return true;
        return File.GetLastWriteTimeUtc(autosave) > File.GetLastWriteTimeUtc(projectPath);
    }

    /// <summary>Removes the autosave for a script (call after a successful real save / clean exit).</summary>
    public static void Clear(string? projectPath)
    {
        try { if (File.Exists(AutoSavePath(projectPath))) File.Delete(AutoSavePath(projectPath)); }
        catch (Exception ex) { Diag.Debug("AutoSave", "clear failed", ex); }
    }
}
