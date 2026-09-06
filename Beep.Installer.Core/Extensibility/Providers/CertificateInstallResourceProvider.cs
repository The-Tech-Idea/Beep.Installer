using Beep.Installer.Engine;
using Beep.Installer.Models;
using System.Security.Cryptography.X509Certificates;

namespace Beep.Installer.Extensibility.Providers;

public sealed record CertificateSnapshot(bool Exists, string Thumbprint, string Subject, string FriendlyName);

public interface IInstallerCertificateStore
{
    bool IsSupported { get; }
    string ReadSourceThumbprint(string sourcePath, string password);
    CertificateSnapshot FindByThumbprint(InstallationScope location, string storeName, string thumbprint);
    string Import(string sourcePath, string password, InstallationScope location, string storeName, string friendlyName);
    void RemoveByThumbprint(InstallationScope location, string storeName, string thumbprint);
}

public sealed class CertificateInstallResourceProvider : IResourceProvider
{
    private readonly IInstallerCertificateStore _store;
    private readonly Dictionary<string, string> _appliedThumbprints = new(StringComparer.Ordinal);

    public CertificateInstallResourceProvider()
        : this(new WindowsInstallerCertificateStore())
    {
    }

    public CertificateInstallResourceProvider(IInstallerCertificateStore store)
    {
        _store = store;
    }

    public string ResourceType => "certificate.install";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem
        | InstallerExtensionPermission.MachineScope
        | InstallerExtensionPermission.Secrets;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var location = StoreLocation(operation, context);
        var storeName = StoreName(operation);
        var thumbprint = ResolveThumbprint(operation, context, requireSource: false);
        var snapshot = !string.IsNullOrWhiteSpace(thumbprint) && _store.IsSupported
            ? _store.FindByThumbprint(location, storeName, thumbprint)
            : new CertificateSnapshot(false, thumbprint, "", "");

