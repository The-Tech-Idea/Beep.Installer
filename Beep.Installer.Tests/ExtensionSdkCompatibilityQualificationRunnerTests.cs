using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Extensibility;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ExtensionSdkCompatibilityQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public ExtensionSdkCompatibilityQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beep-extension-sdk-compatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Run_UsesCanonicalConformanceAcrossEngineVersionMatrix()
    {
        var extensionDir = CreateExtension("beep.compat.provider", minimumEngineVersion: "1.0.0");

        var report = new ExtensionSdkCompatibilityQualificationRunner().Run(new ExtensionSdkCompatibilityQualificationOptions
        {
            ExtensionDirectories = new[] { extensionDir },
            EngineVersions = new[] { "1.0.0", "1.1.0" },
            OutputDirectory = _tempDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine, report.Scenarios.SelectMany(s => s.Diagnostics).Select(d => d.Message)));
        report.ExitCode.Should().Be(0);
        report.EngineVersions.Should().Equal("1.0.0", "1.1.0");
        report.Scenarios.Should().HaveCount(2).And.OnlyContain(s => s.Success);
        report.Scenarios.Should().OnlyContain(s => s.ExtensionCount == 1 && s.ProviderCount == 1);
        File.Exists(report.ReportPath).Should().BeTrue();
        report.Scenarios.Should().OnlyContain(s => File.Exists(s.EvidencePath));
    }

    [Fact]
    public void Run_FailsClosedWhenDeclaredEngineMatrixIncludesIncompatibleVersion()
    {
        var extensionDir = CreateExtension("beep.compat.future", minimumEngineVersion: "1.1.0");

        var report = new ExtensionSdkCompatibilityQualificationRunner().Run(new ExtensionSdkCompatibilityQualificationOptions
        {
            ExtensionDirectories = new[] { extensionDir },
            EngineVersions = new[] { "1.0.0", "1.1.0" },
            OutputDirectory = _tempDir
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Single(s => s.EngineVersion == "1.0.0").Diagnostics.Should().Contain(d => d.Code == "BI4013");
        report.Scenarios.Single(s => s.EngineVersion == "1.1.0").Success.Should().BeTrue();
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
            // Extension assemblies are loaded into the process during conformance checks and
            // can stay locked on Windows until the test host exits.
        }
    }

    private string CreateExtension(string id, string minimumEngineVersion)
    {
        var extensionDir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(extensionDir);
        var assemblyPath = BuildProviderAssembly(extensionDir);
        var manifest = new InstallerExtensionManifest
        {
            Id = id,
            Publisher = "The Tech Idea",
            Version = "1.0.0",
            MinimumEngineVersion = minimumEngineVersion,
            EntryAssembly = "CompatibilityProvider.dll",
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant(),
            ResourceTypes = new() { "compat.resource" },
            Permissions = InstallerExtensionPermission.None
        };
        File.WriteAllText(
            Path.Combine(extensionDir, InstallerExtensionDiscovery.ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return extensionDir;
    }

    private static string BuildProviderAssembly(string extensionDir)
    {
        var projectDir = Path.Combine(extensionDir, "src");
        Directory.CreateDirectory(projectDir);
        var coreProject = LocateRepoFile("Beep.Installer.Core", "Beep.Installer.Core.csproj");
        File.WriteAllText(Path.Combine(projectDir, "CompatibilityProvider.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>CompatibilityProvider</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{coreProject}}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, "Provider.cs"), """
            using Beep.Installer.Engine;
            using Beep.Installer.Extensibility;

            namespace CompatibilityProvider;

            public sealed class CompatibilityResourceProvider : IResourceProvider
            {
                public string ResourceType => "compat.resource";
                public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;
                public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context) => new();
                public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context) => new();
                public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
                    => new() { ChangeKind = ResourceChangeKind.Create, Operations = new() { operation } };
                public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context) => new();
                public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context) => new();
                public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context) => new();
            }
            """);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build CompatibilityProvider.csproj --nologo --verbosity quiet -nodeReuse:false",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // -nodeReuse:false on the command line is what actually stops the build leaving a full set
        // of MSBuild worker nodes running for 15 minutes at a few hundred MB each; the .NET CLI
        // passes an explicit /nodeReuse:true, which overrides the environment variable. Across a
        // suite that spawns several of these it was enough to get the run killed for memory.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet build could not be started.");

        // Start draining before waiting. Reading only after WaitForExit deadlocks the moment the
        // build writes more than the pipe buffer holds, because nothing is emptying it.
        var buildOutput = process.StandardOutput.ReadToEndAsync();
        var buildError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(buildOutput.Result + buildError.Result);

        var builtAssembly = Path.Combine(projectDir, "bin", "Debug", "net10.0", "CompatibilityProvider.dll");
        var extensionAssembly = Path.Combine(extensionDir, "CompatibilityProvider.dll");
        File.Copy(builtAssembly, extensionAssembly, overwrite: true);
        foreach (var dependency in Directory.GetFiles(Path.GetDirectoryName(builtAssembly)!, "*.dll"))
        {
            var target = Path.Combine(extensionDir, Path.GetFileName(dependency));
            if (!File.Exists(target))
                File.Copy(dependency, target);
        }

        return extensionAssembly;
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
