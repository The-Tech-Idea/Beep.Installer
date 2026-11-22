using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beep.Installer.Engine;

public enum TemplateLibraryError { None, Invalid, Unavailable }
public sealed record TemplateLibraryEntry(string PackageDirectory, string PublicKeyPath, string ProjectSha256, string TrustedKeySha256, string ManifestSha256);
public sealed record TemplateLibraryReadResult(IReadOnlyList<TemplateLibraryEntry> Entries, TemplateLibraryError Error);
public sealed record TemplateLibraryDiscovery(IReadOnlyList<ProjectTemplate> Templates, int RejectedEntries, TemplateLibraryError Error);

/// <summary>Saved source references and reviewed hashes. Every discovery verifies the source anew.</summary>
public sealed class TemplateLibraryStore
{
    private readonly string _path;
    public TemplateLibraryStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeepInstaller", "template-library.json"));
    public TemplateLibraryReadResult Read()
    {
        try { using var lease = TemplatePreferenceLock.Acquire(_path); return ReadCore(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or TimeoutException)
        { return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.Unavailable); }
    }
    private TemplateLibraryReadResult ReadCore()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 256 * 1024) return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.Invalid);
            using var document = JsonDocument.Parse(file); var root = document.RootElement;
            if (!Fields(root, "schemaVersion", "entries") || root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException();
            var entries = new List<TemplateLibraryEntry>();
            foreach (var row in root.GetProperty("entries").EnumerateArray())
            {
                if (entries.Count == 32 || !Fields(row, "packageDirectory", "publicKeyPath", "projectSha256", "trustedKeySha256", "manifestSha256")) throw new InvalidDataException();
                var package = row.GetProperty("packageDirectory").GetString()!; var key = row.GetProperty("publicKeyPath").GetString()!;
                var hash = row.GetProperty("projectSha256").GetString()!; var keyHash = row.GetProperty("trustedKeySha256").GetString()!;
                var manifestHash = row.GetProperty("manifestSha256").GetString()!;
                if (!ValidPath(package) || !ValidPath(key) || !Hash(hash) || !Hash(keyHash) || !Hash(manifestHash)) throw new InvalidDataException();
                if (entries.Any(entry => entry.ProjectSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException();
                entries.Add(new(package, key, hash, keyHash, manifestHash));
            }
            return new(entries.AsReadOnly(), TemplateLibraryError.None);
        }
        catch (FileNotFoundException) { return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.None); }
        catch (DirectoryNotFoundException) { return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.None); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        { return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.Invalid); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(Array.Empty<TemplateLibraryEntry>(), TemplateLibraryError.Unavailable); }
    }
    public TemplateLibraryError Remember(string packageDirectory, string publicKeyPath, VerifiedTemplateBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        string? temporary = null;
        try
        {
            using var lease = TemplatePreferenceLock.Acquire(_path);
            var current = ReadCore(); if (current.Error != TemplateLibraryError.None) return current.Error;
            var entry = new TemplateLibraryEntry(Path.GetFullPath(packageDirectory), Path.GetFullPath(publicKeyPath), blueprint.ProjectSha256, blueprint.TrustedKeySha256, blueprint.ManifestSha256);
            if (!ValidPath(entry.PackageDirectory) || !ValidPath(entry.PublicKeyPath)) return TemplateLibraryError.Invalid;
            var entries = current.Entries.Where(old => !old.ProjectSha256.Equals(entry.ProjectSha256, StringComparison.OrdinalIgnoreCase)
                && !old.PackageDirectory.Equals(entry.PackageDirectory, StringComparison.OrdinalIgnoreCase)).Append(entry).ToArray();
            if (entries.Length > 32) return TemplateLibraryError.Invalid;
            var json = JsonSerializer.Serialize(new { schemaVersion = 1, entries = entries.Select(value => new
                { packageDirectory = value.PackageDirectory, publicKeyPath = value.PublicKeyPath, projectSha256 = value.ProjectSha256, trustedKeySha256 = value.TrustedKeySha256, manifestSha256 = value.ManifestSha256 }) });
            var bytes = Encoding.UTF8.GetBytes(json); if (bytes.Length > 256 * 1024) return TemplateLibraryError.Invalid;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!); temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, _path, true); return TemplateLibraryError.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or TimeoutException)
        { return TemplateLibraryError.Unavailable; }
        catch (ArgumentException) { return TemplateLibraryError.Invalid; }
        finally
        { if (temporary is not null) try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
    public TemplateLibraryDiscovery Discover(CancellationToken cancellationToken = default)
    {
        var saved = Read(); var templates = new List<ProjectTemplate>(); var rejected = 0;
        foreach (var entry in saved.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = VerifiedTemplateImporter.Import(entry.PackageDirectory, entry.PublicKeyPath);
            var blueprint = result.Blueprint;
            if (blueprint is null || !blueprint.ProjectSha256.Equals(entry.ProjectSha256, StringComparison.OrdinalIgnoreCase)
                || !blueprint.TrustedKeySha256.Equals(entry.TrustedKeySha256, StringComparison.OrdinalIgnoreCase)
                || !blueprint.ManifestSha256.Equals(entry.ManifestSha256, StringComparison.OrdinalIgnoreCase)) { rejected++; continue; }
            templates.Add(new() { Id = blueprint.Id, Name = blueprint.Name, Description = blueprint.Description, Category = blueprint.Category, Blueprint = blueprint });
        }
        return new(templates.AsReadOnly(), rejected, saved.Error);
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool ValidPath(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Any(char.IsControl) && Path.IsPathFullyQualified(value);
    private static bool Fields(JsonElement value, params string[] expected) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal));
}

internal sealed class TemplatePreferenceLock : IDisposable
{
    private readonly Mutex _mutex;
    private TemplatePreferenceLock(Mutex mutex) => _mutex = mutex;
    internal static TemplatePreferenceLock Acquire(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        var mutex = new Mutex(false, "BeepTemplatePreferences-" + hash);
        try
        {
            bool acquired; try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException(); return new(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
