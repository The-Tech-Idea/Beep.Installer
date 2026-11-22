using System.Text.Json;

namespace Beep.Installer.Engine;

public sealed record TemplateGalleryEntry(string Id, string Name, string Description, string Category,
    int ComponentCount, IReadOnlyList<string> SampleShortcuts, bool HasSample);

public static class TemplateGallery
{
    public static IReadOnlyList<TemplateGalleryEntry> Describe(IEnumerable<ProjectTemplate> templates)
    {
        var builtinIds = ProjectTemplates.Builtins.Select(template => template.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(templates.Select(template =>
        {
            // Sample content comes from the same existing factory used for creation. No source
            // files, live project, validation, trust decision or installation are involved.
            var sample = template.Blueprint?.CreateProject() ?? (builtinIds.Contains(template.Id) ? ProjectTemplates.Create(template.Id, "SampleApp", "1.0.0", "Sample publisher", "") : null);
            return new TemplateGalleryEntry(template.Id, template.Name, template.Description, template.Category,
                sample?.Components.Count ?? 0,
                Array.AsReadOnly(sample?.Shortcuts.Select(shortcut => $"{shortcut.Location}: {shortcut.TargetPath}").ToArray() ?? Array.Empty<string>()), sample is not null);
        }).ToArray());
    }
}

public enum TemplateFavoritesError { None, Unavailable, Invalid }
public sealed record TemplateFavoritesResult(IReadOnlyList<string> Ids, TemplateFavoritesError Error)
{
    public bool Available => Error == TemplateFavoritesError.None;
}

/// <summary>Only template identifiers are persisted. Favorites confer no template trust.</summary>
public sealed class TemplateFavoritesStore
{
    private static readonly object Gate = new();
    private readonly string _path;
    public TemplateFavoritesStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeepInstaller", "template-favorites.json"));

    public TemplateFavoritesResult Read()
    {
        try
        {
            // Acquire before Gate to keep the same lock order as the writer. Readers must
            // finish their file handles before another process replaces the destination.
            using var lease = TemplatePreferenceLock.Acquire(_path);
            lock (Gate) return ReadCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or System.Security.SecurityException)
        { return new(Array.Empty<string>(), TemplateFavoritesError.Unavailable); }
    }
    private TemplateFavoritesResult ReadCore()
    {
        TemplateFavoritesResult Fail(TemplateFavoritesError error) => new(Array.Empty<string>(), error);
        try
        {
            if (!File.Exists(_path))
            {
                // File.Exists also returns false for inaccessible paths. Check the file directly
                // so an unreadable existing favorites file is never replaced as an empty one.
                try { using var probe = File.OpenRead(_path); }
                catch (FileNotFoundException) { return Fail(TemplateFavoritesError.None); }
                catch (DirectoryNotFoundException) { return Fail(TemplateFavoritesError.None); }
            }
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 64 * 1024) return Fail(TemplateFavoritesError.Invalid);
            using var document = JsonDocument.Parse(file);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != root.EnumerateObject().Count()
                || root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("favoriteIds").ValueKind != JsonValueKind.Array)
                return Fail(TemplateFavoritesError.Invalid);
            var ids = root.GetProperty("favoriteIds").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
            if (ids.Length > 256 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
                || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length) return Fail(TemplateFavoritesError.Invalid);
            return new(Array.AsReadOnly(ids), TemplateFavoritesError.None);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return Fail(TemplateFavoritesError.Invalid); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Fail(TemplateFavoritesError.Unavailable); }
    }

    public TemplateFavoritesResult SetFavorite(string id, bool favorite)
    {
        try { using var lease = TemplatePreferenceLock.Acquire(_path); return SetFavoriteWithinProcess(id, favorite); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or System.Security.SecurityException)
        { return new(Array.Empty<string>(), TemplateFavoritesError.Unavailable); }
    }

    private TemplateFavoritesResult SetFavoriteWithinProcess(string id, bool favorite)
    {
        lock (Gate)
        {
            var current = ReadCore(); if (!current.Available) return current;
            if (!ProjectTemplates.Builtins.Any(template => template.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                && !(id.StartsWith("signed:", StringComparison.Ordinal) && id.Length == 71 && id[7..].All(Uri.IsHexDigit)))
                return new(current.Ids, TemplateFavoritesError.Invalid);
            var ids = current.Ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (favorite) ids.Add(id); else ids.Remove(id);
            var values = ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length > 256) return new(current.Ids, TemplateFavoritesError.Invalid);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var json = JsonSerializer.Serialize(new { schemaVersion = 1, favoriteIds = values });
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { var bytes = System.Text.Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(flushToDisk: true); }
                File.Move(temporary, _path, overwrite: true);
                return new(Array.AsReadOnly(values), TemplateFavoritesError.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new(current.Ids, TemplateFavoritesError.Unavailable); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
