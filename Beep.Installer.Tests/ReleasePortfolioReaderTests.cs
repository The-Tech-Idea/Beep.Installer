using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ReleasePortfolioReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReleaseReader_" + Guid.NewGuid().ToString("N"));
    public ReleasePortfolioReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Write(string text) { var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(path, text); return path; }

    [Fact]
    public void ExistingPortfolioContractsLoadWithoutRetainingRawDiagnosticsAndResolveRelativeEvidencePaths()
    {
        var report = new ReleaseQualificationPortfolioReport { Success = true, EvidenceRoot = _root, CompletedUtc = DateTimeOffset.UtcNow,
            Message = "secret-root-message", Evidence = new() { new() { QualificationId = "signing", Found = true, Success = true,
                ReportPath = "signing.json", Message = "secret-item-message", Diagnostics = new() { new(ProjectSchemaDiagnosticSeverity.Error, "failure", "Evidence", "secret-failure-message") } } } };
        var path = Write(JsonSerializer.Serialize(report)); var before = Directory.GetFiles(_root);
        var loaded = ReleasePortfolioReader.Read(path); loaded.Error.Should().Be(ReleasePortfolioLoadError.None);
        loaded.Report!.Evidence.Single().ReportPath.Should().Be(Path.Combine(_root, "signing.json"));
        loaded.Report.Evidence.Single().Success.Should().BeFalse();
        JsonSerializer.Serialize(loaded.Report).Should().NotContain("secret-root-message").And.NotContain("secret-item-message").And.NotContain("secret-failure-message");
        Directory.GetFiles(_root).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"Evidence\":[],\"EvidenceRoot\":\"x\",\"CompletedUtc\":\"2026-10-03T00:00:00Z\",\"Success\":true,\"Diagnostics\":null}")]
    public void InvalidFilesReturnNoReport(string text)
    {
        var loaded = ReleasePortfolioReader.Read(Write(text));
        loaded.Report.Should().BeNull(); loaded.Error.Should().Be(ReleasePortfolioLoadError.Invalid);
    }

    [Fact]
    public void TheProductionPortfolioWriterProducesAnAcceptedReport()
    {
        var path = Path.Combine(_root, ReleaseQualificationPortfolioRunner.ReportFileName);
        ReleaseQualificationPortfolioRunner.WriteReport(new() { OutputDirectory = _root, ReportPath = path,
            EvidenceRoot = _root, Success = true, CompletedUtc = DateTimeOffset.UtcNow,
            Evidence = new() { new() { QualificationId = "signing", Found = true, Success = true } } });
        var loaded = ReleasePortfolioReader.Read(path);
        loaded.Error.Should().Be(ReleasePortfolioLoadError.None); loaded.Report!.Evidence.Should().ContainSingle();
    }

    [Fact]
    public void AmbiguousOrIncompleteEvidenceCannotSilentlyBecomePassed()
    {
        var prefix = "{\"EvidenceRoot\":\"x\",\"Success\":true,\"CompletedUtc\":\"2026-10-03T00:00:00Z\",\"Diagnostics\":[],\"Evidence\":[";
        var incomplete = "{\"QualificationId\":\"signing\",\"Found\":true,\"Success\":true}";
        var ambiguous = "{\"QualificationId\":\"signing\",\"Found\":true,\"Success\":false,\"success\":true,\"Diagnostics\":[]}";
        foreach (var item in new[] { incomplete, ambiguous, "null" })
            ReleasePortfolioReader.Read(Write(prefix + item + "]}")).Error.Should().Be(ReleasePortfolioLoadError.Invalid);
    }

    [Fact]
    public void RootFailuresCannotBecomeAnOverallPassEvenWhenIndividualItemsPassed()
    {
        var report = new ReleaseQualificationPortfolioReport { Success = true, EvidenceRoot = _root, CompletedUtc = DateTimeOffset.UtcNow,
            Diagnostics = new() { new(ProjectSchemaDiagnosticSeverity.Error, "root", "Evidence", "Failure") },
            Evidence = new() { new() { QualificationId = "signing", Found = true, Success = true } } };
        var loaded = ReleasePortfolioReader.Read(Write(JsonSerializer.Serialize(report)));
        loaded.Report!.Success.Should().BeFalse(); loaded.Report.Evidence.Single().Success.Should().BeTrue();
        ReleaseOverview.Create("App", "1.0.0", "Exe", true, false, portfolio: loaded.Report).QualificationReportedSuccess.Should().BeFalse();
    }

    [Fact]
    public void MissingAndOversizedFilesAreDistinctAndRemainUntouched()
    {
        ReleasePortfolioReader.Read(Path.Combine(_root, "missing.json")).Error.Should().Be(ReleasePortfolioLoadError.Unavailable);
        var path = Path.Combine(_root, "large.json");
        using (var stream = File.Create(path)) stream.SetLength(ReleasePortfolioReader.MaximumBytes + 1L);
        ReleasePortfolioReader.Read(path).Error.Should().Be(ReleasePortfolioLoadError.TooLarge);
        new FileInfo(path).Length.Should().Be(ReleasePortfolioReader.MaximumBytes + 1L);
    }
}
