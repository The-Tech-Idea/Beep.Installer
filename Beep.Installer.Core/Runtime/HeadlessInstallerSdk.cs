using Beep.Installer.Models;
using Beep.Installer.Extensibility;
using Beep.Installer.Policy;

namespace Beep.Installer.Engine;

public sealed class HeadlessInstallerRequest
{
    public string ProjectPath { get; init; } = "";
    public bool Strict { get; init; }
    public bool ForbidLiteralSecrets { get; init; }
    public string OutputDirectory { get; init; } = "";
    public InstallerOutputFormat? OutputFormat { get; init; }
    public bool CleanOutput { get; init; }
    public bool RequireSigned { get; init; }
    public string ExpectedSigningSubject { get; init; } = "";
    public string TimestampOutagePolicy { get; init; } = "";
    public int TimestampRetryCount { get; init; } = 2;
    public IProgress<BuildPipeline.BuildProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
    public IInstallerHostBuilder? HostBuilder { get; init; }
    public Func<InstallProject, InstallerPolicyEvaluation?>? PolicyEvaluator { get; init; }
    public IReadOnlyList<string> ExtensionDirectories { get; init; } = Array.Empty<string>();
    public bool RequireSignedExtensions { get; init; }
    public string ExtensionEngineVersion { get; init; } = "1.0.0";
}

public abstract class HeadlessInstallerResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string ProjectPath { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string SourceDirectory { get; init; } = "";
    public InstallProject? Project { get; init; }
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public IReadOnlyList<ProjectSchemaDiagnostic> Diagnostics { get; init; } = Array.Empty<ProjectSchemaDiagnostic>();

    public IReadOnlyList<ProjectSchemaDiagnostic> Errors
        => Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error).ToList();

    public IReadOnlyList<ProjectSchemaDiagnostic> Warnings
        => Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Warning).ToList();
}

public sealed class HeadlessValidationResult : HeadlessInstallerResult
{
    public IReadOnlyList<ProjectSchemaDiagnostic> LintDiagnostics { get; init; } = Array.Empty<ProjectSchemaDiagnostic>();
    public IReadOnlyList<ProjectSchemaDiagnostic> SchemaDiagnostics { get; init; } = Array.Empty<ProjectSchemaDiagnostic>();
    public IReadOnlyList<ProjectSchemaDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<ProjectSchemaDiagnostic>();
    public IReadOnlyList<ProjectSchemaDiagnostic> ExtensionDiagnostics { get; init; } = Array.Empty<ProjectSchemaDiagnostic>();
    public IReadOnlyList<string> BuildErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BuildWarnings { get; init; } = Array.Empty<string>();
}

public sealed class HeadlessPlanResult : HeadlessInstallerResult
{
    public CompiledInstallPlan? Plan { get; init; }
    public string PlanJson { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public int OperationCount { get; init; }
}

public sealed class HeadlessBuildResult : HeadlessInstallerResult
{
    public BuildPipeline.BuildResult? Build { get; init; }
    public string OutputFile { get; init; } = "";
    public string MsixPackagePath { get; init; } = "";
    public string AppInstallerPath { get; init; } = "";
    public string MsixCapabilityReportPath { get; init; } = "";
}

public static class HeadlessInstallerSdk
{
    public static HeadlessValidationResult Validate(HeadlessInstallerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (project, loadDiagnostics, projectPath) = LoadProject(request.ProjectPath);
        if (project is null)
            return ValidationResult(projectPath, null, loadDiagnostics);

        ProjectSchemaService.NormalizeInMemory(project);

        var lint = ProjectScriptLinter.LintFile(projectPath, new ProjectScriptLintOptions { Strict = request.Strict });
        var validationOptions = new ProjectSchemaValidationOptions
        {
            Strict = request.Strict,
            ForbidLiteralSecrets = request.ForbidLiteralSecrets || request.Strict
        };
        var schema = ProjectSchemaService.Validate(project, validationOptions);
        var policyEvaluation = EvaluatePolicy(request, project);
        var extensionDiagnostics = ValidateWithExtensions(request, project, validationOptions, policyEvaluation);
        var build = new BuildPipeline().Validate(project);
        var diagnostics = loadDiagnostics
            .Concat(lint.Diagnostics)
            .Concat(schema.Diagnostics)
            .Concat(policyEvaluation?.Diagnostics ?? Enumerable.Empty<ProjectSchemaDiagnostic>())
            .Concat(extensionDiagnostics)
            .Concat(ToBuildDiagnostics(build))
            .ToList();

        return ValidationResult(projectPath, project, diagnostics, lint.Diagnostics, schema.Diagnostics, policyEvaluation, extensionDiagnostics, build);
    }

