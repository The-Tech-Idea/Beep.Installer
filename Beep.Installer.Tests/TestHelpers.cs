using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;

namespace Beep.Installer.Tests;

/// <summary>
/// Shared test utilities for exercising the build pipeline without a real
/// <c>dotnet publish</c> of the whole installer project.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Legacy no-op flags. These were intended to stop the pipeline publishing a
    /// self-contained host, but the pipeline hardcodes <c>--self-contained true</c>, so they
    /// never had that effect. Kept because many tests still call it; the actual isolation now
    /// comes from <see cref="TestPipeline"/>.
    /// </summary>
    public static InstallProject UseTestDefaults(this InstallProject project)
    {
        project.SelfContained = false;
        project.SingleFile = false;
        return project;
    }

    /// <summary>
    /// A pipeline whose host build is stubbed, so tests exercise staging, script writing,
    /// compression, payload embedding and cleanup without a multi-minute publish.
    /// </summary>
    /// <param name="keepIntermediates">
    /// Defaults to true because most build tests assert on the staged payload, payload.zip and
    /// script.bsetup — which a shipping build deletes once they are embedded in the EXE. Pass
    /// false to exercise the real cleanup behaviour.
    /// </param>
    public static BuildPipeline TestPipeline(bool keepIntermediates = true)
        => new()
        {
            HostBuilder = new StubInstallerHostBuilder(),
            KeepIntermediates = keepIntermediates,
        };
}

/// <summary>
/// Stands in for <see cref="DotnetPublishHostBuilder"/>. Writes a small placeholder file where
/// the published host would go. The pipeline only appends a payload to that file and reports
/// its size, so a real PE image is unnecessary.
/// </summary>
internal sealed class StubInstallerHostBuilder : IInstallerHostBuilder
{
    public bool TryBuildHost(InstallerHostRequest request, BuildPipeline.BuildResult result)
    {
        var dir = Path.GetDirectoryName(request.DestinationExePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // "MZ" so anything sniffing for a PE prefix sees something plausible.
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        File.WriteAllBytes(request.DestinationExePath, bytes);

        return File.Exists(request.DestinationExePath);
    }
}
