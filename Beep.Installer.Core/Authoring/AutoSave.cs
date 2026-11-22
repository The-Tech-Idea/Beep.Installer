using System;
using System.IO;

namespace Beep.Installer.Engine;

public sealed record RecoveryDraftEntry(string RecoveryPath, string? DocumentPath = null, string? DiscoveryError = null);

/// <summary>
/// Crash-recovery auto-save (Track X4). The builder periodically writes a dirty script to an
/// autosave next to the script file (<c>&lt;name&gt;.autosave.bsetup</c>); on next open, a newer
/// autosave triggers a recovery prompt. Pure path/timestamp logic so it is unit-testable.
/// </summary>
public static class AutoSave
{
    public static string RecoveryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeepInstaller", "Recovery");

    /// <summary>Returns untitled drafts from separate sessions, including the legacy shared draft.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> DiscoverUntitledDrafts()
    {
        var paths = new System.Collections.Generic.List<string>();
        if (Directory.Exists(RecoveryDirectory)) paths.AddRange(Directory.GetFiles(RecoveryDirectory, "*.autosave.bsetup"));
        var legacy = AutoSavePath(null);
        if (File.Exists(legacy)) paths.Add(legacy);
        paths.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        return paths;
    }

    public static System.Collections.Generic.IReadOnlyList<RecoveryDraftEntry> DiscoverDrafts(System.Collections.Generic.IEnumerable<string> savedProjects)
    {
        var entries = new System.Collections.Generic.Dictionary<string, RecoveryDraftEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in DiscoverUntitledDrafts())
        {
            var metadata = path + ".origin.json";
            try
            {
                if (path.EndsWith(".named.autosave.bsetup", StringComparison.OrdinalIgnoreCase) && !File.Exists(metadata))
                    throw new InvalidDataException("The recovery draft's original project metadata is missing.");
                var document = File.Exists(metadata) ? System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(metadata)) : null;
                if (File.Exists(metadata))
                {
                    if (string.IsNullOrWhiteSpace(document)) throw new InvalidDataException("The recovery draft's original project metadata is empty.");
                    document = Path.GetFullPath(document);
                }
                entries[Path.GetFullPath(path)] = new(path, document);
            }
            catch (Exception ex) { entries[Path.GetFullPath(path)] = new(path, DiscoveryError: ex.Message); }
        }
        foreach (var document in savedProjects)
        {
            if (string.IsNullOrWhiteSpace(document)) continue;
            var original = Path.GetFullPath(document);
            if (IsRecoveryAvailable(original)) entries[AutoSavePath(original)] = new(AutoSavePath(original), original);
        }
        var result = new System.Collections.Generic.List<RecoveryDraftEntry>(entries.Values);
        result.Sort((a, b) => File.GetLastWriteTimeUtc(b.RecoveryPath).CompareTo(File.GetLastWriteTimeUtc(a.RecoveryPath)));
        return result;
    }

    public static void RecordDocumentIdentity(string recoveryPath, string? documentPath)
    {
        if (documentPath is not null) AtomicFileWriter.WriteAllText(recoveryPath + ".origin.json", System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(documentPath)));
    }

    public static string? RecoveryFingerprint(string path)
    {
        try { return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))); }
        catch { return null; }
    }

    public static string? ClearRecoveryFile(string path, string? expectedFingerprint = null, bool verifyFingerprint = false)
    {
        try
        {
            if (File.Exists(path))
            {
                if (verifyFingerprint && (expectedFingerprint is null || RecoveryFingerprint(path) != expectedFingerprint))
                    return "The recovery draft changed or could not be verified; it has been preserved.";
                File.Delete(path);
            }
            if (File.Exists(path + ".origin.json")) File.Delete(path + ".origin.json");
            return null;
        }
        catch (Exception ex) { Diag.Debug("AutoSave", "clear failed", ex); return ex.Message; }
    }
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
        ClearRecoveryFile(AutoSavePath(projectPath));
    }
}
