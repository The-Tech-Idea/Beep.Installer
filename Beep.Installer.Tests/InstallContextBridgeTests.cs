using System.Collections.Generic;
using System.Collections.ObjectModel;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Guards the authoring→runtime contract bridge.
///
/// The installer authors an <see cref="InstallProject"/>, but every BeepDM step reads an
/// <see cref="InstallConfig"/> from <c>SetupContext</c>. That mismatch previously made every
/// install abort at step validation, and it was invisible because
/// <c>SetupContext.TryGetProperty&lt;T&gt;</c> returns <c>value as T</c> — a missing key and a
/// wrong-typed value both yield null. These tests assert the projection and the context keys
/// directly so the failure can never be silent again.
/// </summary>
public class InstallContextBridgeTests
{
    private static InstallProject MakeProject() => new()
    {
        AppName = "Contoso Suite",
        AppVersion = "2.3.4",
        AppPublisher = "Contoso Ltd",
        DefaultDirName = @"%ProgramFiles%\Contoso",
        DefaultGroupName = @"Contoso\Suite",
        AppSupportURL = "https://support.example.test",
        AppUpdatesURL = "https://updates.example.test/feed.json",
        AppUpdateMode = UpdateMode.Required,
        DefaultInstallType = InstallationType.Complete,
        PrivilegesRequired = PrivilegeLevel.Admin,
        Prefer64Bit = true,
        LicenseText = "EULA text",
        WizardImageFile = @"assets\banner.png",
        SetupIconFile = @"assets\app.ico",
        SelfContained = false,
        PayloadFolderName = "bits",
        Compression = CompressionFormat.Lzma2,
        Components = new ObservableCollection<InstallComponent>
        {
            new() { Id = "core", Name = "Core", Required = true, Selected = true }
        },
        Prerequisites = new ObservableCollection<Prerequisite>
        {
            new() { Id = "dotnet", Name = ".NET", VersionRequired = "10.0" }
        },
        Shortcuts = new ObservableCollection<ShortcutDefinition>
        {
            new() { Name = "Contoso", TargetPath = "app.exe" }
        },
        RegistryEntries = new ObservableCollection<RegistryOperation>
        {
            new() { KeyPath = @"Software\Contoso", ValueName = "Installed", Value = "1" }
        },
    };

    [Fact]
    public void Projection_PreservesAppIdThroughRuntimeSerializationAndRegistration()
    {
        var project = MakeProject();
        project.AppId = System.Guid.NewGuid().ToString("D");
        project.AppName = "BeepIdentityBridge_" + System.Guid.NewGuid().ToString("N");
        var config = InstallConfigProjector.ToInstallConfig(project);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<InstallConfig>(
            System.Text.Json.JsonSerializer.Serialize(config))!;
        roundTrip.AppId.Should().Be(project.AppId);
        var path = UpgradeEngine.RegistrationKeyPath(project.AppId);
        try
        {
            new UpgradeEngine().RegisterInstall(roundTrip, System.IO.Path.GetTempPath(), Microsoft.Win32.Registry.CurrentUser);
            using var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
            registration.Should().NotBeNull();
            registration!.GetValue("AppId").Should().Be(project.AppId);
            registration.GetValue("Publisher").Should().Be(project.AppPublisher);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Projection_MapsEveryLoadBearingField()
    {
        var config = InstallConfigProjector.ToInstallConfig(MakeProject());

        // Identity — used by the manifest, restore point and custom-action macros.
        config.ProductName.Should().Be("Contoso Suite");
        config.ProductVersion.Should().Be("2.3.4");
        config.Publisher.Should().Be("Contoso Ltd");

        // Layout.
        config.DefaultInstallPath.Should().Be(@"%ProgramFiles%\Contoso");
        config.StartMenuFolder.Should().Be(@"Contoso\Suite");
        config.RequireAdminPrivileges.Should().BeTrue();

        // Load-bearing: selects the registry view for the registry/COM/shared-file steps.
        config.Prefer64Bit.Should().BeTrue();

        // Enums map across the duplicate authoring enums.
        config.DefaultInstallType.Should().Be(InstallationType.Complete);
        config.UpdateMode.Should().Be(UpdateMode.Required);

        // Presentation carried so a shipped install-config.json is complete.
        config.LicenseText.Should().Be("EULA text");
        config.BannerImagePath.Should().Be(@"assets\banner.png");
        config.ProductIconPath.Should().Be(@"assets\app.ico");
        config.SupportUrl.Should().Be("https://support.example.test");
        config.UpdateUrl.Should().Be("https://updates.example.test/feed.json");

        config.SchemaVersion.Should().Be(InstallConfig.CurrentSchemaVersion);
    }

    [Fact]
    public void Projection_CarriesCollections_WithoutCopyingElements()
    {
        var project = MakeProject();
        var config = InstallConfigProjector.ToInstallConfig(project);

        config.Components.Should().HaveCount(1);
        config.Prerequisites.Should().HaveCount(1);
        config.Shortcuts.Should().HaveCount(1);
        config.RegistryEntries.Should().HaveCount(1);

        // Elements are shared by reference on purpose: the wizard toggles
        // InstallComponent.Selected on the project and the steps must observe it.
        config.Components[0].Should().BeSameAs(project.Components[0]);

        project.Components[0].Selected = false;
        config.Components[0].Selected.Should().BeFalse();
    }

    [Fact]
    public void Builder_SuppliesEveryKeyTheStepsRequire()
    {
        var context = InstallContextBuilder.ForInstall(
            MakeProject(), @"C:\Program Files\Contoso", perUser: false);

        InstallContextBuilder.FindMissingRequiredKeys(context)
            .Should().BeEmpty("every BeepDM step requirement must be satisfied by the builder");

        context.TryGetProperty<InstallConfig>("InstallConfig").Should().NotBeNull();
        context.TryGetProperty<string>("InstallPath").Should().Be(@"C:\Program Files\Contoso");
    }

    [Fact]
    public void Builder_StoresValueTypeFlags_AsBoxedBooleans()
    {
        // PerUser and IsSelfContained are read with Properties.TryGetValue + pattern match,
        // never TryGetProperty<T> (which is class-constrained). Storing them as anything
        // other than a boxed bool makes the steps silently fall back to their defaults —
        // for PerUser that means writing every registry key to HKLM.
        var context = InstallContextBuilder.ForInstall(MakeProject(), @"C:\App", perUser: true);

        context.Properties.TryGetValue("PerUser", out var perUser).Should().BeTrue();
        perUser.Should().BeOfType<bool>().And.Be(true);

        context.Properties.TryGetValue("IsSelfContained", out var selfContained).Should().BeTrue();
        selfContained.Should().BeOfType<bool>().And.Be(false); // project sets SelfContained = false
    }

    [Fact]
    public void Builder_CarriesPayloadHints_PreviouslyHeldInAGlobalStatic()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject(), @"C:\App", perUser: true);

        context.TryGetProperty<string>("PayloadFolderName").Should().Be("bits");
        context.TryGetProperty<string>("PayloadCompression").Should().Be("lzma2");
    }

