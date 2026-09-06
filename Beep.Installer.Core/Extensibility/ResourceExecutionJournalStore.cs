using Beep.Installer.Engine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Extensibility;

/// <summary>
/// Durable store for provider execution journals.
///
/// The resource journal is the installer's recovery spine: it records validate, detect,
/// plan, apply, verify and rollback decisions in execution order so future resume,
/// audit and enterprise reporting features can reason about what actually happened.
/// </summary>
public sealed class ResourceExecutionJournalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    public const string CurrentSchemaVersion = "1.0";

    public ResourceExecutionJournalStore(string journalPath)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
            throw new ArgumentException("Journal path is required.", nameof(journalPath));

        JournalPath = Path.GetFullPath(journalPath);
    }

    public string JournalPath { get; }

    internal static string Serialize(ResourceExecutionJournal journal) => JsonSerializer.Serialize(journal, SerializerOptions);
    internal static ResourceExecutionJournal Deserialize(string contents)
        => JsonSerializer.Deserialize<ResourceExecutionJournal>(contents, SerializerOptions)
            ?? throw new InvalidDataException("Resource journal is empty.");

    public static string LocationPath(string installRoot, string appId)
        => DefaultPath(installRoot, appId) + ".location.json";

    public static string ResolvePath(string installRoot, string appId, string? explicitPath = null, string? journalOwnerRoot = null)
    {
        var root = Path.GetFullPath(installRoot);
        var canonical = DefaultPath(root, appId);
        var locationPath = LocationPath(root, appId);
        ValidateLocationPath(locationPath);
        string selected;
        if (File.Exists(locationPath))
        {
            var location = JsonSerializer.Deserialize<JournalLocation>(File.ReadAllText(locationPath), SerializerOptions);
            if (location is null || location.SchemaVersion != "1.0" || string.IsNullOrWhiteSpace(location.Path))
                throw new IOException("Installed journal location record is invalid.");
            selected = Path.GetFullPath(location.Path, root);
            if (SamePath(selected, locationPath) || SamePath(selected, canonical) || File.Exists(canonical))
                throw new IOException("Installed journal locations are conflicting or recursive.");
            if (!string.IsNullOrWhiteSpace(explicitPath) && !SamePath(selected, Path.GetFullPath(explicitPath)))
                throw new IOException("Explicit journal differs from the installation's recorded journal location.");
            if (!File.Exists(selected)) throw new IOException("Recorded installation journal is missing: " + selected);
        }
        else
        {
            selected = string.IsNullOrWhiteSpace(explicitPath) ? canonical : Path.GetFullPath(explicitPath);
            if (SamePath(selected, locationPath)) throw new IOException("Journal cannot overwrite its location record.");
            if (!SamePath(selected, canonical) && File.Exists(canonical))
                throw new IOException("A canonical journal already exists; selecting another journal would split recovery state.");
        }
        ValidateLocationPath(selected);
        if (!SamePath(selected, canonical) && File.Exists(selected))
        {
            var loaded = new ResourceExecutionJournalStore(selected).TryLoad();
            if (!loaded.Success || loaded.Journal is null) throw new IOException(loaded.Message);
            var identityError = ResourceJournalRecoveryService.ValidateAppId(loaded.Journal.Metadata, appId);
            if (identityError.Length > 0) throw new IOException(identityError);
            if (string.IsNullOrWhiteSpace(loaded.Journal.Metadata.InstallRoot)
                || !SamePath(loaded.Journal.Metadata.InstallRoot, journalOwnerRoot ?? root))
                throw new IOException("Custom journal is unreadable or belongs to another installation.");
        }
        return selected;
    }

    public static void RecordLocation(string installRoot, string appId, string journalPath)
    {
        var selected = ResolvePath(installRoot, appId, journalPath);
        if (SamePath(selected, DefaultPath(installRoot, appId))) return;
        if (!File.Exists(selected)) throw new IOException("Checkpoint the journal before recording its location.");
        var locationPath = LocationPath(installRoot, appId);
        if (File.Exists(locationPath)) return;
        var relative = Path.GetRelativePath(Path.GetFullPath(installRoot), selected);
        var stored = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative) ? selected : relative;
        AtomicFileWriter.WriteAllText(locationPath, JsonSerializer.Serialize(new JournalLocation { Path = stored }, SerializerOptions));
    }

    private sealed class JournalLocation
    {
        public string SchemaVersion { get; init; } = "1.0";
        public string Path { get; init; } = "";
    }

    private static bool SamePath(string first, string second)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void ValidateLocationPath(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Journal locations cannot traverse filesystem links.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Journal locations cannot be filesystem links.");
    }

    public static string DefaultPath(string installRoot, string appId)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            throw new ArgumentException("Install root is required.", nameof(installRoot));

        if (!Guid.TryParseExact(appId, "D", out var identity) || identity == Guid.Empty)
            throw new ArgumentException("Journal discovery requires a nonzero AppId GUID.", nameof(appId));
        return Path.Combine(
            Path.GetFullPath(installRoot),
            ".beep-installer",
            $"{identity:D}.resource-journal.json");
    }

    public ResourceExecutionJournal Load()
    {
        if (!File.Exists(JournalPath))
            return new ResourceExecutionJournal();

        using var stream = File.OpenRead(JournalPath);
        return JsonSerializer.Deserialize<ResourceExecutionJournal>(stream, SerializerOptions)
               ?? new ResourceExecutionJournal();
    }

    public ResourceExecutionJournalLoadResult TryLoad()
    {
        if (!File.Exists(JournalPath))
        {
            return ResourceExecutionJournalLoadResult.Fail(
                ResourceExecutionJournalLoadStatus.Missing,
                $"Typed resource journal not found at {JournalPath}.",
                JournalPath);
        }

        try
        {
            using var stream = File.OpenRead(JournalPath);
            var journal = JsonSerializer.Deserialize<ResourceExecutionJournal>(stream, SerializerOptions);
            if (journal?.Metadata == null)
            {
                return ResourceExecutionJournalLoadResult.Fail(
                    ResourceExecutionJournalLoadStatus.Corrupt,
                    $"Typed resource journal at {JournalPath} is empty or not a journal object.",
                    JournalPath);
            }

            if (!string.Equals(journal.Metadata.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                return ResourceExecutionJournalLoadResult.Fail(
                    ResourceExecutionJournalLoadStatus.Incompatible,
                    $"Typed resource journal schema '{journal.Metadata.SchemaVersion}' is not supported by this installer; expected '{CurrentSchemaVersion}'.",
                    JournalPath,
                    journal);
            }

            return new ResourceExecutionJournalLoadResult
            {
                Status = ResourceExecutionJournalLoadStatus.Loaded,
                JournalPath = JournalPath,
                Journal = journal,
                Message = "Typed resource journal loaded."
            };
        }
        catch (JsonException ex)
        {
            return ResourceExecutionJournalLoadResult.Fail(
                ResourceExecutionJournalLoadStatus.Corrupt,
                $"Typed resource journal at {JournalPath} is corrupt JSON: {ex.Message}",
                JournalPath);
        }
        catch (NotSupportedException ex)
        {
            return ResourceExecutionJournalLoadResult.Fail(
                ResourceExecutionJournalLoadStatus.Incompatible,
                $"Typed resource journal at {JournalPath} uses unsupported JSON content: {ex.Message}",
                JournalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ResourceExecutionJournalLoadResult.Fail(
                ResourceExecutionJournalLoadStatus.Unreadable,
                $"Typed resource journal at {JournalPath} could not be read: {ex.Message}",
                JournalPath);
        }
    }

    public void Save(ResourceExecutionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        journal.Metadata.UpdatedAt = DateTimeOffset.UtcNow;

        AtomicFileWriter.WriteAllText(JournalPath, Serialize(journal));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }


}

public enum ResourceExecutionJournalLoadStatus
{
    Loaded,
    Missing,
    Corrupt,
    Incompatible,
    Unreadable
}

public sealed class ResourceExecutionJournalLoadResult
{
    public ResourceExecutionJournalLoadStatus Status { get; init; }
    public string JournalPath { get; init; } = "";
    public string Message { get; init; } = "";
    public ResourceExecutionJournal? Journal { get; init; }
    public bool Success => Status == ResourceExecutionJournalLoadStatus.Loaded && Journal != null;

    public static ResourceExecutionJournalLoadResult Fail(
        ResourceExecutionJournalLoadStatus status,
        string message,
        string journalPath,
        ResourceExecutionJournal? journal = null)
        => new()
        {
            Status = status,
            JournalPath = journalPath,
            Message = message,
            Journal = journal
        };
}
