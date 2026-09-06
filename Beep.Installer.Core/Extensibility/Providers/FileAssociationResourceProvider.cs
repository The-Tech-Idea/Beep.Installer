using Beep.Installer.Engine;
using Microsoft.Win32;

namespace Beep.Installer.Extensibility.Providers;

#pragma warning disable CA1416 // RegistryValueKind is installer metadata; registry execution is guarded by IInstallerRegistryStore.IsSupported.

public sealed class FileAssociationResourceProvider : IResourceProvider
{
    private readonly IInstallerRegistryStore _store;

    public FileAssociationResourceProvider()
        : this(new WindowsInstallerRegistryStore())
    {
    }

    public FileAssociationResourceProvider(IInstallerRegistryStore store)
    {
        _store = store;
    }

    public string ResourceType => "file-association.register";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Registry | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var hive = ResolveHive(context);
        var extensionKey = ExtensionKey(operation);
        var progIdKey = ProgIdKey(operation);
        var commandKey = CommandKey(operation);
        var extensionDefault = _store.IsSupported
            ? _store.ReadValue(hive, extensionKey, "")
            : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);
        var command = _store.IsSupported
            ? _store.ReadValue(hive, commandKey, "")
            : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        return new ResourceDetectionResult
        {
            Exists = extensionDefault.Exists
                     && command.Exists
                     && string.Equals(Convert.ToString(extensionDefault.Value, System.Globalization.CultureInfo.InvariantCulture), Input(operation, "progId"), StringComparison.OrdinalIgnoreCase),
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["hive"] = hive.ToString(),
                ["extensionKey"] = extensionKey,
                ["progIdKey"] = progIdKey,
                ["commandKey"] = commandKey,
                ["extensionDefaultExists"] = extensionDefault.Exists ? "true" : "false",
                ["commandExists"] = command.Exists ? "true" : "false"
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var extension = Input(operation, "extension");
        if (string.IsNullOrWhiteSpace(extension))
            return Error("BI5401", $"{operation.Id}.extension", "File association operation is missing extension.");
        if (!extension.StartsWith(".", StringComparison.Ordinal) || extension.Length < 2)
            return Error("BI5402", $"{operation.Id}.extension", "File association extension must start with a dot, for example .bsetup.");
        if (string.IsNullOrWhiteSpace(Input(operation, "progId")))
            return Error("BI5403", $"{operation.Id}.progId", "File association operation is missing ProgId.");
        if (string.IsNullOrWhiteSpace(Input(operation, "executablePath")))
            return Error("BI5404", $"{operation.Id}.executablePath", "File association operation is missing executablePath.");
        if (string.IsNullOrWhiteSpace(Verb(operation)))
            return Error("BI5405", $"{operation.Id}.verb", "File association operation is missing verb.");
        if (!context.DryRun && !_store.IsSupported)
            return Error("BI5406", operation.Id, "File associations require Windows registry support.");

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

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: file association would be registered."
            };

        var hive = ResolveHive(context);
        var extensionKey = ExtensionKey(operation);
        var progIdKey = ProgIdKey(operation);
        var defaultIconKey = DefaultIconKey(operation);
        var verbKey = VerbKey(operation);
        var commandKey = CommandKey(operation);

        try
        {
            _store.WriteValue(hive, extensionKey, "", Input(operation, "progId"), RegistryValueKind.String);
            WriteOptional(hive, extensionKey, "Content Type", Input(operation, "contentType"));
            WriteOptional(hive, extensionKey, "PerceivedType", Input(operation, "perceivedType"));

            _store.WriteValue(hive, progIdKey, "", Description(operation), RegistryValueKind.String);
            if (!string.IsNullOrWhiteSpace(Input(operation, "iconPath")))
                _store.WriteValue(hive, defaultIconKey, "", ResolveValue(Input(operation, "iconPath"), context), RegistryValueKind.String);

            if (!string.IsNullOrWhiteSpace(Input(operation, "verbDisplayName")))
                _store.WriteValue(hive, verbKey, "", Input(operation, "verbDisplayName"), RegistryValueKind.String);

            _store.WriteValue(hive, commandKey, "", BuildCommand(operation, context), RegistryValueKind.String);

            return new ResourceProviderResult
            {
                Message = $"Registered {Input(operation, "extension")} file association for {Input(operation, "progId")}."
            };
        }
        catch (Exception ex)
        {
            return Error("BI5410", operation.Id, $"Failed to register file association '{Input(operation, "extension")}': {ex.Message}");
        }
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: file association rollback would delete owned registry keys."
            };

        var hive = ResolveHive(context);
        var extensionKey = ExtensionKey(operation);
        var progIdKey = ProgIdKey(operation);

        try
        {
            var extensionDefault = _store.ReadValue(hive, extensionKey, "");
            if (extensionDefault.Exists
                && string.Equals(Convert.ToString(extensionDefault.Value, System.Globalization.CultureInfo.InvariantCulture), Input(operation, "progId"), StringComparison.OrdinalIgnoreCase))
            {
                _store.DeleteKeyTree(hive, extensionKey);
            }

            _store.DeleteKeyTree(hive, progIdKey);
            return new ResourceProviderResult
            {
                Message = $"Removed file association for {Input(operation, "extension")}."
            };
        }
        catch (Exception ex)
        {
            return Error("BI5420", operation.Id, $"Failed to roll back file association '{Input(operation, "extension")}': {ex.Message}");
        }
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: file association verification would read registry keys."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var hive = ResolveHive(context);
        var extensionDefault = _store.ReadValue(hive, ExtensionKey(operation), "");
        var command = _store.ReadValue(hive, CommandKey(operation), "");
        if (!extensionDefault.Exists)
            return Error("BI5430", operation.Id, "File association extension key was not registered.");
        if (!string.Equals(Convert.ToString(extensionDefault.Value, System.Globalization.CultureInfo.InvariantCulture), Input(operation, "progId"), StringComparison.OrdinalIgnoreCase))
            return Error("BI5431", operation.Id, "File association extension points at a different ProgId.");
        if (!command.Exists)
            return Error("BI5432", operation.Id, "File association command was not registered.");
        if (!string.Equals(Convert.ToString(command.Value, System.Globalization.CultureInfo.InvariantCulture), BuildCommand(operation, context), StringComparison.Ordinal))
            return Error("BI5433", operation.Id, "File association command differs from the compiled plan.");

        return new ResourceProviderResult { Message = "File association registry keys verified." };
    }

    private void WriteOptional(InstallerRegistryHive hive, string keyPath, string valueName, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _store.WriteValue(hive, keyPath, valueName, value, RegistryValueKind.String);
    }

    private static InstallerRegistryHive ResolveHive(ResourceProviderContext context)
        => context.PerUser ? InstallerRegistryHive.CurrentUser : InstallerRegistryHive.LocalMachine;

    private static string ExtensionKey(CompiledInstallOperation operation)
        => ClassesKey(Input(operation, "extension"));

    private static string ProgIdKey(CompiledInstallOperation operation)
        => ClassesKey(Input(operation, "progId"));

    private static string DefaultIconKey(CompiledInstallOperation operation)
        => ProgIdKey(operation) + "\\DefaultIcon";

    private static string VerbKey(CompiledInstallOperation operation)
        => ProgIdKey(operation) + "\\shell\\" + Verb(operation);

    private static string CommandKey(CompiledInstallOperation operation)
        => VerbKey(operation) + "\\command";

    private static string ClassesKey(string leaf)
        => "Software\\Classes\\" + leaf.Trim('\\');

    private static string Verb(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "verb")) ? "open" : Input(operation, "verb");

    private static string Description(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "description"))
            ? Input(operation, "progId")
            : Input(operation, "description");

    private static string BuildCommand(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var executable = QuoteIfNeeded(ResolveValue(Input(operation, "executablePath"), context));
        var arguments = Input(operation, "arguments");
        if (string.IsNullOrWhiteSpace(arguments))
            arguments = "\"%1\"";
        arguments = ResolveValue(arguments, context)
            .Replace("{file}", "%1", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(arguments)
            ? executable
            : executable + " " + arguments;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.StartsWith('"') && value.EndsWith('"')
            ? value
            : "\"" + value.Trim('"') + "\"";
    }

    private static string ResolveValue(string value, ResourceProviderContext context)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var resolved = value;
        if (!string.IsNullOrWhiteSpace(context.InstallRoot))
            resolved = resolved.Replace("%InstallPath%", context.InstallRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            resolved = resolved.Replace($"%{variable.Key}%", variable.Value, StringComparison.OrdinalIgnoreCase);
            resolved = resolved.Replace($"{{{variable.Key}}}", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

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

#pragma warning restore CA1416
