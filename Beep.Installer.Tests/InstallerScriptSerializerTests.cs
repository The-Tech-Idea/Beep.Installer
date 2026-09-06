using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallerScriptSerializerTests : IDisposable
{
    private readonly string _tempDir;

    public InstallerScriptSerializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepInstallerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_MissingAppIdDoesNotGenerateIdentity()
    {
        var path = Path.Combine(_tempDir, "missing-id.bsetup");
        File.WriteAllText(path, "[Setup]\nAppName=Unidentified\nAppVersion=1.0.0\n");
        InstallerScriptSerializer.Load(path).project!.AppId.Should().BeEmpty();
        InstallerScriptSerializer.Load(path).project!.AppId.Should().BeEmpty();
    }

    [Fact]
    public void AppId_RoundTripsAndSurvivesProductRename()
    {
        const string id = "A34321A2-680B-43A8-AF88-C56D6AFAB012";
        var project = InstallerProjectFactory.CreateNew("Identity", "1.0.0", "Publisher", "");
        project.AppId = id;
        var path = Path.Combine(_tempDir, "identity.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();
        var loaded = InstallerScriptSerializer.Load(path);
        loaded.error.Should().BeNull();
        loaded.project!.AppId.Should().Be(id);
        using var json = JsonDocument.Parse(ProjectCanonicalJsonExporter.ToJson(loaded.project));
        json.RootElement.GetProperty("appId").GetString().Should().Be(id);
        loaded.project.AppName = "Renamed product";
        var compiled = new InstallPlanCompiler().Compile(loaded.project);
        compiled.Success.Should().BeTrue();
        compiled.Plan!.AppId.Should().Be(id.ToLowerInvariant());
        loaded.project.AppId = "b34321a2-680b-43a8-af88-c56d6afab012";
        new InstallPlanCompiler().Compile(loaded.project).Plan!.PlanHash.Should().NotBe(compiled.Plan.PlanHash);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{a34321a2-680b-43a8-af88-c56d6afab012}")]
    public void AppId_RejectsMalformedAuthoredIdentity(string id)
    {
        var project = new InstallProject { AppId = id };
        var result = new InstallPlanCompiler().Compile(project);
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1160");
    }

    [Fact]
    public void CreateNew_PopulatesAllRequiredFields()
    {
        var p = InstallerProjectFactory.CreateNew("MyApp", "2.5.1", "ACME Inc", @"C:\src");

        p.ProjectName.Should().Be("MyApp");
        p.AppName.Should().Be("MyApp");
        p.AppVersion.Should().Be("2.5.1");
        p.AppPublisher.Should().Be("ACME Inc");
        p.SourceDirectory.Should().Be(@"C:\src");
        // Base name only — BuildPipeline appends the extension via EnsureExeFileName.
        p.OutputBaseFilename.Should().Be("Setup-MyApp-2.5.1");
        p.CompressPayload.Should().BeTrue();
        p.ArchitecturesAllowed.Should().Be(Architecture.X64Compatible); // model default
    }

    [Fact]
    public void CreateNew_HandlesNullInputsGracefully()
    {
        var p = InstallerProjectFactory.CreateNew("", "", "", "");

        p.AppName.Should().Be("MyApplication");
        p.AppVersion.Should().Be("1.0.0");
        p.AppPublisher.Should().Be("Publisher");
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var original = InstallerProjectFactory.CreateNew("RoundTrip", "3.1.4", "Tester", @"C:\build");
        original.Components.Add(new InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            SizeBytes = 1024 * 1024,
            Files = new() { new() { SourcePath = "a.exe", DestinationPath = "a.exe", Description = "a" } }
        });
        original.Shortcuts.Add(new ShortcutDefinition
        {
            Name = "App", TargetPath = "app.exe", Location = ShortcutLocation.StartMenu
        });

        var path = Path.Combine(_tempDir, "test.bsetup");
        var (ok, saveErr) = InstallerScriptSerializer.Save(original, path);
        ok.Should().BeTrue(saveErr);

        var (loaded, loadErr) = InstallerScriptSerializer.Load(path);
        loadErr.Should().BeNull();
        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("RoundTrip");
        loaded.AppName.Should().Be("RoundTrip");
        loaded.Components.Should().HaveCount(1);
        loaded.Components[0].Id.Should().Be("core");
        loaded.Components[0].Files.Should().HaveCount(1);
        loaded.Shortcuts.Should().HaveCount(1);
    }

    [Fact]
    public void CanonicalJsonExporter_EmitsDeterministicSchemaBackedProjectJson()
    {
        var project = InstallerProjectFactory.CreateNew("JsonApp", "2.0.0", "ACME", @"src\JsonApp");
        project.SchemaVersion = "";
        project.SourceIncludes.Clear();
        project.SourceIncludes.Add("bin/**");
        project.SourceExcludes.Add("**/*.pdb");
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = @"bin\JsonApp.exe",
                    DestinationPath = "JsonApp.exe",
                    IsRequired = true
                }
            }
        });
        ProjectSchemaService.NormalizeInMemory(project);

        var first = ProjectCanonicalJsonExporter.ToJson(project);
        var second = ProjectCanonicalJsonExporter.ToJson(project);

        first.Should().Be(second);
        using var json = JsonDocument.Parse(first);
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        json.RootElement.GetProperty("appName").GetString().Should().Be("JsonApp");
        json.RootElement.GetProperty("sourceIncludes")[0].GetString().Should().Be("bin/**");
        json.RootElement.GetProperty("sourceExcludes")[0].GetString().Should().Be("**/*.log");
        json.RootElement.GetProperty("sourceExcludes")[1].GetString().Should().Be("**/*.pdb");
        json.RootElement.GetProperty("licenseText").GetString().Should().Be("Enterprise license terms");
        json.RootElement.GetProperty("components")[0].GetProperty("id").GetString().Should().Be("core");
        json.RootElement.GetProperty("components")[0].GetProperty("files")[0].GetProperty("destinationPath").GetString()
            .Should().Be("JsonApp.exe");
        json.RootElement.TryGetProperty("hasCodeSigningCertificate", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("MyApp.bsetup", "MyApp.bsetup.golden.json")]
    [InlineData("ServiceApp.bsetup", "ServiceApp.bsetup.golden.json")]
    public void CanonicalJsonExporter_MatchesCheckedInGoldenFixture(string sampleName, string fixtureName)
    {
        var root = FindRepositoryRoot();
        var samplePath = Path.Combine(root, "Beep.Installer", "samples", sampleName);
        var fixturePath = Path.Combine(root, "Beep.Installer.Tests", "Fixtures", "CanonicalJson", fixtureName);

        var (project, loadError) = InstallerScriptSerializer.Load(samplePath);
        loadError.Should().BeNull();
        project.Should().NotBeNull();
        ProjectSchemaService.NormalizeInMemory(project!);

        var actual = ProjectCanonicalJsonExporter.ToJson(project!);
        var expected = File.ReadAllText(fixturePath);

        actual.Should().Be(expected);
        using var json = JsonDocument.Parse(actual);
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be(ProjectSchemaService.CurrentVersion);
        json.RootElement.TryGetProperty("webDeployPackages", out _).Should().BeTrue();
    }

    [Fact]
    public void Load_ReturnsErrorForMissingFile()
    {
        var (loaded, err) = InstallerScriptSerializer.Load(Path.Combine(_tempDir, "nope.bsetup"));
        loaded.Should().BeNull();
        err.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Load_ReturnsErrorForInvalidScript()
    {
        var path = Path.Combine(_tempDir, "broken.bsetup");
        File.WriteAllText(path, "AppName=NoSection");
        var (loaded, err) = InstallerScriptSerializer.Load(path);
        loaded.Should().BeNull();
        err.Should().Contain("before a section header");
    }

    [Fact]
    public void ReadScalar_ReturnsExpectedProperty()
    {
        var project = InstallerProjectFactory.CreateNew("Scalar", "1.0", "P", "C:\\");
        var path = Path.Combine(_tempDir, "scalar.bsetup");
        InstallerScriptSerializer.Save(project, path);

        InstallerScriptSerializer.ReadScalar(path, "product").Should().Be("Scalar");
        InstallerScriptSerializer.ReadScalar(path, "version").Should().Be("1.0");
        InstallerScriptSerializer.ReadScalar(path, "publisher").Should().Be("P");
        InstallerScriptSerializer.ReadScalar(path, "unknown-prop").Should().BeNull();
    }

    [Fact]
    public void Script_UsesBsetupSections()
    {
        var p = InstallerProjectFactory.CreateNew("CamelTest", "1.0", "P", "");
        var path = Path.Combine(_tempDir, "camel.bsetup");
        InstallerScriptSerializer.Save(p, path);
        var script = File.ReadAllText(path);
        script.Should().Contain("[Setup]");
        script.Should().Contain("ScriptName=CamelTest");
        script.Should().Contain("AppName=CamelTest");
        script.Should().Contain("CompressPayload=yes");
        script.Should().Contain("[Files]");
        script.Should().NotContain("\"InstallProject\"");
    }

    [Fact]
    public void Load_BsetupScript_PopulatesProject()
    {
        var path = Path.Combine(_tempDir, "app.bsetup");
        File.WriteAllText(path, """
            [Setup]
            ScriptName=InstallerScript
            AppName=ScriptApp
            AppVersion=4.2.0
            var pub=ACME
            SourceDir=C:\build\ScriptApp
            DefaultDirName={pf}\ScriptApp
            OutputBaseFilename=Setup-ScriptApp
            SingleFile=yes

            [Components]
            Name: "core"; Description: "Core files"; Required: yes

            [Files]
            Source: "bin\app.exe"; DestDir: "{app}"; Component: core

            [Icons]
            Name: "{group}\ScriptApp"; Filename: "{app}\app.exe"

            [Run]
            Filename: "{app}\post-install.bat"; Timing: AfterInstall; Flags: optional ignoreerrors
            """);

        var (project, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        project.Should().NotBeNull();
        project!.ProjectName.Should().Be("InstallerScript");
        project.AppName.Should().Be("ScriptApp");
        project.AppVersion.Should().Be("4.2.0");
        // The .bsetup loader normalizes this to an .exe filename (the factory does not —
        // an inconsistency worth reconciling, tracked in the plan).
        project.OutputBaseFilename.Should().Be("Setup-ScriptApp.exe");
        project.SingleFile.Should().BeTrue();
        project.Components.Should().ContainSingle(c => c.Id == "core");
        project.Components[0].Files.Should().ContainSingle(f => f.DestinationPath == "app.exe");
        project.Shortcuts.Should().ContainSingle();
        project.CustomActions.Should().ContainSingle(a => !a.Required && !a.FailOnError);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_AllBuilderTabs()
    {
        var original = InstallerProjectFactory.CreateNew("FullTabs", "5.6.7", "ACME", @"C:\src\FullTabs");
        original.ProjectName = "FullTabsScript";
        original.SourceIncludes.Clear();
        original.SourceIncludes.Add("bin/**");
        original.SourceExcludes.Clear();
        original.SourceExcludes.Add("**/*.tmp");
        foreach (var page in new[] { "Welcome", "Destination Folder", "Complete (launch / log)" })
            original.EnabledWizardPages.Add(page);

        original.AppSupportURL = "https://example.test/support";
        original.DefaultDirName = @"%ProgramFiles%\FullTabs";
        original.DefaultGroupName = @"ACME\FullTabs";
        original.PrivilegesRequired = PrivilegeLevel.Lowest;
        original.LicenseText = "Line1\nLine2";
        original.Components.Clear();
        original.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Description = "Core component",
            Required = true,
            Selected = true,
            IncludedIn = InstallationType.Typical,
            ConditionExpression = ConditionExpressionMode.Any,
            Conditions = new()
            {
                new() { Type = ConditionType.Architecture, Value = "x64", Operator = "==" }
            },
            Files = new()
            {
                new()
                {
                    SourcePath = @"bin\app.exe",
                    DestinationPath = "app.exe",
                    Description = "Main app",
                    IsRequired = true,
                    SharedCount = true
                }
            }
        });
        original.Prerequisites.Add(new Prerequisite
        {
            Id = "dotnet",
            Name = ".NET Runtime",
            VersionRequired = "10.0.0",
            DetectionCommand = "dotnet --list-runtimes",
            DetectionPattern = "Microsoft.NETCore.App 10.",
            DownloadUrl = "https://dot.net",
            SilentInstallArgs = "/quiet",
            IsMandatory = true,
            HelpUrl = "https://example.test/help"
        });
        original.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = @"catalogs\runtimes.json",
            Signature = "rsa-sha256:test-signature",
            TrustedPublicKeyPath = @"catalogs\runtimes.pub.pem",
            Required = true
        });
        original.Packages.Add(new PackageNodeDefinition
        {
            Id = "vc-redist",
            Name = "Microsoft Visual C++ Redistributable",
            PackageType = PackageNodeType.Exe,
            SourcePath = @"redist\vc_redist.x64.exe",
            Sha256 = new string('a', 64),
            DetectionCommand = "reg query HKLM\\Software\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64",
            DetectionCommandX86 = "reg query HKLM\\Software\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x86",
            DetectionPattern = "Installed",
            DetectionPatternX86 = "InstalledX86",
            InstallArgs = "/install /quiet /norestart",
            RepairArgs = "/repair /quiet /norestart",
            UninstallCommand = @"{InstallPath}\redist\vc_redist.x64.exe",
            UninstallArgs = "/uninstall /quiet /norestart",
            DependsOn = new() { "dotnet-hosting" },
            IsMandatory = true,
            RemoveOnUninstall = true,
            SuccessExitCodes = "0,3010",
            RebootExitCodes = "3010",
            TimeoutSeconds = 900,
            RetryCount = 2,
            HelpUrl = "https://example.test/vcredist"
        });
        original.Packages.Add(new PackageNodeDefinition
        {
            Id = "dotnet-hosting",
            Name = ".NET Hosting Bundle",
            PackageType = PackageNodeType.Msi,
            DownloadUrl = "https://example.test/dotnet-hosting.msi",
            InstallArgs = "/qn /norestart",
            IsMandatory = true
        });
        original.DeploymentSupersedence.Add(new DeploymentSupersedenceRule
        {
            PackageId = "TheTechIdea.FullTabs.Classic",
            DisplayName = "FullTabs Classic",
            MinimumVersion = "5.0.0",
            MaximumVersion = "5.5.0",
            Mode = DeploymentSupersedenceMode.Replace,
            UninstallPrevious = true,
            DetectionKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FullTabs Classic",
            Notes = "Replace old bootstrapper package."
        });
        original.Shortcuts.Add(new ShortcutDefinition
        {
            Name = "FullTabs",
            TargetPath = @"%InstallPath%\app.exe",
            Arguments = "--open",
            WorkingDirectory = "%InstallPath%",
            IconPath = @"%InstallPath%\app.ico",
            Location = ShortcutLocation.Desktop,
            StartMenuSubfolder = "ACME"
        });
        original.RegistryEntries.Add(new RegistryOperation
        {
            KeyPath = @"HKEY_CURRENT_USER\Software\ACME\FullTabs",
            ValueName = "InstallPath",
            Value = "%InstallPath%",
            ValueKind = RegistryValueKind.ExpandString
        });

        original.AppName = "FullTabs";
        original.WindowTitle = "FullTabs Setup";
        original.WelcomeTitle = "Welcome to FullTabs";
        original.AppPublisher = "ACME";
        original.AppPublisherURL = "https://example.test";
        original.AppSupportEmail = "support@example.test";
        original.WizardImageFile = @"assets\banner.png";
        original.SetupIconFile = @"assets\app.ico";
        original.SidebarBackgroundColor = "#101820";
        original.SidebarTextColor = "#F2AA4C";
        original.AccentColor = "#0057B8";
        original.ShowEula = false;
        original.AllowComponentSelection = false;
        original.AllowPathChange = true;
        original.DefaultTheme = WizardTheme.Compact;

        original.OutputDir = @"C:\release";
        original.OutputBaseFilename = "Setup-FullTabs.exe";
        original.OutputFormat = InstallerOutputFormat.Exe;
        original.MainExecutable = "app.exe";
        original.PayloadFolderName = "appPayload";
        original.WizardImageFile = @"assets\build-banner.png";
        original.LicenseFile = @"docs\eula.txt";
        original.PayloadSource = PayloadSourceType.Local;
        original.PayloadUrl = "https://example.test/payload.zip";
        original.AllowScopeSelection = false;
        original.DefaultScope = InstallationScope.User;
        original.ArchitecturesAllowed = Architecture.Arm64;
        original.Compression = CompressionFormat.Zip;
        original.CompressPayload = true;
        original.CompressionLevel = CompressionStrength.Maximum;
        original.SolidCompression = true;
        original.SingleFile = true;
        original.SelfContained = false;
        original.CreateUninstallEntry = false;
        original.CreateRestorePoint = false;
        original.CodeSignCertificatePath = @"certs\app.pfx";
        original.CodeSignCertificatePassword = "secret";
        original.CodeSignStoreName = "My";
        original.CodeSignStoreLocation = "LocalMachine";
        original.CodeSignStoreThumbprint = "AABBCCDDEEFF0011223344556677889900AABBCC";
        original.CodeSignStoreSubject = "CN=ACME Release";
        original.CodeSignRemoteProvider = "enterprise-hsm";
        original.CodeSignRemoteEndpoint = "https://signing.example.test/api/sign";
        original.CodeSignRemoteKeyId = "release-key";
        original.CodeSignRemoteCredential = "secret://env/REMOTE_SIGNING_TOKEN";
        original.CodeSignTimestampUrl = "https://timestamp.test";
        original.MsixIdentity = "ACME.FullTabs";
        original.MsixPublisher = "CN=ACME";
        original.AppUpdatesURL = "https://updates.example.test/acme/";
        original.AppUpdateMode = UpdateMode.Required;
        original.AppUpdateChannel = "stable";
        original.AppInstallerHoursBetweenUpdateChecks = 3;
        original.AppInstallerShowPrompt = false;
        original.AppInstallerForceUpdateFromAnyVersion = true;
        original.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "stable",
            Name = "Stable",
            Ring = "production",
            FeedUrl = "https://updates.example.test/acme/stable/",
            RolloutPercentage = 100,
            MinimumVersion = "1.2.0",
            DeadlineUtc = new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.Zero),
            Critical = true,
            MaintenanceWindow = "Sun 02:00-04:00 UTC",
            RollbackVersion = "1.1.0",
            Revoked = false
        });
        original.MsixOptionalPackages.Add(new MsixOptionalPackageDefinition
        {
            Name = "ACME.FullTabs.Tools",
            Publisher = "CN=ACME",
            Version = "1.2.3.0",
            Architecture = Architecture.X64,
            Uri = "https://updates.example.test/acme/ACME.FullTabs.Tools.msix",
            Kind = MsixRelatedPackageKind.Package
        });
        original.MsixOptionalPackages.Add(new MsixOptionalPackageDefinition
        {
            Name = "ACME.FullTabs.Extensions",
            Publisher = "CN=ACME",
            Version = "1.2.3.0",
            Uri = "https://updates.example.test/acme/ACME.FullTabs.Extensions.msixbundle",
            Kind = MsixRelatedPackageKind.Bundle
        });

        original.CustomActions.Add(new CustomAction
        {
            Path = @"{InstallPath}\post.bat",
            Arguments = "/silent",
            WorkingDirectory = "{InstallPath}",
            Description = "Post install",
            Timing = CustomActionTiming.AfterInstall,
            Order = 7,
            Required = false,
            FailOnError = false,
            TimeoutMs = 1234
        });
        original.CustomPages.Add(new CustomWizardPage
        {
            Id = "activation",
            Title = "Activation",
            Subtitle = "Enter license",
            Order = 3,
            Fields = new()
            {
                new()
                {
                    Id = "license",
                    Label = "License key",
                    Type = CustomFieldType.Text,
                    DefaultValue = "",
                    Required = true,
                    DestinationMacro = "license"
                },
                new()
                {
                    Id = "tier",
                    Label = "Tier",
                    Type = CustomFieldType.Radio,
                    DefaultValue = "Pro",
                    Options = new() { "Free", "Pro" }
                }
            }
        });

        var path = Path.Combine(_tempDir, "full-tabs.bsetup");
        InstallerScriptSerializer.Save(original, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("FullTabsScript");
        loaded.SourceDirectory.Should().Be(original.SourceDirectory);
        loaded.SourceIncludes.Should().Equal("bin/**");
        loaded.SourceExcludes.Should().Contain("**/*.tmp");
        loaded.EnabledWizardPages.Should().BeEquivalentTo(original.EnabledWizardPages);
        loaded.AppName.Should().Be("FullTabs");
        loaded.PrivilegesRequired.Should().Be(PrivilegeLevel.Lowest);
        loaded.LicenseText.Should().Be("Line1\nLine2");
        loaded.Components.Should().ContainSingle(c => c.Id == "core");
        loaded.Components[0].ConditionExpression.Should().Be(ConditionExpressionMode.Any);
        loaded.Components[0].Conditions.Should().ContainSingle(c => c.Type == ConditionType.Architecture && c.Value == "x64");
        loaded.Components[0].Files.Should().ContainSingle(f => f.SourcePath == @"bin\app.exe" && f.SharedCount);
        loaded.Prerequisites.Should().ContainSingle(p => p.Id == "dotnet" && p.SilentInstallArgs == "/quiet");
        loaded.PrerequisiteCatalogs.Should().ContainSingle(c =>
            c.Path == @"catalogs\runtimes.json"
            && c.Signature == "rsa-sha256:test-signature"
            && c.TrustedPublicKeyPath == @"catalogs\runtimes.pub.pem"
            && c.Required);
        loaded.Packages.Should().ContainSingle(p =>
            p.Id == "vc-redist"
            && p.PackageType == PackageNodeType.Exe
            && p.SourcePath == @"redist\vc_redist.x64.exe"
            && p.DetectionCommandX86 == "reg query HKLM\\Software\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x86"
            && p.DetectionPatternX86 == "InstalledX86"
            && p.DependsOn.Contains("dotnet-hosting")
            && p.RemoveOnUninstall
            && p.TimeoutSeconds == 900
            && p.RetryCount == 2);
        loaded.Packages.Should().ContainSingle(p =>
            p.Id == "dotnet-hosting"
            && p.PackageType == PackageNodeType.Msi
            && p.DownloadUrl == "https://example.test/dotnet-hosting.msi");
        loaded.DeploymentSupersedence.Should().ContainSingle(s =>
            s.PackageId == "TheTechIdea.FullTabs.Classic"
            && s.MaximumVersion == "5.5.0"
            && s.Mode == DeploymentSupersedenceMode.Replace
            && s.UninstallPrevious);
        loaded.Shortcuts.Should().ContainSingle(s => s.Location == ShortcutLocation.Desktop && s.Arguments == "--open");
        loaded.RegistryEntries.Should().ContainSingle(r => r.ValueKind == RegistryValueKind.ExpandString);
        loaded.DefaultTheme.Should().Be(WizardTheme.Compact);
        loaded.AllowComponentSelection.Should().BeFalse();
        loaded.OutputDir.Should().Be(@"C:\release");
        loaded.MainExecutable.Should().Be("app.exe");
        loaded.PayloadFolderName.Should().Be("appPayload");
        loaded.WizardImageFile.Should().Be(@"assets\build-banner.png");
        loaded.LicenseFile.Should().Be(@"docs\eula.txt");
        loaded.PayloadSource.Should().Be(PayloadSourceType.Local);
        loaded.PayloadUrl.Should().Be("https://example.test/payload.zip");
        loaded.AllowScopeSelection.Should().BeFalse();
        loaded.DefaultScope.Should().Be(InstallationScope.User);
        loaded.ArchitecturesAllowed.Should().Be(Architecture.Arm64);
        loaded.Compression.Should().Be(CompressionFormat.Zip);
        loaded.CompressPayload.Should().BeTrue();
        loaded.CompressionLevel.Should().Be(CompressionStrength.Maximum);
        loaded.SolidCompression.Should().BeTrue();
        loaded.SingleFile.Should().BeTrue();
        loaded.SelfContained.Should().BeFalse();
        loaded.CreateUninstallEntry.Should().BeFalse();
        loaded.CreateRestorePoint.Should().BeFalse();
        loaded.CodeSignCertificatePath.Should().Be(@"certs\app.pfx");
        loaded.CodeSignCertificatePassword.Should().Be("secret");
        loaded.CodeSignStoreName.Should().Be("My");
        loaded.CodeSignStoreLocation.Should().Be("LocalMachine");
        loaded.CodeSignStoreThumbprint.Should().Be("AABBCCDDEEFF0011223344556677889900AABBCC");
        loaded.CodeSignStoreSubject.Should().Be("CN=ACME Release");
        loaded.CodeSignRemoteProvider.Should().Be("enterprise-hsm");
        loaded.CodeSignRemoteEndpoint.Should().Be("https://signing.example.test/api/sign");
        loaded.CodeSignRemoteKeyId.Should().Be("release-key");
        loaded.CodeSignRemoteCredential.Should().Be("secret://env/REMOTE_SIGNING_TOKEN");
        loaded.CodeSignTimestampUrl.Should().Be("https://timestamp.test");
        loaded.MsixIdentity.Should().Be("ACME.FullTabs");
        loaded.MsixPublisher.Should().Be("CN=ACME");
        loaded.AppUpdatesURL.Should().Be("https://updates.example.test/acme/");
        loaded.AppUpdateMode.Should().Be(UpdateMode.Required);
        loaded.AppUpdateChannel.Should().Be("stable");
        loaded.AppInstallerHoursBetweenUpdateChecks.Should().Be(3);
        loaded.AppInstallerShowPrompt.Should().BeFalse();
        loaded.AppInstallerForceUpdateFromAnyVersion.Should().BeTrue();
        loaded.UpdateChannels.Should().ContainSingle(channel =>
            channel.Id == "stable"
            && channel.Ring == "production"
            && channel.FeedUrl == "https://updates.example.test/acme/stable/"
            && channel.RolloutPercentage == 100
            && channel.MinimumVersion == "1.2.0"
            && channel.DeadlineUtc == new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.Zero)
            && channel.Critical
            && channel.MaintenanceWindow == "Sun 02:00-04:00 UTC"
            && channel.RollbackVersion == "1.1.0"
            && !channel.Revoked);
        loaded.MsixOptionalPackages.Should().ContainSingle(p =>
            p.Name == "ACME.FullTabs.Tools"
            && p.Uri.EndsWith(".msix")
            && p.Kind == MsixRelatedPackageKind.Package
            && p.Architecture == Architecture.X64);
        loaded.MsixOptionalPackages.Should().ContainSingle(p =>
            p.Name == "ACME.FullTabs.Extensions"
            && p.Uri.EndsWith(".msixbundle")
            && p.Kind == MsixRelatedPackageKind.Bundle);
        loaded.CustomActions.Should().ContainSingle(a => a.Timing == CustomActionTiming.AfterInstall && a.TimeoutMs == 1234);
        loaded.CustomPages.Should().ContainSingle(p => p.Id == "activation");
        loaded.CustomPages[0].Fields.Should().HaveCount(2);
        loaded.CustomPages[0].Fields[1].Options.Should().Equal("Free", "Pro");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Beep.Installer"))
                && Directory.Exists(Path.Combine(dir.FullName, "Beep.Installer.Tests"))
                && Directory.Exists(Path.Combine(dir.FullName, "plans")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Beep.Installer repository root.");
    }
}

