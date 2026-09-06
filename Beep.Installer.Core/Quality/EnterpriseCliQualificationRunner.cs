using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class EnterpriseCliQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class EnterpriseCliQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<EnterpriseCliQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class EnterpriseCliQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class EnterpriseCliQualificationRunner
{
    public const string ReportFileName = "enterprise-cli-qualification.json";

    public EnterpriseCliQualificationReport Run(EnterpriseCliQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "enterprise-cli-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<EnterpriseCliQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for CLI qualification.", new[]
            {
                Error("BI10001", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for CLI qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        ValidateJsonResponseExpansion(projectPath, outputDirectory, scenarios);
        ValidateRspResponseExpansion(projectPath, outputDirectory, scenarios);
        ValidateModernAliasNormalization(projectPath, outputDirectory, scenarios);
        ValidateUnknownArgumentFails(outputDirectory, scenarios);
        ValidateMissingResponseFails(outputDirectory, scenarios);
        ValidatePropertyCatalogContract(project, outputDirectory, scenarios);
        ValidateNoCliSecretLeak(outputDirectory, scenarios);

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(EnterpriseCliQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, EnterpriseCliQualificationJsonContext.Default.EnterpriseCliQualificationReport));
    }

    private static void ValidateJsonResponseExpansion(
        string projectPath,
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var responsePath = Path.Combine(outputDirectory, "unattended.response.json");
        var installPath = Path.Combine(outputDirectory, "Program Files", "My App Ω");
        File.WriteAllText(responsePath, JsonSerializer.Serialize(new
        {
            silent = true,
            json = true,
            noRestart = true,
            dryRun = true,
            script = projectPath,
            installPath,
            components = new[] { "core", "docs" },
            properties = new Dictionary<string, object?>
            {
                ["AcceptLicense"] = true,
                ["Tenant"] = "acme",
                ["ApiToken"] = "secret://env/API_TOKEN"
            }
        }, new JsonSerializerOptions { WriteIndented = true }));

        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        diagnostics.AddRange(result.Diagnostics);
        Require(result.Args, "/S", diagnostics, "BI10002", "JsonResponse.silent");
        Require(result.Args, "/JSON", diagnostics, "BI10003", "JsonResponse.json");
        Require(result.Args, "/NORESTART", diagnostics, "BI10004", "JsonResponse.noRestart");
        Require(result.Args, "/DRYRUN=true", diagnostics, "BI10005", "JsonResponse.dryRun");
        Require(result.Args, "/SCRIPT=" + projectPath, diagnostics, "BI10006", "JsonResponse.script");
        Require(result.Args, "/D=" + installPath, diagnostics, "BI10007", "JsonResponse.installPath");
        Require(result.Args, "/COMPONENTS=core,docs", diagnostics, "BI10008", "JsonResponse.components");
        Require(result.Args, "/PROPERTY:AcceptLicense=True", diagnostics, "BI10009", "JsonResponse.properties");
        Require(result.Args, "/PROPERTY:Tenant=acme", diagnostics, "BI10010", "JsonResponse.properties");
        Require(result.Args, "/PROPERTY:ApiToken=secret://env/API_TOKEN", diagnostics, "BI10011", "JsonResponse.properties");

        scenarios.Add(Scenario("json-response-expansion", "UTF-8 JSON response files expand into canonical unattended installer switches with Unicode path preservation.", diagnostics, outputDirectory));
    }

    private static void ValidateRspResponseExpansion(
        string projectPath,
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var responsePath = Path.Combine(outputDirectory, "quoted install.rsp");
        var planOut = Path.Combine(outputDirectory, "plans with spaces", "install-plan.json");
        File.WriteAllText(responsePath, string.Join(Environment.NewLine, new[]
        {
            "# comment lines are ignored",
            "\"--plan=" + projectPath + "\"",
            "\"--out=" + planOut + "\"",
            "--json",
            "--no-prompt",
            "\"/PROPERTY:DisplayName=My App Ω\""
        }));

        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "@" + responsePath });
        diagnostics.AddRange(result.Diagnostics);
        Require(result.Args, "/PLAN=" + projectPath, diagnostics, "BI10012", "RspResponse.plan");
        Require(result.Args, "/OUT=" + planOut, diagnostics, "BI10013", "RspResponse.out");
        Require(result.Args, "/JSON", diagnostics, "BI10014", "RspResponse.json");
        Require(result.Args, "/NOPROMPT", diagnostics, "BI10015", "RspResponse.noPrompt");
        Require(result.Args, "/PROPERTY:DisplayName=My App Ω", diagnostics, "BI10016", "RspResponse.property");

        scenarios.Add(Scenario("rsp-response-expansion", ".rsp response files preserve quotes, spaces, comments and Unicode values through the canonical parser.", diagnostics, outputDirectory));
    }

    private static void ValidateModernAliasNormalization(
        string projectPath,
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var installPath = Path.Combine(outputDirectory, "modern install");
        var result = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--validate=" + projectPath,
            "--install-dir=" + installPath,
            "--components=core,docs",
            "--log=" + Path.Combine(outputDirectory, "installer.log"),
            "--journal=" + Path.Combine(outputDirectory, "journal.json"),
            "--out=" + Path.Combine(outputDirectory, "validation.json"),
            "--format=json",
            "--json",
            "--dry-run",
            "--silent",
            "--no-prompt"
        });

        diagnostics.AddRange(result.Diagnostics);
        Require(result.Args, "/VALIDATE=" + projectPath, diagnostics, "BI10017", "ModernAliases.validate");
        Require(result.Args, "/D=" + installPath, diagnostics, "BI10018", "ModernAliases.installDir");
        Require(result.Args, "/COMPONENTS=core,docs", diagnostics, "BI10019", "ModernAliases.components");
        Require(result.Args, "/JSON", diagnostics, "BI10020", "ModernAliases.json");
        Require(result.Args, "/DRYRUN=true", diagnostics, "BI10021", "ModernAliases.dryRun");
        Require(result.Args, "/S", diagnostics, "BI10022", "ModernAliases.silent");
        Require(result.Args, "/NOPROMPT", diagnostics, "BI10023", "ModernAliases.noPrompt");

        scenarios.Add(Scenario("modern-alias-normalization", "Modern long-form automation aliases normalize to the installer switch contract used by the rest of the runtime.", diagnostics, outputDirectory));
    }

    private static void ValidateUnknownArgumentFails(
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/NOT-A-REAL-INSTALLER-SWITCH" });
        if (!result.HasErrors || result.Diagnostics.All(d => d.Code != "BI7005"))
            diagnostics.Add(Error("BI10024", "UnknownArgument", "Unknown installer arguments must fail closed with BI7005."));
        scenarios.Add(Scenario("unknown-argument-fails", "Unknown command-line arguments fail closed instead of being ignored.", diagnostics, outputDirectory));
    }

    private static void ValidateMissingResponseFails(
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var missingPath = Path.Combine(outputDirectory, "missing.response.json");
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + missingPath });
        if (!result.HasErrors || result.Diagnostics.All(d => d.Code != "BI7001"))
            diagnostics.Add(Error("BI10025", "MissingResponse", "Missing response files must fail with BI7001."));
        scenarios.Add(Scenario("missing-response-fails", "Missing response files produce a machine-readable error instead of falling back to prompts.", diagnostics, outputDirectory));
    }

    private static void ValidatePropertyCatalogContract(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var catalog = EnterprisePropertyCatalog.ForProject(project);
        var catalogPath = Path.Combine(outputDirectory, "property-catalog.json");
        File.WriteAllText(catalogPath, EnterprisePropertyCatalog.ToJson(catalog));

        RequireProperty(catalog, "InstallPath", "/D=<path>", diagnostics, "BI10026");
        RequireProperty(catalog, "Components", "/COMPONENTS=<id,id>", diagnostics, "BI10027");
        RequireProperty(catalog, "AcceptLicense", "/PROPERTY:AcceptLicense=true", diagnostics, "BI10028");
        RequireProperty(catalog, "InstallType", "/PROPERTY:InstallType=Typical|Complete|Custom", diagnostics, "BI10029");
        RequireProperty(catalog, "CreateStartMenu", "/PROPERTY:CreateStartMenu=true|false", diagnostics, "BI10030");
        RequireProperty(catalog, "CreateDesktopIcon", "/PROPERTY:CreateDesktopIcon=true|false", diagnostics, "BI10031");

        if (catalog.Properties.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            diagnostics.Add(Error("BI10032", "PropertyCatalog", "Property catalog contains duplicate response-file property names."));
        if (catalog.Properties.Any(p => string.IsNullOrWhiteSpace(p.ResponseFileSyntax) || string.IsNullOrWhiteSpace(p.CommandLineSyntax)))
            diagnostics.Add(Error("BI10033", "PropertyCatalog", "Every property must document response-file and command-line syntax."));

        scenarios.Add(Scenario("property-catalog-contract", "The unattended property catalog exposes every core interactive installer choice with response-file and CLI syntax.", diagnostics, outputDirectory));
    }

    private static void ValidateNoCliSecretLeak(
        string outputDirectory,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.AllDirectories)
                     .Where(IsGeneratedCliEvidence))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("hunter2", StringComparison.Ordinal)
                || text.Contains("abc123", StringComparison.Ordinal)
                || text.Contains("super-secret", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI10034", Path.GetFileName(file), "CLI qualification evidence leaked a seeded secret."));
            }
        }

        scenarios.Add(Scenario("no-cli-secret-leak", "Generated CLI qualification evidence contains no seeded secrets.", diagnostics, outputDirectory));
    }

    private static bool IsGeneratedCliEvidence(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".cli.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals(ReportFileName, StringComparison.OrdinalIgnoreCase)
               || name.Equals("property-catalog.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void Require(string[] args, string expected, List<ProjectSchemaDiagnostic> diagnostics, string code, string path)
    {
        if (!args.Contains(expected, StringComparer.OrdinalIgnoreCase))
            diagnostics.Add(Error(code, path, $"Expected canonical argument '{expected}'."));
    }

    private static void RequireProperty(
        EnterprisePropertyCatalog catalog,
        string name,
        string commandLineSyntax,
        List<ProjectSchemaDiagnostic> diagnostics,
        string code)
    {
        var property = catalog.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (property is null)
        {
            diagnostics.Add(Error(code, "PropertyCatalog." + name, $"Missing property '{name}'."));
            return;
        }

        if (!string.Equals(property.CommandLineSyntax, commandLineSyntax, StringComparison.Ordinal))
            diagnostics.Add(Error(code, "PropertyCatalog." + name, $"Property '{name}' has unexpected command-line syntax."));
    }

    private static EnterpriseCliQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<EnterpriseCliQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new EnterpriseCliQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Enterprise CLI qualification completed." : "Enterprise CLI qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static EnterpriseCliQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".cli.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, EnterpriseCliQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new EnterpriseCliQualificationScenario
        {
            Id = id,
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

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EnterpriseCliQualificationReport))]
[JsonSerializable(typeof(EnterpriseCliQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class EnterpriseCliQualificationJsonContext : JsonSerializerContext;
