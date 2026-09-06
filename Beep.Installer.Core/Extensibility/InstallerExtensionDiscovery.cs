using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionDiscoveryOptions
{
    public string EngineVersion { get; init; } = "1.0.0";
    public bool RequireSignature { get; init; }
    public Beep.Installer.Policy.InstallerPolicy? Policy { get; init; }
    public bool LoadProviders { get; init; } = true;
}

public sealed class InstallerExtensionDiscoveryResult
{
    public List<InstallerExtensionReference> Extensions { get; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    /// <summary>Composes validated extensions with built-ins for the shared resource executor.</summary>
    public BuiltInResourceProviderRegistry CreateRegistry()
    {
        if (HasErrors)
            throw new InvalidOperationException("Cannot compose providers from failed extension discovery.");

        var registry = BuiltInInstallerResourceProviders.CreateDefaultRegistry();
        foreach (var extension in Extensions.OrderBy(e => e.Manifest.Id, StringComparer.Ordinal))
        {
            if (extension.Manifest.ResourceTypes.Any(type =>
                !extension.ResourceProviders.Any(provider => string.Equals(provider.ResourceType, type, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException($"Extension '{extension.Manifest.Id}' providers have not been loaded.");

            foreach (var provider in extension.ResourceProviders.OrderBy(p => p.ResourceType, StringComparer.Ordinal))
                registry.Register(provider);
        }
        return registry;
    }
}

public sealed class InstallerExtensionDiscovery
{
    public const string ManifestFileName = "beep-extension.json";

    private readonly InstallerExtensionDiscoveryOptions _options;

    public InstallerExtensionDiscovery(InstallerExtensionDiscoveryOptions? options = null)
    {
        _options = options ?? new InstallerExtensionDiscoveryOptions();
    }

    public InstallerExtensionDiscoveryResult DiscoverExplicitDirectories(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var result = new InstallerExtensionDiscoveryResult();
        var validator = new InstallerExtensionManifestValidator(_options.EngineVersion)
        {
            RequireSignature = _options.RequireSignature || _options.Policy?.RequireExtensionSignatures == true,
            TrustedPublicKeys = _options.Policy?.TrustedExtensionPublicKeys ?? new List<string>()
        };
        var resourceTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolvedDirectory = Path.GetFullPath(directory);
            var manifestPath = Path.Combine(resolvedDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                result.Diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI4101",
                    manifestPath,
                    $"Extension manifest was not found: {manifestPath}"));
                continue;
            }

            InstallerExtensionManifest? manifest;
            try
            {
                manifest = InstallerExtensionManifest.FromJson(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI4102",
                    manifestPath,
                    $"Extension manifest could not be parsed: {ex.Message}"));
                continue;
            }

            if (manifest == null)
            {
                result.Diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI4103",
                    manifestPath,
                    "Extension manifest is empty."));
                continue;
            }
            NormalizeManifest(manifest);

            var reference = new InstallerExtensionReference
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                DirectoryPath = resolvedDirectory,
                EntryAssemblyPath = ResolveEntryAssembly(resolvedDirectory, manifest.EntryAssembly)
            };

            var validation = validator.Validate(reference);
            result.Diagnostics.AddRange(validation.Diagnostics);
            if (validation.HasErrors)
                continue;

            if (_options.Policy is not null)
            {
                var policyEvaluation = Beep.Installer.Policy.InstallerPolicyEvaluator.EvaluateExtensions(
                    _options.Policy, new[] { reference });
                result.Diagnostics.AddRange(policyEvaluation.Diagnostics);
                if (policyEvaluation.HasErrors)
                    continue;
            }

            var loadResult = _options.LoadProviders
                ? LoadAndValidateProviders(reference)
                : InstallerExtensionProviderLoadResult.Empty;
            result.Diagnostics.AddRange(loadResult.Diagnostics);
            if (loadResult.HasErrors)
                continue;

            foreach (var resourceType in manifest.ResourceTypes)
            {
                if (resourceTypes.TryGetValue(resourceType, out var existing))
                {
                    result.Diagnostics.Add(new ProjectSchemaDiagnostic(
                        ProjectSchemaDiagnosticSeverity.Error,
                        "BI4104",
                        $"Extension.ResourceTypes[{resourceType}]",
                        $"Resource type '{resourceType}' is declared by both '{existing}' and '{manifest.Id}'."));
                }
                else
                {
                    resourceTypes[resourceType] = manifest.Id;
                }
            }

