using Beep.Installer.Engine;
using Microsoft.Win32;

namespace Beep.Installer.Extensibility.Providers;

#pragma warning disable CA1416 // RegistryValueKind is installer metadata; concrete registry execution is guarded by IInstallerRegistryStore.IsSupported.

public enum InstallerRegistryHive
{
    CurrentUser,
    LocalMachine
}

public sealed record RegistryValueSnapshot(bool Exists, object? Value, RegistryValueKind ValueKind);

public interface IInstallerRegistryStore
{
    bool IsSupported { get; }
    RegistryValueSnapshot ReadValue(InstallerRegistryHive hive, string keyPath, string valueName);
    void WriteValue(InstallerRegistryHive hive, string keyPath, string valueName, object value, RegistryValueKind valueKind);
    void DeleteValue(InstallerRegistryHive hive, string keyPath, string valueName);
    void DeleteKeyTree(InstallerRegistryHive hive, string keyPath);
}

public sealed class RegistryWriteResourceProvider : IResourceProvider
{
    private readonly IInstallerRegistryStore _store;
    private readonly Dictionary<string, RegistryRollbackState> _rollback = new(StringComparer.Ordinal);

    public RegistryWriteResourceProvider()
        : this(new WindowsInstallerRegistryStore())
    {
    }

    public RegistryWriteResourceProvider(IInstallerRegistryStore store)
    {
        _store = store;
    }

