using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Beep.Installer.Tests;

/// <remarks>Drives the actual composed Main/Dispatch/Application.Run path, with an isolated recent store and inert projects.</remarks>
[Collection("Language")]
public sealed class ColdStartEntryTests
{
    [Fact]
    public void RecentStoreScopesNestWithoutReplacingEitherOwnedStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepRecentScope_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var first = Path.Combine(root, "first.json"); var second = Path.Combine(root, "second.json");
        try
        {
            using (RecentProjects.UseStoreFile(first))
            {
                RecentProjects.Record("first-owned.bsetup");
                using (RecentProjects.UseStoreFile(second)) { RecentProjects.Record("second-owned.bsetup"); RecentProjects.Load().Should().ContainSingle(e => e.Path == "second-owned.bsetup"); }
                RecentProjects.Record("third-owned.bsetup"); RecentProjects.Load().Select(e => e.Path).Should().Equal("third-owned.bsetup", "first-owned.bsetup");
            }
            File.ReadAllText(second).Should().Contain("second-owned.bsetup").And.NotContain("third-owned.bsetup");
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }

    [Theory]
    [InlineData("fresh")]
    [InlineData("recent")]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public void ComposedEntryChoosesGuidedOrReopensRecentUsingItsRegisteredController(string scenario)
    {
        UiStaTest.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepColdStart_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var recentFile = Path.Combine(root, "recent.json"); var projectFile = Path.Combine(root, "owned.bsetup");
            var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName; var closePrompts = 0;
            Exception? failure = null; var observed = false;
            try
            {
                using var recent = RecentProjects.UseStoreFile(recentFile);
                if (scenario == "recent")
                {
                    var project = InstallerProjectFactory.CreateNew("Owned returning project", "7.2.0", "Owned publisher", "");
                    InstallerScriptSerializer.Save(project, projectFile).ok.Should().BeTrue();
                }
                if (scenario == "unreadable") File.WriteAllText(projectFile, "[Setup]\nAppName=Owned locked project\n");
                using var lockedProject = scenario == "unreadable" ? new FileStream(projectFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
                if (scenario != "fresh") RecentProjects.Save(new() { new() { Path = projectFile } });
                var savedBytes = scenario == "recent" ? File.ReadAllBytes(projectFile) : null;
                File.Exists(Path.Combine(AppContext.BaseDirectory, "script.bsetup")).Should().BeFalse("this host must never enter installer runtime mode");
                EmbeddedInstallerResources.Has("script.bsetup").Should().BeFalse();
                LanguageManager.SetLanguage("en");
                using var timer = new System.Windows.Forms.Timer { Interval = 30 };
                timer.Tick += (_, _) =>
                {
                    var window = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Visible && form is BuilderWizardForm or PackageBuilderForm);
                    if (window is null) return; timer.Stop(); observed = true;
                    var controller = Program.Services!.GetRequiredService<InstallerController>();
                    if (window is BuilderWizardForm ownedWizard) ownedWizard.ConfirmClose = (_, _, _) => { closePrompts++; return DialogResult.No; };
                    try
                    {
                        if (scenario == "recent")
                        {
                            window.Should().BeOfType<PackageBuilderForm>();
                            typeof(PackageBuilderForm).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window).Should().BeSameAs(controller);
                            controller.FilePath.Should().Be(projectFile);
                            controller.Project.AppName.Should().Be("Owned returning project"); controller.Project.AppVersion.Should().Be("7.2.0");
                            File.ReadAllBytes(projectFile).Should().Equal(savedBytes!);
                        }
                        else
                        {
                            window.Should().BeOfType<BuilderWizardForm>(); controller.HasFilePath.Should().BeFalse();
                            var wizard = (BuilderWizardForm)window;
                            typeof(PackageBuilderForm).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wizard.AdvancedForm).Should().BeSameAs(controller);
                        }
                    }
                    catch (Exception ex) { failure = ex; Diag.Debug("ColdStartEntryTests", "Owned startup assertion failed", ex); }
                    finally { controller.StopAutoSave(); window.Close(); }
                };
                timer.Start(); Program.Main(Array.Empty<string>()).Should().Be(0); observed.Should().BeTrue(); closePrompts.Should().Be(0);
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                if (scenario == "recent") RecentProjects.Load().Should().ContainSingle(entry => entry.Path == projectFile);
            }
            finally
            {
                LanguageManager.SetLanguage(culture);
                foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
                Directory.Delete(root);
            }
        });
    }
}
