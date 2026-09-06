using System.Text.Json;
using Beep.Installer.Engine.Msi;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class MsiLifecycleMatrixRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public MsiLifecycleMatrixRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepMsiMatrix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_DryRunLocalTarget_ProducesMsiMstMspActionPlanAndReport()
    {
        var msi = Path.Combine(_tempDir, "app.msi");
        var mst = Path.Combine(_tempDir, "enterprise.mst");
        var msp = Path.Combine(_tempDir, "hotfix.msp");
        var logDir = Path.Combine(_tempDir, "logs");
        File.WriteAllText(msi, "msi");
        File.WriteAllText(mst, "mst");
        File.WriteAllText(msp, "msp");

        var options = MsiLifecycleMatrixRunner.Parse(new[]
        {
            "qualify",
            "--environment", "local-win11",
            "--os", "Windows 11 24H2",
            "--arch", "x64",
            "--channel", "local",
            "--plan-hash", "abc123",
            "--log-dir", logDir,
            "--msi", msi,
            "--mst", mst,
            "--msp", msp,
            "--msp-product", msi,
            "--msiexec", @"C:\Windows\System32\msiexec.exe",
            "--property", "INSTALLLEVEL=100",
            "--property", "TENANT=acme",
            "--dry-run"
        });

        var report = MsiLifecycleMatrixRunner.Run(options);
        MsiLifecycleMatrixRunner.WriteReport(report);

        report.Success.Should().BeTrue();
        report.DryRun.Should().BeTrue();
        report.EnvironmentId.Should().Be("local-win11");
        report.PlanHash.Should().Be("abc123");
        report.Actions.Select(a => a.Name).Should().Equal("mst-install", "mst-uninstall", "msp-apply", "msp-remove");
        report.Scenarios.Select(s => s.Id).Should().Equal("mst-lifecycle", "msp-lifecycle");
        report.Actions.Should().OnlyContain(a =>
            a.Success
            && !string.IsNullOrWhiteSpace(a.ScenarioId)
            && a.ToolVersion == "dry-run"
            && a.CommandLine.Contains("/qn", StringComparison.Ordinal)
            && a.CommandLine.Contains("/norestart", StringComparison.Ordinal)
            && a.CommandLine.Contains("INSTALLLEVEL=100", StringComparison.Ordinal)
            && a.CommandLine.Contains("TENANT=acme", StringComparison.Ordinal));
        report.Actions[0].CommandLine.Should().Contain("TRANSFORMS=");

        var reportPath = Path.Combine(logDir, MsiLifecycleMatrixRunner.ReportFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
        document.RootElement.GetProperty("environmentId").GetString().Should().Be("local-win11");
        document.RootElement.GetProperty("scenarios").GetArrayLength().Should().Be(2);
        document.RootElement.GetProperty("actions").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public void Run_ScenarioPack_SelectsNamedUpgradeDriverAndPatchScenarios()
    {
        var msi = Path.Combine(_tempDir, "app.msi");
        var mst = Path.Combine(_tempDir, "enterprise.mst");
        var msp = Path.Combine(_tempDir, "hotfix.msp");
        var scenarioPack = Path.Combine(_tempDir, "matrix-scenarios.json");
        File.WriteAllText(msi, "msi");
        File.WriteAllText(mst, "mst");
        File.WriteAllText(msp, "msp");
        File.WriteAllText(scenarioPack, """
            {
              "version": 1,
              "scenarios": [
                {
                  "id": "upgrade-downgrade-smoke",
                  "category": "upgrade",
                  "description": "Exercise install/repair/uninstall as the base maintenance contract for upgrade policy rows.",
                  "requiredArtifacts": ["msi"],
                  "actions": ["msi-install", "msi-repair", "msi-uninstall"],
                  "properties": ["UPGRADE_POLICY=strict"]
                },
                {
                  "id": "pnp-driver-store-smoke",
                  "category": "driver",
                  "description": "Exercise MSI lifecycle for packages carrying PnP driver-store custom actions.",
                  "requiredArtifacts": ["msi"],
                  "actions": ["msi-install", "msi-uninstall"],
                  "properties": ["DRIVER_POLICY=require-signed"]
                },
                {
                  "id": "msp-supersedence-smoke",
                  "category": "patch",
                  "description": "Exercise MSP apply/remove as the base supersedence and removal contract.",
                  "requiredArtifacts": ["msp", "msp-product"],
                  "actions": ["msp-apply", "msp-remove"],
                  "properties": ["PATCH_POLICY=supersede"]
                }
              ]
            }
            """);

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "local-win11",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "local",
            LogDirectory = Path.Combine(_tempDir, "scenario-logs"),
            MsiPath = msi,
            TransformPath = mst,
            PatchPath = msp,
            PatchProductPath = msi,
            ScenarioPackPath = scenarioPack,
            ScenarioIds = new[] { "upgrade-downgrade-smoke", "pnp-driver-store-smoke", "msp-supersedence-smoke" },
            DryRun = true,
            Properties = new[] { "TENANT=acme" }
        });

        report.Success.Should().BeTrue();
        report.ScenarioPackPath.Should().Be(Path.GetFullPath(scenarioPack));
        report.Scenarios.Select(s => s.Id).Should().Equal("upgrade-downgrade-smoke", "pnp-driver-store-smoke", "msp-supersedence-smoke");
        report.Actions.Select(a => a.ScenarioCategory).Should().Contain(new[] { "upgrade", "driver", "patch" });
        report.Actions.Should().Contain(a => a.ScenarioId == "upgrade-downgrade-smoke" && a.CommandLine.Contains("UPGRADE_POLICY=strict", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.ScenarioId == "pnp-driver-store-smoke" && a.CommandLine.Contains("DRIVER_POLICY=require-signed", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.ScenarioId == "msp-supersedence-smoke" && a.CommandLine.Contains("PATCH_POLICY=supersede", StringComparison.Ordinal));
        report.Actions.Should().OnlyContain(a => a.CommandLine.Contains("TENANT=acme", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_EnterpriseDefaultScenarioPackAlias_LoadsCheckedInScenarioPack()
    {
        var msi = Path.Combine(_tempDir, "app.msi");
        var updatedMsi = Path.Combine(_tempDir, "app-updated.msi");
        var mst = Path.Combine(_tempDir, "enterprise.mst");
        var msp = Path.Combine(_tempDir, "hotfix.msp");
        File.WriteAllText(msi, "msi");
        File.WriteAllText(updatedMsi, "updated-msi");
        File.WriteAllText(mst, "mst");
        File.WriteAllText(msp, "msp");

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "local-win11",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "local",
            LogDirectory = Path.Combine(_tempDir, "enterprise-default-logs"),
            MsiPath = msi,
            UpdatedMsiPath = updatedMsi,
            TransformPath = mst,
            PatchPath = msp,
            PatchProductPath = msi,
            ScenarioPackPath = MsiLifecycleMatrixRunner.EnterpriseDefaultScenarioPack,
            ScenarioIds = new[] { "upgrade-downgrade-policy", "pnp-driver-store-policy", "msp-supersedence-removal" },
            DryRun = true
        });

        report.Success.Should().BeTrue();
        report.ScenarioPackPath.Should().EndWith(Path.Combine("ScenarioPacks", "enterprise-default.matrix.json"));
        report.Scenarios.Select(s => s.Id).Should().Equal("upgrade-downgrade-policy", "pnp-driver-store-policy", "msp-supersedence-removal");
        report.Actions.Select(a => a.Name).Should().ContainInOrder("msi-install", "msi-upgrade", "msi-downgrade-blocked", "msi-uninstall");
        report.Actions.Should().Contain(a => a.ScenarioCategory == "upgrade" && a.CommandLine.Contains("UPGRADE_POLICY=strict", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.Name == "msi-upgrade" && a.CommandLine.Contains(updatedMsi, StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.Name == "msi-downgrade-blocked" && a.StandardOutput == "dry-run expected-failure");
        report.Actions.Should().Contain(a => a.ScenarioCategory == "driver" && a.CommandLine.Contains("DRIVER_POLICY=require-signed", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.ScenarioCategory == "patch" && a.CommandLine.Contains("PATCH_POLICY=supersede", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_DriverDefaultScenarioPackAlias_CapturesDriverStoreSnapshots()
    {
        var msi = Path.Combine(_tempDir, "driver-app.msi");
        File.WriteAllText(msi, "msi");

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "local-driver",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "local",
            LogDirectory = Path.Combine(_tempDir, "driver-default-logs"),
            MsiPath = msi,
            ScenarioPackPath = MsiLifecycleMatrixRunner.DriverDefaultScenarioPack,
            ScenarioIds = new[] { "pnp-driver-store-policy" },
            DryRun = true
        });

        report.Success.Should().BeTrue();
        report.Actions.Select(a => a.Name).Should().Equal("driver-store-before", "msi-install", "driver-store-after-install", "msi-uninstall", "driver-store-after-uninstall");
        report.Actions.Should().Contain(a => a.Name == "driver-store-before" && a.CommandLine.Contains("pnputil", StringComparison.OrdinalIgnoreCase) && a.CommandLine.Contains("/enum-drivers", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.Name == "msi-install" && a.CommandLine.Contains("DRIVER_POLICY=require-signed", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_PatchDefaultScenarioPackAlias_CapturesWindowsInstallerPatchInventory()
    {
        var msi = Path.Combine(_tempDir, "patch-product.msi");
        var msp = Path.Combine(_tempDir, "hotfix.msp");
        File.WriteAllText(msi, "msi");
        File.WriteAllText(msp, "msp");

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "local-patch",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "local",
            LogDirectory = Path.Combine(_tempDir, "patch-default-logs"),
            MsiPath = msi,
            PatchPath = msp,
            PatchProductPath = msi,
            ScenarioPackPath = MsiLifecycleMatrixRunner.PatchDefaultScenarioPack,
            ScenarioIds = new[] { "msp-supersedence-removal" },
            DryRun = true
        });

        report.Success.Should().BeTrue();
        report.Actions.Select(a => a.Name).Should().Equal("msp-inventory-before", "msp-apply", "msp-inventory-after-apply", "msp-remove", "msp-inventory-after-remove");
        report.Actions.Should().Contain(a => a.Name == "msp-inventory-before" && a.CommandLine.Contains("WindowsInstaller.Installer", StringComparison.Ordinal));
        report.Actions.Should().Contain(a => a.Name == "msp-apply" && a.CommandLine.Contains("PATCH_POLICY=supersede", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_HyperVChannel_RoutesScenarioActionsThroughPowerShellDirectAdapter()
    {
        var msi = Path.Combine(_tempDir, "app.msi");
        File.WriteAllText(msi, "msi");
        var invocations = new List<MsiToolInvocation>();

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "win11-hyperv",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "hyperv",
            LogDirectory = Path.Combine(_tempDir, "hyperv-logs"),
            MsiPath = msi,
            ScenarioIds = new[] { "msi-lifecycle" },
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                return new MsiToolResult { ExitCode = 0, ToolVersion = "powershell 7.0" };
            }
        });

        report.Success.Should().BeTrue();
        invocations.Should().HaveCount(3);
        invocations.Should().OnlyContain(i =>
            i.ToolPath == "powershell"
            && i.Arguments.Contains("-Command")
            && i.Arguments.Contains("win11-hyperv")
            && i.CommandLine.Contains("Invoke-Command", StringComparison.Ordinal)
            && i.CommandLine.Contains("Start-Process", StringComparison.Ordinal));
        report.Actions.Should().OnlyContain(a => a.CommandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_AzureChannel_RoutesScenarioActionsThroughAzureRunCommandAdapter()
    {
        var msi = Path.Combine(_tempDir, "app.msi");
        File.WriteAllText(msi, "msi");
        var invocations = new List<MsiToolInvocation>();

        var report = MsiLifecycleMatrixRunner.Run(new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = "rg-installers/win11-azure",
            OperatingSystem = "Windows 11 24H2",
            Architecture = "x64",
            Channel = "azure",
            LogDirectory = Path.Combine(_tempDir, "azure-logs"),
            MsiPath = msi,
            ScenarioIds = new[] { "msi-lifecycle" },
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                return new MsiToolResult { ExitCode = 0, ToolVersion = "az 2.0" };
            }
        });

        report.Success.Should().BeTrue();
        invocations.Should().HaveCount(3);
        invocations.Should().OnlyContain(i =>
            i.ToolPath == "az"
            && i.Arguments.Contains("run-command")
            && i.Arguments.Contains("--resource-group")
            && i.Arguments.Contains("rg-installers")
            && i.Arguments.Contains("win11-azure")
            && i.CommandLine.Contains("RunPowerShellScript", StringComparison.Ordinal)
            && i.CommandLine.Contains("Start-Process", StringComparison.Ordinal));
        report.Actions.Should().OnlyContain(a => a.ToolVersion == "az 2.0");
    }
}