    public string ResourceType => "registry.write";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Registry
        | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var hive = ResolveHive(context);
        var keyPath = ResolveKeyPath(operation, context);
        var valueName = Input(operation, "valueName");
        var snapshot = _store.IsSupported
            ? _store.ReadValue(hive, keyPath, valueName)
            : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        return new ResourceDetectionResult
        {
            Exists = snapshot.Exists,
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["hive"] = hive.ToString(),
                ["keyPath"] = keyPath,
                ["valueName"] = valueName,
                ["valueKind"] = snapshot.ValueKind.ToString()
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "keyPath")))
            return Error("BI3101", $"{operation.Id}.keyPath", "Registry operation is missing keyPath.");

        var keyPath = ResolveKeyPath(operation, context);
        if (IsAbsoluteHivePath(keyPath))
            return Error("BI3102", $"{operation.Id}.keyPath", "Registry keyPath must be hive-relative; install scope chooses the hive.");

        var valueKindText = Input(operation, "valueKind");
        if (!string.IsNullOrWhiteSpace(valueKindText)
            && !Enum.TryParse<RegistryValueKind>(valueKindText, ignoreCase: true, out _))
            return Error("BI3103", $"{operation.Id}.valueKind", $"Unsupported registry value kind: {valueKindText}");

        if (!context.DryRun && !_store.IsSupported)
            return Error("BI3104", operation.Id, "Registry writes require Windows registry support.");

        var conversion = ConvertValue(Input(operation, "value"), ResolveValueKind(operation));
        if (!conversion.Ok)
            return Error("BI3105", $"{operation.Id}.value", conversion.Error);

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

        var hive = ResolveHive(context);
        var keyPath = ResolveKeyPath(operation, context);
        var valueName = Input(operation, "valueName");
        var valueKind = ResolveValueKind(operation);
        var valueText = ResolveValue(Input(operation, "value"), context);
        var conversion = ConvertValue(valueText, valueKind);
        if (!conversion.Ok)
            return Error("BI3105", $"{operation.Id}.value", conversion.Error);

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Dry run: would write {hive}\\{keyPath}\\{valueName}."
            };

        var before = _store.ReadValue(hive, keyPath, valueName);
        _rollback[operation.Id] = new RegistryRollbackState(hive, keyPath, valueName, before);

        try
        {
            _store.WriteValue(hive, keyPath, valueName, conversion.Value!, valueKind);
            return new ResourceProviderResult
            {
                Message = $"Wrote registry value: {hive}\\{keyPath}\\{valueName}"
            };
        }
        catch (Exception ex)
        {
            Restore(hive, keyPath, valueName, before);
            return Error("BI3110", operation.Id, $"Failed to write registry value '{hive}\\{keyPath}\\{valueName}': {ex.Message}");
        }
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: registry rollback would restore or delete the prior value."
            };

        if (!_rollback.TryGetValue(operation.Id, out var state))
        {
            if (context.ReplayRollback)
                return DeleteReplayValue(operation, context);

            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "No registry rollback state recorded for this operation."
            };
        }

        try
        {
            Restore(state.Hive, state.KeyPath, state.ValueName, state.Before);
            return new ResourceProviderResult
            {
                Message = state.Before.Exists
                    ? $"Restored registry value: {state.Hive}\\{state.KeyPath}\\{state.ValueName}"
                    : $"Deleted created registry value: {state.Hive}\\{state.KeyPath}\\{state.ValueName}"
            };
        }
        catch (Exception ex)
        {
            return Error("BI3111", operation.Id, $"Failed to roll back registry value '{state.Hive}\\{state.KeyPath}\\{state.ValueName}': {ex.Message}");
        }
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: registry verification would read the value."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var hive = ResolveHive(context);
        var keyPath = ResolveKeyPath(operation, context);
        var valueName = Input(operation, "valueName");
        var valueKind = ResolveValueKind(operation);
        var expected = ConvertValue(ResolveValue(Input(operation, "value"), context), valueKind);
        var actual = _store.ReadValue(hive, keyPath, valueName);

        if (!actual.Exists)
            return Error("BI3120", operation.Id, $"Registry value is missing: {hive}\\{keyPath}\\{valueName}");
        if (actual.ValueKind != valueKind && actual.ValueKind != RegistryValueKind.Unknown)
            return Error("BI3121", operation.Id, $"Registry value kind differs for {hive}\\{keyPath}\\{valueName}.");
        if (!RegistryValuesEqual(actual.Value, expected.Value, valueKind))
            return Error("BI3122", operation.Id, $"Registry value differs for {hive}\\{keyPath}\\{valueName}.");

        return new ResourceProviderResult
        {
            Message = $"Registry value verified: {hive}\\{keyPath}\\{valueName}"
        };
    }

    private void Restore(
        InstallerRegistryHive hive,
        string keyPath,
        string valueName,
        RegistryValueSnapshot before)
    {
        if (before.Exists)
            _store.WriteValue(hive, keyPath, valueName, before.Value!, before.ValueKind);
        else
            _store.DeleteValue(hive, keyPath, valueName);
    }

    private ResourceProviderResult DeleteReplayValue(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "keyPath")))
            return Error("BI3101", $"{operation.Id}.keyPath", "Registry operation is missing keyPath.");
        if (!context.DryRun && !_store.IsSupported)
            return Error("BI3104", operation.Id, "Registry writes require Windows registry support.");

        var hive = ResolveHive(context);
        var keyPath = ResolveKeyPath(operation, context);
        var valueName = Input(operation, "valueName");

        try
        {
            _store.DeleteValue(hive, keyPath, valueName);
            return new ResourceProviderResult
            {
                Message = $"Replay rollback deleted registry value: {hive}\\{keyPath}\\{valueName}"
            };
        }
        catch (Exception ex)
        {
            return Error("BI3112", operation.Id, $"Replay rollback failed to delete registry value '{hive}\\{keyPath}\\{valueName}': {ex.Message}");
        }
    }

    private static InstallerRegistryHive ResolveHive(ResourceProviderContext context)
        => context.PerUser ? InstallerRegistryHive.CurrentUser : InstallerRegistryHive.LocalMachine;

    private static string ResolveKeyPath(CompiledInstallOperation operation, ResourceProviderContext context)
        => NormalizeKeyPath(ResolveValue(Input(operation, "keyPath"), context));

    private static RegistryValueKind ResolveValueKind(CompiledInstallOperation operation)
        => Enum.TryParse<RegistryValueKind>(Input(operation, "valueKind"), ignoreCase: true, out var valueKind)
            ? valueKind
            : RegistryValueKind.String;

    private static (bool Ok, object? Value, string Error) ConvertValue(string value, RegistryValueKind valueKind)
    {
        try
        {
            return valueKind switch
            {
                RegistryValueKind.DWord => int.TryParse(value, out var dword)
                    ? (true, dword, "")
                    : (false, null, $"Registry DWORD value must be a 32-bit integer: {value}"),
                RegistryValueKind.QWord => long.TryParse(value, out var qword)
                    ? (true, qword, "")
                    : (false, null, $"Registry QWORD value must be a 64-bit integer: {value}"),
                RegistryValueKind.MultiString => (true, value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ""),
                RegistryValueKind.Binary => (true, Convert.FromHexString(value.Replace(" ", "", StringComparison.Ordinal)), ""),
                _ => (true, value, "")
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return (false, null, $"Registry {valueKind} value could not be converted: {ex.Message}");
        }
    }

    private static bool RegistryValuesEqual(object? actual, object? expected, RegistryValueKind valueKind)
    {
        if (actual is string[] actualStrings && expected is string[] expectedStrings)
            return actualStrings.SequenceEqual(expectedStrings, StringComparer.Ordinal);
        if (actual is byte[] actualBytes && expected is byte[] expectedBytes)
            return actualBytes.SequenceEqual(expectedBytes);

        return string.Equals(
            Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
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

    private static string NormalizeKeyPath(string keyPath)
        => keyPath.Replace('/', '\\').Trim('\\');

    private static bool IsAbsoluteHivePath(string keyPath)
        => keyPath.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase)
           || keyPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
           || keyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
           || keyPath.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase);

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

    private sealed record RegistryRollbackState(
        InstallerRegistryHive Hive,
        string KeyPath,
        string ValueName,
        RegistryValueSnapshot Before);
}

internal sealed class WindowsInstallerRegistryStore : IInstallerRegistryStore
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public RegistryValueSnapshot ReadValue(InstallerRegistryHive hive, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        using var baseKey = OpenBaseKey(hive);
        using var key = baseKey.OpenSubKey(keyPath);
        if (key == null)
            return new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value == null
            ? new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown)
            : new RegistryValueSnapshot(true, value, key.GetValueKind(valueName));
    }

    public void WriteValue(InstallerRegistryHive hive, string keyPath, string valueName, object value, RegistryValueKind valueKind)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Registry writes require Windows registry support.");

        using var baseKey = OpenBaseKey(hive);
        using var key = baseKey.CreateSubKey(keyPath, writable: true);
        key.SetValue(valueName, value, valueKind);
    }

    public void DeleteValue(InstallerRegistryHive hive, string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Registry writes require Windows registry support.");

        using var baseKey = OpenBaseKey(hive);
        using var key = baseKey.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteKeyTree(InstallerRegistryHive hive, string keyPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Registry writes require Windows registry support.");

        using var baseKey = OpenBaseKey(hive);
        baseKey.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    private static RegistryKey OpenBaseKey(InstallerRegistryHive hive)
        => RegistryKey.OpenBaseKey(
            hive == InstallerRegistryHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
}

#pragma warning restore CA1416
