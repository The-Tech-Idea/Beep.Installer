using Beep.Installer.Engine;
using Beep.Installer.Engine.Msi;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AdditionalPackageTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepPackageTarget_" + Guid.NewGuid().ToString("N"));
    private InstallProject Project(InstallerOutputFormat format)
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.exe"), "payload");
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", source);
        project.OutputFormat = format;
        project.OutputDir = Path.Combine(_root, "output");
        return project;
    }

    [Theory]
    [InlineData(InstallerOutputFormat.Msi, "msi")]
    [InlineData(InstallerOutputFormat.ClickOnce, "clickOnce")]
    public void NewTargetsSurviveScriptAndCanonicalRoundTrips(InstallerOutputFormat format, string canonicalName)
    {
        var project = Project(format);
        var path = Path.Combine(_root, "project.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();
        var loaded = InstallerScriptSerializer.Load(path);
        loaded.error.Should().BeNull();
        loaded.project!.OutputFormat.Should().Be(format);
        using var canonical = System.Text.Json.JsonDocument.Parse(ProjectCanonicalJsonExporter.ToJson(loaded.project));
        canonical.RootElement.GetProperty("outputFormat").GetString().Should().Be(canonicalName);
        new InstallPlanCompiler().Compile(loaded.project).Plan!.OutputFormat.Should().Be(format.ToString().ToLowerInvariant());
    }

    [Fact]
    public void MsiSelectionInvokesTheWixServiceAndReturnsItsArtifact()
    {
        var project = Project(InstallerOutputFormat.Msi);
        var invoked = false;
        var pipeline = new BuildPipeline
        {
            MsiToolRunner = invocation =>
            {
                invoked = true;
                var outputIndex = invocation.Arguments.ToList().IndexOf("-o");
                File.WriteAllText(invocation.Arguments[outputIndex + 1], "msi artifact");
                return new MsiToolResult { ExitCode = 0, ToolVersion = "test service", StandardOutput = "package built" };
            }
        };
        var result = pipeline.Run(project);
        invoked.Should().BeTrue();
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.ArtifactKind.Should().Be("msi");
        result.OutputFile.Should().EndWith(".msi");
        File.Exists(result.OutputFile).Should().BeTrue();
        result.Steps.Should().Contain("package built");
    }

    [Fact]
    public void MsiServiceFailureIsNotReportedAsAProducedArtifact()
    {
        var project = Project(InstallerOutputFormat.Msi);
        var result = new BuildPipeline { MsiToolRunner = _ => new MsiToolResult { ExitCode = 1, StandardError = "tool failed" } }.Run(project);
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Steps.Should().Contain("tool failed");
    }

    [Fact]
    public void ClickOnceSelectionProducesARealDeploymentManifest()
    {
        var result = new BuildPipeline().Run(Project(InstallerOutputFormat.ClickOnce));
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.ArtifactKind.Should().Be("clickonce");
        result.OutputFile.Should().EndWith(".application");
        File.Exists(result.OutputFile).Should().BeTrue();
        result.Warnings.Should().NotBeEmpty("an unsigned development publish must remain visible");
    }

    [Fact]
    public void ClickOnceRefusesAuthoredSystemChangesBeforePublishing()
    {
        var project = Project(InstallerOutputFormat.ClickOnce);
        project.RegistryEntries.Add(new TheTechIdea.Beep.Installer.RegistryOperation { KeyPath = "Software\\Contoso" });
        PackageTargetDiagnostics.Validate(project).Should().Contain(d => d.Code == "BIUI003");
        var result = new BuildPipeline().Run(project);
        result.Success.Should().BeFalse();
        Directory.Exists(project.OutputDir).Should().BeFalse();
    }

    [Theory]
    [InlineData(InstallerOutputFormat.Msi)]
    [InlineData(InstallerOutputFormat.ClickOnce)]
    public void UnsupportedCleanOutputIsReportedWithoutChangingTheDestination(InstallerOutputFormat format)
    {
        var project = Project(format);
        Directory.CreateDirectory(project.OutputDir);
        var existing = Path.Combine(project.OutputDir, "existing.txt");
        File.WriteAllText(existing, "preserve");
        var result = new BuildPipeline().Run(project, cleanOutput: true);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Clean output"));
        File.ReadAllText(existing).Should().Be("preserve");
        Directory.GetFiles(project.OutputDir).Should().Equal(existing);
    }

    [Fact]
    public void MsiDeploymentManifestDoesNotReuseExeCommandSwitches()
    {
        var project = Project(InstallerOutputFormat.Msi);
        var result = Beep.Installer.Deployment.WinGetManifestExporter.Generate(project,
            new Beep.Installer.Deployment.WinGetManifestOptions
            {
                OutputDirectory = project.OutputDir, InstallerPath = Path.Combine(project.SourceDirectory, "app.exe"),
                InstallerUrl = "https://example.test/app.msi"
            });
        var manifest = File.ReadAllText(Directory.GetFiles(result.ManifestDirectory, "*.installer.yaml").Single());
        manifest.Should().Contain("InstallerType: msi").And.NotContain("InstallerSwitches:");
    }

    [Fact]
    public void ClickOnceDeploymentCannotBeMisrepresentedAsAnExeWinGetManifest()
    {
        var project = Project(InstallerOutputFormat.ClickOnce);
        var action = () => Beep.Installer.Deployment.WinGetManifestExporter.Generate(project,
            new Beep.Installer.Deployment.WinGetManifestOptions { OutputDirectory = project.OutputDir });
        action.Should().Throw<NotSupportedException>().WithMessage("*ClickOnce*");
        Directory.Exists(project.OutputDir).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
