using System.Runtime.InteropServices;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Extensibility.Providers;

public sealed record ShortcutSnapshot(
    bool Exists,
    string TargetPath = "",
    string Arguments = "",
    string WorkingDirectory = "",
    string IconPath = "");

public interface IInstallerShortcutStore
{
    bool IsSupported { get; }
    string GetKnownFolder(Environment.SpecialFolder folder);
    bool FileExists(string path);
    ShortcutSnapshot Read(string linkPath);
    void Write(string linkPath, string targetPath, string arguments, string workingDirectory, string iconPath);
    void Delete(string linkPath);
}

public sealed class ShortcutCreateResourceProvider : IResourceProvider
{
    private readonly IInstallerShortcutStore _store;
    private readonly Dictionary<string, ShortcutRollbackState> _rollback = new(StringComparer.Ordinal);

    public ShortcutCreateResourceProvider()
        : this(new WindowsInstallerShortcutStore())
    {
    }

    public ShortcutCreateResourceProvider(IInstallerShortcutStore store)
    {
        _store = store;
    }

    public string ResourceType => "shortcut.create";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var linkPath = ResolveLinkPath(operation, context);
        var snapshot = !string.IsNullOrWhiteSpace(linkPath) && _store.IsSupported
            ? _store.Read(linkPath)
            : new ShortcutSnapshot(false);

