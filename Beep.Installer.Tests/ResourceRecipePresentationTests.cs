using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ResourceRecipePresentationTests
{
    private static T Field<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static InstallerController Controller()
    {
        var controller = new InstallerController(InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Publisher", Environment.CurrentDirectory));
        controller.StopAutoSave();
        return controller;
    }
    private static ResourceRecipeDialog Dialog(InstallerController controller, ResourceRecipeKind kind)
        => new(() => controller.Project, controller.ApplyResourceRecipe, kind, "{app}\\app.exe");
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { error = ex; }
            finally { LanguageManager.SetLanguage(language); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void PreviewAndCancelLeaveTheProjectUntouched()
    {
        RunSta(() =>
        {
            var controller = Controller();
            controller.Project.MarkClean();
            var before = InstallerScriptSerializer.Write(controller.Project);
            using var dialog = Dialog(controller, ResourceRecipeKind.IisSite);
            dialog.Show();
            Field<CheckBox>(dialog, "Recipe.UseMachineScope").Checked = true;
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeFalse();
            Field<Button>(dialog, "PreviewRecipe").PerformClick();
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeTrue();
            Field<TextBox>(dialog, "RecipeReview").Text.Should().Contain("IisAppPools[0]").And.Contain("IisSites[0]").And.Contain("Setup.AllowScopeSelection");
            Field<Label>(dialog, "RecipeLimits").Text.Should().Contain("previous definitions are not restored");
            dialog.Close();
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            controller.IsDirty.Should().BeFalse();
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void ApplyingPreviewCreatesTheIisPairAndPreservesProjectGraph()
    {
        RunSta(() =>
        {
            var controller = Controller();
            var original = controller.Project;
            using var dialog = Dialog(controller, ResourceRecipeKind.IisSite);
            dialog.Show();
            Field<CheckBox>(dialog, "Recipe.UseMachineScope").Checked = true;
            Field<Button>(dialog, "PreviewRecipe").PerformClick();
            Field<Button>(dialog, "ApplyRecipe").PerformClick();
            dialog.DialogResult.Should().Be(DialogResult.OK);
            controller.Project.Should().BeSameAs(original);
            original.IisSites.Should().ContainSingle();
            original.IisAppPools.Should().ContainSingle(pool => pool.Name == original.IisSites[0].ApplicationPool);
            original.AllowScopeSelection.Should().BeFalse();
            controller.IsDirty.Should().BeTrue();
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void InvalidTimeBlocksApplyAndEditsInvalidateAnEarlierPreview()
    {
        RunSta(() =>
        {
            var controller = Controller();
            using var dialog = Dialog(controller, ResourceRecipeKind.ScheduledTask);
            dialog.Show();
            Field<CheckBox>(dialog, "Recipe.UseMachineScope").Checked = true;
            Field<ComboBox>(dialog, "Recipe.Trigger").SelectedItem = ScheduledTaskTrigger.Daily;
            Field<TextBox>(dialog, "Recipe.StartTime").Text = "25:90";
            Field<Button>(dialog, "PreviewRecipe").PerformClick();
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeFalse();
            Field<TextBox>(dialog, "RecipeReview").Text.Should().Contain("BI1604");
            Field<TextBox>(dialog, "Recipe.StartTime").Text = "10:15";
            Field<Button>(dialog, "PreviewRecipe").PerformClick();
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeTrue();
            Field<TextBox>(dialog, "Recipe.Name").Text = "Changed name";
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeFalse();
            controller.Project.ScheduledTasks.Should().BeEmpty();
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void ChangedProjectCannotApplyAStaleIisPreview()
    {
        RunSta(() =>
        {
            var controller = Controller();
            using var dialog = Dialog(controller, ResourceRecipeKind.IisSite);
            dialog.Show();
            Field<CheckBox>(dialog, "Recipe.UseMachineScope").Checked = true;
            Field<Button>(dialog, "PreviewRecipe").PerformClick();
            controller.Project.IisAppPools.Add(new IisAppPoolDefinition { Name = dialog.CurrentRequest.PoolName });
            Field<Button>(dialog, "ApplyRecipe").PerformClick();
            dialog.DialogResult.Should().NotBe(DialogResult.OK);
            Field<Button>(dialog, "ApplyRecipe").Enabled.Should().BeFalse();
            controller.Project.IisSites.Should().BeEmpty();
            controller.Project.IisAppPools.Should().ContainSingle();
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void LanguageSwitchPreservesTheUncommittedRecipeAndTranslatesItsLimits()
    {
        RunSta(() =>
        {
            var controller = Controller();
            using var dialog = Dialog(controller, ResourceRecipeKind.IisSite);
            dialog.Show();
            Field<TextBox>(dialog, "Recipe.Name").Text = "Draft site";
            Field<TextBox>(dialog, "Recipe.PoolName").Text = "Draft pool";
            Field<CheckBox>(dialog, "Recipe.UseMachineScope").Checked = true;
            var request = dialog.CurrentRequest;
            LanguageManager.SetLanguage("ar");
            dialog.CurrentRequest.Should().Be(request);
            dialog.RightToLeftLayout.Should().BeTrue();
            Field<Label>(dialog, "RecipeLimits").Text.Should().Be(LanguageManager.GetString("Recipe_IisSiteLimits"));
            LanguageManager.SetLanguage("en");
            dialog.CurrentRequest.Should().Be(request);
            dialog.RightToLeftLayout.Should().BeFalse();
            controller.Project.IisSites.Should().BeEmpty();
            controller.StopAutoSave();
        });
    }

    [Theory]
    [InlineData("windowsservices")]
    [InlineData("scheduledtasks")]
    [InlineData("firewallrules")]
    [InlineData("iisapppools")]
    [InlineData("iissites")]
    public void RelevantResourceEditorsExposeRecipeAction(string section)
    {
        RunSta(() =>
        {
            var controller = Controller();
            using var editor = new PackageBuilderForm(controller);
            editor.OpenSection(section);
            editor.Show();
            Field<Button>(editor, "ResourceRecipeButton").AccessibleName.Should().Be("Resource recipes");
            controller.StopAutoSave();
        });
    }
}
