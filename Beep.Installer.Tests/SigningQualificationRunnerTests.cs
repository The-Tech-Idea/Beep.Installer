using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SigningQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public SigningQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSigningQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesSigningAndSecretProviderQualificationEvidence()
    {
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new SigningQualificationRunner().Run(new SigningQualificationOptions
        {
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine,
            report.Scenarios.SelectMany(s => s.Diagnostics.Select(d => $"{s.Id}: {d.Code} {d.Path} {d.Message}"))));
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Equal(new[]
        {
            "pfx-secret-boundary",
            "certificate-store-selector",
            "remote-credential-audit",
            "timestamp-policy",
            "mixed-source-rejection",
            "no-signing-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, SigningQualificationRunner.ReportFileName)).Should().BeTrue();
        File.ReadAllText(Path.Combine(outDir, SigningQualificationRunner.ReportFileName))
            .Should().NotContain("resolved-pfx-password!")
            .And.NotContain("remote-token!");
    }
}
