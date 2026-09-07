using System.Text.Json;

namespace Beep.Installer.Engine.Updates;

/// <summary>Quota reservations and retention for the existing content-addressed download cache.</summary>
internal static class UpdateCacheRetention
{
    private const string MarkerName = ".beep-cache-entry.json";
    private sealed record Entry(int Version, DateTimeOffset LastUsedUtc, long ReservedBytes);

    // The caller owns the entry lease throughout acquisition and application.
    public static void Prepare(string entryPath, long payloadBytes, long maximumBytes, TimeSpan retention)
    {
        if (maximumBytes <= 0 || retention <= TimeSpan.Zero || payloadBytes < 0)
            throw new ArgumentException("Update cache quota and retention must be positive.");
        var reserved = checked(payloadBytes + 17L * 1024 * 1024);
        if (reserved > maximumBytes) throw new IOException("The update exceeds the configured cache quota.");
        var root = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;
        using var cacheLease = InstallationOperationLock.Acquire(root);
        var error = DeltaUpdatePackageService.ValidateDirectoryPath(root);
        if (error is not null) throw new IOException(error);
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.UtcNow;
        var entries = Directory.GetDirectories(root).Select(path =>
        {
            Entry? marker = null;
            var markerPath = Path.Combine(path, MarkerName);
            if (File.Exists(markerPath))
            {
                try { marker = JsonSerializer.Deserialize<Entry>(File.ReadAllText(markerPath)); }
                // A torn or hand-edited marker just means this entry has no recorded last-use;
                // it is then sized from disk and aged out last. The cache is rebuildable.
                catch (JsonException) { /* unreadable marker: fall back to on-disk size */ }
            }
            return (Path: path, Marker: marker, Bytes: Math.Max(Size(path), marker?.ReservedBytes ?? 0));
        }).ToList();
        var current = entries.FirstOrDefault(e => string.Equals(e.Path, entryPath, StringComparison.OrdinalIgnoreCase));
        var requiredCurrent = Math.Max(reserved, current.Bytes);
        var used = checked(entries.Sum(e => e.Bytes) - current.Bytes + requiredCurrent
            + Directory.GetFiles(root).Sum(f => new FileInfo(f).Length));
        foreach (var entry in entries.OrderBy(e => e.Marker?.LastUsedUtc ?? DateTimeOffset.MaxValue))
        {
            if (entry.Path == current.Path || entry.Marker is not { Version: 1 } marker
                || marker.LastUsedUtc == default || marker.ReservedBytes < 0) continue;
            if (used <= maximumBytes && now - marker.LastUsedUtc < retention) continue;
            InstallationOperationLock? lease = null;
            try
            {
                try { lease = InstallationOperationLock.Acquire(entry.Path); }
                catch (IOException) { continue; } // Active entries are never evicted.
                if (!TryDeleteOwnedEntry(entry.Path)) continue;
                used -= entry.Bytes;
            }
            finally { lease?.Dispose(); }
        }
        if (used > maximumBytes)
            throw new IOException("Cache quota cannot be reserved: active or unowned files were preserved. Free space or increase the quota.");
        AtomicFileWriter.WriteAllText(Path.Combine(entryPath, MarkerName),
            JsonSerializer.Serialize(new Entry(1, now, requiredCurrent)));
    }

    private static long Size(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Sum(f => new FileInfo(f).Length);

    private static bool TryDeleteOwnedEntry(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length != 64 || !name.All(Uri.IsHexDigit)
            || DeltaUpdatePackageService.ValidateDirectoryPath(path) is not null) return false;
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(path, file).Replace('\\', '/');
            if (relative is MarkerName or DeltaUpdatePackageService.ManifestFileName or DeltaUpdatePackageService.SignatureFileName
                || relative == DeltaUpdatePackageService.ManifestFileName + ".partial"
                || relative == DeltaUpdatePackageService.SignatureFileName + ".partial") continue;
            var parts = relative.Split('/');
            if (parts.Length != 4 || parts[0] != "blobs" || parts[1] != "sha256"
                || parts[2].Length != 64 || !parts[2].All(Uri.IsHexDigit)) return false;
        }
        // Delete only inspected files and empty directories, never a recursive tree operation.
        foreach (var file in files) File.Delete(file);
        foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            Directory.Delete(directory, recursive: false);
        Directory.Delete(path, recursive: false);
        return true;
    }
}
