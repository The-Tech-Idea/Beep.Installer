using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ComponentFilesPresentationTests
{
    [Fact]
    public void PaletteAndSubscriptionLifetimePreserveTheDraftAndClosedSummary()
    {
        Sta(() =>
        {
            var languageListeners = Listeners(typeof(LanguageManager), "LanguageChanged");
            var paletteListeners = Listeners(typeof(TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager), "ThemeChanged");
            var themes = TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.GetThemes();
            var originalTheme = TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName;
            try
            {
                using var dialog = new ComponentFilesDialog(Component(), null); dialog.Show();
                var status = dialog.Controls.Find("FileSizeSummary", true).OfType<Label>().Single();
                var before = JsonSerializer.Serialize(dialog.ResultFiles);
                foreach (var theme in new[] { themes.First(t => t.PanelBackColor.GetBrightness() > .75),
                    themes.First(t => t.PanelBackColor.GetBrightness() < .25) })
                {
                    TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName = theme.ThemeName;
                    Application.DoEvents();
                    status.ForeColor.Should().Be(Beep.Installer.Ui.InstallerTheme.AccessibleForeground(
                        Beep.Installer.Ui.InstallerTheme.MutedText, Beep.Installer.Ui.InstallerTheme.Panel));
                    JsonSerializer.Serialize(dialog.ResultFiles).Should().Be(before);
                }
                var list = dialog.ResultFiles;
                dialog.Dispose();
                var closedSummary = status.Text;
                list.Add(new FileCopyOperation { SourcePath = "missing-owned-after-dispose.txt" });
                status.Text.Should().Be(closedSummary);
                Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languageListeners);
                Listeners(typeof(TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager), "ThemeChanged").Should().Be(paletteListeners);
            }
            finally { TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName = originalTheme; }
        });
    }

    private static int Listeners(Type type, string name) => ((MulticastDelegate?)type.GetField(name,
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
    [Fact]
    public void LiveLanguageKeepsAuthoredFieldsWhileUpdatingFileDialogCaptions()
    {
        Sta(() =>
        {
            var component = Component();
            using var dialog = new ComponentFilesDialog(component, null);
            dialog.Show();
            var field = Descendants(dialog).OfType<TextBox>().Single(box => box.DataBindings.Cast<Binding>()
                .Any(binding => binding.BindingMemberInfo.BindingField == "Description"));
            field.Text = "Authored 原文"; dialog.Activate(); field.Focus(); field.Select(2, 4);
            var draft = JsonSerializer.Serialize(dialog.ResultFiles);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                dialog.Activate(); field.Focus(); field.Select(2, 4);
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                dialog.Text.Should().Be(string.Format(LanguageManager.CurrentCulture,
                    LanguageManager.GetString("Files_EditTitle"), component.Name));
                field.Text.Should().Be("Authored 原文"); field.SelectionStart.Should().Be(2); field.SelectionLength.Should().Be(4);
                field.AccessibleName.Should().Be(LanguageManager.GetString("Common_Description"));
                JsonSerializer.Serialize(dialog.ResultFiles).Should().Be(draft);
                dialog.RightToLeftLayout.Should().Be(culture == "ar");
                dialog.Activate(); Application.DoEvents(); field.Focused.Should().BeTrue();
            }
            component.Files[0].Description.Should().Be("Original");
        });
    }

    [Fact]
    public void SummaryUsesCapturedSizesAndPickerUsesCurrentCultureWithoutChangingTheDraft()
    {
        Sta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepFilePresentation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var payload = Path.Combine(root, "owned.txt");
            File.WriteAllBytes(payload, new byte[1024 * 1024]);
            try
            {
                var component = Component(); component.Files.Add(new FileCopyOperation { SourcePath = payload, DestinationPath = "owned.txt" });
                var before = JsonSerializer.Serialize(component);
                using var dialog = new ComponentFilesDialog(component, root); dialog.Show();
                UiStaTest.Pump(dialog.PendingSize);
                File.Delete(payload);
                var status = dialog.Controls.Find("FileSizeSummary", true).OfType<Label>().Single();
                var browse = dialog.Controls.Find("FileSourceBrowse", true).OfType<Button>().Single();
                var calls = 0;
                dialog.ShowFilePicker = (picker, owner) =>
                {
                    calls++; owner.Should().BeSameAs(dialog);
                    picker.Title.Should().Be(Beep.Installer.Ui.AuthoringFileDialogs.BrowseCaption(LanguageManager.GetString("Files_SourcePath")));
                    picker.InitialDirectory.Should().Be(root); picker.CheckFileExists.Should().BeTrue();
                    picker.Filter.Should().Contain(LanguageManager.GetString("Picker_AllFiles"));
                    return DialogResult.Cancel;
                };
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    status.Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Files_TotalSize"), 1.0, 2, 1));
                    status.AccessibleName.Should().Be(status.Text);
                    browse.AccessibleName.Should().Be(Beep.Installer.Ui.AuthoringFileDialogs.BrowseCaption(LanguageManager.GetString("Files_SourcePath")));
                    browse.PerformClick();
                }
                calls.Should().Be(8); JsonSerializer.Serialize(component).Should().Be(before);
            }
            finally { if (File.Exists(payload)) File.Delete(payload); Directory.Delete(root); }
        });
    }

    [Fact]
    public void LargeMultilineFieldCaptionFitsAtMinimumWindow()
    {
        Sta(() =>
        {
            using var font = new System.Drawing.Font("Segoe UI", 15);
            using var dialog = new ComponentFilesDialog(Component(), null);
            dialog.Font = font; dialog.Size = dialog.MinimumSize; dialog.Show();
            var caption = Descendants(dialog).OfType<Label>().Single(label => label.Text == LanguageManager.GetString("Files_SourcePath"));
            caption.Text = "Long source path caption\nsecond line\nthird line";
            Application.DoEvents(); dialog.PerformLayout();
            caption.Height.Should().BeGreaterOrEqualTo(caption.GetPreferredSize(new System.Drawing.Size(caption.Width, 0)).Height);
        });
    }

    private static InstallComponent Component()
    {
        var component = new InstallComponent { Id = "core", Name = "Authored component" };
        component.Files.Add(new FileCopyOperation { SourcePath = "missing-owned-fixture.txt", DestinationPath = "app.txt", Description = "Original" });
        return component;
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });
}
