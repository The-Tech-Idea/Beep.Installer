using System.Diagnostics;
using Beep.Installer.Models;
using Beep.Installer.Engine.Msi;

namespace Beep.Installer.Engine;

/// <summary>Routes additional saved output formats to the established packaging services.</summary>
internal static class AlternativePackageBuilder
{
    internal static BuildPipeline.BuildResult Run(InstallProject project, BuildPipeline pipeline, bool cleanOutput)
    {
        var result = pipeline.Validate(project);
        result.ArtifactKind = project.OutputFormat.ToString().ToLowerInvariant();
        var watch = Stopwatch.StartNew();
        try
        {
            pipeline.CancellationToken.ThrowIfCancellationRequested();
            if (cleanOutput)
                result.Errors.Add("Clean output is not supported for MSI or ClickOnce builds. Choose a new output folder for a fresh publication.");
            result.Errors.AddRange(PackageTargetDiagnostics.Validate(project)
                .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error).Select(d => d.Message));
            if (result.Errors.Count > 0) return result;
            var output = Path.GetFullPath(BuildPipeline.ResolveOutputDirectory(project));
            if (project.OutputFormat == InstallerOutputFormat.Msi &&
                !project.Components.Any(c => c.Files is { Count: > 0 }) && Directory.Exists(project.SourceDirectory))
            {
                var scan = new SourceScanner().ScanAndApply(project, project.SourceDirectory,
                    new SourceScanner.Options { AutoDiscoverBuildOutput = false });
                result.Warnings.AddRange(scan.Warnings);
                result.FileCount = scan.FileCount;
            }

            pipeline.Progress?.Report(new(10, "Preparing " + result.ArtifactKind, BuildPipeline.BuildPhase.Preparing));
            if (project.OutputFormat == InstallerOutputFormat.Msi)
            {
                pipeline.Progress?.Report(new(20, "Building MSI package", BuildPipeline.BuildPhase.Building));
                var exported = MsiPackageExporter.Generate(project, new MsiExportOptions
                {
                    OutputDirectory = output, StagePayloads = true, BuildPackage = true,
                    SignOutput = project.HasCodeSigningCertificate,
                    SigningService = pipeline.SigningService,
                    ExpectedSigningSubject = pipeline.ExpectedSigningSubject,
                    TimestampOutagePolicy = pipeline.TimestampOutagePolicy,
                    TimestampRetryCount = pipeline.TimestampRetryCount,
                    ToolRunner = pipeline.MsiToolRunner,
                    Progress = pipeline.Progress
                });
                result.OutputFile = exported.PackagePath;
                result.Steps.Add(exported.WixCommandLine);
                result.Steps.Add(exported.WixStandardOutput);
                result.Steps.Add(exported.WixStandardError);
                result.Errors.AddRange(exported.Findings.Where(f => f.Severity == "error").Select(f => f.Message));
                result.Warnings.AddRange(exported.Findings.Where(f => f.Severity == "warning").Select(f => f.Message));
                if (!File.Exists(result.OutputFile)) result.Errors.Add("The MSI service did not produce a package. Check WiX availability and the build log.");
            }
            else
            {
                var publisher = pipeline.ClickOncePublisher;
                if (publisher is ClickOncePublisher clickOnce) clickOnce.BuildProgress = pipeline.Progress;
                else publisher.Progress = new InlineProgress<(int percent, string message)>(p =>
                    pipeline.Progress?.Report(new(p.percent, p.message)));
                var published = publisher.Publish(project, output, project.AppUpdatesURL, project.HasCodeSigningCertificate);
                result.OutputFile = published.DeploymentManifest;
                result.FileCount = published.DeployFileCount;
                result.Errors.AddRange(published.Errors);
                result.Warnings.AddRange(published.Warnings);
                if (!published.Success && result.Errors.Count == 0) result.Errors.Add("ClickOnce publication failed.");
                if (!File.Exists(result.OutputFile)) result.Errors.Add("The publisher did not produce a deployment manifest.");
            }
            pipeline.CancellationToken.ThrowIfCancellationRequested();
            result.Success = result.Errors.Count == 0;
            if (File.Exists(result.OutputFile)) result.OutputSizeBytes = new FileInfo(result.OutputFile).Length;
        }
        catch (OperationCanceledException) { result.Canceled = true; result.Errors.Add("Build canceled."); }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            Diag.Warn("PackageBuilder", "Alternative package build failed", ex);
        }
        finally
        {
            watch.Stop();
            result.Elapsed = watch.Elapsed;
            pipeline.Progress?.Report(new(100, result.Canceled ? "Build canceled." : result.Success ? "Build complete." : "Build failed.",
                result.Canceled ? BuildPipeline.BuildPhase.Canceled : result.Success ? BuildPipeline.BuildPhase.Completed : BuildPipeline.BuildPhase.Failed));
        }
        return result;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