            result.Extensions.Add(new InstallerExtensionReference
            {
                Manifest = reference.Manifest,
                ManifestPath = reference.ManifestPath,
                DirectoryPath = reference.DirectoryPath,
                EntryAssemblyPath = reference.EntryAssemblyPath,
                ResourceProviders = loadResult.Providers,
                ProjectValidators = loadResult.Validators,
                PackageExporters = loadResult.Exporters
            });
        }

        return result;
    }

    private static void NormalizeManifest(InstallerExtensionManifest manifest)
    {
        manifest.ResourceTypes ??= new List<string>();
        manifest.ValidatorTypes ??= new List<string>();
        manifest.ExporterFormats ??= new List<string>();
    }

    private static InstallerExtensionProviderLoadResult LoadAndValidateProviders(InstallerExtensionReference reference)
    {
        var result = new InstallerExtensionProviderLoadResult();
        Assembly assembly;
        try
        {
            var loadContext = new InstallerExtensionLoadContext(reference.EntryAssemblyPath, reference.DirectoryPath, reference.Manifest.Files);
            using var stream = File.OpenRead(reference.EntryAssemblyPath);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            if (!hash.Equals(reference.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Extension assembly changed after manifest validation.");
            stream.Position = 0;
            assembly = loadContext.LoadFromStream(stream);
        }
        catch (Exception ex)
        {
            result.AddError(
                "BI4110",
                "Extension.EntryAssembly",
                $"Extension entry assembly could not be loaded: {ex.Message}");
            return result;
        }

        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var details = string.Join("; ", ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message).Distinct());
            result.AddError(
                "BI4111",
                "Extension.EntryAssembly",
                string.IsNullOrWhiteSpace(details)
                    ? "Extension provider types could not be inspected."
                    : $"Extension provider types could not be inspected: {details}");
            return result;
        }

        var providerTypes = exportedTypes
            .Where(t => typeof(IResourceProvider).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        var validatorTypes = exportedTypes
            .Where(t => typeof(IInstallerProjectValidator).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        var exporterTypes = exportedTypes
            .Where(t => typeof(IInstallerPackageExporter).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        if (reference.Manifest.ResourceTypes.Count > 0 && providerTypes.Count == 0)
        {
            result.AddError(
                "BI4112",
                "Extension.EntryAssembly",
                $"Extension '{reference.Manifest.Id}' does not export any public IResourceProvider implementation.");
        }

        var declaredTypes = new HashSet<string>(reference.Manifest.ResourceTypes, StringComparer.OrdinalIgnoreCase);
        var loadedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerType in providerTypes)
        {
            IResourceProvider provider;
            try
            {
                provider = (IResourceProvider?)Activator.CreateInstance(providerType)
                    ?? throw new InvalidOperationException("Provider constructor returned null.");
            }
            catch (Exception ex)
            {
                result.AddError(
                    "BI4113",
                    providerType.FullName ?? providerType.Name,
                    $"Provider '{providerType.FullName}' could not be created. Providers must expose a public parameterless constructor. {ex.GetBaseException().Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(provider.ResourceType))
            {
                result.AddError(
                    "BI4114",
                    providerType.FullName ?? providerType.Name,
                    $"Provider '{providerType.FullName}' returned an empty ResourceType.");
                continue;
            }

            if (!declaredTypes.Contains(provider.ResourceType))
            {
                result.AddError(
                    "BI4115",
                    providerType.FullName ?? providerType.Name,
                    $"Provider '{providerType.FullName}' implements resource type '{provider.ResourceType}', but the manifest does not declare it.");
                continue;
            }

            var deniedPermissions = provider.RequiredPermissions & ~reference.Manifest.Permissions;
            if (deniedPermissions != InstallerExtensionPermission.None)
            {
                result.AddError(
                    "BI4116",
                    providerType.FullName ?? providerType.Name,
                    $"Provider '{providerType.FullName}' requires permission(s) '{deniedPermissions}' not granted by the manifest.");
                continue;
            }

            if (!loadedTypes.Add(provider.ResourceType))
            {
                result.AddError(
                    "BI4117",
                    providerType.FullName ?? providerType.Name,
                    $"Resource type '{provider.ResourceType}' is implemented by more than one provider in extension '{reference.Manifest.Id}'.");
                continue;
            }

            result.Providers.Add(provider);
        }

        foreach (var missing in declaredTypes.Where(t => !loadedTypes.Contains(t)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            result.AddError(
                "BI4118",
                $"Extension.ResourceTypes[{missing}]",
                $"Manifest declares resource type '{missing}', but no loaded provider implements it.");
        }

        LoadAndValidateValidators(reference, validatorTypes, result);
        LoadAndValidateExporters(reference, exporterTypes, result);

        return result;
    }

    private static void LoadAndValidateValidators(
        InstallerExtensionReference reference,
        IReadOnlyList<Type> validatorTypes,
        InstallerExtensionProviderLoadResult result)
    {
        if (reference.Manifest.ValidatorTypes.Count == 0)
            return;

        if (validatorTypes.Count == 0)
        {
            result.AddError(
                "BI4120",
                "Extension.EntryAssembly",
                $"Extension '{reference.Manifest.Id}' declares validator types but does not export any public IInstallerProjectValidator implementation.");
            return;
        }

        var declared = new HashSet<string>(reference.Manifest.ValidatorTypes, StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var validatorType in validatorTypes)
        {
            IInstallerProjectValidator validator;
            try
            {
                validator = (IInstallerProjectValidator?)Activator.CreateInstance(validatorType)
                    ?? throw new InvalidOperationException("Validator constructor returned null.");
            }
            catch (Exception ex)
            {
                result.AddError(
                    "BI4121",
                    validatorType.FullName ?? validatorType.Name,
                    $"Validator '{validatorType.FullName}' could not be created. Validators must expose a public parameterless constructor. {ex.GetBaseException().Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(validator.ValidatorId))
            {
                result.AddError("BI4122", validatorType.FullName ?? validatorType.Name, $"Validator '{validatorType.FullName}' returned an empty ValidatorId.");
                continue;
            }

            if (!declared.Contains(validator.ValidatorId))
            {
                result.AddError("BI4123", validatorType.FullName ?? validatorType.Name, $"Validator '{validatorType.FullName}' implements validator type '{validator.ValidatorId}', but the manifest does not declare it.");
                continue;
            }

            var deniedPermissions = validator.RequiredPermissions & ~reference.Manifest.Permissions;
            if (deniedPermissions != InstallerExtensionPermission.None)
            {
                result.AddError("BI4124", validatorType.FullName ?? validatorType.Name, $"Validator '{validatorType.FullName}' requires permission(s) '{deniedPermissions}' not granted by the manifest.");
                continue;
            }

            if (!loaded.Add(validator.ValidatorId))
            {
                result.AddError("BI4125", validatorType.FullName ?? validatorType.Name, $"Validator type '{validator.ValidatorId}' is implemented by more than one validator in extension '{reference.Manifest.Id}'.");
                continue;
            }

            result.Validators.Add(validator);
        }

        foreach (var missing in declared.Where(t => !loaded.Contains(t)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            result.AddError("BI4126", $"Extension.ValidatorTypes[{missing}]", $"Manifest declares validator type '{missing}', but no loaded validator implements it.");
    }

    private static void LoadAndValidateExporters(
        InstallerExtensionReference reference,
        IReadOnlyList<Type> exporterTypes,
        InstallerExtensionProviderLoadResult result)
    {
        if (reference.Manifest.ExporterFormats.Count == 0)
            return;

        if (exporterTypes.Count == 0)
        {
            result.AddError(
                "BI4130",
                "Extension.EntryAssembly",
                $"Extension '{reference.Manifest.Id}' declares exporter formats but does not export any public IInstallerPackageExporter implementation.");
            return;
        }

        var declared = new HashSet<string>(reference.Manifest.ExporterFormats, StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exporterType in exporterTypes)
        {
            IInstallerPackageExporter exporter;
            try
            {
                exporter = (IInstallerPackageExporter?)Activator.CreateInstance(exporterType)
                    ?? throw new InvalidOperationException("Exporter constructor returned null.");
            }
            catch (Exception ex)
            {
                result.AddError(
                    "BI4131",
                    exporterType.FullName ?? exporterType.Name,
                    $"Exporter '{exporterType.FullName}' could not be created. Exporters must expose a public parameterless constructor. {ex.GetBaseException().Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(exporter.Format))
            {
                result.AddError("BI4132", exporterType.FullName ?? exporterType.Name, $"Exporter '{exporterType.FullName}' returned an empty Format.");
                continue;
            }

            if (!declared.Contains(exporter.Format))
            {
                result.AddError("BI4133", exporterType.FullName ?? exporterType.Name, $"Exporter '{exporterType.FullName}' implements exporter format '{exporter.Format}', but the manifest does not declare it.");
                continue;
            }

            var deniedPermissions = exporter.RequiredPermissions & ~reference.Manifest.Permissions;
            if (deniedPermissions != InstallerExtensionPermission.None)
            {
                result.AddError("BI4134", exporterType.FullName ?? exporterType.Name, $"Exporter '{exporterType.FullName}' requires permission(s) '{deniedPermissions}' not granted by the manifest.");
                continue;
            }

            if (!loaded.Add(exporter.Format))
            {
                result.AddError("BI4135", exporterType.FullName ?? exporterType.Name, $"Exporter format '{exporter.Format}' is implemented by more than one exporter in extension '{reference.Manifest.Id}'.");
                continue;
            }

            result.Exporters.Add(exporter);
        }

        foreach (var missing in declared.Where(t => !loaded.Contains(t)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            result.AddError("BI4136", $"Extension.ExporterFormats[{missing}]", $"Manifest declares exporter format '{missing}', but no loaded exporter implements it.");
    }

    private static string ResolveEntryAssembly(string directory, string entryAssembly)
    {
        if (string.IsNullOrWhiteSpace(entryAssembly))
            return "";

        if (Path.IsPathRooted(entryAssembly))
            return Path.GetFullPath(entryAssembly);

        return Path.GetFullPath(Path.Combine(directory, entryAssembly));
    }
}

internal sealed class InstallerExtensionProviderLoadResult
{
    public static InstallerExtensionProviderLoadResult Empty { get; } = new();
    public List<IResourceProvider> Providers { get; } = new();
    public List<IInstallerProjectValidator> Validators { get; } = new();
    public List<IInstallerPackageExporter> Exporters { get; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    public void AddError(string code, string path, string message)
        => Diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, code, path, message));
}

internal sealed class InstallerExtensionLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _packageRoot;
    private readonly IReadOnlyDictionary<string, string> _inventory;

    public InstallerExtensionLoadContext(string entryAssemblyPath, string packageRoot, IReadOnlyDictionary<string, string>? inventory)
        : base($"BeepInstallerExtension:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}:{Guid.NewGuid():N}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _packageRoot = Path.GetFullPath(packageRoot);
        _inventory = inventory is null ? new Dictionary<string, string>() : inventory.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
            AssemblyName.ReferenceMatchesDefinition(a.GetName(), assemblyName));
        if (shared != null)
            return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null) return null;
        ValidateDependencyPath(path);
        using var stream = File.OpenRead(path);
        VerifyDependencyStream(path, stream);
        return LoadFromStream(stream);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is null) return IntPtr.Zero;
        ValidateDependencyPath(path);
        using var stream = File.OpenRead(path);
        VerifyDependencyStream(path, stream);
        return LoadUnmanagedDllFromPath(path);
    }

    private void VerifyDependencyStream(string path, Stream stream)
    {
        if (_inventory.Count == 0) return;
        var relative = Path.GetRelativePath(_packageRoot, path).Replace('\\', '/');
        if (!_inventory.TryGetValue(relative, out var expected)
            || !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Extension dependency no longer matches its verified inventory.");
        stream.Position = 0;
    }

    private void ValidateDependencyPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_packageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Extension dependency resolves outside its package directory.");
        for (var current = fullPath; !string.Equals(current, _packageRoot, StringComparison.OrdinalIgnoreCase);
            current = Path.GetDirectoryName(current)!)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Extension dependency path contains a link.");
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(InstallerExtensionManifest))]
internal sealed partial class InstallerExtensionJsonContext : JsonSerializerContext
{
}
