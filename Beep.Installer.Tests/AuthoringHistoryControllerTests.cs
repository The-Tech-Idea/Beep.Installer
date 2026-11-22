using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AuthoringHistoryControllerTests
{
    private static InstallerController Controller()
    {
        var controller = new InstallerController(); controller.StopAutoSave(); return controller;
    }

    [Fact]
    public void NestedActionsRestoreAsOneChangeAndNotifyEditors()
    {
        var controller = Controller(); var id = controller.Project.AppId; var name = controller.Project.AppName;
        controller.RunAuthoringChange("Details", () =>
        {
            controller.Project.AppName = "Changed";
            controller.RunAuthoringChange("Version", () => controller.Project.AppVersion = "7.0.0");
            controller.Project.WindowsServices.Add(new() { Name = "Service" });
        });
        var reloads = 0; controller.ProjectReloaded += (_, _) => reloads++;
        controller.Undo().Should().NotBeNull();
        controller.Project.AppName.Should().Be(name); controller.Project.WindowsServices.Should().BeEmpty();
        controller.Project.AppId.Should().Be(id); controller.Project.IsDirty.Should().BeTrue();
        controller.CanUndo.Should().BeFalse(); controller.CanRedo.Should().BeTrue(); reloads.Should().Be(1);
        controller.Redo(); controller.Project.AppName.Should().Be("Changed");
        controller.Project.AppVersion.Should().Be("7.0.0"); controller.Project.WindowsServices.Should().ContainSingle();
    }

    [Fact]
    public void RecipeRestoresScopeSettingsAndIisPairTogether()
    {
        var controller = Controller(); var scope = controller.Project.DefaultScope;
        var preview = controller.ApplyResourceRecipe(new(ResourceRecipeKind.IisSite, "Site", "{app}\\app.exe", PoolName: "Pool", Port: 8080, UseMachineScope: true));
        preview.CanApply.Should().BeTrue();
        controller.Project.IisSites.Should().ContainSingle(); controller.Project.IisAppPools.Should().ContainSingle();
        controller.Undo(); controller.Project.IisSites.Should().BeEmpty(); controller.Project.IisAppPools.Should().BeEmpty();
        controller.Project.DefaultScope.Should().Be(scope); controller.CanUndo.Should().BeFalse();
        controller.Redo(); controller.Project.IisSites.Should().ContainSingle(); controller.Project.IisAppPools.Should().ContainSingle();
    }

    [Fact]
    public void SavePreservesHistoryAndDocumentIdentityButOpenAndNewResetIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "History_" + Guid.NewGuid().ToString("N") + ".bsetup");
        var controller = Controller();
        try
        {
            var original = controller.Project.AppName;
            controller.Project.AppName = "Saved change"; controller.SaveAs(path).ok.Should().BeTrue();
            controller.Undo(); controller.FilePath.Should().Be(path); controller.Project.AppName.Should().Be(original);
            controller.Project.IsDirty.Should().BeTrue(); controller.Redo(); controller.Project.AppName.Should().Be("Saved change");
            controller.Open(path, false).ok.Should().BeTrue(); controller.CanUndo.Should().BeFalse(); controller.CanRedo.Should().BeFalse();
            controller.Project.AppVersion = "8.0.0"; controller.CommitAuthoringChanges();
            controller.New("New", "1.0.0", "Publisher", ""); controller.CanUndo.Should().BeFalse();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PendingEditsCommitBeforeUndoAndNewEditsDiscardRedo()
    {
        var controller = Controller(); var name = controller.Project.AppName;
        controller.Project.AppName = "Pending"; controller.Undo(); controller.Project.AppName.Should().Be(name);
        controller.Project.AppVersion = "4.0.0"; controller.CommitAuthoringChanges();
        controller.CanRedo.Should().BeFalse(); controller.Undo(); controller.Project.AppVersion.Should().Be("1.0.0");
    }
}
