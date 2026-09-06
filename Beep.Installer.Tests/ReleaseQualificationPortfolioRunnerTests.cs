using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ReleaseQualificationPortfolioRunnerTests : IDisposable
{
    private readonly string _root;

    public ReleaseQualificationPortfolioRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BeepReleasePortfolio_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Run_Passes_WhenRequiredQualificationReportsArePresentAndGreen()
    {
        var evidence = Path.Combine(_root, "evidence");
        WriteReport(Path.Combine(evidence, "plan"), CompiledPlanQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "security"), SupplyChainQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "offline"), OfflineLayoutQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "updates"), UpdateChannelQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "delta"), DeltaUpdateQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "winget"), WinGetManifestExporter.QualificationFileName, success: true);
        WriteReport(Path.Combine(evidence, "sdk"), SdkPackagePublisher.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "cli"), EnterpriseCliQualificationRunner.ReportFileName, success: true);

        var report = new ReleaseQualificationPortfolioRunner().Run(new ReleaseQualificationPortfolioOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "out"),
            RequiredQualifications = new[]
            {
                "compiled-plan",
                "supply-chain",
                "offline-layout",
                "update-channel",
                "delta",
                "winget",
                "sdk-publish",
                "enterprise-cli"
            }
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.RequestedQualificationsInput.Should().Equal(
            "compiled-plan",
            "supply-chain",
            "offline-layout",
            "update-channel",
            "delta",
            "winget",
            "sdk-publish",
            "enterprise-cli");
        report.RequiredQualifications.Should().BeEquivalentTo(new[]
        {
            "compiled-plan",
            "supply-chain",
            "offline-layout",
            "update-channel",
            "delta",
            "winget",
            "sdk-publish",
            "enterprise-cli"
        });
        report.DiscoveredQualifications.Should().BeEquivalentTo(new[]
        {
            "compiled-plan",
            "supply-chain",
            "offline-layout",
            "update-channel",
            "delta",
            "winget",
            "sdk-publish",
            "enterprise-cli"
        });
        report.Summary.RequiredCount.Should().Be(8);
        report.Summary.DiscoveredCount.Should().Be(8);
        report.Summary.EvidenceCount.Should().Be(8);
        report.Summary.PassedCount.Should().Be(8);
        report.Summary.MissingCount.Should().Be(0);
        report.Summary.FailedCount.Should().Be(0);
        report.Summary.ErrorCount.Should().Be(0);
        report.Evidence.Select(e => e.QualificationId).Should().BeEquivalentTo(new[]
        {
            "compiled-plan",
            "supply-chain",
            "offline-layout",
            "update-channel",
            "delta",
            "winget",
            "sdk-publish",
            "enterprise-cli"
        });
        report.Evidence.Should().OnlyContain(e => e.Passed);
        File.Exists(report.ReportPath).Should().BeTrue();
        File.ReadAllText(report.ReportPath).Should().NotMatchRegex("(?i)(password|secret|token)");
    }

    [Fact]
    public void ParseRequiredQualifications_NormalizesKnownReportFileNames()
    {
        var required = ReleaseQualificationPortfolioRunner.ParseRequiredQualifications(
            $"{WinGetManifestExporter.QualificationFileName},{SdkPackagePublisher.ReportFileName},{UpdateChannelQualificationRunner.ReportFileName}");

        required.Should().Equal("winget", "sdk-publish", "update-channel");
    }

    [Fact]
    public void ParseRequiredQualifications_ExpandsNamedPresetsAndRemovesDuplicates()
    {
        var required = ReleaseQualificationPortfolioRunner.ParseRequiredQualifications("core,supply-chain,enterprise-cli");

        required.Should().Equal("compiled-plan", "enterprise-cli", "release-evidence", "supply-chain");
    }

    [Fact]
    public void Run_FailsClosed_WhenReleasePresetEvidenceIsIncomplete()
    {
        var evidence = Path.Combine(_root, "preset-evidence");
        WriteReport(Path.Combine(evidence, "plan"), CompiledPlanQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "cli"), EnterpriseCliQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "release"), ReleaseEvidenceQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "security"), SupplyChainQualificationRunner.ReportFileName, success: true);

        var report = new ReleaseQualificationPortfolioRunner().Run(new ReleaseQualificationPortfolioOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "preset-out"),
            RequiredQualifications = new[] { "release" }
        });

        report.Success.Should().BeFalse();
        report.RequestedQualificationsInput.Should().Equal("release");
        report.RequiredQualifications.Should().Contain(new[] { "compiled-plan", "supply-chain", "a11y", "vm", "winget" });
        report.DiscoveredQualifications.Should().BeEquivalentTo(new[] { "compiled-plan", "enterprise-cli", "release-evidence", "supply-chain" });
        report.Summary.RequiredCount.Should().Be(report.RequiredQualifications.Count);
        report.Summary.DiscoveredCount.Should().Be(4);
        report.Summary.EvidenceCount.Should().Be(report.RequiredQualifications.Count);
        report.Summary.PassedCount.Should().Be(4);
        report.Summary.MissingCount.Should().Be(report.RequiredQualifications.Count - 4);
        report.Summary.FailedCount.Should().Be(0);
        report.Summary.ErrorCount.Should().Be(report.RequiredQualifications.Count - 4);
        report.Evidence.Should().Contain(e => e.QualificationId == "compiled-plan" && e.Passed);
        report.Evidence.Should().Contain(e => e.QualificationId == "supply-chain" && e.Passed);
        report.Evidence.Should().Contain(e => e.QualificationId == "vm" && !e.Found);
        report.Diagnostics.Should().Contain(d => d.Code == "BI2902" && d.Path == "vm");
        File.Exists(report.GapPlanPath).Should().BeTrue();
        File.Exists(report.GapManifestPath).Should().BeTrue();

        using var gapJson = JsonDocument.Parse(File.ReadAllText(report.GapManifestPath));
        var gapRoot = gapJson.RootElement;
        gapRoot.GetProperty("PortfolioPassed").GetBoolean().Should().BeFalse();
        gapRoot.GetProperty("MissingCount").GetInt32().Should().Be(report.RequiredQualifications.Count - 4);
        var items = gapRoot.GetProperty("Items").EnumerateArray().ToList();
        items.Should().Contain(item =>
            item.GetProperty("QualificationId").GetString() == "vm"
            && item.GetProperty("Status").GetString() == "missing"
            && item.GetProperty("FeatureId").GetString() == "F28"
            && item.GetProperty("PhaseId").GetString() == "08"
            && item.GetProperty("Priority").GetString() == "P0"
            && item.GetProperty("RoadmapDocument").GetString()!.EndsWith("F28_VM_QUALIFICATION.md", StringComparison.Ordinal)
            && item.GetProperty("CaptureCommand").GetString()!.Contains("/QUALIFYVM=", StringComparison.Ordinal));

        var gapPlan = File.ReadAllText(report.GapPlanPath);
        gapPlan.Should().Contain("# Release Evidence Gap Plan");
        gapPlan.Should().Contain("### vm");
        gapPlan.Should().Contain("Roadmap: `F28` phase `08` priority `P0`");
        gapPlan.Should().Contain("Beep.Installer.exe /QUALIFYVM=<evidence-root>");
    }

    [Fact]
    public void Run_FailsClosed_WhenRequiredReportIsMissingOrNotGreen()
    {
        var evidence = Path.Combine(_root, "evidence-fail");
        WriteReport(Path.Combine(evidence, "plan"), CompiledPlanQualificationRunner.ReportFileName, success: false);

        var report = new ReleaseQualificationPortfolioRunner().Run(new ReleaseQualificationPortfolioOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "out-fail"),
            RequiredQualifications = new[] { "compiled-plan", "vm" }
        });

        report.Success.Should().BeFalse();
        report.Summary.RequiredCount.Should().Be(2);
        report.Summary.DiscoveredCount.Should().Be(1);
        report.Summary.EvidenceCount.Should().Be(2);
        report.Summary.PassedCount.Should().Be(0);
        report.Summary.MissingCount.Should().Be(1);
        report.Summary.FailedCount.Should().Be(1);
        report.Summary.ErrorCount.Should().Be(2);
        report.Diagnostics.Should().Contain(d => d.Code == "BI2902" && d.Path == "vm");
        report.Diagnostics.Should().Contain(d => d.Code == "BI2903" && d.Path == "compiled-plan");
        report.Diagnostics.Count(d => d.Code == "BI2903" && d.Path == "compiled-plan").Should().Be(1);
        report.Evidence.Should().Contain(e => e.QualificationId == "vm" && !e.Found);
        report.Evidence.Should().Contain(e => e.QualificationId == "compiled-plan" && e.Found && e.Success == false);
    }

    [Fact]
    public void Run_OnlyAppliesDiagnosticsForRequestedEvidenceSet()
    {
        var evidence = Path.Combine(_root, "scoped-evidence");
        WriteReport(Path.Combine(evidence, "plan"), CompiledPlanQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "cli"), EnterpriseCliQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "release"), ReleaseEvidenceQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "security"), SupplyChainQualificationRunner.ReportFileName, success: true);
        WriteReport(Path.Combine(evidence, "vm"), VmQualificationReadinessEvaluator.ReportFileName, success: false);

        var report = new ReleaseQualificationPortfolioRunner().Run(new ReleaseQualificationPortfolioOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "scoped-out"),
            RequiredQualifications = new[] { "core" }
        });

        report.Success.Should().BeTrue();
        report.RequiredQualifications.Should().Equal("compiled-plan", "enterprise-cli", "release-evidence", "supply-chain");
        report.DiscoveredQualifications.Should().Contain("vm");
        report.Evidence.Should().NotContain(e => e.QualificationId == "vm");
        report.Diagnostics.Should().NotContain(d => d.Code == "BI2903" && d.Path == "vm");
        report.Summary.ErrorCount.Should().Be(0);
    }

    private static void WriteReport(string directory, string fileName, bool success)
    {
        Directory.CreateDirectory(directory);
        var payload = new
        {
            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "passed" : "failed"
        };
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
