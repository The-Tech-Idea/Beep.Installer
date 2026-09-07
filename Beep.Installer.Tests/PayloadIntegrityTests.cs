using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Integrity of a remote payload (2.C.2 / 8.A.3).
///
/// A URL-sourced payload used to be downloaded, extracted and installed with no verification at
/// all: a poisoned mirror, a hijacked CDN edge or a plain-HTTP hop was enough to put arbitrary
/// files on the machine. The archive is now checked against the SHA-256 the project declares,
/// while it is still an inert temp file.
/// </summary>
public sealed class PayloadIntegrityTests : IDisposable
{
    private readonly string _root;

    public PayloadIntegrityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepPayloadIntegrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private static InstallProject Project(string url, string sha256) => new()
    {
        AppId = "5f0c9d21-8e47-4b13-9c6a-71ad02f3e884",
        AppName = "PayloadApp",
        AppVersion = "1.0.0",
        PayloadSource = PayloadSourceType.Url,
        PayloadUrl = url,
        PayloadSha256 = sha256,
        Components = new ObservableCollection<InstallComponent>
        {
            new() { Id = "core", Name = "Core", Required = true, Selected = true }
        }
    };

    /// <summary>Serves a prepared archive, standing in for the publisher's host.</summary>
    private sealed class StubFetcher : IPayloadFetcher
    {
        private readonly string _archive;
        public StubFetcher(string archive) => _archive = archive;
        public int Fetches { get; private set; }

        public void Fetch(string url, string destinationPath, IProgress<PassedArgs>? progress)
        {
            Fetches++;
            File.Copy(_archive, destinationPath, overwrite: true);
        }
    }

    /// <summary>Builds a payload archive on disk and returns its path and real hash.</summary>
    private (string Archive, string Sha256) PublishArchive(string content)
    {
        var staging = Path.Combine(_root, "staging", Guid.NewGuid().ToString("N"), "payload");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "app.txt"), content);

        var archive = Path.Combine(_root, $"payload_{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(staging)!, archive);

        using var stream = File.OpenRead(archive);
        var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return (archive, sha);
    }

    private const string PayloadUrl = "https://downloads.example.test/payload.zip";

    private static SetupContext ContextFor(InstallProject project, string installPath)
        => InstallContextBuilder.ForInstall(project, installPath, perUser: true);

    [Fact]
    public void DeclaredHash_ReachesTheStepThroughTheContext()
    {
        var context = ContextFor(Project("https://host/payload.zip", "abc123"), Path.Combine(_root, "install"));

        context.TryGetProperty<string>(InstallContextKeys.PayloadSha256).Should().Be("abc123");
    }

    [Fact]
    public void NoHashIsProjected_WhenTheProjectDeclaresNone()
    {
        var context = ContextFor(Project("https://host/payload.zip", ""), Path.Combine(_root, "install"));

        context.TryGetProperty<string>(InstallContextKeys.PayloadSha256).Should().BeNull();
    }

    [Fact]
    public void MatchingArchive_IsExtracted()
    {
        var (archive, sha) = PublishArchive("genuine payload");
        var context = ContextFor(Project(PayloadUrl, sha), Path.Combine(_root, "install"));

        var result = new PayloadDownloadStep(fetcher: new StubFetcher(archive)).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        var payloadRoot = context.TryGetProperty<string>(InstallContextKeys.PayloadRoot);
        payloadRoot.Should().NotBeNullOrWhiteSpace();
        File.ReadAllText(Path.Combine(payloadRoot!, "app.txt")).Should().Be("genuine payload");
    }

    [Fact]
    public void TamperedArchive_IsRefusedAndNothingIsExtracted()
    {
        // The publisher's hash, but the mirror serves different bytes.
        var (_, publishedSha) = PublishArchive("genuine payload");
        var (tamperedArchive, tamperedSha) = PublishArchive("attacker payload");
        tamperedSha.Should().NotBe(publishedSha, "the fixture has to actually differ");

        var context = ContextFor(Project(PayloadUrl, publishedSha), Path.Combine(_root, "install"));

        var result = new PayloadDownloadStep(fetcher: new StubFetcher(tamperedArchive)).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Failed);
        result.Message.Should().Contain("does not match the declared SHA-256");
        context.TryGetProperty<string>(InstallContextKeys.PayloadRoot).Should().BeNull(
            "a payload that failed verification must never become the payload root");
        context.Properties.ContainsKey("PayloadExtracted").Should().BeFalse();
    }

    [Fact]
    public void UndeclaredHash_StillInstalls_ButIsRecordedAsUnverifiable()
    {
        // Hard-failing here would break every project already shipping a remote payload, so the
        // gap is recorded rather than enforced.
        var (archive, _) = PublishArchive("unverified payload");
        var context = ContextFor(Project(PayloadUrl, ""), Path.Combine(_root, "install"));
        Diag.Reset();

        var result = new PayloadDownloadStep(fetcher: new StubFetcher(archive)).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        Diag.Recent.Should().Contain(e => e.EventId == "BI2610" && e.Level == "WARN",
            "an unverifiable payload has to leave a trace");
    }

    [Fact]
    public void HashComparison_IsCaseInsensitive()
    {
        var (archive, sha) = PublishArchive("genuine payload");
        var context = ContextFor(Project(PayloadUrl, sha.ToUpperInvariant()), Path.Combine(_root, "install"));

        var result = new PayloadDownloadStep(fetcher: new StubFetcher(archive)).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
    }

    [Fact]
    public void PayloadSha256_RoundTripsThroughTheScript()
    {
        var (_, sha) = PublishArchive("genuine payload");
        var project = Project(PayloadUrl, sha);
        var path = Path.Combine(_root, "roundtrip.bsetup");

        InstallerScriptSerializer.Save(project, path);
        var (loaded, error) = InstallerScriptSerializer.Load(path);

        error.Should().BeNull();
        loaded!.PayloadSha256.Should().Be(sha);
    }
}
