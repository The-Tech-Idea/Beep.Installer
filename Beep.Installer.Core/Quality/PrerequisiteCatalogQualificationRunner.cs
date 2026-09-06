using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;

namespace Beep.Installer.Quality;

public sealed class PrerequisiteCatalogQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string LayoutDirectory { get; init; } = "";
    public string LayoutSigningPrivateKeyPath { get; init; } = "";
    public string LayoutTrustedPublicKeyPath { get; init; } = "";
    public string LayoutTrustedPublicKey { get; init; } = "";
    public bool RequireSignedLayout { get; init; }
    public bool DownloadRemotePackages { get; init; }
    public InstallerPolicy? Policy { get; init; }
}

public sealed class PrerequisiteCatalogQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string LayoutDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<PrerequisiteCatalogQualificationScenario> Scenarios { get; init; } = new();
    public List<PrerequisiteCatalogQualifiedPackage> Packages { get; init; } = new();
}

public sealed class PrerequisiteCatalogQualificationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class PrerequisiteCatalogQualifiedPackage
{
    public string PackageId { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string PackageType { get; init; } = "";
    public string SourceKind { get; init; } = "";
    public string ContentAddress { get; init; } = "";
    public string BlobPath { get; init; } = "";
    public string Algorithm { get; init; } = "";
    public string ActualHash { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool AvailableOffline { get; init; }
}

public sealed class PrerequisiteCatalogQualificationRunner
{
    public const string ReportFileName = "prerequisite-catalog-qualification.json";

    public PrerequisiteCatalogQualificationReport Run(PrerequisiteCatalogQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "catalog-qualification")
            : options.OutputDirectory);
        var layoutDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.LayoutDirectory)
            ? Path.Combine(outputDirectory, "offline-layout")
            : options.LayoutDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<PrerequisiteCatalogQualificationScenario>();
        var packages = new List<PrerequisiteCatalogQualifiedPackage>();
        var baseDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "preflight", "Load the installer project that references prerequisite catalogs.", new List<ProjectSchemaDiagnostic>
            {
                Error("BI0501", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, layoutDirectory, started, scenarios, packages);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "preflight", "Load the installer project that references prerequisite catalogs.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));
        scenarios.Add(ResolveCatalogs(project, baseDirectory, options, outputDirectory));
        scenarios.Add(CompileCatalogPackageOperations(project, options, outputDirectory));
        scenarios.Add(BuildOfflineLayout(project, baseDirectory, layoutDirectory, options, outputDirectory));
        scenarios.Add(VerifyOfflineLayout(layoutDirectory, options, outputDirectory));
        packages.AddRange(ReadPackages(layoutDirectory));
        scenarios.Add(VerifyCatalogPackageOfflineCoverage(project, packages, outputDirectory));

        return Complete(projectPath, outputDirectory, layoutDirectory, started, scenarios, packages);
    }

