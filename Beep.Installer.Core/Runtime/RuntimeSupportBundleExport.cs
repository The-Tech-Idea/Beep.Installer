using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beep.Installer.Engine;

public enum SupportBundlePreviewError { None, Unavailable, TooLarge, Invalid }

/// <summary>Validated and re-redacted captured diagnostics, detached from the live installer.</summary>
public sealed class RuntimeSupportBundlePreview
{
    internal RuntimeSupportBundlePreview(string sourcePath, string sourceSha256, string json, SupportBundlePreviewError error)
    { SourcePath = sourcePath; SourceSha256 = sourceSha256; Json = json; Error = error; }
    public string SourcePath { get; }
    public string SourceSha256 { get; }
    public string Json { get; }
    public SupportBundlePreviewError Error { get; }
    public bool CanExport => Error == SupportBundlePreviewError.None;
}

public static class RuntimeSupportBundleExport
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public static RuntimeSupportBundlePreview ReadPreview(string path)
    {
        RuntimeSupportBundlePreview Fail(SupportBundlePreviewError error) => new("", "", "", error);
        try
        {
            var fullPath = Path.GetFullPath(path);
            using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumBytes) return Fail(SupportBundlePreviewError.TooLarge);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = file.Read(buffer)) > 0)
            {
                if (bytes.Length + read > MaximumBytes) return Fail(SupportBundlePreviewError.TooLarge);
                bytes.Write(buffer, 0, read);
            }
            var content = bytes.ToArray();
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (HasDuplicateKeys(root) || root.ValueKind != JsonValueKind.Object
                || root.GetProperty("schemaVersion").GetString() != "1.0"
                || root.GetProperty("action").GetString() is not ("install" or "repair" or "uninstall")
                || root.GetProperty("success").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.GetProperty("exitCode").TryGetInt32(out _)
                || !root.GetProperty("privacy").GetProperty("redactionApplied").GetBoolean()
                || !root.GetProperty("privacy").GetProperty("sharingRequiresReview").GetBoolean()
                || !ValidOutcome(root))
                return Fail(SupportBundlePreviewError.Invalid);
            var redacted = RuntimeSupportBundleGenerator.RedactJsonDocument(Encoding.UTF8.GetString(content));
            var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            return new(fullPath, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), json, SupportBundlePreviewError.None);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return Fail(SupportBundlePreviewError.Invalid); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Fail(SupportBundlePreviewError.Unavailable); }
    }

    private static bool ValidOutcome(JsonElement root)
    {
        if (!root.TryGetProperty("operationOutcome", out var outcome) || outcome.ValueKind == JsonValueKind.Null) return true;
        if (outcome.ValueKind != JsonValueKind.Object || outcome.GetProperty("succeeded").GetBoolean() != root.GetProperty("success").GetBoolean()
            || outcome.GetProperty("fullRecoveryVerified").GetBoolean()) return false;
        var attempted = outcome.GetProperty("rollbackAttempted").GetBoolean();
        _ = outcome.GetProperty("restartRequired").GetBoolean();
        var requested = outcome.GetProperty("rollbackActionsRequested").GetInt32();
        var completed = outcome.GetProperty("rollbackActionsCompleted").GetInt32();
        if (requested < 0 || completed < 0 || completed > requested || (!attempted && completed != 0)) return false;
        if (!Enum.TryParse<BackupRecoveryState>(outcome.GetProperty("backupRecovery").GetString(), out var backup) || !Enum.IsDefined(backup)) return false;
        return !root.GetProperty("success").GetBoolean() || (!attempted && backup == BackupRecoveryState.NotAttempted);
    }

    public static void Export(RuntimeSupportBundlePreview preview, string destination)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.CanExport) throw new InvalidOperationException("A valid support preview is required.");
        var output = Path.GetFullPath(destination);
        if (string.Equals(output, preview.SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a destination different from the captured support artifact.");
        // Export precisely the reviewed snapshot, preserving the original privacy/result fields.
        // The source can disappear or change after review without changing the exported evidence.
        var directory = Path.GetDirectoryName(output)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var content = Encoding.UTF8.GetBytes(preview.Json); stream.Write(content); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool HasDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateKeys(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) if (HasDuplicateKeys(child)) return true;
        return false;
    }
}
