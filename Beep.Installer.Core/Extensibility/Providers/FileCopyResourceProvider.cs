using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility.Providers;

public sealed class FileCopyResourceProvider : IResourceProvider
{
    private readonly Dictionary<string, FileRollbackState> _rollback = new(StringComparer.Ordinal);

    public string ResourceType => "file.copy";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var destination = ResolveDestination(operation, context);
        if (string.IsNullOrWhiteSpace(destination))
            return new ResourceDetectionResult();

        return new ResourceDetectionResult
        {
            Exists = File.Exists(destination),
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination"] = destination
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!operation.Inputs.ContainsKey("source"))
            return Error("BI3001", $"{operation.Id}.source", "File copy operation is missing source.");
        if (!operation.Inputs.ContainsKey("destination"))
            return Error("BI3002", $"{operation.Id}.destination", "File copy operation is missing destination.");
        if (string.IsNullOrWhiteSpace(context.InstallRoot))
            return Error("BI3003", "ResourceProviderContext.InstallRoot", "InstallRoot is required.");

        var destination = ResolveDestination(operation, context);
        if (!IsUnderRoot(destination, context.InstallRoot))
            return Error("BI3004", $"{operation.Id}.destination", "File copy destination must stay under InstallRoot.");

        var source = ResolveSource(operation, context);
        if (string.IsNullOrWhiteSpace(source))
            return Error("BI3005", $"{operation.Id}.source", "File copy source resolved to an empty path.");

        if (!File.Exists(source) && Required(operation))
            return Error("BI3006", $"{operation.Id}.source", $"Required source file does not exist: {source}");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var source = ResolveSource(operation, context);
        var destination = ResolveDestination(operation, context);

        if (!File.Exists(source))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Optional source file is missing: {source}"
            };

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Dry run: would copy '{source}' to '{destination}'."
            };

        if (File.Exists(destination))
        {
            if (!BoolInput(operation, "overwrite", defaultValue: true))
                return new ResourceProviderResult
                {
                    Code = ResourceProviderResultCode.Skipped,
                    Message = $"Destination exists and overwrite is disabled: {destination}"
                };

            if (BoolInput(operation, "skipIfNewer", defaultValue: false)
                && File.GetLastWriteTimeUtc(destination) > File.GetLastWriteTimeUtc(source))
                return new ResourceProviderResult
                {
                    Code = ResourceProviderResultCode.Skipped,
                    Message = $"Destination is newer than source: {destination}"
                };
        }

        var existedBefore = File.Exists(destination);
        var backupPath = "";
        if (existedBefore)
        {
            backupPath = Path.Combine(
                Path.GetTempPath(),
                "BeepInstaller",
                "resource-backups",
                Guid.NewGuid().ToString("N"),
                Path.GetFileName(destination));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(destination, backupPath, overwrite: true);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            _rollback[operation.Id] = new FileRollbackState(destination, existedBefore, backupPath);

            return new ResourceProviderResult
            {
                Message = existedBefore
                    ? $"Updated file: {destination}"
                    : $"Copied file: {destination}"
            };
        }
        catch (Exception ex)
        {
            if (existedBefore && File.Exists(backupPath))
                File.Copy(backupPath, destination, overwrite: true);
            else if (!existedBefore && File.Exists(destination))
                File.Delete(destination);

            return Error("BI3010", operation.Id, $"Failed to copy file '{source}' to '{destination}': {ex.Message}");
        }
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: file rollback would restore backup or delete created destination."
            };

        if (!_rollback.TryGetValue(operation.Id, out var state))
        {
            if (context.ReplayRollback)
                return DeleteReplayDestination(operation, context);

            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "No file rollback state recorded for this operation."
            };
        }

        try
        {
            if (state.ExistedBefore)
            {
                if (!File.Exists(state.BackupPath))
                    return Error("BI3011", operation.Id, $"File rollback backup is missing: {state.BackupPath}");

                Directory.CreateDirectory(Path.GetDirectoryName(state.Destination)!);
                File.Copy(state.BackupPath, state.Destination, overwrite: true);
            }
            else if (File.Exists(state.Destination))
            {
                File.Delete(state.Destination);
            }

            return new ResourceProviderResult
            {
                Message = state.ExistedBefore
                    ? $"Restored previous file: {state.Destination}"
                    : $"Deleted created file: {state.Destination}"
            };
        }
        catch (Exception ex)
        {
            return Error("BI3012", operation.Id, $"Failed to roll back file '{state.Destination}': {ex.Message}");
        }
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var detection = Detect(operation, context);
        if (!detection.Exists && !Required(operation))
        {
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Optional destination file is absent."
            };
        }

        var source = ResolveSource(operation, context);
        var destination = ResolveDestination(operation, context);
        if (detection.Exists && File.Exists(source))
        {
            var sourceLength = new FileInfo(source).Length;
            var destinationLength = new FileInfo(destination).Length;
            if (sourceLength != destinationLength)
            {
                return Error(
                    "BI3013",
                    operation.Id,
                    $"Destination file length differs from source: {destination}");
            }
        }

        return new ResourceProviderResult
        {
            Code = detection.Exists ? ResourceProviderResultCode.Succeeded : ResourceProviderResultCode.Failed,
            Message = detection.Exists ? "Destination file exists." : "Destination file is missing."
        };
    }

    private static string ResolveSource(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!operation.Inputs.TryGetValue("source", out var source) || string.IsNullOrWhiteSpace(source))
            return "";

        source = ResolveValue(source, context);
        if (Path.IsPathRooted(source))
            return Path.GetFullPath(source);

        if (context.Variables.TryGetValue("PayloadRoot", out var payloadRoot) && !string.IsNullOrWhiteSpace(payloadRoot))
            return Path.GetFullPath(Path.Combine(payloadRoot, source));

        return Path.GetFullPath(source);
    }

    private static string ResolveDestination(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!operation.Inputs.TryGetValue("destination", out var destination) || string.IsNullOrWhiteSpace(destination))
            return "";

        destination = ResolveValue(destination, context);
        if (Path.IsPathRooted(destination))
            return Path.GetFullPath(destination);

        return Path.GetFullPath(Path.Combine(context.InstallRoot, destination));
    }

    private static string ResolveValue(string value, ResourceProviderContext context)
    {
        var resolved = value;
        if (!string.IsNullOrWhiteSpace(context.InstallRoot))
            resolved = resolved.Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            resolved = resolved.Replace($"%{variable.Key}%", variable.Value, StringComparison.OrdinalIgnoreCase);
            resolved = resolved.Replace($"{{{variable.Key}}}", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static bool Required(CompiledInstallOperation operation)
        => BoolInput(operation, "required", defaultValue: true);

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static bool IsUnderRoot(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            return false;

        var fullCandidate = Path.GetFullPath(candidatePath);
        var fullRoot = Path.GetFullPath(rootPath);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceProviderResult DeleteReplayDestination(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var destination = ResolveDestination(operation, context);
        if (!File.Exists(destination))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Replay rollback skipped because file is already absent: {destination}"
            };

        if (!IsUnderRoot(destination, context.InstallRoot))
            return Error("BI3014", operation.Id, $"Replay rollback refused to delete a file outside InstallRoot: {destination}");

        try
        {
            File.Delete(destination);
            return new ResourceProviderResult { Message = $"Replay rollback deleted file: {destination}" };
        }
        catch (Exception ex)
        {
            return Error("BI3015", operation.Id, $"Replay rollback failed to delete file '{destination}': {ex.Message}");
        }
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

    private sealed record FileRollbackState(string Destination, bool ExistedBefore, string BackupPath);
}
