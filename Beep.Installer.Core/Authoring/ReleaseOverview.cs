using Beep.Installer.Quality;

namespace Beep.Installer.Engine;

public enum ReleaseBuildState { NoBuild, Building, Completed, Failed, Cancelled }
public enum ReleaseQualificationState { Missing, ReportedPassed, ReportedFailed }
public sealed record ReleaseFile(string Kind, string Path, bool Exists);
public sealed record ReleaseSigning(string Kind, string ArtifactPath, bool ArtifactExists,
    bool ReportedSuccess, bool? Timestamped, string CertificateSubject, string FileDigestSha256);
public sealed record ReleaseQualification(string Id, string ReportPath, bool ReportExists,
    ReleaseQualificationState State, DateTimeOffset? CompletedUtc);

/// <summary>A read-only presentation snapshot. Generated files and reported checks do not prove deployment.</summary>
public sealed record ReleaseOverviewSnapshot(string ProjectName, string Version, string Format,
    bool IsCurrentProject, ReleaseBuildState BuildState, bool? SigningConfigured,
    IReadOnlyList<ReleaseFile> PackageFiles, IReadOnlyList<ReleaseSigning> Signing,
    IReadOnlyList<ReleaseFile> SelectedDeploymentFiles, IReadOnlyList<ReleaseQualification> Qualifications,
    DateTimeOffset? QualificationCompletedUtc, bool? QualificationReportedSuccess)
{
    // Portfolio reports contain no project/build identity contract. Their claims remain unbound.
    public bool QualificationAssociatedWithBuild => false;
    public bool? DeploymentSucceeded => null;
}

public static class ReleaseOverview
{
    public static ReleaseOverviewSnapshot Create(string projectName, string version, string format,
        bool isCurrentProject, bool? signingConfigured, BuildPipeline.BuildResult? build = null,
        IEnumerable<string>? selectedDeploymentFiles = null, ReleaseQualificationPortfolioReport? portfolio = null, bool buildRunning = false)
    {
        var files = new List<ReleaseFile>();
        void FileRow(string kind, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !files.Any(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                files.Add(new(kind, path, File.Exists(path)));
        }
        if (build is not null)
        {
            FileRow(build.ArtifactKind, build.OutputFile);
            FileRow("msix", build.MsixPackagePath);
            FileRow("appinstaller", build.AppInstallerPath);
            FileRow("payload", build.PayloadPath);
            FileRow("setup-script", build.SetupScriptPath);
            FileRow("capability-report", build.MsixCapabilityReportPath);
        }
        var state = buildRunning ? ReleaseBuildState.Building : build is null ? ReleaseBuildState.NoBuild
            : build.Canceled ? ReleaseBuildState.Cancelled
            : build.Success && build.Errors.Count == 0 ? ReleaseBuildState.Completed : ReleaseBuildState.Failed;
        var signing = build?.SigningEvidence.Select(item => new ReleaseSigning(item.ArtifactKind,
            item.ArtifactPath, File.Exists(item.ArtifactPath), item.Success, item.Timestamped,
            item.CertificateSubject, item.FileDigestSha256)).ToArray() ?? Array.Empty<ReleaseSigning>();
        var selected = (selectedDeploymentFiles ?? Array.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new ReleaseFile("selected-asset", path, File.Exists(path))).ToArray();
        var qualifications = (portfolio?.Evidence ?? new()).Select(item => new ReleaseQualification(
            item.QualificationId, item.ReportPath, File.Exists(item.ReportPath),
            !item.Found ? ReleaseQualificationState.Missing
                : item.Success == true && item.Diagnostics is not null && !item.Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
                    ? ReleaseQualificationState.ReportedPassed : ReleaseQualificationState.ReportedFailed,
            item.CompletedUtc)).ToArray();
        return new(projectName, version, format, isCurrentProject, state, signingConfigured,
            files.ToArray(), signing, selected, qualifications, portfolio?.CompletedUtc,
            portfolio is null ? null : portfolio.Success && portfolio.Diagnostics is not null
                && !portfolio.Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
                && qualifications.Length > 0 && qualifications.All(item => item.State == ReleaseQualificationState.ReportedPassed));
    }
}