        return new ResourceDetectionResult
        {
            Exists = snapshot.Exists,
            CurrentVersion = snapshot.Thumbprint,
            Facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["storeLocation"] = location.ToString(),
                ["storeName"] = storeName,
                ["thumbprint"] = thumbprint,
                ["subject"] = snapshot.Subject,
                ["friendlyName"] = snapshot.FriendlyName
            }
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "sourcePath")))
            return Error("BI5501", $"{operation.Id}.sourcePath", "Certificate operation is missing sourcePath.");
        if (string.IsNullOrWhiteSpace(StoreName(operation)))
            return Error("BI5502", $"{operation.Id}.storeName", "Certificate operation is missing storeName.");
        if (!IsSupportedLocation(Input(operation, "storeLocation")))
            return Error("BI5503", $"{operation.Id}.storeLocation", "Certificate storeLocation must be machine, localMachine, user or currentUser.");
        if (!string.IsNullOrWhiteSpace(Input(operation, "thumbprint")) && !IsValidThumbprint(Input(operation, "thumbprint")))
            return Error("BI5504", $"{operation.Id}.thumbprint", "Certificate thumbprint must be a 40-character SHA-1 hex value.");
        if (!context.DryRun && !_store.IsSupported)
            return Error("BI5505", operation.Id, "Certificate installation requires Windows certificate store support.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.None : ResourceChangeKind.Create,
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
                Message = "Dry run: certificate would be imported into the Windows certificate store."
            };

        var detection = Detect(operation, context);
        if (detection.Exists)
        {
            _appliedThumbprints[operation.Id] = detection.CurrentVersion;
            return new ResourceProviderResult
            {
                Message = $"Certificate already exists in {StoreLocation(operation, context)}\\{StoreName(operation)}."
            };
        }

        try
        {
            var thumbprint = _store.Import(
                ResolveValue(Input(operation, "sourcePath"), context),
                Input(operation, "password"),
                StoreLocation(operation, context),
                StoreName(operation),
                Input(operation, "friendlyName"));
            _appliedThumbprints[operation.Id] = thumbprint;

            return new ResourceProviderResult
            {
                Message = $"Imported certificate into {StoreLocation(operation, context)}\\{StoreName(operation)}."
            };
        }
        catch (Exception ex)
        {
            return Error("BI5510", operation.Id, $"Failed to import certificate: {ex.Message}");
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
                Message = "Dry run: certificate rollback would remove the certificate by thumbprint."
            };

        var thumbprint = _appliedThumbprints.TryGetValue(operation.Id, out var applied)
            ? applied
            : ResolveThumbprint(operation, context, requireSource: false);
        if (string.IsNullOrWhiteSpace(thumbprint))
            return Error("BI5520", operation.Id, "Certificate rollback requires a thumbprint or an available source certificate.");

        try
        {
            var location = StoreLocation(operation, context);
            var storeName = StoreName(operation);
            if (!_store.FindByThumbprint(location, storeName, thumbprint).Exists)
                return new ResourceProviderResult
                {
                    Code = ResourceProviderResultCode.Skipped,
                    Message = "Certificate rollback skipped because the certificate is not installed."
                };

            _store.RemoveByThumbprint(location, storeName, thumbprint);
            return new ResourceProviderResult
            {
                Message = $"Removed certificate from {location}\\{storeName}."
            };
        }
        catch (Exception ex)
        {
            return Error("BI5521", operation.Id, $"Failed to remove certificate: {ex.Message}");
        }
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: certificate verification would query the target certificate store."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var detection = Detect(operation, context);
        if (!detection.Exists)
            return Error("BI5530", operation.Id, $"Certificate was not found in {StoreLocation(operation, context)}\\{StoreName(operation)}.");

        return new ResourceProviderResult { Message = "Certificate exists in the requested certificate store." };
    }

    private static InstallationScope StoreLocation(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var value = Input(operation, "storeLocation");
        if (value.Equals("user", StringComparison.OrdinalIgnoreCase) || value.Equals("currentUser", StringComparison.OrdinalIgnoreCase))
            return InstallationScope.User;
        if (context.PerUser)
            return InstallationScope.User;
        return InstallationScope.Machine;
    }

    private static bool IsSupportedLocation(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals("machine", StringComparison.OrdinalIgnoreCase)
           || value.Equals("localMachine", StringComparison.OrdinalIgnoreCase)
           || value.Equals("user", StringComparison.OrdinalIgnoreCase)
           || value.Equals("currentUser", StringComparison.OrdinalIgnoreCase);

    private static string StoreName(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "storeName")) ? "My" : Input(operation, "storeName");

    private string ResolveThumbprint(CompiledInstallOperation operation, ResourceProviderContext context, bool requireSource)
    {
        var thumbprint = NormalizeThumbprint(Input(operation, "thumbprint"));
        if (!string.IsNullOrWhiteSpace(thumbprint))
            return thumbprint;

        if (!requireSource && string.IsNullOrWhiteSpace(Input(operation, "sourcePath")))
            return "";

        try
        {
            return NormalizeThumbprint(_store.ReadSourceThumbprint(
                ResolveValue(Input(operation, "sourcePath"), context),
                Input(operation, "password")));
        }
        catch
        {
            return "";
        }
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

    private static bool IsValidThumbprint(string value)
    {
        var normalized = NormalizeThumbprint(value);
        return normalized.Length == 40 && normalized.All(Uri.IsHexDigit);
    }

    private static string NormalizeThumbprint(string value)
        => value.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

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

internal sealed class WindowsInstallerCertificateStore : IInstallerCertificateStore
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string ReadSourceThumbprint(string sourcePath, string password)
    {
        using var certificate = LoadCertificate(sourcePath, password, InstallationScope.User);
        return certificate.Thumbprint ?? "";
    }

    public CertificateSnapshot FindByThumbprint(InstallationScope location, string storeName, string thumbprint)
    {
        if (!IsSupported)
            return new CertificateSnapshot(false, thumbprint, "", "");

        using var store = Open(location, storeName, OpenFlags.ReadOnly);
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .FirstOrDefault();

        return certificate == null
            ? new CertificateSnapshot(false, thumbprint, "", "")
            : new CertificateSnapshot(true, certificate.Thumbprint ?? thumbprint, certificate.Subject, certificate.FriendlyName);
    }

    public string Import(string sourcePath, string password, InstallationScope location, string storeName, string friendlyName)
    {
        using var certificate = LoadCertificate(sourcePath, password, location);
        if (!string.IsNullOrWhiteSpace(friendlyName) && OperatingSystem.IsWindows())
            certificate.FriendlyName = friendlyName;

        using var store = Open(location, storeName, OpenFlags.ReadWrite);
        store.Add(certificate);
        return certificate.Thumbprint ?? "";
    }

    public void RemoveByThumbprint(InstallationScope location, string storeName, string thumbprint)
    {
        using var store = Open(location, storeName, OpenFlags.ReadWrite);
        foreach (var certificate in store.Certificates
                     .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                     .OfType<X509Certificate2>())
        {
            store.Remove(certificate);
        }
    }

    private static X509Store Open(InstallationScope location, string storeName, OpenFlags flags)
    {
        var store = new X509Store(
            storeName,
            location == InstallationScope.User ? StoreLocation.CurrentUser : StoreLocation.LocalMachine);
        store.Open(flags);
        return store;
    }

    private static X509KeyStorageFlags StorageFlags(InstallationScope location)
        => X509KeyStorageFlags.PersistKeySet
           | (location == InstallationScope.User
               ? X509KeyStorageFlags.UserKeySet
               : X509KeyStorageFlags.MachineKeySet);

    private static X509Certificate2 LoadCertificate(string sourcePath, string password, InstallationScope location)
    {
        var extension = Path.GetExtension(sourcePath);
        return extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase)
            ? X509CertificateLoader.LoadPkcs12FromFile(sourcePath, password, StorageFlags(location))
            : X509CertificateLoader.LoadCertificateFromFile(sourcePath);
    }
}
