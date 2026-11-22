using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Pages;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using TheTechIdea.Beep.Installer.Steps;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class WizardPreviewControlsTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Find<T>(Control owner, string name) where T : Control => owner.Controls.Find(name, true).OfType<T>().Single();
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(language); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public void PageSelectionScaleAndResetUseAnIndependentCapturedProject()
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Author", "1.0.0", "Publisher", ""); project.MarkClean();
            using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
            var context = Field<InstallContext>(preview.HostedWizard, "_ctx"); context.PreviewOnly.Should().BeTrue();
            context.Project.Should().NotBeSameAs(project); context.Project.Components.Single().Name = "Preview edit";
            project.Components.Single().Name.Should().Be("Core"); project.IsDirty.Should().BeFalse();
            var pages = Find<ComboBox>(preview, "PreviewPage"); pages.Items.Count.Should().Be(preview.HostedWizard.PreviewPageTitles.Count);
            for (var index = 0; index < pages.Items.Count; index++) { pages.SelectedIndex = index; preview.HostedWizard.PreviewCurrentPage.Should().Be(index); }
            Field<bool>(preview.HostedWizard, "_installComplete").Should().BeFalse();
            var scale = Find<ComboBox>(preview, "PreviewScale"); scale.SelectedIndex = 3;
            preview.HostedWizard.Width.Should().Be(1960); Find<Panel>(preview, "PreviewViewport").AutoScrollMinSize.Width.Should().Be(1960);
            scale.SelectedIndex = 0; preview.HostedWizard.Width.Should().Be(980);
            Find<Button>(preview, "PreviewReset").PerformClick();
            Field<InstallContext>(preview.HostedWizard, "_ctx").Project.Components.Single().Name.Should().Be("Core");
            preview.HostedWizard.PreviewCurrentPage.Should().Be(0); project.IsDirty.Should().BeFalse();
        });
    }
    [Fact]
    public void LiveLanguageChangesKeepPageAndScaleAndDisposeSafely()
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Preview", "1.0.0", "Publisher", "");
            using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
            var pages = Find<ComboBox>(preview, "PreviewPage"); pages.SelectedIndex = pages.Items.Count - 1;
            Find<ComboBox>(preview, "PreviewScale").SelectedIndex = 1;
            LanguageManager.SetLanguage("ar"); Application.DoEvents();
            preview.RightToLeft.Should().Be(RightToLeft.Yes); preview.HostedWizard.PreviewCurrentPage.Should().Be(pages.Items.Count - 1);
            Find<ComboBox>(preview, "PreviewScale").SelectedIndex.Should().Be(1);
            preview.Dispose(); Action translate = () => LanguageManager.SetLanguage("de"); translate.Should().NotThrow();
        });
    }
    [Fact]
    public void PreviewSelectionApiCannotNavigateARealInstaller()
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Runtime", "1.0.0", "Publisher", "");
            using var runtime = new BeepModernInstallerForm(project, previewMode: false);
            var current = runtime.PreviewCurrentPage; runtime.SelectPreviewPage(1).Should().BeFalse(); runtime.PreviewCurrentPage.Should().Be(current);
        });
    }
    [Fact]
    public void FinishingASelectedCompletionPageClosesThePreviewOwner()
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Author", "1.0.0", "Publisher", ""); project.MarkClean();
            using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
            preview.HostedWizard.SelectPreviewPage(preview.HostedWizard.PreviewPageTitles.Count - 2).Should().BeTrue();
            preview.HostedWizard.GetType().GetMethod("OnNextClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(preview.HostedWizard, new object[] { null!, EventArgs.Empty });
            Application.DoEvents(); preview.Visible.Should().BeFalse(); project.IsDirty.Should().BeFalse();
        });
    }
    [Fact]
    public void PreviewCaptionsAreEmbeddedInEveryLanguageWithMatchingArguments()
    {
        var keys = new[] { "Preview_Title", "Preview_LayoutHint", "Preview_Page", "Preview_Scale", "Preview_Reset", "Preview_SimulatedFailure", "Preview_PrerequisitesNotChecked" };
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>().Where(entry => keys.Contains((string)entry.Key))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
        var english = Read("en"); english.Keys.Should().BeEquivalentTo(keys);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(keys);
            foreach (var key in keys)
            {
                translated[key].Should().NotBeNullOrWhiteSpace();
                System.Text.CompositeFormat.Parse(translated[key]).MinimumArgumentCount.Should().Be(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount);
            }
        }
    }
    [Fact]
    public void ResetPreservesScaleAndLiveCaptionsFollowTheRecreatedWizardInEveryLanguage()
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Captured name", "1.0.0", "Publisher", "");
            using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
            Find<ComboBox>(preview, "PreviewScale").SelectedIndex = 2; Find<Button>(preview, "PreviewReset").PerformClick();
            var pages = Find<ComboBox>(preview, "PreviewPage"); pages.SelectedIndex = pages.Items.Count - 1;
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                preview.Text.Should().Be(string.Format(LanguageManager.GetString("Preview_Title"), "Captured name"));
                pages.AccessibleName.Should().Be(LanguageManager.GetString("Preview_Page"));
                Find<ComboBox>(preview, "PreviewScale").AccessibleName.Should().Be(LanguageManager.GetString("Preview_Scale"));
                Find<Button>(preview, "PreviewReset").Text.Should().Be(LanguageManager.GetString("Preview_Reset"));
                pages.Items.Cast<string>().Should().Equal(preview.HostedWizard.PreviewPageTitles);
                Find<TextBox>(preview, "RuntimeFailureSummary").Text.Should().Contain(LanguageManager.GetString("Preview_SimulatedFailure"));
                preview.HostedWizard.PreviewCurrentPage.Should().Be(pages.Items.Count - 1);
                Find<ComboBox>(preview, "PreviewScale").SelectedIndex.Should().Be(2);
            }
        });
    }
    [Fact]
    public void PreviewDoesNotRunAuthoredPrerequisiteCommandsOrDownloadActions()
    {
        var marker = Path.Combine(Path.GetTempPath(), "BeepPreview-" + Guid.NewGuid().ToString("N") + ".marker");
        try
        {
            Sta(() =>
            {
                var project = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Preview", "1.0.0", "Publisher", "");
                project.Prerequisites.Add(new() { Id = "preview-check", Name = "Authored check", IsMandatory = true,
                    DetectionCommand = "cmd /c echo marker > \"" + marker + "\"", DownloadUrl = "preview-disabled://never-download" });
                using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
                var pages = Field<List<IInstallerPage>>(preview.HostedWizard, "_pages");
                var index = pages.FindIndex(page => page is PrerequisitePage); index.Should().BeGreaterThanOrEqualTo(0);
                preview.HostedWizard.SelectPreviewPage(index).Should().BeTrue(); File.Exists(marker).Should().BeFalse();
                var prerequisite = (PrerequisitePage)pages[index]; prerequisite.CanGoNext.Should().BeTrue();
                var result = Field<List<PrerequisiteDetector.PrerequisiteResult>>(prerequisite, "_results").Single(value => value.Config.Id == "preview-check");
                result.Detected.Should().BeFalse(); var before = result.StatusText;
                var install = (Task)prerequisite.GetType().GetMethod("InstallPrerequisiteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(prerequisite, new object[] { result })!;
                install.IsCompletedSuccessfully.Should().BeTrue(); result.StatusText.Should().Be(before); result.IsChecking.Should().BeFalse();
                var list = Field<Panel>(prerequisite, "_listPanel");
                list.Controls.Cast<Control>().SelectMany(row => row.Controls.Cast<Control>()).Where(control => control.Text is "Download" or "Install")
                    .Should().NotBeEmpty().And.OnlyContain(control => !control.Enabled);
                LanguageManager.SetLanguage("ar"); Application.DoEvents(); result.StatusText.Should().Be(LanguageManager.GetString("Preview_PrerequisitesNotChecked"));
                File.Exists(marker).Should().BeFalse();
            });
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }
    [Fact]
    public void PreviewToolbarFollowsLiveThemeAndUnsubscribesAfterDisposal()
    {
        Sta(() =>
        {
            using var preview = new WizardPreviewForm(ProjectTemplates.Create(ProjectTemplates.EmptyId, "Theme", "1.0.0", "Publisher", ""));
            preview.Show(); Application.DoEvents();
            var reset = Find<Button>(preview, "PreviewReset");
            preview.BackColor = reset.BackColor = System.Drawing.Color.Magenta;
            BeepThemesManager.CurrentThemeName = BeepThemesManager.CurrentThemeName; Application.DoEvents();
            preview.BackColor.Should().Be(InstallerTheme.Panel); reset.BackColor.Should().Be(InstallerTheme.Panel);
            reset.ForeColor.Should().Be(InstallerTheme.Text);
            preview.Dispose();
            Action change = () => BeepThemesManager.CurrentThemeName = BeepThemesManager.CurrentThemeName; change.Should().NotThrow();
        });
    }
    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    public void PopulatedConfiguredPagesRemainSelectableAndScrollableAtMinimumWindowSize(string culture)
    {
        Sta(() =>
        {
            var project = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Populated preview", "1.0.0", "Publisher", "");
            project.EnabledWizardPages.Clear(); project.EnabledWizardPages.Add(WizardPageIds.Components); project.EnabledWizardPages.Add(WizardPageIds.Ready);
            project.CustomPages.Add(new() { Id = "configuration", Title = "Authored configuration", Fields = new()
            {
                new() { Id = "server", Label = "Server", DefaultValue = "server.example", Required = true },
                new() { Id = "secret", Label = "Password", Type = CustomFieldType.Password, DefaultValue = "preview-value" },
                new() { Id = "choice", Label = "Deployment", Type = CustomFieldType.Radio, DefaultValue = "Production", Options = new() { "Development", "Production" } }
            } });
            project.MarkClean();
            using var preview = new WizardPreviewForm(project); preview.Show(); preview.Size = preview.MinimumSize;
            LanguageManager.SetLanguage(culture); Application.DoEvents();
            var pages = Field<List<IInstallerPage>>(preview.HostedWizard, "_pages");
            pages.Select(page => page.GetType()).Should().Equal(typeof(ComponentSelectionPage), typeof(ReadyPage), typeof(CustomPage), typeof(CompletePage), typeof(ErrorPage));
            var choices = Find<ComboBox>(preview, "PreviewPage"); var scale = Find<ComboBox>(preview, "PreviewScale"); var reset = Find<Button>(preview, "PreviewReset");
            foreach (var control in new Control[] { choices, scale, reset }) control.Parent!.ClientRectangle.Contains(control.Bounds).Should().BeTrue();
            choices.Bounds.IntersectsWith(scale.Bounds).Should().BeFalse(); scale.Bounds.IntersectsWith(reset.Bounds).Should().BeFalse();
            for (var factor = 0; factor < 4; factor++)
            {
                scale.SelectedIndex = factor; Application.DoEvents();
                for (var index = 0; index < choices.Items.Count; index++) { choices.SelectedIndex = index; preview.HostedWizard.PreviewCurrentPage.Should().Be(index); }
                var viewport = Find<Panel>(preview, "PreviewViewport");
                viewport.AutoScrollMinSize.Should().Be(preview.HostedWizard.Size);
                viewport.HorizontalScroll.Visible.Should().BeTrue(); viewport.VerticalScroll.Visible.Should().BeTrue();
                choices.SelectedIndex = 2;
                var custom = (CustomPage)pages[2]; custom.CanGoNext.Should().BeTrue(); custom.Validate().Should().BeTrue();
                custom.Controls.OfType<FlowLayoutPanel>().Single().Controls.Cast<Control>().SelectMany(row => row.Controls.OfType<TextBox>())
                    .Should().Contain(text => text.UseSystemPasswordChar && text.Text == "preview-value");
            }
            project.IsDirty.Should().BeFalse(); project.CustomPages.Single().Fields[0].DefaultValue.Should().Be("server.example");
        });
    }
    [Fact]
    public void PreviewInstallActionCompletesWithoutExecutingPayloadOrCustomActions()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepPreviewRun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var payload = Path.Combine(root, "payload.txt"); File.WriteAllText(payload, "original payload");
        var destination = Path.Combine(root, "installed"); var marker = Path.Combine(root, "action.marker");
        try
        {
            Sta(() =>
            {
                var project = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Install preview", "1.0.0", "Publisher", root);
                project.DefaultDirName = destination;
                project.Components.Single().Files.Add(new() { SourcePath = payload, DestinationPath = "payload.txt" });
                project.CustomActions.Add(new() { Path = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
                    Arguments = "/c echo ran > \"" + marker + "\"", Timing = CustomActionTiming.AfterInstall });
                project.MarkClean();
                using var preview = new WizardPreviewForm(project); preview.Show(); Application.DoEvents();
                var wizard = preview.HostedWizard; var pages = Field<List<IInstallerPage>>(wizard, "_pages");
                wizard.SelectPreviewPage(pages.FindIndex(page => page is LicensePage));
                Field<CheckBox>(pages.OfType<LicensePage>().Single(), "_acceptCheck").Checked = true;
                wizard.SelectPreviewPage(pages.FindIndex(page => page is ReadyPage));
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                var run = (Task)wizard.GetType().GetMethod("RunInstallAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, null)!;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!run.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                run.IsCompletedSuccessfully.Should().BeTrue("preview task status is {0}: {1}", run.Status, run.Exception);
                var completed = pages.OfType<CompletePage>().Single();
                Find<TextBox>(wizard, "RuntimeCompletionSummary").Text.Should().Contain(LanguageManager.GetString("Runtime_Preview"));
                completed.SupportBundlePath.Should().BeNull(); completed.LogPath.Should().BeNull();
                Directory.Exists(destination).Should().BeFalse(); File.Exists(marker).Should().BeFalse();
                Directory.GetFiles(root).Should().Equal(payload); File.ReadAllText(payload).Should().Be("original payload"); project.IsDirty.Should().BeFalse();
            });
        }
        finally
        {
            // Only this test's direct, known files are removed; unexpected runtime output remains inspectable.
            File.Delete(payload); File.Delete(marker);
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
    }
}
