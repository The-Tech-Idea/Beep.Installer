using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SdkPackagePublisherTests : IDisposable
{
    private readonly string _tempDir;

    public SdkPackagePublisherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beep-sdk-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Publish_DryRunWritesEvidenceWithoutExecutingNuGetPush()
    {
        var package = Package();

        var result = new SdkPackagePublisher().Publish(new SdkPackagePublishOptions
        {
            PackagePath = package,
            Source = "https://nuget.example.test/v3/index.json",
            ApiKey = "secret://env/NUGET_API_KEY",
            SecretProvider = new FixedSecretProvider("env", "resolved-api-key"),
            DryRun = true,
            OutputDirectory = _tempDir,
            ToolRunner = _ => throw new InvalidOperationException("dry-run must not execute dotnet nuget push")
        });

        result.Success.Should().BeTrue();
        result.DryRun.Should().BeTrue();
        result.ApiKeyWasSecretReference.Should().BeTrue();
        result.CommandLine.Should().Contain("dotnet nuget push");
        result.CommandLine.Should().Contain("--skip-duplicate");
        result.CommandLine.Should().NotContain("resolved-api-key");
        result.CommandLine.Should().Contain("[redacted]");
        File.ReadAllText(result.ReportPath).Should().NotContain("resolved-api-key");
    }

    [Fact]
    public void Publish_ExecutesNuGetPushThroughInjectedRunnerAndRedactsSecretEvidence()
    {
        var package = Package();
        SdkPackagePublishInvocation? invocation = null;

        var result = new SdkPackagePublisher().Publish(new SdkPackagePublishOptions
        {
            PackagePath = package,
            Source = "contoso-feed",
            ApiKey = "secret://env/NUGET_API_KEY",
            SecretProvider = new FixedSecretProvider("env", "resolved-api-key"),
            OutputDirectory = _tempDir,
            ToolRunner = call =>
            {
                invocation = call;
                return new SdkPackagePublishToolResult
                {
                    ExitCode = 0,
                    ToolVersion = "dotnet 10.0.11",
                    StandardOutput = "Pushed with resolved-api-key"
                };
            }
        });

        result.Success.Should().BeTrue();
        invocation.Should().NotBeNull();
        invocation!.Arguments.Should().ContainInOrder("nuget", "push", package, "--source", "contoso-feed", "--skip-duplicate", "--api-key", "resolved-api-key");
        result.StandardOutput.Should().NotContain("resolved-api-key");
        File.ReadAllText(result.ReportPath).Should().NotContain("resolved-api-key");
    }

    [Fact]
    public void Publish_FailsBeforeExecutionWhenPackageOrSourceIsMissing()
    {
        var result = new SdkPackagePublisher().Publish(new SdkPackagePublishOptions
        {
            PackagePath = Path.Combine(_tempDir, "missing.nupkg"),
            Source = "",
            OutputDirectory = _tempDir,
            ToolRunner = _ => throw new InvalidOperationException("invalid publish inputs must not execute")
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI2611");
        result.Diagnostics.Should().Contain(d => d.Code == "BI2612");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private string Package()
    {
        var path = Path.Combine(_tempDir, "TheTechIdea.Beep.Installer.Sdk.1.0.0.nupkg");
        File.WriteAllText(path, "package");
        return path;
    }

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _scheme;
        private readonly string _value;

        public FixedSecretProvider(string scheme, string value)
        {
            _scheme = scheme;
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => SecretResolutionResult.Found(_value);
    }
}
