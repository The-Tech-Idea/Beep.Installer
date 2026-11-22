using System.Drawing;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringFileDialogPresentationTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(previous); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();

    [Fact]
    public void ProjectPickersTranslateDescriptionsAndKeepFilePatternsNamesAndSafetyDefaults()
    {
        Sta(() =>
        {
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture);
                using var open = AuthoringFileDialogs.OpenProject(); using var save = AuthoringFileDialogs.SaveProject("Authored 原文.bsetup");
                open.Title.Should().Be(LanguageManager.GetString("Builder_OpenScriptTitle")); save.Title.Should().Be(LanguageManager.GetString("Builder_SaveAsTitle"));
                var parts = open.Filter.Split('|'); parts.Should().HaveCount(4);
                parts[0].Should().Be(LanguageManager.GetString("Picker_ProjectFiles")); parts[1].Should().Be("*.bsetup");
                parts[2].Should().Be(LanguageManager.GetString("Picker_AllFiles")); parts[3].Should().Be("*.*");
                save.Filter.Should().Be(open.Filter); save.FileName.Should().Be("Authored 原文.bsetup");
                open.CheckFileExists.Should().BeTrue(); save.OverwritePrompt.Should().BeTrue(); save.AddExtension.Should().BeTrue();
                open.DefaultExt.Should().Be("bsetup"); save.DefaultExt.Should().Be("bsetup");
                using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources"); using var reader = new ResourceReader(stream!);
                var resources = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string);
                resources["Picker_ProjectFiles"].Should().NotBeNullOrWhiteSpace(); resources["Picker_AllFiles"].Should().NotBeNullOrWhiteSpace();
            }
        });
    }

    [Fact]
    public void FolderPickerDescribesTheFieldAndStartsAtAnExistingAuthoredFolder()
    {
        Sta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "beep-picker-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture);
                    var field = LanguageManager.GetString("NewProject_SourceDirectory");
                    using var picker = AuthoringFileDialogs.Folder(field, directory);
                    picker.Description.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Resource_Browse"), field.TrimEnd(':', '：')));
                    picker.UseDescriptionForTitle.Should().BeTrue(); picker.SelectedPath.Should().Be(directory);
                    using var invalid = AuthoringFileDialogs.Folder(field, Path.Combine(directory, "absent")); invalid.SelectedPath.Should().BeEmpty();
                    AuthoringFileDialogs.BrowseCaption("Authored field：").Should().Contain("Authored field").And.NotContain("：");
                }
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Theory]
    [InlineData("*.ico")]
    [InlineData("*.png;*.jpg;*.bmp")]
    [InlineData("*.rtf;*.txt;*.md")]
    [InlineData("*.pfx")]
    public void FileRowPatternsProduceValidLocalizedPickerFilters(string patterns)
    {
        Sta(() =>
        {
            Action previousRawFilter = () => { using var invalid = new OpenFileDialog { Filter = patterns }; };
            previousRawFilter.Should().Throw<ArgumentException>("raw field patterns are not a valid WinForms description/pattern filter");
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); using var picker = AuthoringFileDialogs.File("Authored field", patterns);
                var parts = picker.Filter.Split('|'); parts.Should().HaveCount(4);
                parts[0].Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Picker_MatchingFiles"), patterns));
                parts[1].Should().Be(patterns); parts[2].Should().Be(LanguageManager.GetString("Picker_AllFiles")); parts[3].Should().Be("*.*");
                picker.Title.Should().Be(AuthoringFileDialogs.BrowseCaption("Authored field")); picker.CheckFileExists.Should().BeTrue();
            }
        });
    }

    [Fact]
    public void GuidedBrowseFocusAndDraftSurviveLanguageRebuildAndSourceHelpFitsMinimumWindow()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var wizard = new BuilderWizardForm(controller); wizard.Show(); wizard.Size = wizard.MinimumSize;
            Find<TextBox>(wizard, "AppName").Text = "Authored name Ω";
            Find<TextBox>(wizard, "SourceDirectory").Text = @"C:\Authored\source";
            var before = InstallerScriptSerializer.Write(controller.Project);
            Application.DoEvents(); wizard.Activate(); Application.DoEvents();
            Find<Button>(wizard, "BrowseCreationSource").Focus().Should().BeTrue();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                var browse = Find<Button>(wizard, "BrowseCreationSource");
                browse.AccessibleName.Should().Be(AuthoringFileDialogs.BrowseCaption(LanguageManager.GetString("NewProject_SourceDirectory")));
                browse.Focused.Should().BeTrue(); browse.Visible.Should().BeTrue(); browse.Enabled.Should().BeTrue();
                Find<TextBox>(wizard, "AppName").Text.Should().Be("Authored name Ω"); Find<TextBox>(wizard, "SourceDirectory").Text.Should().Be(@"C:\Authored\source");
                var help = Find<Label>(wizard, "CreationSourceHelp"); help.AutoSize.Should().BeTrue(); help.Visible.Should().BeTrue();
                help.ForeColor.Should().Be(InstallerTheme.MutedText);
                help.Height.Should().BeGreaterThanOrEqualTo(help.GetPreferredSize(new Size(help.Width, 0)).Height);
                var layout = (TableLayoutPanel)help.Parent!; layout.RowStyles.Cast<RowStyle>().Should().OnlyContain(row => row.SizeType == SizeType.AutoSize);
                ((Panel)layout.Parent!).AutoScroll.Should().BeTrue();
                Find<TextBox>(wizard, "AppName").Width.Should().BeGreaterThan(160);
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
        });
    }
}
