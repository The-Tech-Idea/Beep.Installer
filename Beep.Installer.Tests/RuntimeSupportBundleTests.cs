using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Policy;
using FluentAssertions;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Diagnostics")]
public class RuntimeSupportBundleTests : IDisposable
{
    private readonly string _tempDir;

    public RuntimeSupportBundleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSupport_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generate_WritesRedactedRuntimeEvidenceWithPolicyAndPlan()
    {
        var source = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.txt"), "payload");

        var project = InstallerProjectFactory.CreateNew("SupportApp", "1.2.3", "ACME", source);
        var installPath = Path.Combine(_tempDir, "install");
        Directory.CreateDirectory(installPath);
        var context = InstallContextBuilder.ForInstall(project, installPath, perUser: true);
        var journalPath = Path.Combine(_tempDir, "journal.json");
        File.WriteAllText(journalPath, """{"secret":"do-not-share"}""");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var logPath = Path.Combine(_tempDir, "install.log");
        File.WriteAllText(logPath, "ApiToken=super-secret-value\nInstalled to " + installPath);
        Diag.Reset();
        using (Diag.BeginScope("install", "test-correlation"))
        {
            Diag.Warn("UnitTest", "password=hunter2", eventId: "BI2498");
        }

        var policyEvaluation = InstallerPolicyEvaluator.EvaluateProject(new InstallerPolicy
        {
            Name = "Enterprise",
            PolicyIssuer = "CN=ACME",
            AllowedPublishers = { "ACME" },
            RequireProvenance = true,
            ForbidLiteralSecrets = false,
            ForbidUnsignedDrivers = false,
        }, project);
        policyEvaluation.Sources.Add(new InstallerPolicySource
        {
            Kind = "machine",
            Name = "machine-policy.json",
            Sha256 = new string('a', 64)
        });

        var outputPath = Path.Combine(_tempDir, "support.json");
        var result = new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
        {
            Action = "install",
            Project = project,
            InstallPath = installPath,
            Success = false,
            ExitCode = 1603,
            Message = "failed with token=abc123",
            LogPath = logPath,
            Context = context,
            PolicyEvaluation = policyEvaluation,
            OutputPath = outputPath,
            CorrelationId = "test-correlation"
        });

        result.Path.Should().Be(outputPath);
        result.SizeBytes.Should().BeGreaterThan(0);
        result.Sha256.Should().HaveLength(64);

        var json = File.ReadAllText(outputPath);
        json.Should().Contain("\"schemaVersion\": \"1.0\"");
        json.Should().Contain("\"correlationId\": \"test-correlation\"");
        json.Should().Contain("\"diagnosticJsonLogPath\"");
        json.Should().Contain("\"policy\"");
        json.Should().Contain("\"effectivePolicySha256\"");
        json.Should().Contain("\"plan\"");
        json.Should().Contain("\"logs\"");
        json.Should().Contain("\"journal\"");
        json.Should().Contain("***REDACTED***");
        json.Should().NotContain("super-secret-value");
        json.Should().NotContain("hunter2");
        json.Should().NotContain("do-not-share");
        json.Should().Contain("%TEMP%");

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("policy").GetProperty("status").GetString().Should().Be("passed");
        document.RootElement.GetProperty("plan").GetProperty("ProductName").GetString().Should().Be("SupportApp");
        document.RootElement.GetProperty("journal").GetProperty("sha256").GetString().Should().HaveLength(64);
        var diagnostic = document.RootElement.GetProperty("diagnostics")
            .EnumerateArray()
            .Should()
            .ContainSingle(item => item.GetProperty("eventId").GetString() == "BI2498")
            .Subject;
        diagnostic.GetProperty("operation").GetString().Should().Be("install");
        diagnostic.GetProperty("correlationId").GetString().Should().Be("test-correlation");
    }

    [Fact]
    public void Generate_WritesPreviewPrivacyMetadataAndPrunesExpiredBundles()
    {
        var source = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.txt"), "payload");

        var project = InstallerProjectFactory.CreateNew("SupportApp", "1.2.3", "ACME", source);
        var outputDirectory = Path.Combine(_tempDir, "support");
        Directory.CreateDirectory(outputDirectory);

        var expiredBundle = Path.Combine(outputDirectory, "SupportApp-install-support-bundle.json");
        File.WriteAllText(expiredBundle, "{}");
        File.SetLastWriteTimeUtc(expiredBundle, DateTime.UtcNow.AddDays(-45));

        var unrelatedFile = Path.Combine(outputDirectory, "keep-diagnostics.json");
        File.WriteAllText(unrelatedFile, "{}");
        File.SetLastWriteTimeUtc(unrelatedFile, DateTime.UtcNow.AddDays(-45));

        var currentOutput = Path.Combine(outputDirectory, "SupportApp-repair-support-bundle.json");
        Diag.Reset();

        var result = new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
        {
            Action = "repair",
            Project = project,
            InstallPath = Path.Combine(_tempDir, "install"),
            Success = true,
            ExitCode = 0,
            OutputPath = currentOutput,
            PreviewOnly = true,
            ConsentGranted = false,
            RetentionDays = 30
        });

        result.Path.Should().Be(currentOutput);
        result.PreviewOnly.Should().BeTrue();
        result.ConsentGranted.Should().BeFalse();
        result.RetentionDays.Should().Be(30);
        result.RetentionDeletedCount.Should().Be(1);
        File.Exists(expiredBundle).Should().BeFalse();
        File.Exists(unrelatedFile).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(currentOutput));
        var privacy = document.RootElement.GetProperty("privacy");
        privacy.GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        privacy.GetProperty("consentGranted").GetBoolean().Should().BeFalse();
        privacy.GetProperty("redactionApplied").GetBoolean().Should().BeTrue();
        privacy.GetProperty("sharingRequiresReview").GetBoolean().Should().BeTrue();

        var retention = document.RootElement.GetProperty("retention");
        retention.GetProperty("enabled").GetBoolean().Should().BeTrue();
        retention.GetProperty("retentionDays").GetInt32().Should().Be(30);
        retention.GetProperty("deletedExpiredBundleCount").GetInt32().Should().Be(1);
        retention.GetProperty("matchPattern").GetString().Should().Be("*-support-bundle.json");
    }
}