    [Fact]
    public void Builder_OmitsPayloadUrl_WhenSourceIsLocal()
    {
        var project = MakeProject();
        project.PayloadSource = PayloadSourceType.Local;
        project.PayloadUrl = "https://example.test/payload.zip";

        var context = InstallContextBuilder.ForInstall(project, @"C:\App", perUser: true);

        // A local-payload project must not trigger the download step.
        context.TryGetProperty<string>("PayloadUrl").Should().BeNull();
    }

    [Fact]
    public void Builder_SuppliesPayloadUrl_WhenSourceIsUrl()
    {
        var project = MakeProject();
        project.PayloadSource = PayloadSourceType.Url;
        project.PayloadUrl = "https://example.test/payload.zip";

        var context = InstallContextBuilder.ForInstall(project, @"C:\App", perUser: true);

        context.TryGetProperty<string>("PayloadUrl").Should().Be("https://example.test/payload.zip");
    }

    [Fact]
    public void Builder_PassesCustomActionsAndValues()
    {
        var project = MakeProject();
        project.CustomActions.Add(new CustomAction { Path = "post.exe", Timing = CustomActionTiming.AfterInstall });

        var values = new Dictionary<string, string> { ["license"] = "KEY-1" };
        var context = InstallContextBuilder.ForInstall(project, @"C:\App", perUser: true, customValues: values);

        context.TryGetProperty<List<CustomAction>>("CustomActions").Should().HaveCount(1);
        context.TryGetProperty<Dictionary<string, string>>("CustomValues")!["license"].Should().Be("KEY-1");
    }

    [Fact]
    public void FindMissingRequiredKeys_NamesWhatIsMissing()
    {
        // An empty context is exactly the state that used to abort every install silently.
        var empty = new TheTechIdea.Beep.SetUp.SetupContext();

        var missing = InstallContextBuilder.FindMissingRequiredKeys(empty);

        missing.Should().HaveCount(4);
        missing.Should().Contain(m => m.Contains("InstallConfig"));
        missing.Should().Contain(m => m.Contains("InstallPath"));
        missing.Should().Contain(m => m.Contains("PerUser"));
        missing.Should().Contain(m => m.Contains("IsSelfContained"));
    }

    [Fact]
    public void ForUninstall_SuppliesConfigAndScope()
    {
        var context = InstallContextBuilder.ForUninstall(MakeProject(), @"C:\App", perUser: false);

        // UninstallStep reads the manifest from disk, but still needs the config for scope.
        context.TryGetProperty<InstallConfig>("InstallConfig").Should().NotBeNull();
        context.TryGetProperty<string>("InstallPath").Should().Be(@"C:\App");
        context.Properties.TryGetValue("PerUser", out var perUser).Should().BeTrue();
        perUser.Should().Be(false);
    }
}
