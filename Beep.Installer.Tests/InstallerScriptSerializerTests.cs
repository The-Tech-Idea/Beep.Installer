using System;
using System.IO;
using System.Linq;
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
    public void CreateNew_PopulatesAllRequiredFields()
    {
        var p = InstallerProjectFactory.CreateNew("MyApp", "2.5.1", "ACME Inc", @"C:\src");

        p.ProjectName.Should().Be("MyApp");
        p.AppName.Should().Be("MyApp");
        p.AppVersion.Should().Be("2.5.1");
        p.AppPublisher.Should().Be("ACME Inc");
        p.SourceDirectory.Should().Be(@"C:\src");
        p.OutputBaseFilename.Should().Be("Setup-MyApp-2.5.1.exe");
        p.CompressPayload.Should().BeTrue();
        p.ArchitecturesAllowed.Should().Be(Architecture.X64);
    }

    [Fact]
    public void CreateNew_HandlesNullInputsGracefully()
    {
        var p = InstallerProjectFactory.CreateNew("", "", "", "");

        p.AppName.Should().Be("MyApplication");
        p.AppVersion.Should().Be("1.0.0");
        p.AppPublisher.Should().Be("AppPublisher");
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
        InstallerScriptSerializer.ReadScalar(path, "AppPublisher").Should().Be("P");
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
        original.EnabledWizardPages.AddRange(new[] { "Welcome", "Destination Folder", "Complete (launch / log)" });

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
            Conditions = new()
            {
                new() { Type = ConditionType.ArchitecturesAllowed, Value = Architecture.X64, Operator = "==" }
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
        original.var pub = "ACME";
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
        original.ArchitecturesAllowed = "arm64";
        original.Compression = CompressionFormat.Zip;
        original.CompressPayload = true;
        original.CompressionLevel = 9;
        original.SolidCompression = true;
        original.SingleFile = true;
        original.SelfContained = false;
        original.CreateUninstallEntry = false;
        original.CreateRestorePoint = false;
        original.CodeSignCertificatePath = @"certs\app.pfx";
        original.CodeSignCertificatePassword = "secret";
        original.CodeSignTimestampUrl = "https://timestamp.test";
        original.MsixIdentity = "ACME.FullTabs";
        original.MsixPublisher = "CN=ACME";

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
        loaded.Components[0].Conditions.Should().ContainSingle(c => c.Type == ConditionType.Architecture && c.Value == "x64");
        loaded.Components[0].Files.Should().ContainSingle(f => f.SourcePath == @"bin\app.exe" && f.SharedCount);
        loaded.Prerequisites.Should().ContainSingle(p => p.Id == "dotnet" && p.SilentInstallArgs == "/quiet");
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
        loaded.CodeSignTimestampUrl.Should().Be("https://timestamp.test");
        loaded.MsixIdentity.Should().Be("ACME.FullTabs");
        loaded.MsixPublisher.Should().Be("CN=ACME");
        loaded.CustomActions.Should().ContainSingle(a => a.Timing == CustomActionTiming.AfterInstall && a.TimeoutMs == 1234);
        loaded.CustomPages.Should().ContainSingle(p => p.Id == "activation");
        loaded.CustomPages[0].Fields.Should().HaveCount(2);
        loaded.CustomPages[0].Fields[1].Options.Should().Equal("Free", "Pro");
    }
}

