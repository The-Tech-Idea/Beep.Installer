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

        try
        {
            // Backing up the file we are about to replace reads the destination, so it fails for
            // exactly the same reason the copy would: the file is in use. This used to sit outside
            // the try, so a locked destination threw straight out of Apply -- past the provider
            // contract, past the plan executor, and out to a catch that blamed the journal.
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HandleLockedDestination(operation, source, destination, existedBefore, backupPath, ex);
        }
        catch (Exception ex)
        {
            RestoreAfterFailedCopy(destination, existedBefore, backupPath);
            return Error("BI3010", operation.Id, $"Failed to copy file '{source}' to '{destination}': {ex.Message}");
        }
    }

    /// <summary>
    /// A destination open in another process cannot be replaced in place. Elevated, the swap can be
    /// queued in <c>PendingFileRenameOperations</c> via <c>MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)</c>
    /// and the install completes pending a reboot; unelevated that key is not writable, so the only
    /// honest outcome is a failure that names the file and says what to do about it.
    ///
    /// BeepDM's <c>FileCopyStep</c> has always done this, but the wizard graph routes file copies
    /// through the typed resource providers instead, so in the shipping installer the handling was
    /// unreachable and a locked file could never produce exit code 3010.
    /// </summary>
    private ResourceProviderResult HandleLockedDestination(
        CompiledInstallOperation operation,
        string source,
        string destination,
        bool existedBefore,
        string backupPath,
        Exception ex)
    {
        if (!IsInUse(destination))
        {
            RestoreAfterFailedCopy(destination, existedBefore, backupPath);
            return Error("BI3010", operation.Id, $"Failed to copy file '{source}' to '{destination}': {ex.Message}");
        }

        // Stage beside the destination rather than under %TEMP%: a deferred MoveFileEx cannot rename
        // across volumes, and %TEMP% is frequently on a different one.
        var staged = destination + ".pending-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, staged, overwrite: true);

            if (TheTechIdea.Beep.Installer.InstallHelpers.ScheduleFileForRestart(staged, destination))
            {
                // Undo here means removing the staged copy, not restoring the destination: the
                // destination was never modified -- that is the whole reason a reboot is needed. The
                // queued rename then finds no source at boot and does nothing. Recording the backup
                // instead would claim a restore is possible from a file that was never written,
                // because the backup read is what failed in the first place.
                _rollback[operation.Id] = new FileRollbackState(staged, ExistedBefore: false, BackupPath: "");
                return new ResourceProviderResult
                {
                    Code = ResourceProviderResultCode.RebootRequired,
                    Message = $"In use - scheduled for replacement at next reboot: {destination}"
                };
            }
        }
        catch (Exception stageEx)
        {
            TryDelete(staged);
            RestoreAfterFailedCopy(destination, existedBefore, backupPath);
            return Error("BI3016", operation.Id,
                $"The file '{destination}' is in use and could not be staged for replacement: {stageEx.Message}");
        }

        TryDelete(staged);
        RestoreAfterFailedCopy(destination, existedBefore, backupPath);
        return Error("BI3016", operation.Id,
            $"The file '{destination}' is in use and could not be replaced. Close the application " +
            "using it and retry, or run the installer elevated so the replacement can be scheduled " +
            "for the next reboot.");
    }

    /// <summary>
    /// Distinguishes "another process holds this file" from every other IO failure -- a read-only
    /// destination or a full disk must not be answered with a reboot.
    /// </summary>
    private static bool IsInUse(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RestoreAfterFailedCopy(string destination, bool existedBefore, string backupPath)
    {
        try
        {
            if (existedBefore && !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                File.Copy(backupPath, destination, overwrite: true);
            else if (!existedBefore && File.Exists(destination))
                File.Delete(destination);
        }
        catch
        {
            // Best effort: the plan executor rolls the whole attempt back, and throwing here would
            // replace the real failure with this one.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
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