    public static HeadlessPlanResult Plan(HeadlessInstallerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (project, loadDiagnostics, projectPath) = LoadProject(request.ProjectPath);
        if (project is null)
            return PlanResult(projectPath, null, loadDiagnostics);

        var policyEvaluation = EvaluatePolicy(request, project);
        var policyDiagnostics = policyEvaluation?.Diagnostics ?? Enumerable.Empty<ProjectSchemaDiagnostic>();
        var validationOptions = new ProjectSchemaValidationOptions
        {
            Strict = request.Strict,
            ForbidLiteralSecrets = request.ForbidLiteralSecrets || request.Strict
        };
        var extensionDiagnostics = ValidateWithExtensions(request, project, validationOptions, policyEvaluation);
        var gateDiagnostics = loadDiagnostics
            .Concat(policyDiagnostics)
            .Concat(extensionDiagnostics)
            .ToList();
        if (gateDiagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return PlanResult(projectPath, project, gateDiagnostics, null, policyEvaluation);

        var compile = new InstallPlanCompiler().Compile(project, policyEvaluation);
        var diagnostics = gateDiagnostics.Concat(compile.Diagnostics).ToList();
        return PlanResult(projectPath, project, diagnostics, compile.Plan, policyEvaluation);
    }

    public static HeadlessBuildResult Build(HeadlessInstallerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (project, loadDiagnostics, projectPath) = LoadProject(request.ProjectPath);
        if (project is null)
            return BuildResult(projectPath, null, loadDiagnostics);

        if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            project.OutputDir = request.OutputDirectory;

        if (request.OutputFormat.HasValue)
            project.OutputFormat = request.OutputFormat.Value;

        if (request.RequireSigned && !project.HasCodeSigningCertificate)
        {
            var signingDiagnostics = loadDiagnostics.Append(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI2603",
                "Setup.CodeSigning",
                "RequireSigned was requested, but no code-signing certificate is configured. Configure PFX signing or a Windows certificate-store selector."))
                .ToList();

            return BuildResult(projectPath, project, signingDiagnostics);
        }

        var policyEvaluation = EvaluatePolicy(request, project);
        var policyDiagnostics = policyEvaluation?.Diagnostics ?? Enumerable.Empty<ProjectSchemaDiagnostic>();
        var validationOptions = new ProjectSchemaValidationOptions
        {
            Strict = request.Strict,
            ForbidLiteralSecrets = request.ForbidLiteralSecrets || request.Strict
        };
        var extensionDiagnostics = ValidateWithExtensions(request, project, validationOptions, policyEvaluation);
        var gateDiagnostics = loadDiagnostics
            .Concat(policyDiagnostics)
            .Concat(extensionDiagnostics)
            .ToList();
        if (gateDiagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return BuildResult(projectPath, project, gateDiagnostics);

        var pipeline = new BuildPipeline
        {
            ExtensionDirectories = request.ExtensionDirectories,
            ExtensionPolicy = policyEvaluation?.Policy,
            Progress = request.Progress,
            CancellationToken = request.CancellationToken,
            ExpectedSigningSubject = NullIfWhiteSpace(request.ExpectedSigningSubject),
            TimestampOutagePolicy = NullIfWhiteSpace(request.TimestampOutagePolicy),
            TimestampRetryCount = request.TimestampRetryCount
        };

        if (request.HostBuilder is not null)
            pipeline.HostBuilder = request.HostBuilder;

        var build = pipeline.Run(project, request.CleanOutput);
        var diagnostics = gateDiagnostics.Concat(ToBuildDiagnostics(build)).ToList();
        return BuildResult(projectPath, project, diagnostics, build);
    }

    private static (InstallProject? Project, List<ProjectSchemaDiagnostic> Diagnostics, string ProjectPath) LoadProject(string projectPath)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI2601",
                "ProjectPath",
                "ProjectPath is required for headless installer operations."));
            return (null, diagnostics, "");
        }

        var fullPath = Path.GetFullPath(projectPath);
        try
        {
            var (project, error) = InstallerScriptSerializer.Load(fullPath);
            if (project is null)
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI2602",
                    fullPath,
                    error ?? $"Project script could not be loaded: {fullPath}"));
                return (null, diagnostics, fullPath);
            }

            InstallerScriptSerializer.ResolveRelativePaths(project, fullPath);
            return (project, diagnostics, fullPath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI2602",
                fullPath,
                $"Project script could not be loaded: {ex.Message}"));
            return (null, diagnostics, fullPath);
        }
    }

    private static HeadlessValidationResult ValidationResult(
        string projectPath,
        InstallProject? project,
        IReadOnlyList<ProjectSchemaDiagnostic> diagnostics,
        IReadOnlyList<ProjectSchemaDiagnostic>? lintDiagnostics = null,
        IReadOnlyList<ProjectSchemaDiagnostic>? schemaDiagnostics = null,
        InstallerPolicyEvaluation? policyEvaluation = null,
        IReadOnlyList<ProjectSchemaDiagnostic>? extensionDiagnostics = null,
        BuildPipeline.BuildResult? build = null)
    {
        var success = diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        return new HeadlessValidationResult
        {
            Success = success,
            ExitCode = ExitCode(success, diagnostics),
            ProjectPath = projectPath,
            ProjectName = project?.ProjectName ?? "",
            ProductName = project?.AppName ?? "",
            ProductVersion = project?.AppVersion ?? "",
            SourceDirectory = project?.SourceDirectory ?? "",
            Project = project,
            PolicyEvaluation = policyEvaluation,
            Diagnostics = diagnostics.ToList(),
            LintDiagnostics = lintDiagnostics?.ToList() ?? new List<ProjectSchemaDiagnostic>(),
            SchemaDiagnostics = schemaDiagnostics?.ToList() ?? new List<ProjectSchemaDiagnostic>(),
            PolicyDiagnostics = policyEvaluation?.Diagnostics.ToList() ?? new List<ProjectSchemaDiagnostic>(),
            ExtensionDiagnostics = extensionDiagnostics?.ToList() ?? new List<ProjectSchemaDiagnostic>(),
            BuildErrors = build?.Errors.ToList() ?? new List<string>(),
            BuildWarnings = build?.Warnings.ToList() ?? new List<string>()
        };
    }

    private static IReadOnlyList<ProjectSchemaDiagnostic> ValidateWithExtensions(
        HeadlessInstallerRequest request,
        InstallProject project,
        ProjectSchemaValidationOptions options,
        InstallerPolicyEvaluation? policyEvaluation)
        => InstallerExtensionProjectValidationService.Validate(project, new InstallerExtensionProjectValidationOptions
        {
            ExtensionDirectories = request.ExtensionDirectories,
            EngineVersion = request.ExtensionEngineVersion,
            RequireSignedExtensions = request.RequireSignedExtensions,
            Policy = policyEvaluation?.Policy,
            ProjectValidationOptions = options
        });

    private static HeadlessPlanResult PlanResult(
        string projectPath,
        InstallProject? project,
        IReadOnlyList<ProjectSchemaDiagnostic> diagnostics,
        CompiledInstallPlan? plan = null,
        InstallerPolicyEvaluation? policyEvaluation = null)
    {
        var success = plan is not null && diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        return new HeadlessPlanResult
        {
            Success = success,
            ExitCode = ExitCode(success, diagnostics),
            ProjectPath = projectPath,
            ProjectName = project?.ProjectName ?? "",
            ProductName = project?.AppName ?? "",
            ProductVersion = project?.AppVersion ?? "",
            SourceDirectory = project?.SourceDirectory ?? "",
            Project = project,
            PolicyEvaluation = policyEvaluation,
            Diagnostics = diagnostics.ToList(),
            Plan = plan,
            PlanJson = plan is null ? "" : InstallPlanCompiler.ToJson(plan),
            PlanHash = plan?.PlanHash ?? "",
            OperationCount = plan?.Operations.Count ?? 0
        };
    }

    private static HeadlessBuildResult BuildResult(
        string projectPath,
        InstallProject? project,
        IReadOnlyList<ProjectSchemaDiagnostic> diagnostics,
        BuildPipeline.BuildResult? build = null)
    {
        var success = build?.Success == true && diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        return new HeadlessBuildResult
        {
            Success = success,
            ExitCode = ExitCode(success, diagnostics),
            ProjectPath = projectPath,
            ProjectName = project?.ProjectName ?? "",
            ProductName = project?.AppName ?? "",
            ProductVersion = project?.AppVersion ?? "",
            SourceDirectory = project?.SourceDirectory ?? "",
            Project = project,
            Diagnostics = diagnostics.ToList(),
            Build = build,
            OutputFile = build?.OutputFile ?? "",
            MsixPackagePath = build?.MsixPackagePath ?? "",
            AppInstallerPath = build?.AppInstallerPath ?? "",
            MsixCapabilityReportPath = build?.MsixCapabilityReportPath ?? ""
        };
    }

    private static IEnumerable<ProjectSchemaDiagnostic> ToBuildDiagnostics(BuildPipeline.BuildResult build)
    {
        foreach (var error in build.Errors)
            yield return new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2604", "Build", error);

        foreach (var warning in build.Warnings)
            yield return new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Warning, "BI2605", "Build", warning);
    }

    private static InstallerPolicyEvaluation? EvaluatePolicy(HeadlessInstallerRequest request, InstallProject project)
        => request.PolicyEvaluator?.Invoke(project);

    private static int ExitCode(bool success, IReadOnlyList<ProjectSchemaDiagnostic> diagnostics)
    {
        if (success)
            return 0;

        return diagnostics.Any(d => d.Code is "BI2601" or "BI2602") ? 2 : 1;
    }

    private static string? NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
