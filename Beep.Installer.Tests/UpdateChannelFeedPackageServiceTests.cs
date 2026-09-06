using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Sockets;
using Beep.Installer.Policy;
using Beep.Installer.Security;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateChannelFeedPackageServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Check_RequiresTrustedAppIdBeforeOpeningFeed(string appId)
    {
        var result = UpdateChannelFeedPackageService.Check(new()
        {
            FeedPath = "missing-feed.json", ExpectedAppId = appId,
            ExpectedAppName = "ChannelApp", ExpectedAppPublisher = "Acme"
        }, "2.0.0");
        result.Success.Should().BeFalse();
        result.Decision.Should().BeNull();
        result.Verification.Diagnostics.Should().ContainSingle(d => d.Code == "BI1578");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verify_BindsSignedFeedToExplicitAppId(bool matching)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var project = Project();
        var export = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem())
        });
        export.Success.Should().BeTrue();
        var verified = UpdateChannelFeedPackageService.Verify(new()
        {
            FeedPath = export.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ExpectedAppId = matching ? project.AppId.ToUpperInvariant() : Guid.NewGuid().ToString("D")
        });
        verified.SignatureTrusted.Should().BeTrue();
        verified.Success.Should().Be(matching);
        verified.Manifest!.AppId.Should().Be(project.AppId.ToLowerInvariant());
        if (!matching) verified.Diagnostics.Should().Contain(d => d.Path == "UpdateChannels.AppId");
    }

    [Theory]
    [InlineData("older")]
    [InlineData("renamed-older")]
    [InlineData("different-product")]
    [InlineData("same-time-changed")]
    [InlineData("repeat")]
    [InlineData("corrupt-state")]
    public void Check_PersistsPublicationHighWaterMark(string scenario)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var project = Project();
        var export = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem())
        });
        var node = JsonNode.Parse(File.ReadAllText(export.FeedPath))!;
        var published = node["CreatedUtc"]!.GetValue<DateTimeOffset>();
        var options = new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"),
            ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = project.AppName, ExpectedAppPublisher = project.AppPublisher
        };
        UpdateChannelCheckResult Check() => UpdateChannelFeedPackageService.Check(options, "2.0.0", nowUtc: published.AddMinutes(2));
        Check().Success.Should().BeTrue();
        var state = Directory.GetFiles(options.ReplayStateDirectory, "*.json").Single();
        if (scenario == "older") node["CreatedUtc"] = published.AddMinutes(-1);
        if (scenario == "renamed-older")
        {
            node["CreatedUtc"] = published.AddMinutes(-1);
            node["AppName"] = "Renamed application";
            node["AppPublisher"] = "Renamed publisher";
            options = options with { ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = "Renamed application", ExpectedAppPublisher = "Renamed publisher" };
        }
        if (scenario == "same-time-changed") node["Issuer"] = "Changed issuer";
        if (scenario == "different-product")
        {
            node["AppId"] = Guid.NewGuid().ToString("D");
            options = options with { ExpectedAppId = node["AppId"]!.GetValue<string>() };
            node["CreatedUtc"] = published.AddMinutes(-1);
        }
        if (scenario == "corrupt-state") File.WriteAllText(state, "not a checkpoint");
        var before = File.ReadAllText(state);
        if (scenario is "older" or "renamed-older" or "different-product" or "same-time-changed")
        {
            var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
            File.WriteAllBytes(export.FeedPath, bytes);
            File.WriteAllText(export.SignaturePath, RsaSha256DetachedSignatureVerifier.SignaturePrefix
                + Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        }
        var result = Check();
        result.Success.Should().Be(scenario is "repeat" or "different-product");
        result.Verification.SignatureTrusted.Should().BeTrue();
        File.ReadAllText(state).Should().Be(before);
        Directory.GetFiles(options.ReplayStateDirectory, "*.json").Should().HaveCount(scenario == "different-product" ? 2 : 1);
        if (scenario is not ("repeat" or "different-product"))
        {
            result.Decision.Should().BeNull();
            result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1580");
        }
    }

    [Theory]
    [InlineData(-301, false)]
    [InlineData(-300, true)]
    [InlineData(0, true)]
    [InlineData(604799, true)]
    [InlineData(604800, false)]
    public void Check_EnforcesSignedPublicationFreshnessBeforeEligibility(int ageSeconds, bool accepted)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var project = Project();
        var export = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem())
        });
        export.Success.Should().BeTrue();
        var options = new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = project.AppName, ExpectedAppPublisher = project.AppPublisher
        };
        var published = UpdateChannelFeedPackageService.Verify(options).Manifest!.CreatedUtc;
        var result = UpdateChannelFeedPackageService.Check(options, "2.0.0", nowUtc: published.AddSeconds(ageSeconds));
        result.Verification.SignatureTrusted.Should().BeTrue();
        result.Success.Should().Be(accepted);
        if (!accepted)
        {
            result.Decision.Should().BeNull();
            result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1579");
            var apply = UpdateChannelFeedPackageService.ApplyDelta(options, new() { CurrentVersion = "2.0.0" },
                nowUtc: published.AddSeconds(ageSeconds));
            apply.Apply.Should().BeNull("stale metadata must never reach payload acquisition or apply");
        }
    }

    [Fact]
    public void Check_RejectsSignedFeedWithoutPublicationTime()
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var project = Project();
        var export = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem())
        });
        var node = JsonNode.Parse(File.ReadAllText(export.FeedPath))!;
        node.AsObject().Remove("CreatedUtc");
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        File.WriteAllBytes(export.FeedPath, bytes);
        File.WriteAllText(export.SignaturePath, RsaSha256DetachedSignatureVerifier.SignaturePrefix
            + Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        var result = UpdateChannelFeedPackageService.Check(new()
        {
            FeedPath = export.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = project.AppName, ExpectedAppPublisher = project.AppPublisher
        }, "2.0.0");
        result.Verification.SignatureTrusted.Should().BeTrue();
        result.Decision.Should().BeNull();
        result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1579");
    }

    [Theory]
    [InlineData("{\"Channels\":null}", "BI1564")]
    [InlineData("{\"Channels\":[null]}", "BI1573")]
    [InlineData("{\"Channels\":[{\"Id\":\"stable\"},{\"Id\":\"STABLE\"}]}", "BI1573")]
    [InlineData("{\"Channels\":[{\"Id\":\"stable\"}],\"SignatureFile\":\"../outside.sig\"}", "BI1574")]
    [InlineData("{\"Channels\":[{\"Id\":\"stable\"}],\"SchemaVersion\":\"99\"}", "BI1573")]
    [InlineData("{\"Channels\":[{\"Id\":\"stable\",\"RolloutPercentage\":101}]}", "BI1573")]
    [InlineData("{\"Channels\":[{\"Id\":\"stable\",\"MaintenanceWindow\":\"invalid\"}]}", "BI1573")]
    public void Verify_RejectsMalformedManifestBeforeTrustProcessing(string manifest, string code)
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write(UpdateChannelFeedPackageService.FeedFileName, manifest);
        var result = UpdateChannelFeedPackageService.Verify(new() { FeedPath = path, TrustedPublicKey = "malformed" });
        result.Success.Should().BeFalse();
        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().Contain(d => d.Code == code);
    }

    [Fact]
    public void Verify_MalformedTrustKeyReturnsDiagnosticInsteadOfThrowing()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write(UpdateChannelFeedPackageService.FeedFileName, "{\"AppId\":\"a34321a2-680b-43a8-af88-c56d6afab012\",\"Channels\":[{\"Id\":\"stable\"}]}");
        var result = UpdateChannelFeedPackageService.Verify(new() { FeedPath = path, TrustedPublicKey = "malformed" });
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1569");
    }

    [Fact]
    public void Export_And_Verify_Signed_Update_Channel_Feed()
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var privateKeyPath = workspace.Write("channel-feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var project = Project();

        var export = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
        {
            Project = project,
            OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = privateKeyPath,
            Issuer = "Release Engineering"
        });

        export.Success.Should().BeTrue();
        File.Exists(export.FeedPath).Should().BeTrue();
        File.Exists(export.SignaturePath).Should().BeTrue();
        export.PublicKeySha256.Should().HaveLength(64);

        var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath,
            TrustedPublicKey = publicKey
        });

        verification.Success.Should().BeTrue();
        verification.SignatureTrusted.Should().BeTrue();
        verification.Manifest!.Issuer.Should().Be("Release Engineering");
        verification.Manifest.SelectedChannelId.Should().Be("stable");
        verification.Manifest.Channels.Should().ContainSingle(channel =>
            channel.Id == "stable"
            && channel.RolloutPercentage == 25
            && channel.Critical
            && channel.FeedUrl == "https://updates.example.test/stable/");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_AttachesDeltaPackageMetadataToSelectedChannel(bool wrongAppId)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var privateKeyPath = workspace.Write("channel-feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var baseDir = System.IO.Path.Combine(workspace.Path, "base");
        var targetDir = System.IO.Path.Combine(workspace.Path, "target");
        var deltaDir = System.IO.Path.Combine(workspace.Path, "delta");
        Write(baseDir, "App.exe", "old");
        Write(targetDir, "App.exe", "new");
        WriteInstalledJournal(baseDir, "ChannelApp", "Acme", "2.0.0");
        WriteInstalledJournal(targetDir, "ChannelApp", "Acme", "2.1.0");
        var delta = new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedProductName = "ChannelApp", ExpectedPublisher = "Acme",
            BaseDirectory = baseDir,
            UpdatedDirectory = targetDir,
            OutputDirectory = deltaDir,
            BaseVersion = "2.0.0",
            TargetVersion = "2.1.0"
        });
        delta.Success.Should().BeTrue(delta.Error);

        var project = Project();
        if (wrongAppId)
        {
            project.AppId = Guid.NewGuid().ToString("D");
            workspace.Write(UpdateChannelFeedPackageService.FeedFileName, "existing feed");
            workspace.Write(UpdateChannelFeedPackageService.SignatureFileName, "existing signature");
        }

        var export = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
        {
            Project = project,
            OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = privateKeyPath,
            DeltaPackageDirectory = deltaDir,
            DeltaChannelId = "stable"
        });

        if (wrongAppId)
        {
            export.Success.Should().BeFalse();
            export.Diagnostics.Should().Contain(d => d.Code == "BI1576");
            File.ReadAllText(Path.Combine(workspace.Path, UpdateChannelFeedPackageService.FeedFileName)).Should().Be("existing feed");
            File.ReadAllText(Path.Combine(workspace.Path, UpdateChannelFeedPackageService.SignatureFileName)).Should().Be("existing signature");
            return;
        }
        export.Success.Should().BeTrue();
        var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath,
            TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem()
        });
        verification.Success.Should().BeTrue();
        var deltaPackage = verification.Manifest!.Channels.Single().DeltaPackages.Single();
        deltaPackage.BaseVersion.Should().Be("2.0.0");
        deltaPackage.TargetVersion.Should().Be("2.1.0");
        deltaPackage.BaseTreeSha256.Should().Be(delta.BaseTreeSha256);
        deltaPackage.TargetTreeSha256.Should().Be(delta.TargetTreeSha256);
        deltaPackage.ManifestFile.Should().Be(DeltaUpdatePackageService.ManifestFileName);
        deltaPackage.SignatureFile.Should().Be(DeltaUpdatePackageService.SignatureFileName);
    }

    [Fact]
    public void Verify_Fails_Closed_When_Feed_Is_Tampered()
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var privateKeyPath = workspace.Write("channel-feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var export = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
        {
            Project = Project(),
            OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = privateKeyPath
        });

        var feed = File.ReadAllText(export.FeedPath).Replace("\"RolloutPercentage\": 25", "\"RolloutPercentage\": 100");
        File.WriteAllText(export.FeedPath, feed);

        var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath,
            TrustedPublicKey = publicKey
        });

        verification.Success.Should().BeFalse();
        verification.SignatureTrusted.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI1567");
    }

    [Fact]
    public void Verify_Fails_Closed_Without_Trusted_Key()
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var privateKeyPath = workspace.Write("channel-feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var export = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
        {
            Project = Project(),
            OutputDirectory = workspace.Path,
            SigningPrivateKeyPath = privateKeyPath
        });

        var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = export.FeedPath
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI1566");
    }

    [Theory]
    [InlineData("window")]
    [InlineData("version")]
    [InlineData("rollout")]
    [InlineData("duplicate")]
    [InlineData("selection")]
    public void Export_InvalidConfigurationDoesNotOverwriteReleaseArtifacts(string invalidField)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var feedPath = workspace.Write(UpdateChannelFeedPackageService.FeedFileName, "existing feed");
        var signaturePath = workspace.Write(UpdateChannelFeedPackageService.SignatureFileName, "existing signature");
        var project = Project();
        switch (invalidField)
        {
            case "window": project.UpdateChannels[0].MaintenanceWindow = "invalid"; break;
            case "version": project.UpdateChannels[0].MinimumVersion = "1..5"; break;
            case "rollout": project.UpdateChannels[0].RolloutPercentage = 101; break;
            case "duplicate": project.UpdateChannels.Add(new() { Id = "STABLE" }); break;
            case "selection": project.AppUpdateChannel = "missing"; break;
        }
        var result = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
        });
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1573");
        File.ReadAllText(feedPath).Should().Be("existing feed");
        File.ReadAllText(signaturePath).Should().Be("existing signature");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_LockedSignatureRestoresPriorFeedOrRemovesNewFeed(bool existingFeed)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var feedPath = Path.Combine(workspace.Path, UpdateChannelFeedPackageService.FeedFileName);
        if (existingFeed) File.WriteAllText(feedPath, "previous feed");
        var signaturePath = workspace.Write(UpdateChannelFeedPackageService.SignatureFileName, "previous signature");
        using (var lockedSignature = new FileStream(signaturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = UpdateChannelFeedPackageService.Export(new()
            {
                Project = Project(), OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
            });
            result.Success.Should().BeFalse();
            result.Diagnostics.Should().Contain(d => d.Code == "BI1556");
            if (existingFeed) File.ReadAllText(feedPath).Should().Be("previous feed");
            else File.Exists(feedPath).Should().BeFalse();
        }
        File.ReadAllText(signaturePath).Should().Be("previous signature");
        Directory.GetFiles(workspace.Path, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(workspace.Path, "*.backup").Should().BeEmpty();
        // A subsequent export must be able to acquire the publication lock and verify.
        var retry = UpdateChannelFeedPackageService.Export(new()
        {
            Project = Project(), OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
        });
        retry.Success.Should().BeTrue();
        UpdateChannelFeedPackageService.Verify(new()
        {
            FeedPath = retry.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem()
        }).Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("channel", "BI8038")]
    [InlineData("host", "BI8029")]
    [InlineData("transport", "BI8030")]
    public void Check_AppliesOrganizationPolicyBeforeReturningDecision(string restriction, string code)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var project = Project();
        var policy = new Beep.Installer.Policy.InstallerPolicy();
        if (restriction == "channel") policy.AllowedUpdateChannels.Add("internal");
        if (restriction == "host") policy.AllowedUpdateHosts.Add("approved.example.test");
        if (restriction == "transport")
        {
            project.UpdateChannels[0].FeedUrl = "http://updates.example.test/stable/";
            policy.ForbidInsecureRemoteSources = true;
        }
        var export = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
        });
        export.Success.Should().BeTrue();
        var check = UpdateChannelFeedPackageService.Check(new()
        {
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = "ChannelApp", ExpectedAppPublisher = "Acme",
            FeedPath = export.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem()
        }, "2.0.0", policyEvaluation: new() { Policy = policy });
        check.Verification.SignatureTrusted.Should().BeTrue();
        check.Success.Should().BeFalse();
        check.Decision.Should().BeNull();
        check.PolicyEvaluation!.Diagnostics.Should().Contain(d => d.Code == code);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false, "2.0")]
    [InlineData(false, false, "2.0", "remote")]
    [InlineData(true, false, "2.0", "remote")]
    [InlineData(false, true, "2.0", "remote")]
    [InlineData(false, false, "2.0", "denied")]
    [InlineData(false, false, "2.0", "redirect")]
    [InlineData(false, false, "2.0", "corrupt-blob")]
    [InlineData(false, false, "2.0", "wrong-product")]
    [InlineData(false, false, "2.0", "wrong-publisher")]
    [InlineData(false, false, "2.0", "wrong-delta-app-id")]
    public void ApplyDelta_UsesSignedChannelGateAndExistingApplyEngine(bool hold, bool substituted, string installedVersion = "2.0.0", string remoteMode = "")
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("feed.key", rsa.ExportPkcs8PrivateKeyPem());
        var current = Path.Combine(workspace.Path, "current");
        var target = Path.Combine(workspace.Path, "target");
        var delta = Path.Combine(workspace.Path, "delta");
        var stage = Path.Combine(workspace.Path, "stage");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        WriteInstalledJournal(current, "ChannelApp", "Acme", "2.0.0");
        WriteInstalledJournal(target, "ChannelApp", "Acme", "2.1.0");
        new DeltaUpdatePackageService().Build(new()
        {
            ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedProductName = "ChannelApp", ExpectedPublisher = "Acme",
            BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
            BaseVersion = "2.0.0", TargetVersion = "2.1.0", SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem()
        }).Success.Should().BeTrue();
        var project = Project();
        using var server = remoteMode == "" ? null : new DeltaHttpServer(delta, remoteMode);
        project.UpdateChannels[0].RolloutPercentage = hold ? 0 : 100;
        project.UpdateChannels[0].MaintenanceWindow = "";
        var exported = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = Path.Combine(workspace.Path, "feed"),
            SigningPrivateKeyPath = key, DeltaPackageDirectory = delta,
            DeltaPackageBaseUrl = server?.Url ?? ""
        });
        exported.Success.Should().BeTrue();
        if (remoteMode == "wrong-delta-app-id")
        {
            var feedNode = JsonNode.Parse(File.ReadAllText(exported.FeedPath))!;
            project.AppId = Guid.NewGuid().ToString("D");
            feedNode["AppId"] = project.AppId;
            var bytes = Encoding.UTF8.GetBytes(feedNode.ToJsonString());
            File.WriteAllBytes(exported.FeedPath, bytes);
            File.WriteAllText(exported.SignaturePath, RsaSha256DetachedSignatureVerifier.SignaturePrefix
                + Convert.ToBase64String(rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        }
        if (substituted)
        {
            Write(target, "app.txt", "substituted");
            new DeltaUpdatePackageService().Build(new()
            {
                ExpectedAppId = project.AppId, ExpectedProductName = project.AppName, ExpectedPublisher = project.AppPublisher,
                BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
                BaseVersion = "2.0.0", TargetVersion = "2.1.0", SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem()
            }).Success.Should().BeTrue();
        }
        var applied = UpdateChannelFeedPackageService.ApplyDelta(new()
        {
            CacheDirectory = Path.Combine(workspace.Path, "cache"),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = project.AppId, ExpectedAppName = remoteMode == "wrong-product" ? "AnotherApp" : "ChannelApp",
            ExpectedAppPublisher = remoteMode == "wrong-publisher" ? "AnotherPublisher" : "Acme",
            FeedPath = exported.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem()
        }, new()
        {
            DeltaDirectory = server is null ? delta : "", CurrentInstallDirectory = current, StageDirectory = stage,
            CurrentVersion = installedVersion, TrustedPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() }
        }, policyEvaluation: remoteMode == "denied" ? new InstallerPolicyEvaluation
        {
            Policy = new InstallerPolicy { AllowedUpdateHosts = { "updates.example.test" } }
        } : null);
        var blocked = hold || substituted || remoteMode is "denied" or "redirect" or "corrupt-blob" or "wrong-product" or "wrong-publisher" or "wrong-delta-app-id";
        applied.Success.Should().Be(!blocked, applied.Error);
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be(blocked ? "old" : "new");
        if (blocked)
        {
            if (server is not null && (hold || remoteMode is "denied" or "wrong-product" or "wrong-publisher")) server.RequestCount.Should().Be(0);
            if (remoteMode is "wrong-product" or "wrong-publisher")
            {
                applied.Check.Verification.SignatureTrusted.Should().BeTrue();
                applied.Check.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1578");
                applied.Check.Decision.Should().BeNull();
            }
            if (hold) applied.Apply.Should().BeNull();
            if (substituted) applied.Error.Should().Contain("authorized update channel");
            Directory.Exists(stage).Should().BeFalse();
            File.Exists(current + ".delta-journal.json").Should().BeFalse();
        }
        else
        {
            File.Exists(applied.Apply!.JournalPath).Should().BeTrue();
            new DeltaUpdatePackageService().RollbackAtomicApply(new() { JournalPath = applied.Apply.JournalPath })
                .Success.Should().BeTrue();
            File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
        }
    }

    internal sealed class DeltaHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private int _requests;
        private int _rangeRequests;
        public bool InterruptBlobs { get; set; }
        public int RangeRequestCount => Volatile.Read(ref _rangeRequests);
        public string Url { get; }
        public int RequestCount => Volatile.Read(ref _requests);

        public DeltaHttpServer(string directory, string mode)
        {
            // Allow-list only test package paths; requests cannot address arbitrary local files.
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
                p => "/" + Path.GetRelativePath(directory, p).Replace('\\', '/'), p => p);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _worker = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    string? header;
                    var offset = 0;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(_stop.Token)))
                        if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                        {
                            offset = int.Parse(header["Range: bytes=".Length..].TrimEnd('-'));
                            Interlocked.Increment(ref _rangeRequests);
                        }
                    Interlocked.Increment(ref _requests);
                    var path = Uri.UnescapeDataString(request!.Split(' ')[1]);
                    if (Uri.TryCreate(path, UriKind.Absolute, out var proxyTarget) && proxyTarget.Scheme is "http" or "https")
                        path = proxyTarget.AbsolutePath;
                    var found = files.TryGetValue(path, out var filePath);
                    var body = found ? File.ReadAllBytes(filePath!) : Array.Empty<byte>();
                    if (mode == "corrupt-blob" && path.StartsWith("/blobs/")) body = Enumerable.Repeat((byte)'X', body.Length).ToArray();
                    var status = mode == "redirect" ? "302 Found" : found ? "200 OK" : "404 Not Found";
                    var range = "";
                    if (offset > 0)
                    {
                        range = $"Content-Range: bytes {offset}-{body.Length - 1}/{body.Length}\r\n";
                        body = body[offset..]; status = "206 Partial Content";
                    }
                    var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\n{range}Location: {Url}redirected\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _stop.Token);
                    if (InterruptBlobs && path.StartsWith("/blobs/")) body = body[..(body.Length / 2)];
                    await stream.WriteAsync(body, _stop.Token);
                }
            });
        }

        public void Dispose()
        {
            _stop.Cancel(); _listener.Stop();
            try { _worker.GetAwaiter().GetResult(); }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is (OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)) { }
            _stop.Dispose();
        }
    }

    [Theory]
    [InlineData("downgrade")]
    [InlineData("same-version")]
    [InlineData("invalid-version")]
    [InlineData("duplicate-base")]
    [InlineData("null-collection")]
    [InlineData("null-entry")]
    [InlineData("manifest-path")]
    [InlineData("signature-path")]
    [InlineData("hash")]
    [InlineData("negative-size")]
    [InlineData("credential-url")]
    [InlineData("file-url")]
    public void Check_RejectsMalformedAttachmentsEvenWithValidPublisherSignature(string invalid)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var manifest = new UpdateChannelFeedManifest
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            AppName = "ChannelApp", SelectedChannelId = "stable",
            Channels = { new() { Id = "stable", RolloutPercentage = 100, DeltaPackages = { new()
            {
                BaseVersion = "2.0.0", TargetVersion = "2.1.0",
                BaseTreeSha256 = new string('a', 64), TargetTreeSha256 = new string('b', 64)
            } } } }
        };
        var document = JsonSerializer.SerializeToNode(manifest)!;
        var channel = document["Channels"]![0]!;
        var packages = channel["DeltaPackages"]!.AsArray();
        var package = packages[0]!;
        switch (invalid)
        {
            case "downgrade": package["TargetVersion"] = "1.9"; break;
            case "same-version": package["TargetVersion"] = "2.0.0.0"; break;
            case "invalid-version": package["BaseVersion"] = "2..0"; break;
            case "duplicate-base":
                var duplicate = package.DeepClone(); duplicate["BaseVersion"] = "2.0";
                packages.Add(duplicate); break;
            case "null-collection": channel["DeltaPackages"] = null; break;
            case "null-entry": packages.Add((JsonNode?)null); break;
            case "manifest-path": package["ManifestFile"] = "../manifest.json"; break;
            case "signature-path": package["SignatureFile"] = "other.sig"; break;
            case "hash": package["BaseTreeSha256"] = "invalid"; break;
            case "negative-size": package["DeltaBytes"] = -1; break;
            case "credential-url": package["PackageBaseUrl"] = "https://user:secret@example.test/release/"; break;
            case "file-url": package["PackageBaseUrl"] = "file:///C:/release/"; break;
        }
        var feed = document.ToJsonString();
        var feedPath = workspace.Write(UpdateChannelFeedPackageService.FeedFileName, feed);
        workspace.Write(UpdateChannelFeedPackageService.SignatureFileName,
            RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(
                rsa.SignData(Encoding.UTF8.GetBytes(feed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        var result = UpdateChannelFeedPackageService.Check(new()
        {
            FeedPath = feedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = "ChannelApp", ExpectedAppPublisher = "Acme"
        }, "2.0.0");
        result.Success.Should().BeFalse();
        result.Decision.Should().BeNull();
        result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1576");
    }

    [Theory]
    [InlineData("1.9")]
    [InlineData("2.0.0.0")]
    [InlineData("2.1.0", "missing-channel")]
    public void Export_RefusesInvalidDeltaAttachmentWithoutReplacingPublishedFeed(string targetVersion, string channelId = "stable")
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem());
        var current = Path.Combine(workspace.Path, "current");
        var target = Path.Combine(workspace.Path, "target");
        var delta = Path.Combine(workspace.Path, "delta");
        Write(current, "app.txt", "old"); Write(target, "app.txt", "new");
        new DeltaUpdatePackageService().Build(new()
        {
            BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
            BaseVersion = "2.0.0", TargetVersion = targetVersion
        }).Success.Should().BeTrue();
        var feed = workspace.Write(UpdateChannelFeedPackageService.FeedFileName, "published feed");
        var signature = workspace.Write(UpdateChannelFeedPackageService.SignatureFileName, "published signature");
        var result = UpdateChannelFeedPackageService.Export(new()
        {
            Project = Project(), OutputDirectory = workspace.Path, SigningPrivateKeyPath = key,
            DeltaPackageDirectory = delta, DeltaChannelId = channelId
        });
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1576");
        File.ReadAllText(feed).Should().Be("published feed");
        File.ReadAllText(signature).Should().Be("published signature");
    }

    [Theory]
    [InlineData("allowed")]
    [InlineData("denied-host")]
    [InlineData("insecure")]
    [InlineData("tampered")]
    [InlineData("redirect")]
    [InlineData("missing-key")]
    public void CheckRemoteFeed_EnforcesTransportPolicyAndSignatureBeforeDecision(string scenario)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("private.pem", rsa.ExportPkcs8PrivateKeyPem());
        var project = Project();
        project.UpdateChannels[0].FeedUrl = "http://127.0.0.1/stable/";
        project.UpdateChannels[0].RolloutPercentage = 100;
        var exported = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
        });
        exported.Success.Should().BeTrue();
        if (scenario == "tampered") File.AppendAllText(exported.FeedPath, " ");
        using var server = new DeltaHttpServer(workspace.Path, scenario);
        var policy = new InstallerPolicy
        {
            AllowedUpdateChannels = { "stable" },
            AllowedUpdateHosts = { scenario == "denied-host" ? "not-allowed.test" : "127.0.0.1" },
            ForbidInsecureRemoteSources = scenario == "insecure"
        };
        var result = UpdateChannelFeedPackageService.Check(new()
        {
            FeedPath = server.Url + UpdateChannelFeedPackageService.FeedFileName,
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = "ChannelApp", ExpectedAppPublisher = "Acme",
            TrustedPublicKey = scenario == "missing-key" ? "" : rsa.ExportSubjectPublicKeyInfoPem()
        }, "2.0", policyEvaluation: new InstallerPolicyEvaluation { Policy = policy, EffectivePolicySha256 = "test-provenance" });
        result.Success.Should().Be(scenario == "allowed");
        result.PolicyEvaluation!.EffectivePolicySha256.Should().Be("test-provenance");
        if (scenario != "allowed") result.Decision.Should().BeNull();
        if (scenario is "denied-host" or "insecure" or "missing-key") server.RequestCount.Should().Be(0);
        if (scenario == "tampered") result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1567");
    }

    [Theory]
    [InlineData("", "Acme")]
    [InlineData("ChannelApp", "")]
    public void Check_RequiresCallerPinnedIdentityBeforeAcquisition(string name, string publisher)
    {
        using var workspace = new TempWorkspace();
        using var server = new DeltaHttpServer(workspace.Path, "remote");
        var result = UpdateChannelFeedPackageService.Check(new()
        {
            FeedPath = server.Url + UpdateChannelFeedPackageService.FeedFileName,
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = name, ExpectedAppPublisher = publisher
        }, "2.0");
        result.Success.Should().BeFalse();
        result.Decision.Should().BeNull();
        result.Verification.Diagnostics.Should().Contain(d => d.Code == "BI1578");
        server.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData("ChannelApp", "Acme", true)]
    [InlineData("OtherApp", "Acme", false)]
    [InlineData("ChannelApp", "", false)]
    public void Verify_HonorsSuppliedIdentityPin(string name, string publisher, bool accepted)
    {
        using var workspace = new TempWorkspace();
        using var rsa = RSA.Create(2048);
        var key = workspace.Write("key.pem", rsa.ExportPkcs8PrivateKeyPem());
        var feed = UpdateChannelFeedPackageService.Export(new()
        {
            Project = Project(), OutputDirectory = workspace.Path, SigningPrivateKeyPath = key
        });
        var result = UpdateChannelFeedPackageService.Verify(new()
        {
            FeedPath = feed.FeedPath, TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ReplayStateDirectory = Path.Combine(workspace.Path, "state"), ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedAppName = name, ExpectedAppPublisher = publisher
        });
        result.SignatureTrusted.Should().BeTrue();
        result.Success.Should().Be(accepted);
        if (!accepted) result.Diagnostics.Should().Contain(d => d.Code == "BI1578");
    }

    internal static void WriteInstalledJournal(string root, string name, string publisher, string version)
    {
        new Beep.Installer.Extensibility.ResourceExecutionJournalStore(
            Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(root, "a34321a2-680b-43a8-af88-c56d6afab012")).Save(new()
        {
            Metadata = new() { AppId = "a34321a2-680b-43a8-af88-c56d6afab012", ProductName = name, Publisher = publisher, ProductVersion = version,
                InstallScope = "user", AttemptId = "channel-fixture", PlanHash = new string('a', 64) }
        });
    }

    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("ChannelApp", "2.0.0", "Acme", "src");
        project.AppId = "a34321a2-680b-43a8-af88-c56d6afab012";
        project.AppUpdateChannel = "stable";
        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "stable",
            Name = "Stable",
            Ring = "production",
            FeedUrl = "https://updates.example.test/stable/",
            RolloutPercentage = 25,
            MinimumVersion = "1.5.0",
            Critical = true,
            MaintenanceWindow = "Sun 02:00-04:00 UTC",
            RollbackVersion = "1.4.0"
        });
        return project;
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = System.IO.Path.Combine(root, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "beep_update_feed_" + Guid.NewGuid().ToString("N"));

        public TempWorkspace()
        {
            Directory.CreateDirectory(Path);
        }

        public string Write(string fileName, string content)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
