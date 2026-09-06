using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility.Providers;

public sealed record EnvironmentVariableSnapshot(bool Exists, string? Value);

public interface IInstallerEnvironmentStore
{
    bool IsSupported { get; }
    string? Read(string name, EnvironmentVariableTarget target);
    void Write(string name, string? value, EnvironmentVariableTarget target);
}

public sealed class EnvironmentSetResourceProvider : IResourceProvider
{
    private readonly IInstallerEnvironmentStore _store;
    private readonly Dictionary<string, EnvironmentRollbackState> _rollback = new(StringComparer.Ordinal);

    public EnvironmentSetResourceProvider()
        : this(new SystemInstallerEnvironmentStore())
    {
    }

    public EnvironmentSetResourceProvider(IInstallerEnvironmentStore store)
    {
        _store = store;
    }

    public string ResourceType => "environment.set";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var name = Input(operation, "name");
        var target = ResolveTarget(operation, context);
        var value = _store.IsSupported ? _store.Read(name, target) : null;

        return new ResourceDetectionResult
        {
            Exists = value != null,
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["scope"] = target.ToString(),
                ["value"] = value ?? ""
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI3201", $"{operation.Id}.name", "Environment variable operation is missing name.");

        var scope = Input(operation, "scope");
        if (!string.IsNullOrWhiteSpace(scope)
            && !Enum.TryParse<EnvironmentVariableTarget>(scope, ignoreCase: true, out _))
            return Error("BI3202", $"{operation.Id}.scope", $"Unsupported environment variable scope: {scope}");

        if (!context.DryRun && !_store.IsSupported)
            return Error("BI3203", operation.Id, "Environment variable writes are not supported on this platform.");

        if (Input(operation, "value") == "<redacted>")
            return Error("BI3204", $"{operation.Id}.value", "Environment variable value was redacted and cannot be applied directly.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
    {
        var desired = ResolveValue(operation, context);
        return new ResourcePlanResult
        {
            ChangeKind = detection.Exists && string.Equals(detection.Facts.GetValueOrDefault("value"), desired, StringComparison.Ordinal)
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

        var name = Input(operation, "name");
        var target = ResolveTarget(operation, context);
        var value = ResolveValue(operation, context);

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Dry run: environment variable '{name}' would be set for {target}."
            };

        var prior = _store.Read(name, target);
        _rollback[operation.Id] = new EnvironmentRollbackState(target, new EnvironmentVariableSnapshot(prior != null, prior));
        _store.Write(name, value, target);

        return new ResourceProviderResult
        {
            Message = $"Set environment variable '{name}' for {target}."
        };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: environment variable verification skipped."
            };

        var name = Input(operation, "name");
        var target = ResolveTarget(operation, context);
        var actual = _store.Read(name, target);
        var expected = ResolveValue(operation, context);

        return string.Equals(actual, expected, StringComparison.Ordinal)
            ? new ResourceProviderResult { Message = $"Verified environment variable '{name}' for {target}." }
            : Error("BI3205", $"{operation.Id}.value", $"Environment variable '{name}' verification failed.");
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: environment variable rollback skipped."
            };

        if (!_rollback.TryGetValue(operation.Id, out var state))
        {
            if (context.ReplayRollback)
                return DeleteReplayVariable(operation, context);

            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "No environment rollback state captured for this operation."
            };
        }

        var name = Input(operation, "name");
        _store.Write(name, state.Previous.Exists ? state.Previous.Value : null, state.Target);
        return new ResourceProviderResult
        {
            Message = state.Previous.Exists
                ? $"Restored environment variable '{name}' for {state.Target}."
                : $"Removed environment variable '{name}' for {state.Target}."
        };
    }

    private static EnvironmentVariableTarget ResolveTarget(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var requested = Enum.TryParse<EnvironmentVariableTarget>(Input(operation, "scope"), ignoreCase: true, out var parsed)
            ? parsed
            : EnvironmentVariableTarget.User;

        return context.PerUser && requested == EnvironmentVariableTarget.Machine
            ? EnvironmentVariableTarget.User
            : requested;
    }

    private ResourceProviderResult DeleteReplayVariable(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI3201", $"{operation.Id}.name", "Environment variable operation is missing name.");
        if (!context.DryRun && !_store.IsSupported)
            return Error("BI3203", operation.Id, "Environment variable writes are not supported on this platform.");

        var name = Input(operation, "name");
        var target = ResolveTarget(operation, context);
        _store.Write(name, null, target);
        return new ResourceProviderResult
        {
            Message = $"Replay rollback removed environment variable '{name}' for {target}."
        };
    }

    private static string ResolveValue(CompiledInstallOperation operation, ResourceProviderContext context)
        => Expand(Input(operation, "value"), context);

    private static string Expand(string value, ResourceProviderContext context)
    {
        var expanded = value
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

    private sealed record EnvironmentRollbackState(
        EnvironmentVariableTarget Target,
        EnvironmentVariableSnapshot Previous);
}

public sealed class SystemInstallerEnvironmentStore : IInstallerEnvironmentStore
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string? Read(string name, EnvironmentVariableTarget target)
        => Environment.GetEnvironmentVariable(name, target);

    public void Write(string name, string? value, EnvironmentVariableTarget target)
        => Environment.SetEnvironmentVariable(name, value, target);
}
