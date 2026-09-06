using System.Text.Json.Serialization;
using Beep.Installer.Policy;

namespace Beep.Installer.Engine;

public sealed class CompiledInstallPlan
{
    public string SchemaVersion { get; init; } = "";
    public string PlanVersion { get; init; } = "1.0";
    public string ProductName { get; init; } = "";
    public string AppId { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallScope { get; init; } = "";
    public string OutputFormat { get; init; } = "";
    public string PlanHash { get; set; } = "";
    public InstallerPolicyDecisionEvidence? Policy { get; init; }
    public List<CompiledInstallOperation> Operations { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class CompiledInstallOperation
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public List<string> DependsOn { get; init; } = new();
    public SortedDictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
    public List<string> SensitiveInputs { get; init; } = new();
    public string Condition { get; init; } = "";
    public bool RollbackSupported { get; init; } = true;
    public string RebootBehavior { get; init; } = "none";
}

public sealed class PlanCompileResult
{
    public CompiledInstallPlan? Plan { get; init; }
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Plan != null && Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

[JsonSerializable(typeof(CompiledInstallPlan))]
[JsonSerializable(typeof(CompiledInstallOperation))]
[JsonSerializable(typeof(ProjectSchemaDiagnostic))]
[JsonSerializable(typeof(InstallerPolicyDecisionEvidence))]
[JsonSerializable(typeof(InstallerPolicySource))]
internal sealed partial class CompiledInstallPlanJsonContext : JsonSerializerContext
{
}
