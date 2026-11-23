using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Xunit;

namespace Beep.Installer.Tests;

/// <remarks>Blind spot: owned WinForms fixtures, not installed-app, Narrator or real Windows DPI acceptance.
/// The save fixture sets an existing file path directly to avoid modifying the user's recent-project store.</remarks>
[Collection("Language")]
public sealed class BuilderMenuBarTests
{
    [Fact]
    public void FileCommandsBelongToMenusAndToolbarContainsSixPaintedSvgActions()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
            editor.MainMenuStrip.Should().NotBeNull();
            editor.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Select(item => item.Name)
                .Should().Equal("Menu_File", "Menu_Edit", "Menu_View", "Menu_Project", "Menu_Help");
            var file = editor.MainMenuStrip.Items.OfType<ToolStripMenuItem>().First();
            file.DropDownItems.OfType<ToolStripMenuItem>().Select(item => item.Name).Should()
                .Equal("Toolbar_New", "Toolbar_Open", "Builder_Recent", "Common_Save", "Builder_SaveAsTitle", "Recovery_Title", "Menu_Exit");
            Command(editor, "Common_Save").ShortcutKeys.Should().Be(Keys.Control | Keys.S);
            Command(editor, "Builder_SaveAsTitle").ShortcutKeys.Should().Be(Keys.Control | Keys.Shift | Keys.S);
            Command(editor, "Toolbar_New").ShortcutKeys.Should().Be(Keys.Control | Keys.N);
            Command(editor, "Toolbar_Open").ShortcutKeys.Should().Be(Keys.Control | Keys.O);
            var toolbar = editor.Controls.Find("BuilderToolbar", true).OfType<ToolStrip>().Single();
            toolbar.Items.OfType<ToolStripButton>().Select(item => item.Name).Should().Equal(
                "Builder_ValidationTitle", "Builder_BuildTitle", "Toolbar_Preview", "Packaging_Button", "Signing_Button", "Builder_PublishTitle");
            editor.MainMenuStrip.Bottom.Should().BeLessThanOrEqualTo(toolbar.Top);
            foreach (var item in toolbar.Items.OfType<ToolStripButton>())
            {
                item.DisplayStyle.Should().Be(ToolStripItemDisplayStyle.Image); item.Size.Should().Be(new Size(48, 48));
                item.AccessibleName.Should().NotBeNullOrWhiteSpace(); item.ToolTipText.Should().NotBeNullOrWhiteSpace();
                item.Image.Should().NotBeNull();
                var bitmap = (Bitmap)item.Image!;
                Enumerable.Range(0, bitmap.Width).Any(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).A > 16)).Should().BeTrue();
            }
            var directory = Environment.GetEnvironmentVariable("BEEP_MENU_RENDER_DIR");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory); editor.Size = new Size(1440, 1024); Application.DoEvents();
                using var bitmap = new Bitmap(editor.Width, editor.Height);
                editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));
                bitmap.Save(Path.Combine(directory, "implemented-main-window.png"));
                file.ShowDropDown(); Application.DoEvents();
                using var menu = new Bitmap(file.DropDown.Width, file.DropDown.Height);
                file.DropDown.DrawToBitmap(menu, new Rectangle(Point.Empty, menu.Size));
                menu.Save(Path.Combine(directory, "implemented-file-menu.png")); file.HideDropDown();
            }
            controller.StopAutoSave();
        });
    }

    [Theory]
    [InlineData("en")][InlineData("ar")][InlineData("de")][InlineData("es")]
    [InlineData("fr")][InlineData("pt")][InlineData("ja")][InlineData("zh")]
    public void MenuLanguageAndRtlKeepTheCurrentProjectAndEditor(string culture)
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
            var project = editor.Project; var panel = editor.ActiveSection;
            var before = InstallerScriptSerializer.Write(project);
            LanguageManager.SetLanguage(culture); Application.DoEvents();
            editor.RightToLeftLayout.Should().Be(culture == "ar");
            foreach (var menu in editor.MainMenuStrip!.Items.OfType<ToolStripMenuItem>())
            {
                menu.Text.Should().Be(LanguageManager.GetString(menu.Name!));
                menu.AccessibleName.Should().Be(menu.Text!.Replace("&", ""));
                foreach (var command in menu.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Name != "Builder_Recent"))
                {
                    command.Text.Should().Be(LanguageManager.GetString(command.Name!));
                    command.AccessibleName.Should().Be(command.Text!.Replace("&", ""));
                }
            }
            editor.Project.Should().BeSameAs(project); editor.ActiveSection.Should().BeSameAs(panel);
            InstallerScriptSerializer.Write(project).Should().Be(before); controller.StopAutoSave();
        });
    }

    [Fact]
    public void SaveMenuCommitsTheCurrentEditorToItsExistingOwnedFile()
    {
        Sta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "BeepMenuSave_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "owned.bsetup");
            var controller = new InstallerController(); controller.StopAutoSave();
            try
            {
                typeof(InstallerController).GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, path);
                using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
                var name = Descendants(editor.ActiveSection!).OfType<TextBox>().Single(box => box.DataBindings.Cast<Binding>()
                    .Any(binding => binding.BindingMemberInfo.BindingField == "AppName"));
                name.Text = "Saved from the File menu"; Command(editor, "Common_Save").PerformClick();
                File.Exists(path).Should().BeTrue();
                InstallerScriptSerializer.Load(path).project!.AppName.Should().Be("Saved from the File menu");
                controller.IsDirty.Should().BeFalse();
            }
            finally { controller.StopAutoSave(); if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
        });
    }

    [Fact]
    public void ToolbarPaletteAndMenuSubscriptionsFollowTheWindowLifetime()
    {
        Sta(() =>
        {
            static int Listeners(Type type, string name) => ((MulticastDelegate?)type.GetField(name,
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
            var languages = Listeners(typeof(LanguageManager), "LanguageChanged");
            var themes = Listeners(typeof(BeepThemesManager), "ThemeChanged");
            var controller = new InstallerController(); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
            var before = InstallerScriptSerializer.Write(controller.Project);
            var toolbar = editor.Controls.Find("BuilderToolbar", true).OfType<ToolStrip>().Single();
            foreach (var theme in new[] { BeepThemesManager.GetThemes().First(t => t.PanelBackColor.GetBrightness() > .75),
                BeepThemesManager.GetThemes().First(t => t.PanelBackColor.GetBrightness() < .25) })
            {
                BeepThemesManager.CurrentThemeName = theme.ThemeName; Application.DoEvents();
                var expected = Beep.Installer.Ui.InstallerTheme.AccessibleForeground(Beep.Installer.Ui.InstallerTheme.Text,
                    Beep.Installer.Ui.InstallerTheme.Panel).ToArgb();
                foreach (var button in toolbar.Items.OfType<ToolStripButton>())
                {
                    var bitmap = (Bitmap)button.Image!;
                    Enumerable.Range(0, bitmap.Width).Any(x => Enumerable.Range(0, bitmap.Height)
                        .Any(y => bitmap.GetPixel(x, y).ToArgb() == expected)).Should().BeTrue();
                }
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            editor.Dispose(); controller.StopAutoSave();
            Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languages);
            Listeners(typeof(BeepThemesManager), "ThemeChanged").Should().Be(themes);
        });
    }

    [Fact]
    public void EmptyWelcomeKeepsFileEntryPointsButDisablesProjectOnlyCommands()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show();
            Command(editor, "Toolbar_New").Enabled.Should().BeTrue(); Command(editor, "Toolbar_Open").Enabled.Should().BeTrue();
            Command(editor, "Common_Save").Enabled.Should().BeFalse(); Command(editor, "Builder_SaveAsTitle").Enabled.Should().BeFalse();
            var toolbar = editor.Controls.Find("BuilderToolbar", true).OfType<ToolStrip>().Single();
            toolbar.Items.OfType<ToolStripButton>().Should().OnlyContain(item => !item.Enabled);
            editor.OpenSection("identity");
            Command(editor, "Common_Save").Enabled.Should().BeTrue();
            toolbar.Items.OfType<ToolStripButton>().Should().OnlyContain(item => item.Enabled); controller.StopAutoSave();
        });
    }

    private static ToolStripMenuItem Command(PackageBuilderForm editor, string key) => editor.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
        .SelectMany(menu => menu.DropDownItems.OfType<ToolStripMenuItem>()).Single(item => item.Name == key);
    private static IEnumerable<Control> Descendants(Control owner) => owner.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName; var theme = BeepThemesManager.CurrentThemeName;
        try { LanguageManager.SetLanguage("en"); BeepThemesManager.SetCurrentTheme("ModernTheme"); action(); }
        finally { LanguageManager.SetLanguage(language); BeepThemesManager.CurrentThemeName = theme; }
    });
}
