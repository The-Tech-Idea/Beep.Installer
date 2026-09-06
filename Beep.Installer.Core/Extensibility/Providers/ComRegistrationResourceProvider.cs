using Beep.Installer.Engine;
using Microsoft.Win32;

namespace Beep.Installer.Extensibility.Providers;

#pragma warning disable CA1416 // RegistryValueKind is installer metadata; registry execution is guarded by IInstallerRegistryStore.IsSupported.

public sealed class ComRegistrationResourceProvider : IResourceProvider
{
    private readonly IInstallerRegistryStore _store;

    public ComRegistrationResourceProvider()
        : this(new WindowsInstallerRegistryStore())
    {
    }

    public ComRegistrationResourceProvider(IInstallerRegistryStore store)
    {
        _store = store;
    }

    public string ResourceType => "com.register";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Registry | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var hive = ResolveHive(context);
        var clsidKey = ClsidKey(operation);
        var serverKey = ServerKey(operation);
        var clsidDefault = _store.IsSupported
            ? _store.ReadValue(hive, clsidKey, "")
            : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);
        var serverDefault = _store.IsSupported
            ? _store.ReadValue(hive, serverKey, "")
            : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        return new ResourceDetectionResult
        {
            Exists = clsidDefault.Exists && serverDefault.Exists,
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["hive"] = hive.ToString(),
                ["clsid"] = Clsid(operation),
                ["clsidKey"] = clsidKey,
                ["serverKey"] = serverKey,
                ["serverType"] = ServerType(operation),
                ["clsidExists"] = clsidDefault.Exists ? "true" : "false",
                ["serverExists"] = serverDefault.Exists ? "true" : "false"
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "clsid")))
            return Error("BI5601", $"{operation.Id}.clsid", "COM registration operation is missing clsid.");
        if (!Guid.TryParse(Input(operation, "clsid"), out _))
            return Error("BI5602", $"{operation.Id}.clsid", "COM registration clsid must be a GUID.");
        if (string.IsNullOrWhiteSpace(Input(operation, "serverPath")))
            return Error("BI5603", $"{operation.Id}.serverPath", "COM registration operation is missing serverPath.");
        if (!IsSupportedServerType(Input(operation, "serverType")))
            return Error("BI5604", $"{operation.Id}.serverType", "COM serverType must be inProc, inproc, localServer, local or exe.");
        if (!IsValidThreadingModel(Input(operation, "threadingModel")))
            return Error("BI5605", $"{operation.Id}.threadingModel", "COM threadingModel must be Apartment, Free, Both, Neutral or empty.");
        if (!string.IsNullOrWhiteSpace(Input(operation, "typeLibId")) && !Guid.TryParse(Input(operation, "typeLibId"), out _))
            return Error("BI5606", $"{operation.Id}.typeLibId", "COM typeLibId must be a GUID.");
        if (!context.DryRun && !_store.IsSupported)
            return Error("BI5607", operation.Id, "COM registration requires Windows registry support.");

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
                Message = "Dry run: COM registration would write HKCR-compatible registry keys."
            };

        var hive = ResolveHive(context);
        var clsid = Clsid(operation);
        var clsidKey = ClsidKey(operation);
        var description = Description(operation);

        try
        {
            _store.WriteValue(hive, clsidKey, "", description, RegistryValueKind.String);
            _store.WriteValue(hive, ServerKey(operation), "", BuildServerCommand(operation, context), RegistryValueKind.String);

            if (IsInProc(operation) && !string.IsNullOrWhiteSpace(Input(operation, "threadingModel")))
                _store.WriteValue(hive, ServerKey(operation), "ThreadingModel", Input(operation, "threadingModel"), RegistryValueKind.String);

            WriteOptional(hive, clsidKey + "\\ProgID", "", Input(operation, "progId"));
            WriteOptional(hive, clsidKey + "\\VersionIndependentProgID", "", Input(operation, "versionIndependentProgId"));
            WriteOptional(hive, clsidKey + "\\TypeLib", "", NormalizeGuid(Input(operation, "typeLibId")));
            WriteOptional(hive, clsidKey + "\\Version", "", Input(operation, "version"));

            RegisterProgId(hive, Input(operation, "progId"), clsid, description);
            RegisterProgId(hive, Input(operation, "versionIndependentProgId"), clsid, description);

            return new ResourceProviderResult
            {
                Message = $"Registered COM class {clsid}."
            };
        }
        catch (Exception ex)
        {
            return Error("BI5610", operation.Id, $"Failed to register COM class '{clsid}': {ex.Message}");
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
                Message = "Dry run: COM registration rollback would delete owned registry keys."
            };

        var hive = ResolveHive(context);
        var clsid = Clsid(operation);
        try
        {
            _store.DeleteKeyTree(hive, ClsidKey(operation));
            DeleteProgIdIfOwned(hive, Input(operation, "progId"), clsid);
            DeleteProgIdIfOwned(hive, Input(operation, "versionIndependentProgId"), clsid);
            return new ResourceProviderResult { Message = $"Removed COM class {clsid}." };
        }
        catch (Exception ex)
        {
            return Error("BI5620", operation.Id, $"Failed to roll back COM class '{clsid}': {ex.Message}");
        }
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: COM registration verification would read registry keys."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var hive = ResolveHive(context);
        var serverDefault = _store.ReadValue(hive, ServerKey(operation), "");
        if (!serverDefault.Exists)
            return Error("BI5630", operation.Id, "COM server registry value is missing.");
        if (!string.Equals(Convert.ToString(serverDefault.Value, System.Globalization.CultureInfo.InvariantCulture), BuildServerCommand(operation, context), StringComparison.Ordinal))
            return Error("BI5631", operation.Id, "COM server registry value differs from the compiled plan.");

        var progId = Input(operation, "progId");
        if (!string.IsNullOrWhiteSpace(progId))
        {
            var clsidValue = _store.ReadValue(hive, ProgIdKey(progId) + "\\CLSID", "");
            if (!clsidValue.Exists
                || !string.Equals(Convert.ToString(clsidValue.Value, System.Globalization.CultureInfo.InvariantCulture), Clsid(operation), StringComparison.OrdinalIgnoreCase))
            {
                return Error("BI5632", operation.Id, "COM ProgId registry mapping is missing or points to another CLSID.");
            }
        }

        return new ResourceProviderResult { Message = "COM registry keys verified." };
    }

    private void RegisterProgId(InstallerRegistryHive hive, string progId, string clsid, string description)
    {
        if (string.IsNullOrWhiteSpace(progId))
            return;

        var key = ProgIdKey(progId);
        _store.WriteValue(hive, key, "", description, RegistryValueKind.String);
        _store.WriteValue(hive, key + "\\CLSID", "", clsid, RegistryValueKind.String);
    }

    private void DeleteProgIdIfOwned(InstallerRegistryHive hive, string progId, string clsid)
    {
        if (string.IsNullOrWhiteSpace(progId))
            return;

        var key = ProgIdKey(progId);
        var owner = _store.ReadValue(hive, key + "\\CLSID", "");
        if (owner.Exists
            && string.Equals(Convert.ToString(owner.Value, System.Globalization.CultureInfo.InvariantCulture), clsid, StringComparison.OrdinalIgnoreCase))
        {
            _store.DeleteKeyTree(hive, key);
        }
    }

    private void WriteOptional(InstallerRegistryHive hive, string keyPath, string valueName, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _store.WriteValue(hive, keyPath, valueName, value, RegistryValueKind.String);
    }

    private static InstallerRegistryHive ResolveHive(ResourceProviderContext context)
        => context.PerUser ? InstallerRegistryHive.CurrentUser : InstallerRegistryHive.LocalMachine;

    private static string ClsidKey(CompiledInstallOperation operation)
        => "Software\\Classes\\CLSID\\" + Clsid(operation);

    private static string ServerKey(CompiledInstallOperation operation)
        => ClsidKey(operation) + (IsInProc(operation) ? "\\InprocServer32" : "\\LocalServer32");

    private static string ProgIdKey(string progId)
        => "Software\\Classes\\" + progId.Trim('\\');

    private static bool IsInProc(CompiledInstallOperation operation)
        => !ServerType(operation).Equals("localServer", StringComparison.OrdinalIgnoreCase);

    private static string ServerType(CompiledInstallOperation operation)
    {
        var value = Input(operation, "serverType");
        return value.Equals("local", StringComparison.OrdinalIgnoreCase)
               || value.Equals("localServer", StringComparison.OrdinalIgnoreCase)
               || value.Equals("exe", StringComparison.OrdinalIgnoreCase)
               || value.Equals("LocalServer", StringComparison.OrdinalIgnoreCase)
            ? "localServer"
            : "inProc";
    }

    private static bool IsSupportedServerType(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals("inProc", StringComparison.OrdinalIgnoreCase)
           || value.Equals("inproc", StringComparison.OrdinalIgnoreCase)
           || value.Equals("localServer", StringComparison.OrdinalIgnoreCase)
           || value.Equals("local", StringComparison.OrdinalIgnoreCase)
           || value.Equals("exe", StringComparison.OrdinalIgnoreCase);

    private static string BuildServerCommand(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var server = QuoteIfNeeded(ResolveValue(Input(operation, "serverPath"), context));
        if (IsInProc(operation))
            return server;

        var arguments = ResolveValue(Input(operation, "arguments"), context);
        return string.IsNullOrWhiteSpace(arguments) ? server : server + " " + arguments;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.StartsWith('"') && value.EndsWith('"')
            ? value
            : "\"" + value.Trim('"') + "\"";
    }

    private static string Description(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "description"))
            ? Clsid(operation)
            : Input(operation, "description");

    private static string Clsid(CompiledInstallOperation operation)
        => NormalizeGuid(Input(operation, "clsid"));

    private static string NormalizeGuid(string value)
        => Guid.TryParse(value, out var guid) ? guid.ToString("B").ToUpperInvariant() : value;

    private static bool IsValidThreadingModel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Equals("Apartment", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Free", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Both", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Neutral", StringComparison.OrdinalIgnoreCase);
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
