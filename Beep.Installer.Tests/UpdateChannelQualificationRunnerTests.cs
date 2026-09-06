using System.Security.Cryptography;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateChannelQualificationRunnerTests
{
    [Fact]
    public void Run_Writes_Qualification_Report_For_Feed_And_Transition_Scenarios()
    {
        var root = Path.Combine(Path.GetTempPath(), "beep_update_channel_qualification_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var rsa = RSA.Create(2048);
            var privateKeyPath = Path.Combine(root, "channels.private.pem");
            var publicKeyPath = Path.Combine(root, "channels.public.pem");
            File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

            var export = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
            {
                Project = Project(),
                OutputDirectory = Path.Combine(root, "feed"),
                SigningPrivateKeyPath = privateKeyPath
            });
            export.Success.Should().BeTrue();

            var report = new UpdateChannelQualificationRunner().Run(new UpdateChannelQualificationOptions
            {
                FeedPath = export.FeedPath,
                TrustedPublicKeyPath = publicKeyPath,
                OutputDirectory = Path.Combine(root, "qualification"),
                CurrentChannelId = "stable",
                TargetChannelId = "beta",
                InstalledVersion = "2.0.0",
                CohortSeed = "device-001",
                VerifyLifecycle = true,
                DryRun = true,
                ScriptPath = Path.Combine(root, "current.bsetup"),
                UpdatedScriptPath = Path.Combine(root, "updated.bsetup"),
                DowngradeScriptPath = Path.Combine(root, "older.bsetup"),
                InstallDirectory = Path.Combine(root, "install")
            });

            report.Success.Should().BeTrue();
            File.Exists(report.ReportPath).Should().BeTrue();
            report.Scenarios.Select(s => s.Id).Should().Contain(new[]
            {
                "verify-feed",
                "tampered-feed-failure",
                "offline-reconnect",
                "transition",
                "rollout-hold",
                "revoked-rollback",
                "install-current",
                "update-target",
                "downgrade-blocked",
                "uninstall-current"
            });
            report.Scenarios.Should().OnlyContain(s => s.Success);
            report.Scenarios.Where(s => s.Category == "lifecycle").Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.CommandLine));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("ChannelApp", "2.0.0", "Acme", "src");
        project.AppUpdateChannel = "stable";
        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "stable",
            Name = "Stable",
            Ring = "production",
            FeedUrl = "https://updates.example.test/stable/",
            RolloutPercentage = 100,
            MinimumVersion = "1.0.0",
            RollbackVersion = "1.0.0"
        });
        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "beta",
            Name = "Beta",
            Ring = "early",
            FeedUrl = "https://updates.example.test/beta/",
            RolloutPercentage = 100,
            MinimumVersion = "2.0.0",
            Critical = true,
            RollbackVersion = "1.0.0"
        });
        return project;
    }
}
