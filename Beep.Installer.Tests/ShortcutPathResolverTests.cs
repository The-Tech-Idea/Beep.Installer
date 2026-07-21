using System;
using System.IO;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Guards the shared shortcut-path resolver.
///
/// ShortcutCreateStep and UninstallStep previously each had their own copy and the copies
/// disagreed — the create side fell back to <c>InstallConfig.StartMenuFolder</c> and appended
/// ".lnk" only when missing; the remove side did neither. Both mismatches orphaned shortcuts
/// at uninstall, and neither copy honoured install scope.
/// </summary>
public class ShortcutPathResolverTests
{
    private static readonly InstallConfig Config = new() { StartMenuFolder = "Contoso" };

    [Fact]
    public void StartMenu_PerUser_UsesUserPrograms()
    {
        var path = ShortcutPathResolver.Resolve(
            new ShortcutDefinition { Name = "App", Location = ShortcutLocation.StartMenu },
            Config, perUser: true);

        path.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        path.Should().EndWith(Path.Combine("Contoso", "App.lnk"));
    }

    [Fact]
    public void StartMenu_PerMachine_UsesCommonPrograms()
    {
        var path = ShortcutPathResolver.Resolve(
            new ShortcutDefinition { Name = "App", Location = ShortcutLocation.StartMenu },
            Config, perUser: false);

        path.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
    }

    [Fact]
    public void Desktop_HonoursScope()
    {
        var shortcut = new ShortcutDefinition { Name = "App", Location = ShortcutLocation.Desktop };

        ShortcutPathResolver.Resolve(shortcut, Config, perUser: true)
            .Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        ShortcutPathResolver.Resolve(shortcut, Config, perUser: false)
            .Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    }

    [Fact]
    public void Startup_HonoursScope()
    {
        var shortcut = new ShortcutDefinition { Name = "App", Location = ShortcutLocation.Startup };

        ShortcutPathResolver.Resolve(shortcut, Config, perUser: true)
            .Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.Startup));

        ShortcutPathResolver.Resolve(shortcut, Config, perUser: false)
            .Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
    }

    [Fact]
    public void ExplicitSubfolder_WinsOverProductFolder()
    {
        var path = ShortcutPathResolver.Resolve(
            new ShortcutDefinition { Name = "App", Location = ShortcutLocation.StartMenu, StartMenuSubfolder = "Tools" },
            Config, perUser: true);

        path.Should().EndWith(Path.Combine("Tools", "App.lnk"));
    }

    [Fact]
    public void NameEndingInLnk_IsNotDoubled()
    {
        // The uninstall copy appended ".lnk" unconditionally, so "App.lnk" became "App.lnk.lnk"
        // and the shortcut was never found.
        var path = ShortcutPathResolver.Resolve(
            new ShortcutDefinition { Name = "App.lnk", Location = ShortcutLocation.Desktop },
            Config, perUser: true);

        path.Should().EndWith("App.lnk");
        path.Should().NotContain("App.lnk.lnk");
    }

    [Theory]
    [InlineData(ShortcutLocation.Desktop)]
    [InlineData(ShortcutLocation.StartMenu)]
    [InlineData(ShortcutLocation.Startup)]
    public void CreateAndRemove_ResolveIdentically(ShortcutLocation location)
    {
        // The property that actually matters: whatever the install writes, the uninstall must
        // look for in exactly the same place.
        var shortcut = new ShortcutDefinition { Name = "RoundTrip", Location = location };

        foreach (var perUser in new[] { true, false })
        {
            var created = ShortcutPathResolver.Resolve(shortcut, Config, perUser);
            var removed = ShortcutPathResolver.Resolve(shortcut, Config, perUser);
            removed.Should().Be(created);
        }
    }
}
