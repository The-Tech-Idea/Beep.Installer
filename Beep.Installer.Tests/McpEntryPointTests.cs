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
    public void AdvancedBuilder_HasAnMcpToolbarButton()
    {
        UiStaTest.Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var form = new PackageBuilderForm(controller);
            form.Show();

            static IEnumerable<Control> All(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(All));
            var toolbar = All(form).OfType<ToolStrip>().First(t => t.Items.OfType<ToolStripButton>().Any(b => b.Name == "Toolbar_Open" || b.Text == "Open"));
            var names = toolbar.Items.OfType<ToolStripButton>().Select(b => b.Name).ToList();
            names.Should().Contain("Toolbar_Mcp");
            // The occasional tools live in the More menu, so the MCP button is a top-level button beside Preview/Build
            // rather than behind the overflow chevron.
            names.Should().NotContain("Update_Title");
            names.IndexOf("Toolbar_Mcp").Should().BeGreaterThan(names.IndexOf("Builder_BuildTitle"));
            var more = toolbar.Items.OfType<ToolStripDropDownButton>().Single(b => b.Name == "Toolbar_More");
            more.DropDownItems.OfType<ToolStripButton>().Select(b => b.Name).Should().Contain("Update_Title");
            controller.StopAutoSave();
        });
    }
}
