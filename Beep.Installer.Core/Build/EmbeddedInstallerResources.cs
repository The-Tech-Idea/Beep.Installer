using System;
using System.IO;
using System.IO.Compression;

namespace Beep.Installer.Engine;

/// <summary>Reads runtime metadata from the embedded installer archive inside Setup.exe.</summary>
public static class EmbeddedInstallerResources
{
    private static string? _extractDir;

    public static bool Has(string relativePath)
    {
        var dir = EnsureExtracted();
        return dir != null && File.Exists(Path.Combine(dir, relativePath));
    }

    public static string? ReadText(string relativePath)
    {
        var dir = EnsureExtracted();
        if (dir == null) return null;
        var path = Path.Combine(dir, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string? DirectoryPath => EnsureExtracted();

    public static string? PathFor(string relativePath)
    {
        var dir = EnsureExtracted();
        if (dir == null) return null;
        var path = Path.Combine(dir, relativePath);
        return File.Exists(path) ? path : null;
    }

    private static string? EnsureExtracted()
    {
        if (_extractDir != null && Directory.Exists(_extractDir)) return _extractDir;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || PePayloadWriter.FindEmbedded(exe) == null)
            return null;

        var tempZip = Path.Combine(Path.GetTempPath(), $"BeepInstallerResources_{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"BeepInstallerResources_{Guid.NewGuid():N}");
        try
        {
            PePayloadWriter.Extract(exe, tempZip);
            ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);
            _extractDir = extractDir;
            return _extractDir;
        }
        catch (Exception ex)
        {
            Diag.Debug("EmbeddedInstallerResources", "embedded resource extraction failed", ex);
            return null;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }
}
