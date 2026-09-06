using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class HeadlessSdkQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public HeadlessSdkQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beep-headless-sdk-qualification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Run_PacksSdkAndBuildsExternalPackageReferenceConsumer()
    {
        var report = new HeadlessSdkQualificationRunner().Run(new HeadlessSdkQualificationOptions
        {
            ProjectPath = LocateRepoFile("Beep.Installer", "samples", "ServiceApp.bsetup"),
            SdkProjectPath = LocateRepoFile("Beep.Installer.Core", "Beep.Installer.Core.csproj"),
            PackageVersion = "1.0.0-f26test",
            OutputDirectory = _tempDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine, report.Scenarios.SelectMany(s => s.Diagnostics).Select(d => $"{d.Code} {d.Path}: {d.Message}")));
        report.ExitCode.Should().Be(0);
        report.PackageId.Should().Be(HeadlessSdkQualificationRunner.PackageId);
        report.PackagePath.Should().EndWith("TheTechIdea.Beep.Installer.Sdk.1.0.0-f26test.nupkg");
        File.Exists(report.PackagePath).Should().BeTrue();
        File.Exists(report.ReportPath).Should().BeTrue();
        report.PlanHash.Should().NotBeNullOrWhiteSpace();
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "sdk-direct-plan",
            "sdk-package-metadata",
            "dotnet-pack",
            "nupkg-contract",
            "external-consumer-restore",
            "external-consumer-build",
            "external-consumer-validate",
            "external-consumer-plan",
            "no-sdk-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string LocateRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(segments));
    }
}
