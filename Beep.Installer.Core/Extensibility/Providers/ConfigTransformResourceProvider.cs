using Beep.Installer.Engine;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;

namespace Beep.Installer.Extensibility.Providers;

public sealed class ConfigTransformResourceProvider : IResourceProvider
{
    public string ResourceType => "config.transform";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var targetPath = ResolvePath(Input(operation, "targetPath"), context);
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["targetPath"] = targetPath,
            ["format"] = Format(operation),
            ["keyPath"] = Input(operation, "keyPath"),
            ["section"] = Input(operation, "section"),
            ["backupPath"] = BackupPath(targetPath)
        };

        if (!File.Exists(targetPath))
            return new ResourceDetectionResult { Exists = false, Facts = facts };

        facts["targetExists"] = "true";
        facts["backupExists"] = File.Exists(BackupPath(targetPath)) ? "true" : "false";
        return new ResourceDetectionResult
        {
            Exists = IsDesiredState(operation, targetPath, context),
            Facts = facts
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var targetPath = ResolvePath(Input(operation, "targetPath"), context);
        if (string.IsNullOrWhiteSpace(targetPath))
            return Error("BI5201", $"{operation.Id}.targetPath", "Configuration transform operation is missing targetPath.");
        if (string.IsNullOrWhiteSpace(Input(operation, "keyPath")))
            return Error("BI5202", $"{operation.Id}.keyPath", "Configuration transform operation is missing keyPath.");
        if (Format(operation) == "ini" && string.IsNullOrWhiteSpace(Input(operation, "section")))
            return Error("BI5203", $"{operation.Id}.section", "INI configuration transform operation is missing section.");
        if (Operation(operation) != "delete"
            && !TryResolveValue(Input(operation, "value"), context, out _, out var secretError))
            return Error("BI5205", $"{operation.Id}.value", secretError!);
        if (!context.DryRun && !File.Exists(targetPath))
            return Error("BI5204", $"{operation.Id}.targetPath", $"Configuration transform target '{targetPath}' does not exist.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.None : ResourceChangeKind.Update,
            Operations = detection.Exists ? new List<CompiledInstallOperation>() : new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: configuration transform would be applied."
            };

        var targetPath = ResolvePath(Input(operation, "targetPath"), context);
        if (IsDesiredState(operation, targetPath, context))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Configuration transform is already applied."
            };

        if (BoolInput(operation, "backupOnInstall", true))
            PreserveBackup(targetPath);

        try
        {
            ApplyTransform(operation, targetPath, context);
        }
        catch (Exception ex)
        {
            return Error("BI5210", operation.Id, $"Failed to apply configuration transform: {ex.Message}");
        }

        return new ResourceProviderResult { Message = "Configuration transform applied." };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!BoolInput(operation, "restoreOnRollback", true))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Configuration transform rollback skipped by policy."
            };

        var targetPath = ResolvePath(Input(operation, "targetPath"), context);
        var backupPath = BackupPath(targetPath);
        if (!File.Exists(backupPath))
            return Error("BI5220", operation.Id, $"Configuration transform rollback backup '{backupPath}' is missing.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
            File.Copy(backupPath, targetPath, overwrite: true);
            File.Delete(backupPath);
        }
        catch (Exception ex)
        {
            return Error("BI5221", operation.Id, $"Failed to restore configuration transform backup: {ex.Message}");
        }

        return new ResourceProviderResult { Message = "Configuration transform backup restored." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: configuration transform verification would inspect the target file."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var targetPath = ResolvePath(Input(operation, "targetPath"), context);
        return IsDesiredState(operation, targetPath, context)
            ? new ResourceProviderResult { Message = "Configuration transform desired state verified." }
            : Error("BI5230", operation.Id, "Configuration transform verification failed because the target is not in the desired state.");
    }

    private static void ApplyTransform(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        switch (Format(operation))
        {
            case "xml":
                ApplyXml(operation, targetPath, context);
                break;
            case "ini":
                ApplyIni(operation, targetPath, context);
                break;
            default:
                ApplyJson(operation, targetPath, context);
                break;
        }
    }

    private static bool IsDesiredState(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        try
        {
            return Format(operation) switch
            {
                "xml" => IsXmlDesired(operation, targetPath, context),
                "ini" => IsIniDesired(operation, targetPath, context),
                _ => IsJsonDesired(operation, targetPath, context)
            };
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyJson(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var root = JsonNode.Parse(File.ReadAllText(targetPath))?.AsObject()
                   ?? throw new InvalidOperationException("JSON root must be an object.");
        var segments = JsonSegments(Input(operation, "keyPath")).ToList();
        if (segments.Count == 0)
            throw new InvalidOperationException("JSON key path is empty.");

        var parent = root;
        foreach (var segment in segments.Take(segments.Count - 1))
        {
            if (parent[segment] is not JsonObject child)
            {
                child = new JsonObject();
                parent[segment] = child;
            }

            parent = child;
        }

        var leaf = segments[^1];
        if (Operation(operation) == "delete")
            parent.Remove(leaf);
        else
            parent[leaf] = Value(operation, context);

        File.WriteAllText(targetPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static bool IsJsonDesired(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var node = JsonNode.Parse(File.ReadAllText(targetPath));
        foreach (var segment in JsonSegments(Input(operation, "keyPath")))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node))
                return Operation(operation) == "delete";
        }

        return Operation(operation) == "delete"
            ? node == null
            : string.Equals(node?.GetValue<string>(), Value(operation, context), StringComparison.Ordinal);
    }

    private static void ApplyXml(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var doc = LoadXml(targetPath);
        var node = doc.SelectSingleNode(Input(operation, "keyPath"))
                   ?? throw new InvalidOperationException($"XML selector '{Input(operation, "keyPath")}' did not match a node.");

        if (Operation(operation) == "delete")
        {
            if (node is XmlAttribute attr)
                attr.OwnerElement?.Attributes.Remove(attr);
            else
                node.ParentNode?.RemoveChild(node);
        }
        else
        {
            node.Value = Value(operation, context);
            if (node is XmlElement element)
                element.InnerText = Value(operation, context);
        }

        using var writer = XmlWriter.Create(targetPath, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        doc.Save(writer);
    }

    private static bool IsXmlDesired(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var node = LoadXml(targetPath).SelectSingleNode(Input(operation, "keyPath"));
        return Operation(operation) == "delete"
            ? node == null
            : string.Equals(node is XmlElement element ? element.InnerText : node?.Value, Value(operation, context), StringComparison.Ordinal);
    }

    private static XmlDocument LoadXml(string targetPath)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.Load(targetPath);
        return doc;
    }

    private static void ApplyIni(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var lines = File.ReadAllLines(targetPath).ToList();
        var section = Input(operation, "section");
        var key = Input(operation, "keyPath");
        var sectionIndex = FindIniSection(lines, section);
        if (sectionIndex < 0)
        {
            lines.Add("");
            lines.Add($"[{section}]");
            sectionIndex = lines.Count - 1;
        }

        var endIndex = FindIniSectionEnd(lines, sectionIndex);
        var keyIndex = FindIniKey(lines, key, sectionIndex + 1, endIndex);
        if (Operation(operation) == "delete")
        {
            if (keyIndex >= 0)
                lines.RemoveAt(keyIndex);
        }
        else if (keyIndex >= 0)
        {
            lines[keyIndex] = $"{key}={Value(operation, context)}";
        }
        else
        {
            lines.Insert(endIndex, $"{key}={Value(operation, context)}");
        }

        File.WriteAllLines(targetPath, lines, new UTF8Encoding(false));
    }

    private static bool IsIniDesired(CompiledInstallOperation operation, string targetPath, ResourceProviderContext context)
    {
        var lines = File.ReadAllLines(targetPath).ToList();
        var sectionIndex = FindIniSection(lines, Input(operation, "section"));
        if (sectionIndex < 0)
            return Operation(operation) == "delete";

        var endIndex = FindIniSectionEnd(lines, sectionIndex);
        var keyIndex = FindIniKey(lines, Input(operation, "keyPath"), sectionIndex + 1, endIndex);
        if (Operation(operation) == "delete")
            return keyIndex < 0;

        if (keyIndex < 0)
            return false;

        var idx = lines[keyIndex].IndexOf('=');
        return idx >= 0 && string.Equals(lines[keyIndex][(idx + 1)..].Trim(), Value(operation, context), StringComparison.Ordinal);
    }

    private static int FindIniSection(IReadOnlyList<string> lines, string section)
    {
        var header = $"[{section}]";
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    private static int FindIniSectionEnd(IReadOnlyList<string> lines, int sectionIndex)
    {
        for (var i = sectionIndex + 1; i < lines.Count; i++)
            if (lines[i].TrimStart().StartsWith("[", StringComparison.Ordinal))
                return i;

        return lines.Count;
    }

    private static int FindIniKey(IReadOnlyList<string> lines, string key, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            var idx = trimmed.IndexOf('=');
            if (idx >= 0 && trimmed[..idx].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static IEnumerable<string> JsonSegments(string keyPath)
        => keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void PreserveBackup(string targetPath)
    {
        var backupPath = BackupPath(targetPath);
        if (File.Exists(backupPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? ".");
        File.Copy(targetPath, backupPath, overwrite: false);
    }

    private static string BackupPath(string targetPath)
        => targetPath + ".beepbak";

    private static string ResolvePath(string value, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var resolved = value
            .Replace("{InstallPath}", context.InstallRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            resolved = resolved
                .Replace("{" + variable.Key + "}", variable.Value, StringComparison.OrdinalIgnoreCase)
                .Replace("%" + variable.Key + "%", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return Path.IsPathRooted(resolved)
            ? Path.GetFullPath(resolved)
            : Path.GetFullPath(Path.Combine(context.InstallRoot, resolved));
    }

    private static string Format(CompiledInstallOperation operation)
        => Input(operation, "format").ToLowerInvariant() switch
        {
            "xml" => "xml",
            "ini" => "ini",
            _ => "json"
        };

    private static string Operation(CompiledInstallOperation operation)
        => Input(operation, "operation").Equals("delete", StringComparison.OrdinalIgnoreCase) ? "delete" : "set";

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string Value(CompiledInstallOperation operation, ResourceProviderContext context)
        => TryResolveValue(Input(operation, "value"), context, out var resolved, out _)
            ? resolved
            : "";

    private static bool TryResolveValue(string value, ResourceProviderContext context, out string resolved, out string? error)
    {
        resolved = value;
        error = null;
        if (string.IsNullOrEmpty(value) || !SecretReference.IsReference(value))
            return true;

        if (!SecretReference.TryParse(value, out var reference, out error))
            return false;

        var secret = context.SecretProvider.Resolve(reference);
        if (!secret.Success)
        {
            error = secret.Error ?? $"Secret reference '{value}' could not be resolved.";
            return false;
        }

        resolved = secret.Value ?? "";
        return true;
    }

    private static ResourceProviderResult Error(string code, string path, string message)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Diagnostics = new List<ProjectSchemaDiagnostic>
            {
                new(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };
}
