using System.Collections;
using System.Text.Json;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public enum ProjectChangeCategory { Identity, Destinations, Package, Resources, Other }
public enum ProjectChangeKind { Added, Removed, Changed }
public sealed record ProjectComparisonEntry(ProjectChangeCategory Category, ProjectChangeKind Kind,
    string Path, string Before, string After);
public sealed record ProjectComparisonSnapshot(DateTimeOffset CapturedUtc, IReadOnlyList<ProjectComparisonEntry> Changes);

public static class ProjectComparison
{
    /// <summary>Canonical configuration review. Known protected values are excluded, including secret references.
    /// Sensitivity declared by either side also masks the corresponding member on the other side.</summary>
    public static ProjectComparisonSnapshot Create(InstallProject before, InstallProject after)
    {
        var capture = CaptureProtected(before, after);
        var entries = ProjectAuthoringWorkspace.DiffCanonicalJson(capture.before, capture.after).Select(entry =>
            new ProjectComparisonEntry(Category(entry.Path), entry.CurrentValue.Length == 0 ? ProjectChangeKind.Added
                : entry.UpdatedValue.Length == 0 ? ProjectChangeKind.Removed : ProjectChangeKind.Changed,
                entry.Path, entry.CurrentValue, entry.UpdatedValue)).ToArray();
        return new(DateTimeOffset.UtcNow, Array.AsReadOnly(entries));
    }

    public static string ProtectedCanonicalJson(InstallProject project)
    {
        using var document = JsonDocument.Parse(CaptureProtected(project, project).after);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static (string before, string after) CaptureProtected(InstallProject before, InstallProject after)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        var sensitiveMembers = new HashSet<(Type, string)>();
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in new[] { before, after })
            AuthoringHistory.Walk(project, (_, _) => { }, (owner, member, _, _, sensitive) =>
            {
                if (!sensitive) return;
                if (owner is IDictionary) sensitiveKeys.Add(member);
                else sensitiveMembers.Add((owner.GetType(), member));
            });
        object? Redact(object owner, string member, object? value)
            => value is string text && (sensitiveMembers.Contains((owner.GetType(), member))
                || owner is IDictionary && sensitiveKeys.Contains(member) || AuthoringHistory.Sensitive(owner, member, text))
                ? "<protected>" : value;
        string Capture(InstallProject project) => JsonSerializer.Serialize(ProjectCanonicalJsonExporter.ToObject(project, Redact));
        return (Capture(before), Capture(after));
    }

    private static ProjectChangeCategory Category(string path)
    {
        var top = path[2..].Split('.', '[')[0];
        if (top is "appName" or "appId" or "appVersion" or "appPublisher" or "projectName" or "sideBySide") return ProjectChangeCategory.Identity;
        if (top is "defaultDirName" or "defaultGroupName" or "defaultScope" or "sourceDirectory"
            || path.EndsWith(".destinationPath", StringComparison.Ordinal) || path.EndsWith(".physicalPath", StringComparison.Ordinal)) return ProjectChangeCategory.Destinations;
        if (top is "outputFormat" or "outputDir" or "outputBaseFilename" or "compression" or "compressPayload" or "singleFile"
            or "selfContained" or "prefer64Bit" or "payloadSource" or "payloadUrl" or "payloadSha256"
            || top.StartsWith("msix", StringComparison.Ordinal) || top.StartsWith("codeSign", StringComparison.Ordinal)
            || top.StartsWith("appInstaller", StringComparison.Ordinal)) return ProjectChangeCategory.Package;
        if (path.Contains('[')) return ProjectChangeCategory.Resources;
        return ProjectChangeCategory.Other;
    }
}
