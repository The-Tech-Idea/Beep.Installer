using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The build half of payload integrity (8.A.3).
///
/// Runtime verification is only as good as the pin the author declares, and computing a SHA-256 by
/// hand after every build is exactly the step people skip. The build now reports the digest, writes
/// it beside the archive for upload, and says so when a URL-hosted project has not pinned it — or
/// has pinned a stale value, which would otherwise surface as a failed install on a customer's
/// machine rather than as a warning here.
/// </summary>
public sealed class PayloadDigestBuildTests : IDisposable
{
    private readonly string _root;

    public PayloadDigestBuildTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepPayloadDigest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private InstallProject Project(Action<InstallProject>? configure = null)
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.exe"), "app");
        File.WriteAllText(Path.Combine(source, "readme.txt"), "readme");

        var project = InstallerProjectFactory.CreateNew("DigestApp", "1.0.0", "ACME", source).UseTestDefaults();
        project.OutputDir = Path.Combine(_root, "out");
        project.MainExecutable = "App.exe";
        project.CreateUninstallEntry = false;
        configure?.Invoke(project);
        return project;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static BuildPipeline.BuildResult Build(InstallProject project)
        => TestHelpers.TestPipeline().Run(project);

    [Fact]
    public void TheDigestMatchesTheArchiveThatWasActuallyProduced()
    {
        var result = Build(Project());

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.PayloadSha256.Should().NotBeNullOrWhiteSpace();
        result.PayloadSha256.Should().Be(Sha256Of(result.PayloadPath),
            "the recorded digest has to describe the archive on disk, sidecars included");
    }

    [Fact]
    public void TheDigestIsWrittenBesideTheArchiveForUpload()
    {
        var result = Build(Project());

        var sidecar = result.PayloadPath + ".sha256";
        File.Exists(sidecar).Should().BeTrue();
        File.ReadAllText(sidecar).Trim().Should().Be(result.PayloadSha256);
    }

    [Fact]
    public void AUrlHostedPayloadWithNoPin_IsCalledOut_WithTheValueToUse()
    {
        var result = Build(Project(p =>
        {
            p.PayloadSource = PayloadSourceType.Url;
            p.PayloadUrl = "https://downloads.example.test/payload.zip";
        }));

        result.Warnings.Should().Contain(w => w.Contains("declares no PayloadSha256"));
        result.Warnings.Should().Contain(w => w.Contains(result.PayloadSha256),
            "the warning has to carry the digest, or the author still has to compute it");
    }

    [Fact]
    public void AStalePin_IsCaughtAtBuildTime_NotOnACustomerMachine()
    {
        var result = Build(Project(p =>
        {
            p.PayloadSource = PayloadSourceType.Url;
            p.PayloadUrl = "https://downloads.example.test/payload.zip";
            p.PayloadSha256 = new string('a', 64); // a previous build's digest
        }));

        result.Warnings.Should().Contain(w => w.Contains("does not match the payload just built"));
    }

    [Fact]
    public void RebuildingChangesTheDigest_SoAStalePinIsAlwaysReported()
    {
        // By default the archive records when it was packed — zip entry timestamps and the build
        // time in version.txt — so a rebuild changes the digest and the warning has to name the
        // new one. Setting BuildPipeline.SourceDateEpoch (or SOURCE_DATE_EPOCH) stops that;
        // ReproduciblePayloadTests covers it, and a pin then survives a rebuild.
        var first = Build(Project(p =>
        {
            p.PayloadSource = PayloadSourceType.Url;
            p.PayloadUrl = "https://downloads.example.test/payload.zip";
        }));

        var second = Build(Project(p =>
        {
            p.PayloadSource = PayloadSourceType.Url;
            p.PayloadUrl = "https://downloads.example.test/payload.zip";
            p.PayloadSha256 = first.PayloadSha256;
        }));

        second.PayloadSha256.Should().NotBe(first.PayloadSha256);
        second.Warnings.Should().Contain(w => w.Contains(first.PayloadSha256) && w.Contains(second.PayloadSha256),
            "a stale pin is only actionable if the warning shows both the declared and the built digest");
    }

    [Fact]
    public void ALocalPayloadIsNotNagged_ItIsEmbedded_NotDownloaded()
    {
        var result = Build(Project(p => p.PayloadSource = PayloadSourceType.Local));

        result.PayloadSha256.Should().NotBeNullOrWhiteSpace("the digest is still useful evidence");
        result.Warnings.Should().NotContain(w => w.Contains("PayloadSha256"));
    }
}
