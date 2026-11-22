using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beep.Installer.Security;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class UpdatePreviewTests
{
    [Theory]
    [InlineData("1", UpdateDeltaSelectionStatus.Selected)]
    [InlineData("1.0.0.0", UpdateDeltaSelectionStatus.Selected)]
    [InlineData("2", UpdateDeltaSelectionStatus.Missing)]
    [InlineData("1-beta", UpdateDeltaSelectionStatus.InvalidVersion)]
    public void SelectionUsesTheSameNumericBaseVersionAsApply(string version, UpdateDeltaSelectionStatus expected)
        => UpdateChannelFeedPackageService.SelectDelta(Check(), version).Status.Should().Be(expected);

    [Fact]
    public void DuplicateNumericBasesAreAmbiguousAndCannotApply()
    {
        var check = Check();
        check.Verification.Manifest!.Channels[0].DeltaPackages.Add(new() { BaseVersion = "1.0.0", TargetVersion = "3" });
        UpdateChannelFeedPackageService.SelectDelta(check, "1").Status.Should().Be(UpdateDeltaSelectionStatus.Ambiguous);
        UpdatePreview.Create(check, "1", "", Array.Empty<string>()).CanApply.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TrustAndTransitionAuthorizationAreBothRequired(bool trusted, bool allowed)
    {
        var check = Check(trusted, allowed);
        UpdateChannelFeedPackageService.SelectDelta(check, "1").Status.Should().Be(UpdateDeltaSelectionStatus.Unauthorized);
        UpdatePreview.Create(check, "1", "", Array.Empty<string>()).CanApply.Should().BeFalse();
    }

    [Fact]
    public void RemotePreviewDoesNotAcquirePayloadAndDistinguishesActualTargetFromFeedVersion()
    {
        using var rsa = RSA.Create(2048);
        var snapshot = UpdatePreview.Create(Check(), "1", "", new[] { rsa.ExportSubjectPublicKeyInfoPem() });
        snapshot.CanApply.Should().BeTrue(); snapshot.LocalSignatureTrusted.Should().BeFalse();
        snapshot.Payload.Should().Be(UpdatePreviewPayload.RemoteDelta);
        snapshot.TargetVersion.Should().Be("2"); snapshot.FeedVersion.Should().Be("9");
        snapshot.DeltaBytes.Should().Be(42); snapshot.TargetBytes.Should().Be(200);
        snapshot.FullPackageFallbackAvailable.Should().BeFalse();
    }

    [Fact]
    public void MissingKeySourceOrPolicyPermissionBlockApply()
    {
        using var rsa = RSA.Create(2048);
        var keys = new[] { rsa.ExportSubjectPublicKeyInfoPem() };
        UpdatePreview.Create(Check(), "1", "", new[] { "invalid key" }).Blocker.Should().Be(UpdatePreviewBlocker.MissingDeltaKey);
        var missingSource = Check(); missingSource.Verification.Manifest!.Channels[0].DeltaPackages.Clear();
        missingSource.Verification.Manifest.Channels[0].DeltaPackages.Add(new() { BaseVersion = "1", TargetVersion = "2" });
        UpdatePreview.Create(missingSource, "1", "", keys).Blocker.Should().Be(UpdatePreviewBlocker.MissingSource);
        UpdatePreview.Create(Check(policy: new() { Policy = new() { AllowedUpdateHosts = { "approved.example.test" } } }),
            "1", "", keys).Blocker.Should().Be(UpdatePreviewBlocker.AcquisitionPolicyDenied);
    }

    [Fact]
    public void LocalPreviewVerifiesSignedContractWithoutTouchingInstallation()
    {
        using var fixture = new SignedFixture();
        var before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var preview = UpdatePreview.Create(fixture.Check(), "1.0", fixture.Delta, new[] { fixture.PublicKey });
        preview.CanApply.Should().BeTrue(); preview.LocalSignatureTrusted.Should().BeTrue();
        Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().Should().Equal(before.Append(
            Directory.GetFiles(Path.Combine(fixture.Root, "state"), "*.json").Single()).Distinct().Order());
        File.ReadAllText(Path.Combine(fixture.Current, "app.txt")).Should().Be("old");
        var mismatched = Check();
        UpdatePreview.Create(mismatched, "1.0", fixture.Delta, new[] { fixture.PublicKey }).Blocker.Should()
            .Be(UpdatePreviewBlocker.LocalContractMismatch);
        File.AppendAllText(Path.Combine(fixture.Delta, DeltaUpdatePackageService.ManifestFileName), " ");
        var tampered = UpdatePreview.Create(fixture.Check(), "1.0", fixture.Delta, new[] { fixture.PublicKey });
        tampered.Blocker.Should().Be(UpdatePreviewBlocker.InvalidLocalSignature); tampered.CanApply.Should().BeFalse();
    }

    [Fact]
    public void ApplyRejectsAFeedDigestDifferentFromTheReviewedOneBeforeWritingStageOrJournal()
    {
        using var fixture = new SignedFixture();
        var check = fixture.Check(); check.Success.Should().BeTrue();
        var stage = Path.Combine(fixture.Root, "stage"); var journal = Path.Combine(fixture.Root, "update.json");
        var result = UpdateChannelFeedPackageService.ApplyDelta(fixture.Options, new()
        {
            CurrentInstallDirectory = fixture.Current, StageDirectory = stage, JournalPath = journal,
            CurrentVersion = "1.0", DeltaDirectory = fixture.Delta, RequireSignature = true,
            TrustedPublicKeys = { fixture.PublicKey }
        }, "stable", "stable", expectedFeedSha256: new string('f', 64));
        result.Success.Should().BeFalse(); result.Error.Should().Contain("changed since preview");
        result.Apply.Should().BeNull(); Directory.Exists(stage).Should().BeFalse(); File.Exists(journal).Should().BeFalse();
        File.ReadAllText(Path.Combine(fixture.Current, "app.txt")).Should().Be("old");
    }

    [Fact]
    public void MatchingReviewedFeedRetainsTheExistingSignedApplyPath()
    {
        using var fixture = new SignedFixture(); var check = fixture.Check();
        var result = UpdateChannelFeedPackageService.ApplyDelta(fixture.Options, new()
        {
            CurrentInstallDirectory = fixture.Current, StageDirectory = Path.Combine(fixture.Root, "stage"),
            JournalPath = Path.Combine(fixture.Root, "update.json"), CurrentVersion = "1.0", DeltaDirectory = fixture.Delta,
            RequireSignature = true, TrustedPublicKeys = { fixture.PublicKey }
        }, "stable", "stable", expectedFeedSha256: check.Verification.FeedSha256);
        result.Success.Should().BeTrue(result.Error + result.Apply?.Error);
        File.ReadAllText(Path.Combine(fixture.Current, "app.txt")).Should().Be("new");
    }

    [Fact]
    public void ANewTrustedPublicationRequiresFreshPreviewEvenWhenTheDeltaContractIsUnchanged()
    {
        using var fixture = new SignedFixture(); var reviewed = fixture.Check();
        var document = JsonNode.Parse(File.ReadAllText(fixture.Options.FeedPath))!;
        document["CreatedUtc"] = document["CreatedUtc"]!.GetValue<DateTimeOffset>().AddSeconds(1);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString()); File.WriteAllBytes(fixture.Options.FeedPath, bytes);
        using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(Path.Combine(fixture.Root, "key.pem")));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Options.FeedPath)!, UpdateChannelFeedPackageService.SignatureFileName),
            RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        var stage = Path.Combine(fixture.Root, "stage"); var journal = Path.Combine(fixture.Root, "update.json");
        var result = UpdateChannelFeedPackageService.ApplyDelta(fixture.Options, new()
        { CurrentInstallDirectory = fixture.Current, StageDirectory = stage, JournalPath = journal, CurrentVersion = "1.0",
            DeltaDirectory = fixture.Delta, RequireSignature = true, TrustedPublicKeys = { fixture.PublicKey } },
            "stable", "stable", expectedFeedSha256: reviewed.Verification.FeedSha256);
        result.Check.Success.Should().BeTrue(); result.Check.Verification.SignatureTrusted.Should().BeTrue();
        result.Error.Should().Contain("changed since preview"); result.Apply.Should().BeNull();
        Directory.Exists(stage).Should().BeFalse(); File.Exists(journal).Should().BeFalse();
        File.ReadAllText(Path.Combine(fixture.Current, "app.txt")).Should().Be("old");
    }

    private static UpdateChannelCheckResult Check(bool trusted = true, bool allowed = true, InstallerPolicyEvaluation? policy = null)
        => new()
        {
            PolicyEvaluation = policy,
            Verification = new() { SignatureTrusted = trusted, FeedSha256 = new string('a', 64), Manifest = new()
            {
                AppVersion = "9", Channels = { new() { Id = "stable", DeltaPackages = { new()
                { BaseVersion = "1.0", TargetVersion = "2", BaseTreeSha256 = new string('b', 64), TargetTreeSha256 = new string('c', 64),
                    DeltaBytes = 42, TargetBytes = 200, PackageBaseUrl = "https://updates.example.test/delta/" } } } }
            } },
            Decision = new() { Allowed = allowed, CurrentChannelId = "preview", TargetChannelId = "stable", Action = "transition", RollbackVersion = "0.9" }
        };

    internal sealed class SignedFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "p13_update_preview_" + Guid.NewGuid().ToString("N"));
        public string Current => Path.Combine(Root, "current");
        public string Delta => Path.Combine(Root, "delta");
        public string PublicKey { get; }
        public UpdateChannelFeedVerificationOptions Options { get; }
        public SignedFixture()
        {
            using var rsa = RSA.Create(2048); PublicKey = rsa.ExportSubjectPublicKeyInfoPem();
            var target = Path.Combine(Root, "target"); Directory.CreateDirectory(Current); Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(Current, "app.txt"), "old"); File.WriteAllText(Path.Combine(target, "app.txt"), "new");
            UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(Current, "ChannelApp", "Acme", "1.0", null, "app.txt");
            UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(target, "ChannelApp", "Acme", "2.0", null, "app.txt");
            new DeltaUpdatePackageService().Build(new() { ExpectedAppId = UpdateChannelFeedPackageServiceTests.FixtureAppId,
                ExpectedProductName = "ChannelApp", ExpectedPublisher = "Acme", BaseDirectory = Current, UpdatedDirectory = target,
                OutputDirectory = Delta, BaseVersion = "1.0", TargetVersion = "2.0", SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }).Success.Should().BeTrue();
            var privateKey = Path.Combine(Root, "key.pem"); File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
            var project = InstallerProjectFactory.CreateNew("ChannelApp", "2.0", "Acme", "src");
            project.AppId = UpdateChannelFeedPackageServiceTests.FixtureAppId; project.AppUpdateChannel = "stable";
            project.UpdateChannels.Add(new() { Id = "stable", Name = "Stable", FeedUrl = "https://updates.example.test/stable/", RolloutPercentage = 100 });
            var exported = UpdateChannelFeedPackageService.Export(new() { Project = project, OutputDirectory = Path.Combine(Root, "feed"),
                SigningPrivateKeyPath = privateKey, DeltaPackageDirectory = Delta, DeltaChannelId = "stable" });
            exported.Success.Should().BeTrue();
            Options = new() { FeedPath = exported.FeedPath, TrustedPublicKey = PublicKey, ExpectedAppId = project.AppId,
                ExpectedAppName = project.AppName, ExpectedAppPublisher = project.AppPublisher, ReplayStateDirectory = Path.Combine(Root, "state") };
        }
        public UpdateChannelCheckResult Check() => UpdateChannelFeedPackageService.Check(Options, "1.0", "stable", "stable");
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
