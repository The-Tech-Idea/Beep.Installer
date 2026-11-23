using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>The MCP server must be reachable from the app itself: the default (guided) window and the advanced builder both offer it.</summary>
[Collection("Language")]
public sealed class McpEntryPointTests
{
    [Fact]
    public void GuidedWindow_HasAToolsMenuItemAndAHeaderButtonForTheMcpServer()
    {
        UiStaTest.Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var form = new BuilderWizardForm(controller);
            form.Show();

            form.MainMenuStrip.Should().NotBeNull("the default window needs a menu bar");
            var menuItem = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .SelectMany(m => m.DropDownItems.OfType<ToolStripMenuItem>()).Single(i => i.Name == "GuidedMenuMcp");
            menuItem.Text.Should().NotBeNullOrWhiteSpace();
            form.Controls.Find("GuidedMcp", true).OfType<Button>().Should().ContainSingle();
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void AdvancedBuilder_HasAnMcpProjectMenuItem()
    {
        UiStaTest.Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var form = new PackageBuilderForm(controller);
            form.Show();

            form.MainMenuStrip.Should().NotBeNull();
            var project = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(menu => menu.Name == "Menu_Project");
            project.DropDownItems.OfType<ToolStripMenuItem>().Select(item => item.Name)
                .Should().Contain("Toolbar_Mcp").And.Contain("Update_Title");
            controller.StopAutoSave();
        });
    }
}
