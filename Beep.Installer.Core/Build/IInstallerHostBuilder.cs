using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Beep.Installer.Engine;

/// <summary>
/// Produces the installer *host* executable — the self-contained Beep.Installer binary that
/// the payload is later appended to.
///
/// This is a seam on purpose. The only real implementation shells <c>dotnet publish</c> of the
/// whole installer project, which takes minutes and needs the source tree on disk. Without an
/// injection point every build-pipeline test had to perform that publish, so the pipeline's
/// staging, compression, embedding and cleanup logic was effectively untestable.
/// </summary>
public interface IInstallerHostBuilder
{
    /// <summary>
    /// Produces the host executable at <see cref="InstallerHostRequest.DestinationExePath"/>.
    /// Returns false and populates <paramref name="result"/>.Errors on failure.
    /// </summary>
    bool TryBuildHost(InstallerHostRequest request, BuildPipeline.BuildResult result);
}

/// <summary>Inputs for one host build.</summary>
public sealed class InstallerHostRequest
{
    /// <summary>.NET runtime identifier, e.g. <c>win-x64</c>.</summary>
    public required string RuntimeIdentifier { get; init; }

    /// <summary>Scratch directory the publish writes into. The caller deletes it afterwards.</summary>
    public required string PublishDir { get; init; }

    /// <summary>Final location for the host executable.</summary>
    public required string DestinationExePath { get; init; }

    /// <summary>Hard limit for the publish. Exceeding it is an error, not a silent truncation.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Precompile the host to native code (ReadyToRun).
    ///
    /// Off by default. R2R trades a large amount of BUILD time for a faster first start, and
    /// it was previously forced on: compiling the installer's whole dependency tree (BeepDM
    /// plus the Beep.Winform control library) pushed a single `/BUILD` past ten minutes and
    /// made it time out. An installer is run once or twice and its startup is dominated by
    /// payload extraction, so paying minutes per build for milliseconds at launch is a bad
    /// trade — enable it deliberately if a release build wants it.
    /// </summary>
    public bool ReadyToRun { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

/// <summary>
/// Real implementation: <c>dotnet publish</c> of the Beep.Installer project as an uncompressed
/// single-file, self-contained, ReadyToRun binary.
///
/// The single-file bundle must stay UNCOMPRESSED. A compressed .NET single-file appends its own
/// bundle footer to the PE, and appending our payload after that corrupts the host.
/// </summary>
public sealed class DotnetPublishHostBuilder : IInstallerHostBuilder
{
    private readonly Func<string?> _projectPathResolver;

    public DotnetPublishHostBuilder(Func<string?>? projectPathResolver = null)
        => _projectPathResolver = projectPathResolver ?? BuildPipeline.FindBeepInstallerProjectPath;

    public bool TryBuildHost(InstallerHostRequest request, BuildPipeline.BuildResult result)
    {
        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            var csprojPath = _projectPathResolver();
            if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            {
                result.Errors.Add(
                    "Could not locate Beep.Installer.csproj to publish the installer host. " +
                    "Building a Setup.exe requires the installer source tree.");
                return false;
            }

            var csprojDir = Path.GetDirectoryName(csprojPath)!;
            var publishArgs = $"publish \"{csprojPath}\" -c Release -r {request.RuntimeIdentifier} " +
                              $"--self-contained true " +
                              $"-p:PublishSingleFile=true " +
                              $"-p:PublishReadyToRun={(request.ReadyToRun ? "true" : "false")} " +
                              $"-o \"{request.PublishDir}\"";

            var psi = new ProcessStartInfo("dotnet", publishArgs)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = csprojDir,
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Errors.Add("Failed to start dotnet publish.");
                return false;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var stdoutClosed = new ManualResetEventSlim(false);
            using var stderrClosed = new ManualResetEventSlim(false);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    stdoutClosed.Set();
                    return;
                }

                stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    stderrClosed.Set();
                    return;
                }

                stderr.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var deadlineUtc = DateTime.UtcNow.Add(request.Timeout);
            while (!process.WaitForExit(250))
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    result.Errors.Add("dotnet publish was canceled.");
                    return false;
                }

                if (DateTime.UtcNow >= deadlineUtc)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    result.Errors.Add($"dotnet publish timed out after {request.Timeout.TotalMinutes:0.#} minutes.");
                    return false;
                }
            }

            if (request.CancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                result.Errors.Add("dotnet publish was canceled.");
                return false;
            }

            // The parameterless overload additionally waits for redirected output to flush.
            // Without it the reads below can return truncated text on a fast-exiting process.
            process.WaitForExit();
            stdoutClosed.Wait(TimeSpan.FromSeconds(2));
            stderrClosed.Wait(TimeSpan.FromSeconds(2));

            if (process.ExitCode != 0)
            {
                result.Errors.Add($"dotnet publish failed (exit {process.ExitCode}).");
                foreach (var line in NonEmptyLines(stderr.ToString())) result.Errors.Add(line);
                foreach (var line in NonEmptyLines(stdout.ToString())) result.Warnings.Add(line);
                return false;
            }

            var publishedExe = Directory.EnumerateFiles(request.PublishDir, "Beep.Installer.exe").FirstOrDefault()
                            ?? Directory.EnumerateFiles(request.PublishDir, "*.exe").FirstOrDefault();
            if (publishedExe == null)
            {
                result.Errors.Add($"Published EXE not found in {request.PublishDir}.");
                return false;
            }

            File.Copy(publishedExe, request.DestinationExePath, overwrite: true);
            return File.Exists(request.DestinationExePath);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Self-contained publish failed: {ex.Message}");
            return false;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> NonEmptyLines(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.TrimEnd());
}
