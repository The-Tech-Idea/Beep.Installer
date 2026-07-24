using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Beep.Installer.Engine;

/// <summary>
/// Payload packaging (Track A2). Supports:
/// <list type="bullet">
/// <item><term>Solid/deduplicated zip (A2.2)</term><description>identical files stored once under
///   <c>_blobs/&lt;sha256&gt;</c> with a <c>_payload-manifest.json</c> mapping each path → blob.</description></item>
/// <item><term>Plain zip</term><description>standard zip of the payload tree.</description></item>
/// <item><term>LZMA2 (.7z) via 7z.exe (A2.1)</term><description>best-effort; requires 7z at build and runtime.</description></item>
/// </list>
/// </summary>
public static class PayloadPackager
{
    private const string ManifestName = "_payload-manifest.json";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ── Solid (deduplicated) zip ───────────────────────────────────────────

    /// <summary>
    /// Creates a solid zip: each unique file content is stored once under <c>_blobs/&lt;hash&gt;</c>,
    /// and <c>_payload-manifest.json</c> records the path → blob mapping. Duplicate files across
    /// components collapse to a single stored copy.
    /// </summary>
    /// <returns>number of input files / number of unique blobs (for reporting).</returns>
    public static (int files, int blobs, long originalBytes, long storedBytes) CreateSolid(string srcDir, string zipPath, CompressionLevel level)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var storedHashes = new HashSet<string>();
        var entries = new List<SolidEntry>();
        long originalBytes = 0, storedBytes = 0;

        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file).Replace('\\', '/');
            var bytes = File.ReadAllBytes(file);
            originalBytes += bytes.Length;
            var hash = Convert.ToHexString(SHA256.HashData(bytes));

            if (storedHashes.Add(hash))
            {
                var blobEntry = zip.CreateEntry("_blobs/" + hash, level);
                using (var s = blobEntry.Open())
                    s.Write(bytes, 0, bytes.Length);
                storedBytes += bytes.Length;
            }
            entries.Add(new SolidEntry { Path = rel, Blob = hash, Size = bytes.Length });
        }

        var manifest = new SolidManifest { Solid = true, Entries = entries };
        var manifestJson = JsonSerializer.Serialize(manifest, _json);
        var mEntry = zip.CreateEntry(ManifestName);
        using (var stream = mEntry.Open())
        using (var sw = new StreamWriter(stream))
            sw.Write(manifestJson);

        return (entries.Count, storedHashes.Count, originalBytes, storedBytes);
    }

    /// <summary>True when the zip carries a solid dedup manifest.</summary>
    public static bool IsSolid(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.GetEntry(ManifestName) != null;
        }
        catch (Exception ex) { Diag.Debug("PayloadPackager", $"IsSolid peek failed for {zipPath}", ex); return false; }
    }

    /// <summary>Materializes the real payload tree from a solid zip into <paramref name="destDir"/>.</summary>
    public static void ExtractSolid(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var zip = ZipFile.OpenRead(zipPath);
        var mEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("Not a solid payload (missing manifest).");

        SolidManifest manifest;
        using (var sr = new StreamReader(mEntry.Open()))
            manifest = JsonSerializer.Deserialize<SolidManifest>(sr.ReadToEnd(), _json)
                       ?? new SolidManifest();

        foreach (var e in manifest.Entries)
        {
            var blobEntry = zip.GetEntry("_blobs/" + e.Blob)
                            ?? throw new InvalidDataException($"Missing blob {e.Blob} for {e.Path}");
            var destFile = Path.Combine(destDir, e.Path.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            blobEntry.ExtractToFile(destFile, overwrite: true);
        }
    }

    /// <summary>
    /// Expands a solid zip's content-addressed store into loose files under
    /// <paramref name="destDir"/>: <c>_payload-manifest.json</c> plus one <c>_blobs/&lt;hash&gt;</c>
    /// file per unique blob. This is the shape the update feed serves — a delta updater fetches
    /// the manifest, diffs blob hashes against what is installed, and downloads only the blobs it
    /// is missing by <c>&lt;blobBaseUrl&gt;/&lt;hash&gt;</c>.
    /// </summary>
    public static void ExpandSolidToLooseStore(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var mEntry = zip.GetEntry(ManifestName)
                     ?? throw new InvalidDataException("Not a solid payload (missing manifest); a delta feed needs solid compression.");

        Directory.CreateDirectory(Path.Combine(destDir, "_blobs"));
        mEntry.ExtractToFile(Path.Combine(destDir, ManifestName), overwrite: true);

        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("_blobs/", StringComparison.Ordinal) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            var dest = Path.Combine(destDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    // ── Shared extraction (solid + plain zip) ──────────────────────────────

    /// <summary>
    /// Extracts a payload archive into <paramref name="extractRoot"/> and returns the payload
    /// root. Solid zips materialize base-less into <paramref name="extractRoot"/>; plain zips
    /// (built with a base folder) resolve to <c>extractRoot/&lt;folder&gt;</c> when present.
    /// </summary>
    public static string ExtractZip(string zipPath, string extractRoot, string folder)
    {
        if (IsSolid(zipPath))
        {
            ExtractSolid(zipPath, extractRoot);
            return extractRoot;
        }
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
        var sub = Path.Combine(extractRoot, folder);
        return Directory.Exists(sub) ? sub : extractRoot;
    }

    // ── LZMA2 via 7z (best-effort) ─────────────────────────────────────────

    /// <summary>Locates 7z.exe on PATH or in Program Files. Null when not installed.</summary>
    public static string? FindSevenZip()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
                try { var c = Path.Combine(dir, "7z.exe"); if (File.Exists(c)) return c; }
                catch (Exception ex) { Diag.Debug("PayloadPackager", "7z PATH entry skipped", ex); }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var c = Path.Combine(root, "7-Zip", "7z.exe");
            if (File.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>Creates a .7z archive via 7z.exe. Returns false (with error) if 7z is unavailable.</summary>
    public static bool TryCreateLzma(string srcDir, string outPath, int level, out string error)
    {
        var sevenZ = FindSevenZip();
        if (sevenZ == null) { error = "7z.exe not found — cannot create LZMA payload."; return false; }
        if (File.Exists(outPath)) File.Delete(outPath);

        var mx = Math.Clamp(level, 0, 9);
        var args = $"a -t7z -mx={mx} -y \"{outPath}\" \"{srcDir.TrimEnd('\\', '/')}\\*\"";
        return RunSevenZip(sevenZ, args, out error);
    }

    /// <summary>Extracts a .7z archive via 7z.exe. Returns false (with error) if 7z is unavailable.</summary>
    public static bool TryExtractLzma(string archivePath, string destDir, out string error)
    {
        var sevenZ = FindSevenZip();
        if (sevenZ == null) { error = "7z.exe not found on the target machine — cannot extract LZMA payload."; return false; }
        Directory.CreateDirectory(destDir);
        var args = $"x -y -o\"{destDir}\" \"{archivePath}\"";
        return RunSevenZip(sevenZ, args, out error);
    }

    private static bool RunSevenZip(string exe, string args, out string error)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe, Arguments = args,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            p.WaitForExit(120_000);
            error = p.StandardError.ReadToEnd().Trim();
            return p.ExitCode == 0;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // ── manifest model ──
    private sealed class SolidManifest { public bool Solid { get; set; } public List<SolidEntry> Entries { get; set; } = new(); }
    private sealed class SolidEntry { public string Path { get; set; } = ""; public string Blob { get; set; } = ""; public long Size { get; set; } }
}