        return new ResourceDetectionResult
        {
            Exists = snapshot.Exists,
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["linkPath"] = linkPath,
                ["targetPath"] = snapshot.TargetPath,
                ["location"] = Input(operation, "location")
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI3301", $"{operation.Id}.name", "Shortcut operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "targetPath")))
            return Error("BI3302", $"{operation.Id}.targetPath", "Shortcut operation is missing targetPath.");

        var location = Input(operation, "location");
        if (!string.IsNullOrWhiteSpace(location)
            && !Enum.TryParse<ShortcutLocation>(location, ignoreCase: true, out _))
            return Error("BI3303", $"{operation.Id}.location", $"Unsupported shortcut location: {location}");

        var linkPath = ResolveLinkPath(operation, context);
        if (string.IsNullOrWhiteSpace(linkPath))
            return Error("BI3304", operation.Id, "Shortcut link path could not be resolved.");

        if (!context.DryRun && !_store.IsSupported)
            return Error("BI3305", operation.Id, "Shortcut creation requires Windows shortcut support.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
    {
        var desiredTarget = ResolveTargetPath(operation, context);
        var currentTarget = detection.Facts.GetValueOrDefault("targetPath") ?? "";
        return new ResourcePlanResult
        {
            ChangeKind = detection.Exists && string.Equals(currentTarget, desiredTarget, StringComparison.OrdinalIgnoreCase)
                ? ResourceChangeKind.None
                : detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };
    }

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var linkPath = ResolveLinkPath(operation, context);
        var targetPath = ResolveTargetPath(operation, context);
        var arguments = Expand(Input(operation, "arguments"), context);
        var workingDirectory = ResolveOptionalPath(Input(operation, "workingDirectory"), context);
        var iconPath = ResolveOptionalPath(Input(operation, "iconPath"), context);

        if (!_store.FileExists(targetPath))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Shortcut target does not exist: {targetPath}"
            };

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Dry run: shortcut '{Input(operation, "name")}' would be created."
            };

        var prior = _store.Read(linkPath);
        _rollback[operation.Id] = new ShortcutRollbackState(linkPath, prior);
        _store.Write(linkPath, targetPath, arguments, workingDirectory, iconPath);

        return new ResourceProviderResult
        {
            Message = $"Created shortcut '{Input(operation, "name")}'."
        };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: shortcut verification skipped."
            };

        var linkPath = ResolveLinkPath(operation, context);
        var targetPath = ResolveTargetPath(operation, context);
        var snapshot = _store.Read(linkPath);
        return snapshot.Exists && string.Equals(snapshot.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)
            ? new ResourceProviderResult { Message = $"Verified shortcut '{Input(operation, "name")}'." }
            : Error("BI3306", operation.Id, $"Shortcut verification failed for '{Input(operation, "name")}'.");
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: shortcut rollback skipped."
            };

        if (!_rollback.TryGetValue(operation.Id, out var state))
        {
            if (context.ReplayRollback)
                return DeleteReplayShortcut(operation, context);

            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "No shortcut rollback state captured for this operation."
            };
        }

        if (state.Previous.Exists)
        {
            _store.Write(
                state.LinkPath,
                state.Previous.TargetPath,
                state.Previous.Arguments,
                state.Previous.WorkingDirectory,
                state.Previous.IconPath);
            return new ResourceProviderResult { Message = $"Restored shortcut '{Input(operation, "name")}'." };
        }

        _store.Delete(state.LinkPath);
        return new ResourceProviderResult { Message = $"Removed shortcut '{Input(operation, "name")}'." };
    }

    private ResourceProviderResult DeleteReplayShortcut(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var linkPath = ResolveLinkPath(operation, context);
        _store.Delete(linkPath);
        return new ResourceProviderResult
        {
            Message = $"Replay rollback removed shortcut '{Input(operation, "name")}'."
        };
    }

    private string ResolveLinkPath(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var name = Input(operation, "name").Trim();
        if (!name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name += ".lnk";

        var location = Enum.TryParse<ShortcutLocation>(Input(operation, "location"), ignoreCase: true, out var parsed)
            ? parsed
            : ShortcutLocation.StartMenu;

        return location switch
        {
            ShortcutLocation.Desktop => Path.Combine(Folder(
                context.PerUser ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.CommonDesktopDirectory), name),

            ShortcutLocation.StartMenu => Path.Combine(
                Folder(context.PerUser ? Environment.SpecialFolder.Programs : Environment.SpecialFolder.CommonPrograms),
                StartMenuSubfolder(operation, context),
                name),

            ShortcutLocation.Startup => Path.Combine(Folder(
                context.PerUser ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.CommonStartup), name),

            ShortcutLocation.QuickLaunch => Path.Combine(
                Folder(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch",
                name),

            _ => ""
        };
    }

    private string Folder(Environment.SpecialFolder folder)
        => _store.GetKnownFolder(folder) ?? "";

    private static string StartMenuSubfolder(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var authored = Expand(Input(operation, "startMenuSubfolder"), context);
        return string.IsNullOrWhiteSpace(authored) ? SanitizePathSegment(context.ProductName) : authored;
    }

    private static string ResolveTargetPath(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var target = Expand(Input(operation, "targetPath"), context);
        return Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(context.InstallRoot, target));
    }

    private static string ResolveOptionalPath(string value, ResourceProviderContext context)
    {
        var expanded = Expand(value, context);
        if (string.IsNullOrWhiteSpace(expanded))
            return "";

        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(context.InstallRoot, expanded));
    }

    private static string Expand(string value, ResourceProviderContext context)
    {
        var expanded = (value ?? "")
            .Replace("{InstallPath}", context.InstallRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            expanded = expanded
                .Replace("{" + variable.Key + "}", variable.Value, StringComparison.OrdinalIgnoreCase)
                .Replace("%" + variable.Key + "%", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static ResourceProviderResult Error(string code, string path, string message)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Diagnostics =
            {
                new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };

    private sealed record ShortcutRollbackState(string LinkPath, ShortcutSnapshot Previous);
}

public sealed class WindowsInstallerShortcutStore : IInstallerShortcutStore
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string GetKnownFolder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder) ?? "";

    public bool FileExists(string path)
        => File.Exists(path);

    public ShortcutSnapshot Read(string linkPath)
        => File.Exists(linkPath) ? new ShortcutSnapshot(true) : new ShortcutSnapshot(false);

    public void Write(string linkPath, string targetPath, string arguments, string workingDirectory, string iconPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Shortcut creation requires Windows shell COM support.");

        var directory = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new PlatformNotSupportedException("WScript.Shell is not available.");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(linkPath);
        shortcut.TargetPath = targetPath;
        if (!string.IsNullOrWhiteSpace(arguments)) shortcut.Arguments = arguments;
        if (!string.IsNullOrWhiteSpace(workingDirectory)) shortcut.WorkingDirectory = workingDirectory;
        if (!string.IsNullOrWhiteSpace(iconPath)) shortcut.IconLocation = iconPath;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    public void Delete(string linkPath)
    {
        if (File.Exists(linkPath))
            File.Delete(linkPath);
    }
}
