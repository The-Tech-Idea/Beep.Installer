using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class MsiPolicyCliTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceRoot;
    private readonly string _scriptPath;
    private readonly string _policyPath;

    public MsiPolicyCliTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepMsiPolicyCli_{Guid.NewGuid():N}");
        _sourceRoot = Path.Combine(_root, "src");
        Directory.CreateDirectory(_sourceRoot);
        File.WriteAllText(Path.Combine(_sourceRoot, "app.exe"), "app");

        var project = InstallerProjectFactory.CreateNew("Msi Policy App", "1.0.0", "ACME", _sourceRoot);
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = Path.Combine(_sourceRoot, "app.exe"),
                    DestinationPath = "app.exe"
                }
            }
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Nested INI",
            Format = ConfigTransformFormat.Ini,
            Operation = ConfigTransformOperation.Set,
            TargetPath = @"{InstallPath}\config\app.ini",
            Section = "app",
            KeyPath = "Mode",
            Value = "Enterprise"
        });

        _scriptPath = Path.Combine(_root, "policy-msi.bsetup");
        InstallerScriptSerializer.Save(project, _scriptPath);

        _policyPath = Path.Combine(_root, "warning.policy.json");
        File.WriteAllText(_policyPath, JsonSerializer.Serialize(new InstallerPolicy
        {
            UnsupportedMsiOperationSeverity = "warning"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void MsiExport_UsesPolicySeverityForUnsupportedOperations()
    {
        var output = Path.Combine(_root, "msi");

        var result = InstallerCliRunner.Run($"/MSI=\"{_scriptPath}\" /POLICY=\"{_policyPath}\" /OUT=\"{output}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardOutput.Should().Contain("WARNING BI1601");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "msi-capabilities.json")));
        report.RootElement.GetProperty("findings").EnumerateArray().Should().Contain(f =>
            f.GetProperty("Code").GetString() == "BI1601"
            && f.GetProperty("Severity").GetString() == "warning"
            && f.GetProperty("OperationType").GetString() == "config.transform");
    }
}
