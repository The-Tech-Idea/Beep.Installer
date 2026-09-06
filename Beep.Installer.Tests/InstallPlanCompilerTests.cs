using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallPlanCompilerTests
{
    [Fact]
    public void Compile_ExtensionResourcesRoundTripAndHashDeterministically()
    {
        var project = CreateProject();
        project.Resources.Add(new CompiledInstallOperation
        {
            Id = "extension:first", Type = "sample.resource",
            Inputs = new() { ["source"] = "payload;with-quotes\".txt", ["destination"] = "{InstallPath}/file.txt" }
        });
        project.Resources.Add(new CompiledInstallOperation
        {
            Id = "extension:second", Type = "sample.resource", DependsOn = new() { "extension:first" },
            Inputs = new() { ["credential"] = "secret://env/INSTALL_TOKEN" }, SensitiveInputs = new() { "credential" }
        });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bsetup");
        try
        {
            InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();
            ProjectScriptLinter.LintFile(path, new() { Strict = true }).Diagnostics.Should().NotContain(d => d.Code == "BI0102");
            var loaded = InstallerScriptSerializer.Load(path).project!;
            loaded.Resources.Should().HaveCount(2);
            loaded.Resources[0].Inputs["source"].Should().Be("payload;with-quotes\".txt");
            var first = new InstallPlanCompiler().Compile(loaded);
            first.Success.Should().BeTrue();
            var item = loaded.Resources[0];
            loaded.Resources.RemoveAt(0);
            loaded.Resources.Add(item);
            var second = new InstallPlanCompiler().Compile(loaded);
            second.Plan!.PlanHash.Should().Be(first.Plan!.PlanHash);
            second.Plan.Operations.Should().Contain(o => o.Id == "extension:second" && o.DependsOn.Contains("extension:first"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("duplicate", "BI2003")]
    [InlineData("missing", "BI2001")]
    [InlineData("secret", "BI1703")]
    [InlineData("builtin", "BI1701")]
    public void Compile_RejectsInvalidExtensionResources(string scenario, string diagnostic)
    {
        var project = CreateProject();
        project.Resources.Add(new CompiledInstallOperation
        {
            Id = "extension:test", Type = scenario == "builtin" ? "file.copy" : "sample.resource",
            DependsOn = scenario == "missing" ? new() { "not-present" } : new(),
            Inputs = new() { ["credential"] = "literal-password" },
            SensitiveInputs = scenario == "secret" ? new() { "credential" } : new()
        });
        if (scenario == "duplicate") project.Resources.Add(project.Resources[0]);
        var result = new InstallPlanCompiler().Compile(project);
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == diagnostic);
    }

    [Fact]
    public void Compile_ProducesStableHashForEquivalentProjects()
    {
        var first = CreateProject();
        var second = CreateProject();
        second.AppId = first.AppId;

        var firstPlan = new InstallPlanCompiler().Compile(first).Plan;
        var secondPlan = new InstallPlanCompiler().Compile(second).Plan;

        firstPlan.Should().NotBeNull();
        secondPlan.Should().NotBeNull();
        firstPlan!.PlanHash.Should().Be(secondPlan!.PlanHash);
        firstPlan.Operations.Should().Contain(o => o.Type == "file.copy");
    }

    [Fact]
    public void Compile_RedactsSensitiveCommandArguments()
    {
        var project = CreateProject();
        project.CustomActions.Add(new CustomAction
        {
            Path = "post-install.exe",
            Arguments = "--token=super-secret",
            Timing = CustomActionTiming.AfterInstall
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan;

        var action = plan!.Operations.Single(o => o.Type == "custom-action.run");
        action.Inputs["arguments"].Should().Be("<redacted>");
    }

    [Fact]
    public void Compile_FailsComponentDependencyCycle()
    {
        var project = CreateProject();
        project.Components.Clear();
        project.Components.Add(new InstallComponent { Id = "a", Name = "A", DependsOn = new List<string> { "b" } });
        project.Components.Add(new InstallComponent { Id = "b", Name = "B", DependsOn = new List<string> { "a" } });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI2002");
    }

    [Fact]
    public void Compile_EmitsMachineReadableComponentConditionInputs()
    {
        var project = CreateProject();
        project.Components[0].ConditionExpression = ConditionExpressionMode.Any;
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.FileExists,
            Value = @"{InstallPath}\feature.flag",
            Operator = "=="
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan;

        var component = plan!.Operations.Single(o => o.Type == "component.select");
        component.Condition.Should().Contain("FileExists");
        component.Inputs["conditionExpression"].Should().Be("any");
        component.Inputs["conditionCount"].Should().Be("1");
        component.Inputs["condition.0.type"].Should().Be(nameof(ConditionType.FileExists));
        component.Inputs["condition.0.value"].Should().Be(@"{InstallPath}\feature.flag");
        component.Inputs["condition.0.operator"].Should().Be("==");
    }

    [Fact]
    public void Compile_PromotesPrerequisitesToPackageOperationsAndComponentDependencies()
    {
        var project = CreateProject();
        project.Prerequisites.Add(new Prerequisite
        {
            Id = "dotnet-desktop",
            Name = ".NET Desktop Runtime",
            VersionRequired = "10.0.0",
            DetectionCommand = "dotnet --list-runtimes",
            DetectionPattern = "Microsoft.WindowsDesktop.App 10.",
            DownloadUrl = "https://example.test/windowsdesktop-runtime.exe",
            SilentInstallArgs = "/install /quiet /norestart",
            IsMandatory = true
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan!;

        var package = plan.Operations.Single(o => o.Type == "package.install");
        package.Id.Should().Be("package:dotnet-desktop");
        package.RollbackSupported.Should().BeFalse();
        package.RebootBehavior.Should().Be("possible");
        package.Inputs["packageType"].Should().Be("exe");
        package.Inputs["detectionCommand"].Should().Be("dotnet --list-runtimes");
        package.Inputs["detectionPattern"].Should().Be("Microsoft.WindowsDesktop.App 10.");
        package.Inputs["installArgs"].Should().Be("/install /quiet /norestart");
        package.Inputs["successExitCodes"].Should().Be("0,3010,1641");

        var component = plan.Operations.Single(o => o.Type == "component.select");
        component.DependsOn.Should().Contain("package:dotnet-desktop");
    }

    [Fact]
    public void Compile_EmitsExplicitPackageNodesWithPackageDependencies()
    {
        var project = CreateProject();
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "dotnet-hosting",
            Name = ".NET Hosting Bundle",
            PackageType = PackageNodeType.Msi,
            DownloadUrl = "https://example.test/dotnet-hosting.msi",
            InstallArgs = "/qn /norestart",
            IsMandatory = true
        });
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "vc-redist",
            Name = "VC++ Runtime",
            PackageType = PackageNodeType.Exe,
            SourcePath = @"redist\vc_redist.x64.exe",
            Sha256 = new string('b', 64),
            DetectionCommand = "detect-vc",
            InstallArgs = "/quiet",
            UninstallCommand = "uninstall-vc",
            UninstallArgs = "/quiet",
            DependsOn = new() { "dotnet-hosting" },
            RemoveOnUninstall = true,
            SuccessExitCodes = "0,3010",
            RebootExitCodes = "3010",
            TimeoutSeconds = 900,
            RetryCount = 2
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan!;

        var hosting = plan.Operations.Single(o => o.Id == "package:dotnet-hosting");
        hosting.Inputs["packageType"].Should().Be("msi");
        hosting.Inputs["downloadUrl"].Should().Be("https://example.test/dotnet-hosting.msi");

        var redist = plan.Operations.Single(o => o.Id == "package:vc-redist");
        redist.Type.Should().Be("package.install");
        redist.RollbackSupported.Should().BeTrue();
        redist.DependsOn.Should().Equal("package:dotnet-hosting");
        redist.Inputs["sourcePath"].Should().Be(@"redist\vc_redist.x64.exe");
        redist.Inputs["sha256"].Should().Be(new string('b', 64));
        redist.Inputs["uninstallCommand"].Should().Be("uninstall-vc");
        redist.Inputs["timeoutSeconds"].Should().Be("900");

        plan.Operations.Single(o => o.Type == "component.select")
            .DependsOn.Should().Contain(new[] { "package:dotnet-hosting", "package:vc-redist" });
    }

    [Fact]
    public void Compile_IncludesPolicyDecisionEvidenceWhenPolicyIsEvaluated()
    {
        var project = CreateProject();
        project.CodeSignCertificatePath = "certs/release.pfx";
        var policy = new InstallerPolicy
        {
            RequireSignedInstaller = true,
            RequireSupplyChainScan = true,
            AllowedPublishers = { "ACME" },
            TrustedPolicyIssuerSubjects = { "CN=The Tech Idea Policy Authority" },
            PolicyIssuer = "CN=The Tech Idea Policy Authority"
        };
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(policy, project);
        evaluation.Sources.Add(new InstallerPolicySource
        {
            Kind = "machine",
            Name = "machine-policy.json",
            Sha256 = new string('a', 64)
        });

        var plan = new InstallPlanCompiler().Compile(project, evaluation).Plan!;
        var json = InstallPlanCompiler.ToJson(plan);

        plan.Policy.Should().NotBeNull();
        plan.Policy!.Status.Should().Be("passed");
        plan.Policy.RequiredControls.Should().Contain(new[] { "signed-installer", "supply-chain-scan" });
        plan.Policy.Sources.Should().ContainSingle(s => s.Kind == "machine" && s.Name == "machine-policy.json");
        json.Should().Contain("\"Policy\"");
        json.Should().Contain("\"EffectivePolicySha256\"");
        json.Should().Contain("\"machine-policy.json\"");
        json.Should().NotContain("TrustedPolicySigningPublicKeys");
    }

    private static InstallProject CreateProject()
    {
        var project = InstallerProjectFactory.CreateNew("PlanApp", "1.2.3", "ACME", @"C:\src");
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files = new List<FileCopyOperation>
            {
                new() { SourcePath = @"bin\app.exe", DestinationPath = "app.exe", Description = "Application" }
            }
        });
        project.RegistryEntries.Add(new RegistryOperation
        {
            KeyPath = @"Software\ACME\PlanApp",
            ValueName = "InstallPath",
            Value = "%InstallPath%"
        });
        return project;
    }
}
