using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class HeadlessInstallerSdkTests : IDisposable
{
    private readonly string _tempRoot;

    public HeadlessInstallerSdkTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "beep-headless-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_EmbedsExtensionAndRuntimeLoadsItForInstallAndUninstall(bool signed)
    {
        var script = CreateProjectScript();
        var project = InstallerScriptSerializer.Load(script).project!;
        project.SolidCompression = false;
        project.CreateUninstallEntry = false;
        project.Resources.Add(new CompiledInstallOperation
        {
            Id = "extension:copy", Type = "sample.resource",
            Inputs = new() { ["source"] = "app.exe", ["destination"] = "extension.txt" }
        });
        InstallerScriptSerializer.Save(project, script).ok.Should().BeTrue();
        var packageDirectory = Path.Combine(_tempRoot, "extension-package");
        Directory.CreateDirectory(packageDirectory);
        foreach (var name in new[] { "SampleProvider.dll", "beep-extension.json" })
            File.Copy(Path.Combine(SampleExtensionDirectory(), name), Path.Combine(packageDirectory, name));
        File.WriteAllText(Path.Combine(packageDirectory, "credentials.json"), "private build configuration");
        using var signingKey = System.Security.Cryptography.RSA.Create(2048);
        var extensionPolicy = new Beep.Installer.Policy.InstallerPolicy();
        if (signed)
        {
            extensionPolicy.RequireExtensionSignatures = true;
            extensionPolicy.TrustedExtensionPublicKeys.Add(signingKey.ExportSubjectPublicKeyInfoPem());
            var manifestPath = Path.Combine(packageDirectory, "beep-extension.json");
            var manifestText = File.ReadAllText(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<Beep.Installer.Extensibility.InstallerExtensionManifest>(manifestText)!;
            var unsignedDiscovery = new Beep.Installer.Extensibility.InstallerExtensionDiscovery(new() { LoadProviders = false })
                .DiscoverExplicitDirectories(new[] { packageDirectory });
            unsignedDiscovery.Extensions.Single().Manifest.CanonicalSigningPayload().Should().Be(manifest.CanonicalSigningPayload());
            manifest.Files = Beep.Installer.Extensibility.InstallerExtensionPackageInventory.Capture(packageDirectory, manifest.EntryAssembly);
            var signature = signingKey.SignData(System.Text.Encoding.UTF8.GetBytes(manifest.CanonicalSigningPayload()),
                System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var document = System.Text.Json.Nodes.JsonNode.Parse(manifestText)!;
            document["Files"] = System.Text.Json.JsonSerializer.SerializeToNode(manifest.Files);
            document["Signature"] = "rsa-sha256:" + Convert.ToBase64String(signature);
            File.WriteAllText(manifestPath, document.ToJsonString());
        }
        var built = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = script, ExtensionDirectories = new[] { packageDirectory },
            PolicyEvaluator = model => Beep.Installer.Policy.InstallerPolicyEvaluator.EvaluateProject(extensionPolicy, model),
            HostBuilder = new StubInstallerHostBuilder()
        });
        built.Success.Should().BeTrue(string.Join("; ", built.Diagnostics.Select(d => d.Message)));
        var archive = Path.Combine(_tempRoot, "embedded.zip");
        PePayloadWriter.Extract(built.OutputFile, archive);
        var extracted = Path.Combine(_tempRoot, "extracted");
        System.IO.Compression.ZipFile.ExtractToDirectory(archive, extracted);
        Directory.EnumerateFiles(extracted, "credentials.json", SearchOption.AllDirectories).Should().BeEmpty();
        var runtimeProject = InstallerScriptSerializer.Load(Path.Combine(extracted, "script.bsetup")).project!;
        runtimeProject.Resources.Should().ContainSingle();
        var installRoot = Path.Combine(_tempRoot, "installed");
        Directory.CreateDirectory(installRoot);
        var context = InstallContextBuilder.ForInstall(runtimeProject, installRoot, true,
            payloadRoot: Path.Combine(extracted, runtimeProject.PayloadFolderName));
        context.Properties[InstallContextKeys.ExtensionBundleRoot] = extracted;
        var step = new Beep.Installer.Steps.ResourceProviderStep();
        step.Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);
        File.ReadAllText(Path.Combine(installRoot, "extension.txt")).Should().Be("placeholder");
        // Fresh registry and provider instances prove persisted journal replay.
        context.Properties.TryRemove(InstallContextKeys.ResourceProviderRegistry, out _);
        new Beep.Installer.Steps.ResourceProviderUninstallStep().Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);
        File.Exists(Path.Combine(installRoot, "extension.txt")).Should().BeFalse();
        if (signed)
        {
            using var endpointKey = System.Security.Cryptography.RSA.Create(2048);
            var endpointPolicy = new Beep.Installer.Policy.InstallerPolicy
            {
                RequireExtensionSignatures = true,
                TrustedExtensionPublicKeys = { endpointKey.ExportSubjectPublicKeyInfoPem() }
            };
            var endpointLoad = () => Beep.Installer.Extensibility.InstallerExtensionBundle.Load(extracted, endpointPolicy);
            endpointLoad.Should().Throw<InvalidOperationException>().WithMessage("*failed extension discovery*");
        }

        var tamperedRoot = Path.Combine(_tempRoot, "tampered");
        System.IO.Compression.ZipFile.ExtractToDirectory(archive, tamperedRoot);
        var dll = Path.Combine(tamperedRoot, Beep.Installer.Extensibility.InstallerExtensionBundle.DirectoryName, "0000", "SampleProvider.dll");
        File.AppendAllText(dll, "tampered");
        var load = () => Beep.Installer.Extensibility.InstallerExtensionBundle.Load(tamperedRoot);
        load.Should().Throw<InvalidOperationException>().WithMessage("*hash mismatch*");
        File.Copy(Path.Combine(packageDirectory, "SampleProvider.dll"), dll, true);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(dll)!, "unlisted.dll"), "unlisted");
        load.Should().Throw<InvalidOperationException>().WithMessage("*unlisted file*");
    }

    [Fact]
    public void Build_ResolvesRelativeOutputBeforePublishingHost()
    {
        var script = CreateProjectScript();
        var expected = Path.Combine(_tempRoot, "relative-output");
        var host = new AbsolutePathHostBuilder();
        var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = script,
            OutputDirectory = Path.GetRelativePath(Environment.CurrentDirectory, expected),
            HostBuilder = host
        });
        result.Success.Should().BeTrue();
        host.Request.Should().NotBeNull();
        host.Request!.PublishDir.Should().Be(Path.Combine(expected, "_publish"));
        Path.IsPathFullyQualified(host.Request.DestinationExePath).Should().BeTrue();
        Path.GetDirectoryName(result.OutputFile).Should().Be(expected);
    }

    private sealed class AbsolutePathHostBuilder : IInstallerHostBuilder
    {
        public InstallerHostRequest? Request { get; private set; }
        public bool TryBuildHost(InstallerHostRequest request, BuildPipeline.BuildResult result)
        {
            Request = request;
            return new StubInstallerHostBuilder().TryBuildHost(request, result);
        }
    }

    [Fact]
    public void Validate_ReturnsCliEquivalentDiagnosticsWithoutWinForms()
    {
        var scriptPath = CreateProjectScript();

        var result = HeadlessInstallerSdk.Validate(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            Strict = true
        });

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.ProductName.Should().Be("HeadlessApp");
        result.ProductVersion.Should().Be("1.2.3");
        result.Errors.Should().BeEmpty();
        result.LintDiagnostics.Should().BeEmpty();
        result.SchemaDiagnostics.Should().BeEmpty();
        result.BuildErrors.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ReturnsDeterministicCompilerPlanAndJson()
    {
        var scriptPath = CreateProjectScript();
        var (loaded, error) = InstallerScriptSerializer.Load(scriptPath);
        loaded.Should().NotBeNull(error);
        InstallerScriptSerializer.ResolveRelativePaths(loaded!, scriptPath);
        var directPlan = new InstallPlanCompiler().Compile(loaded!).Plan;

        var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            Strict = true
        });

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Plan.Should().NotBeNull();
        result.PlanHash.Should().Be(directPlan!.PlanHash);
        result.OperationCount.Should().Be(directPlan.Operations.Count);
        result.PlanJson.Should().Contain(result.PlanHash);
        result.PlanJson.Should().Contain("file.copy");
    }

    [Fact]
    public void Plan_RunsExtensionProjectValidatorsBeforeCompiler()
    {
        var scriptPath = CreateProjectScript("Blocked HeadlessApp");

        var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            ExtensionDirectories = new[] { SampleExtensionDirectory() }
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Plan.Should().BeNull();
        result.PlanJson.Should().BeEmpty();
        result.OperationCount.Should().Be(0);
        result.Errors.Should().ContainSingle(d => d.Code == "SAMPLE101");
    }

    [Fact]
    public void Build_HonorsRequireSignedGateBeforeProducingUnsignedArtifacts()
    {
        var scriptPath = CreateProjectScript();

        var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            RequireSigned = true,
            HostBuilder = new StubInstallerHostBuilder()
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.OutputFile.Should().BeEmpty();
        result.Errors.Should().ContainSingle(d => d.Code == "BI2603" && d.Path == "Setup.CodeSigning");
    }

    [Fact]
    public void Build_UsesCanonicalPipelineWithProgressAndStubbedHostBuilder()
    {
        var scriptPath = CreateProjectScript();
        var outputDir = Path.Combine(_tempRoot, "out");
        var progress = new RecordingProgress();

        var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            OutputDirectory = outputDir,
            HostBuilder = new StubInstallerHostBuilder(),
            Progress = progress
        });

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.OutputFile.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.OutputFile).Should().BeTrue();
        result.Build.Should().NotBeNull();
        result.Build!.SetupScriptPath.Should().NotBeNullOrWhiteSpace();
        progress.Events.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_CancellationCleansIntermediatePayloadAndReportsCanceled()
    {
        var scriptPath = CreateProjectScript();
        var outputDir = Path.Combine(_tempRoot, "cancel-out");
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(value =>
        {
            if (value.Percent >= 8)
                cancellation.Cancel();
        });

        var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            OutputDirectory = outputDir,
            HostBuilder = new StubInstallerHostBuilder(),
            Progress = progress,
            CancellationToken = cancellation.Token
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Errors.Should().Contain(d => d.Code == "BI2604" && d.Message == "Build canceled.");
        Directory.Exists(Path.Combine(outputDir, "payload")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "payload.zip")).Should().BeFalse();
        Directory.Exists(Path.Combine(outputDir, "_publish")).Should().BeFalse();
        result.Build!.Steps.Should().Contain(s => s.Contains("canceled-build intermediates cleaned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Build_ParallelHeadlessBuildsUseIsolatedOutputDirectories()
    {
        var scriptPath = CreateProjectScript();
        var firstOut = Path.Combine(_tempRoot, "parallel-a");
        var secondOut = Path.Combine(_tempRoot, "parallel-b");

        var firstTask = Task.Run(() => HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            OutputDirectory = firstOut,
            HostBuilder = new StubInstallerHostBuilder()
        }));
        var secondTask = Task.Run(() => HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            OutputDirectory = secondOut,
            HostBuilder = new StubInstallerHostBuilder()
        }));

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Should().OnlyContain(r => r.Success);
        results.Select(r => r.OutputFile).Should().OnlyHaveUniqueItems();
        results.Select(r => Path.GetDirectoryName(r.OutputFile)).Should().BeEquivalentTo(firstOut, secondOut);
        File.Exists(results[0].OutputFile).Should().BeTrue();
        File.Exists(results[1].OutputFile).Should().BeTrue();
    }

    [Fact]
    public void Build_RunsExtensionProjectValidatorsBeforePipeline()
    {
        var scriptPath = CreateProjectScript("Blocked HeadlessApp");
        var outputDir = Path.Combine(_tempRoot, "blocked-sdk-build");
        var progress = new RecordingProgress();

        var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
        {
            ProjectPath = scriptPath,
            OutputDirectory = outputDir,
            ExtensionDirectories = new[] { SampleExtensionDirectory() },
            HostBuilder = new StubInstallerHostBuilder(),
            Progress = progress
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.OutputFile.Should().BeEmpty();
        result.Build.Should().BeNull();
        result.Errors.Should().ContainSingle(d => d.Code == "SAMPLE101");
        progress.Events.Should().BeEmpty("extension validation must stop the SDK build before BuildPipeline starts");
        Directory.Exists(outputDir).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string CreateProjectScript(string appName = "HeadlessApp")
    {
        var sourceDir = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(sourceDir);
        var appPath = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(appPath, "placeholder");

        var outputDir = Path.Combine(_tempRoot, "build");
        var project = InstallerProjectFactory.CreateNew(appName, "1.2.3", "ACME", sourceDir);
        project.OutputDir = outputDir;
        project.OutputFormat = InstallerOutputFormat.Exe;
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files = new List<FileCopyOperation>
            {
                new() { SourcePath = appPath, DestinationPath = "app.exe", Description = "Application" }
            }
        });

        var scriptPath = Path.Combine(_tempRoot, "project.bsetup");
        var (ok, error) = InstallerScriptSerializer.Save(project, scriptPath);
        ok.Should().BeTrue(error);
        return scriptPath;
    }

    private static string SampleExtensionDirectory()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Beep.Installer",
            "samples",
            "Extensions",
            "SampleProvider"));

    private sealed class RecordingProgress : IProgress<BuildPipeline.BuildProgress>
    {
        private readonly Action<BuildPipeline.BuildProgress>? _onReport;
        public List<BuildPipeline.BuildProgress> Events { get; } = new();

        public RecordingProgress(Action<BuildPipeline.BuildProgress>? onReport = null)
            => _onReport = onReport;

        public void Report(BuildPipeline.BuildProgress value)
        {
            Events.Add(value);
            _onReport?.Invoke(value);
        }
    }
}
