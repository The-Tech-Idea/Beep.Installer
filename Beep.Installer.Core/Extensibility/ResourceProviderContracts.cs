using Beep.Installer.Engine;
using Beep.Installer.Policy;

namespace Beep.Installer.Extensibility;

public enum ResourceChangeKind
{
    None,
    Create,
    Update,
    Delete,
    Repair
}

public enum ResourceProviderResultCode
{
    Succeeded,
    Skipped,
    Failed,
    RebootRequired
}

public sealed class ResourceProviderContext
{
    public string InstallRoot { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public bool PerUser { get; init; }
    public bool DryRun { get; init; }
    public bool ReplayRollback { get; init; }
    public string AttemptId { get; init; } = Guid.NewGuid().ToString("N");
    public string ExecutionMode { get; init; } = "install";
    public ISecretProvider SecretProvider { get; init; } = new CompositeSecretProvider();
    public InstallerPolicy? Policy { get; init; }
    public IInstallerConditionFacts? ConditionFacts { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public interface IInstallerConditionFacts
{
    Version OsVersion { get; }
    string Architecture { get; }
    bool IsAdmin { get; }
    bool FileExists(string path);
    bool DirectoryExists(string path);
    bool RegistryKeyExists(string keyPath);
    string? RegistryValue(string keyPath, string valueName);
    CommandConditionResult RunCommand(string command);
}

public sealed record CommandConditionResult(int ExitCode, string Output);

public sealed class ResourceProviderResult
{
    public ResourceProviderResultCode Code { get; init; } = ResourceProviderResultCode.Succeeded;
    public string Message { get; init; } = "";
    public SortedDictionary<string, string> Evidence { get; init; } = new(StringComparer.Ordinal);
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class ResourceDetectionResult
{
    public bool Exists { get; init; }
    public string CurrentVersion { get; init; } = "";
    public SortedDictionary<string, string> Facts { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ResourcePlanResult
{
    public ResourceChangeKind ChangeKind { get; init; } = ResourceChangeKind.None;
    public List<CompiledInstallOperation> Operations { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public interface IResourceProvider
{
    string ResourceType { get; }
    InstallerExtensionPermission RequiredPermissions { get; }

    ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context);
    ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context);
    ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context);
    ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context);
    ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context);
    ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context);
}