    public static void WriteReport(PrerequisiteCatalogQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, PrerequisiteCatalogQualificationJsonContext.Default.PrerequisiteCatalogQualificationReport));
    }

    private static PrerequisiteCatalogQualificationScenario ResolveCatalogs(
        InstallProject project,
        string baseDirectory,
        PrerequisiteCatalogQualificationOptions options,
        string outputDirectory)
    {
        if (project.PrerequisiteCatalogs.Count == 0)
        {
            return Scenario("catalog-resolution", "catalog", "Resolve signed prerequisite catalogs into package nodes.", new List<ProjectSchemaDiagnostic>
            {
                Error("BI0502", "PrerequisiteCatalogs", "Project does not reference any prerequisite catalogs.")
            }, outputDirectory);
        }

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions
        {
            BaseDirectory = baseDirectory,
            Policy = options.Policy
        });
        var diagnostics = result.Diagnostics.ToList();
        if (result.CatalogsRead == 0)
            diagnostics.Add(Error("BI0503", "PrerequisiteCatalogs", "No prerequisite catalogs were accepted by the resolver."));
        if (result.PackagesAdded == 0)
            diagnostics.Add(Error("BI0504", "PrerequisiteCatalogs", "No prerequisite packages were projected from accepted catalogs."));
        return Scenario("catalog-resolution", "catalog", "Resolve signed prerequisite catalogs into package nodes.", diagnostics, outputDirectory);
    }

    private static PrerequisiteCatalogQualificationScenario CompileCatalogPackageOperations(
        InstallProject project,
        PrerequisiteCatalogQualificationOptions options,
        string outputDirectory)
    {
        var policyEvaluation = options.Policy is null ? null : InstallerPolicyEvaluator.EvaluateProject(options.Policy, project);
        var result = new InstallPlanCompiler().Compile(project, policyEvaluation);
        var diagnostics = result.Diagnostics.ToList();
        var packageOps = result.Plan?.Operations.Where(o => o.Type == "package.install").ToList() ?? new();
        if (packageOps.Count == 0)
            diagnostics.Add(Error("BI0505", "CompiledPlan.Packages", "Catalog packages did not compile into package.install operations."));
        if (packageOps.Any(o => string.IsNullOrWhiteSpace(Input(o, "downloadUrl")) && string.IsNullOrWhiteSpace(Input(o, "sourcePath"))))
            diagnostics.Add(Error("BI0506", "CompiledPlan.Packages", "Every catalog package operation must have a source path or download URL."));

        return Scenario("compiled-package-operations", "compiled-plan", "Catalog packages compile into canonical package.install operations.", diagnostics, outputDirectory);
    }

    private static PrerequisiteCatalogQualificationScenario BuildOfflineLayout(
        InstallProject project,
        string baseDirectory,
        string layoutDirectory,
        PrerequisiteCatalogQualificationOptions options,
        string outputDirectory)
    {
        var result = new OfflineLayoutBuilder().Build(project, layoutDirectory, new OfflineLayoutOptions
        {
            BaseDirectory = baseDirectory,
            DownloadRemotePackages = options.DownloadRemotePackages,
            SigningPrivateKeyPath = options.LayoutSigningPrivateKeyPath
        });
        return Scenario("offline-layout-build", "offline-layout", "Build the catalog-backed offline layout through the canonical offline layout builder.", result.Diagnostics, outputDirectory);
    }

    private static PrerequisiteCatalogQualificationScenario VerifyOfflineLayout(
        string layoutDirectory,
        PrerequisiteCatalogQualificationOptions options,
        string outputDirectory)
    {
        var result = new OfflineLayoutBuilder().Verify(layoutDirectory, new OfflineLayoutVerificationOptions
        {
            RequireSignature = options.RequireSignedLayout,
            TrustedPublicKeyPath = options.LayoutTrustedPublicKeyPath,
            TrustedPublicKey = options.LayoutTrustedPublicKey
        });
        return Scenario("air-gapped-layout-verification", "offline-layout", "Verify the offline inventory/blob set without contacting remote package sources.", result.Diagnostics, outputDirectory);
    }

    private static PrerequisiteCatalogQualificationScenario VerifyCatalogPackageOfflineCoverage(
        InstallProject project,
        IReadOnlyList<PrerequisiteCatalogQualifiedPackage> packages,
        string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var catalogPackageIds = project.Packages.Select(p => p.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in catalogPackageIds)
        {
            var matches = packages.Where(p => p.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                diagnostics.Add(Error("BI0507", $"OfflineLayout.{id}", $"Catalog package '{id}' is missing from the offline layout inventory."));
                continue;
            }

            if (matches.Any(p => !p.AvailableOffline || string.IsNullOrWhiteSpace(p.BlobPath) || string.IsNullOrWhiteSpace(p.ContentAddress)))
                diagnostics.Add(Error("BI0508", $"OfflineLayout.{id}", $"Catalog package '{id}' is not fully available offline with content-addressed blob evidence."));
        }

        return Scenario("catalog-package-offline-coverage", "offline-layout", "Every resolved catalog package has content-addressed offline blob evidence.", diagnostics, outputDirectory);
    }

    private static IReadOnlyList<PrerequisiteCatalogQualifiedPackage> ReadPackages(string layoutDirectory)
    {
        var inventoryPath = Path.Combine(layoutDirectory, OfflineLayoutBuilder.InventoryFileName);
        if (!File.Exists(inventoryPath))
            return Array.Empty<PrerequisiteCatalogQualifiedPackage>();

        using var document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        if (!document.RootElement.TryGetProperty("Packages", out var packages) && !document.RootElement.TryGetProperty("packages", out packages))
            return Array.Empty<PrerequisiteCatalogQualifiedPackage>();

        return packages.EnumerateArray().Select(p => new PrerequisiteCatalogQualifiedPackage
        {
            PackageId = Text(p, "PackageId", "packageId"),
            OperationId = Text(p, "OperationId", "operationId"),
            Architecture = Text(p, "Architecture", "architecture"),
            PackageType = Text(p, "PackageType", "packageType"),
            SourceKind = Text(p, "SourceKind", "sourceKind"),
            ContentAddress = Text(p, "ContentAddress", "contentAddress"),
            BlobPath = Text(p, "BlobPath", "blobPath"),
            Algorithm = Text(p, "Algorithm", "algorithm"),
            ActualHash = Text(p, "ActualHash", "actualHash"),
            SizeBytes = Long(p, "SizeBytes", "sizeBytes"),
            AvailableOffline = Bool(p, "AvailableOffline", "availableOffline")
        }).ToList();
    }

    private static PrerequisiteCatalogQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string layoutDirectory,
        DateTimeOffset started,
        List<PrerequisiteCatalogQualificationScenario> scenarios,
        List<PrerequisiteCatalogQualifiedPackage> packages)
    {
        var success = scenarios.All(s => s.Success);
        var report = new PrerequisiteCatalogQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            LayoutDirectory = layoutDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Prerequisite catalog qualification completed." : "Prerequisite catalog qualification failed.",
            Scenarios = scenarios,
            Packages = packages
        };
        WriteReport(report);
        return report;
    }

    private static PrerequisiteCatalogQualificationScenario Scenario(string id, string category, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, PrerequisiteCatalogQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new PrerequisiteCatalogQualificationScenario
        {
            Id = id,
            Category = category,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static string Input(CompiledInstallOperation operation, string name)
        => operation.Inputs.TryGetValue(name, out var value) ? value : "";

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static string Text(JsonElement element, params string[] names)
        => TryProperty(element, out var property, names) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";

    private static long Long(JsonElement element, params string[] names)
        => TryProperty(element, out var property, names) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value) ? value : 0;

    private static bool Bool(JsonElement element, params string[] names)
        => TryProperty(element, out var property, names) && property.ValueKind == JsonValueKind.True;

    private static bool TryProperty(JsonElement element, out JsonElement property, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out property))
                return true;

        property = default;
        return false;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PrerequisiteCatalogQualificationReport))]
[JsonSerializable(typeof(PrerequisiteCatalogQualificationScenario))]
[JsonSerializable(typeof(PrerequisiteCatalogQualifiedPackage))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class PrerequisiteCatalogQualificationJsonContext : JsonSerializerContext;
