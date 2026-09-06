using System.Text.Json;
using System.Text.Json.Nodes;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public sealed class ProjectAuthoringSnapshot
{
    public string SchemaVersion { get; init; } = "";
    public string CanonicalJson { get; init; } = "";
    public string Script { get; init; } = "";
    public string PlanJson { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class ProjectTemplateUpdatePreview
{
    public string TemplateId { get; init; } = "";
    public ProjectAuthoringSnapshot Current { get; init; } = new();
    public ProjectAuthoringSnapshot Updated { get; init; } = new();
    public List<ProjectTemplateDiffEntry> Diff { get; init; } = new();
    public bool HasChanges => Diff.Count > 0 || Current.PlanHash != Updated.PlanHash;
}

public sealed class ProjectTemplateDiffEntry
{
    public string Path { get; init; } = "";
    public string CurrentValue { get; init; } = "";
    public string UpdatedValue { get; init; } = "";
}

public static class ProjectAuthoringWorkspace
{
    public static ProjectAuthoringSnapshot CreateSnapshot(
        InstallProject project,
        ProjectSchemaValidationOptions? validationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        ProjectSchemaService.NormalizeInMemory(project);
        var validation = ProjectSchemaService.Validate(project, validationOptions ?? new ProjectSchemaValidationOptions());
        var planResult = new InstallPlanCompiler().Compile(project);
        var diagnostics = validation.Diagnostics
            .Concat(planResult.Diagnostics)
            .DistinctBy(d => $"{d.Severity}|{d.Code}|{d.Path}|{d.Message}")
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .ToList();

        return new ProjectAuthoringSnapshot
        {
            SchemaVersion = project.SchemaVersion,
            CanonicalJson = ProjectCanonicalJsonExporter.ToJson(project),
            Script = InstallerScriptSerializer.Write(project),
            PlanJson = planResult.Plan is null ? "" : InstallPlanCompiler.ToJson(planResult.Plan),
            PlanHash = planResult.Plan?.PlanHash ?? "",
            Diagnostics = diagnostics
        };
    }

    public static ProjectTemplateUpdatePreview PreviewTemplateUpdate(InstallProject current, string templateId)
    {
        ArgumentNullException.ThrowIfNull(current);

        var target = CreateTemplateCandidate(current, templateId);

        var currentSnapshot = CreateSnapshot(current, new ProjectSchemaValidationOptions { Strict = true });
        var updatedSnapshot = CreateSnapshot(target, new ProjectSchemaValidationOptions { Strict = true });

        return new ProjectTemplateUpdatePreview
        {
            TemplateId = templateId,
            Current = currentSnapshot,
            Updated = updatedSnapshot,
            Diff = DiffCanonicalJson(currentSnapshot.CanonicalJson, updatedSnapshot.CanonicalJson)
        };
    }

    public static InstallProject CreateTemplateCandidate(InstallProject current, string templateId)
    {
        ArgumentNullException.ThrowIfNull(current);

        var target = ProjectTemplates.Create(
            templateId,
            current.AppName,
            current.AppVersion,
            current.AppPublisher,
            current.SourceDirectory);

        target.AppId = current.AppId;
        target.OutputDir = current.OutputDir;
        target.OutputBaseFilename = current.OutputBaseFilename;
        target.DefaultDirName = current.DefaultDirName;
        target.DefaultGroupName = current.DefaultGroupName;
        target.DefaultScope = current.DefaultScope;
        target.PrivilegesRequired = current.PrivilegesRequired;
        target.CodeSignCertificatePath = current.CodeSignCertificatePath;
        target.CodeSignCertificatePassword = current.CodeSignCertificatePassword;
        target.CodeSignStoreName = current.CodeSignStoreName;
        target.CodeSignStoreLocation = current.CodeSignStoreLocation;
        target.CodeSignStoreThumbprint = current.CodeSignStoreThumbprint;
        target.CodeSignStoreSubject = current.CodeSignStoreSubject;
        target.CodeSignTimestampUrl = current.CodeSignTimestampUrl;
        target.CodeSignRemoteProvider = current.CodeSignRemoteProvider;
        target.CodeSignRemoteEndpoint = current.CodeSignRemoteEndpoint;
        target.CodeSignRemoteKeyId = current.CodeSignRemoteKeyId;
        target.CodeSignRemoteCredential = current.CodeSignRemoteCredential;

        return target;
    }

    private static List<ProjectTemplateDiffEntry> DiffCanonicalJson(string currentJson, string updatedJson)
    {
        var current = JsonNode.Parse(currentJson);
        var updated = JsonNode.Parse(updatedJson);
        var currentValues = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var updatedValues = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Flatten(current, "$", currentValues);
        Flatten(updated, "$", updatedValues);

        return currentValues.Keys
            .Union(updatedValues.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !string.Equals(
                currentValues.GetValueOrDefault(k, ""),
                updatedValues.GetValueOrDefault(k, ""),
                StringComparison.Ordinal))
            .Select(k => new ProjectTemplateDiffEntry
            {
                Path = k,
                CurrentValue = currentValues.GetValueOrDefault(k, ""),
                UpdatedValue = updatedValues.GetValueOrDefault(k, "")
            })
            .ToList();
    }

    private static void Flatten(JsonNode? node, string path, IDictionary<string, string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    Flatten(property.Value, path + "." + property.Key, values);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Flatten(array[i], $"{path}[{i}]", values);
                break;
            case null:
                values[path] = "null";
                break;
            default:
                values[path] = node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                break;
        }
    }
}
