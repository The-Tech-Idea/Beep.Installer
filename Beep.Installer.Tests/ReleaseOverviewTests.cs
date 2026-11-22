using Beep.Installer.Engine;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ReleaseOverviewTests
{
    [Fact]
    public void WithoutABuildConfiguredSigningDoesNotBecomeSigningOrDeploymentEvidence()
    {
        var snapshot = ReleaseOverview.Create("App", "1.0.0", "Msi", true, true);
        snapshot.BuildState.Should().Be(ReleaseBuildState.NoBuild); snapshot.SigningConfigured.Should().BeTrue();
        snapshot.Signing.Should().BeEmpty(); snapshot.PackageFiles.Should().BeEmpty();
        snapshot.DeploymentSucceeded.Should().BeNull(); snapshot.QualificationAssociatedWithBuild.Should().BeFalse();
    }

    [Fact]
    public void RunningJobsAreShownAsBuildingBeforeAResultExists()
    {
        ReleaseOverview.Create("App", "1.0.0", "Exe", true, false, buildRunning: true).BuildState.Should().Be(ReleaseBuildState.Building);
    }

    [Fact]
    public void MissingOutputAndSelectedFilesRemainMissingAndNoFilesAreGenerated()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReleaseOverview_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "app.msix"); var manifest = Path.Combine(root, "deployment-kit.json");
        var result = new BuildPipeline.BuildResult { Success = true, ArtifactKind = "msix", OutputFile = output, MsixPackagePath = output };
        var snapshot = ReleaseOverview.Create("App", "1.0.0", "Msix", true, false, result, new[] { manifest, manifest });
        snapshot.PackageFiles.Should().ContainSingle().Which.Exists.Should().BeFalse();
        snapshot.SelectedDeploymentFiles.Should().ContainSingle().Which.Exists.Should().BeFalse();
        Directory.Exists(root).Should().BeFalse(); snapshot.DeploymentSucceeded.Should().BeNull();
    }

    [Fact]
    public void EvidenceIsDetachedAndOmitsCredentialAndErrorMetadata()
    {
        var result = new BuildPipeline.BuildResult { Success = true, OutputFile = "app.exe" };
        result.SigningEvidence.Add(new() { ArtifactKind = "exe", ArtifactPath = "app.exe", Success = true,
            CertificateSubject = "CN=Publisher", Error = "secret-token-value", RemoteEndpoint = "https://host/?token=secret", CertificatePath = "private.pfx" });
        var snapshot = ReleaseOverview.Create("App", "1.0.0", "Exe", false, true, result);
        result.OutputFile = "other.exe"; result.SigningEvidence.Clear(); result.Success = false;
        snapshot.IsCurrentProject.Should().BeFalse(); snapshot.BuildState.Should().Be(ReleaseBuildState.Completed);
        snapshot.PackageFiles.Single().Path.Should().Be("app.exe"); snapshot.Signing.Should().ContainSingle();
        System.Text.Json.JsonSerializer.Serialize(snapshot).Should().NotContain("secret-token-value").And.NotContain("private.pfx").And.NotContain("token=secret");
    }

    [Fact]
    public void PortfolioSummaryCannotOverrideFailedOrMissingEvidenceOrAssociateItWithABuild()
    {
        var report = new ReleaseQualificationPortfolioReport { Success = true, Evidence = new()
        {
            new() { QualificationId = "passed", Found = true, Success = true, ReportPath = "missing-report.json" },
            new() { QualificationId = "failed", Found = true, Success = false },
            new() { QualificationId = "missing", Found = false, Success = true },
            new() { QualificationId = "errors", Found = true, Success = true, Diagnostics = new() { new(ProjectSchemaDiagnosticSeverity.Error, "test", "Evidence", "Qualification failure") } }
        } };
        var snapshot = ReleaseOverview.Create("App", "1.0.0", "Exe", true, false, portfolio: report);
        snapshot.Qualifications.Select(item => item.State).Should().Equal(ReleaseQualificationState.ReportedPassed,
            ReleaseQualificationState.ReportedFailed, ReleaseQualificationState.Missing, ReleaseQualificationState.ReportedFailed);
        snapshot.Qualifications[0].ReportExists.Should().BeFalse();
        snapshot.QualificationAssociatedWithBuild.Should().BeFalse(); snapshot.DeploymentSucceeded.Should().BeNull();
        report.Evidence.Clear(); snapshot.Qualifications.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(true, true, false, ReleaseBuildState.Cancelled)]
    [InlineData(true, false, true, ReleaseBuildState.Failed)]
    [InlineData(false, false, false, ReleaseBuildState.Failed)]
    public void CancellationAndErrorsCannotAppearAsACompletedBuild(bool success, bool cancelled, bool error, ReleaseBuildState expected)
    {
        var result = new BuildPipeline.BuildResult { Success = success, Canceled = cancelled };
        if (error) result.Errors.Add("Build failure");
        ReleaseOverview.Create("App", "1.0.0", "Exe", true, false, result).BuildState.Should().Be(expected);
    }
}
