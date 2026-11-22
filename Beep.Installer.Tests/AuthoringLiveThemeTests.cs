using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringLiveThemeTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Find<T>(Control owner, string name) where T : Control => owner.Controls.Find(name, true).OfType<T>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = BeepThemesManager.CurrentThemeName;
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { BeepThemesManager.CurrentThemeName = original; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static string[] Themes()
    {
        var themes = BeepThemesManager.GetThemes();
        var light = themes.FirstOrDefault(theme => theme.PanelBackColor.GetBrightness() > .75);
        var dark = themes.FirstOrDefault(theme => theme.PanelBackColor.GetBrightness() < .25);
        light.Should().NotBeNull(); dark.Should().NotBeNull();
        return new[] { light!.ThemeName, dark!.ThemeName, light.ThemeName };
    }
    private static void Change(string theme) { BeepThemesManager.CurrentThemeName = theme; Application.DoEvents(); }
    private static int Listeners() => ((MulticastDelegate?)typeof(BeepThemesManager).GetField("ThemeChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    [Fact]
    public void AdvancedCachedFieldsResourcesAndResultsFollowLightAndDarkThemesWithoutChangingValuesOrFocus()
    {
        Sta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Authored product", "1.0.0", "Publisher", "");
            project.WindowsServices.Add(new() { Name = "First", ExecutablePath = "first.exe" }); project.WindowsServices.Add(new() { Name = "Second", ExecutablePath = "second.exe" });
            using var editor = new PackageBuilderForm(new InstallerController(project)); editor.Show(); editor.OpenSection("windowsservices");
            var resources = editor.ActiveSection!; var grid = Descendants(resources).OfType<DataGridView>().Single(); var rows = (BindingSource)grid.DataSource!; rows.Position = 1;
            var selected = rows.Current; var properties = Find<PropertyGrid>(resources, "ResourceProperties");
            editor.RecordLastBuild(new BuildPipeline.BuildResult { Success = false, Errors = { "Fixture failure" } }); editor.OpenSection("result");
            var result = editor.ActiveSection!; var banner = Find<Label>(result, "StatusBanner");
            editor.OpenSection("identity"); var identity = editor.ActiveSection!; var name = Find<TextBox>(identity, "AppName");
            name.Focus(); name.Select(2, 5); var before = InstallerScriptSerializer.Write(project);
            foreach (var theme in Themes())
            {
                Change(theme);
                editor.BackColor.Should().Be(InstallerTheme.Shell); name.BackColor.Should().Be(InstallerTheme.Panel); name.ForeColor.Should().Be(InstallerTheme.Text);
                Field<ToolStrip>(editor, "_toolbar").BackColor.Should().Be(InstallerTheme.Panel);
                grid.BackgroundColor.Should().Be(InstallerTheme.Panel); grid.DefaultCellStyle.BackColor.Should().Be(InstallerTheme.Panel);
                grid.DefaultCellStyle.ForeColor.Should().Be(InstallerTheme.Text); grid.DefaultCellStyle.SelectionBackColor.Should().Be(InstallerTheme.Accent);
                grid.DefaultCellStyle.SelectionForeColor.Should().Be(InstallerTheme.AccentText); grid.ColumnHeadersDefaultCellStyle.BackColor.Should().Be(InstallerTheme.Sidebar);
                properties.ViewBackColor.Should().Be(InstallerTheme.Panel); properties.ViewForeColor.Should().Be(InstallerTheme.Text);
                properties.HelpBackColor.Should().Be(InstallerTheme.Sidebar); properties.SelectedItemWithFocusBackColor.Should().Be(InstallerTheme.Accent);
                Find<Label>(resources, "SectionHelp").ForeColor.Should().Be(InstallerTheme.MutedText); banner.ForeColor.Should().Be(InstallerTheme.Danger);
                var lastBuild = Field<ToolStripStatusLabel>(editor, "_buildResultLabel"); lastBuild.ForeColor.Should().Be(InstallerTheme.Danger); lastBuild.LinkColor.Should().Be(lastBuild.ForeColor);
                editor.ActiveSection.Should().BeSameAs(identity); name.Text.Should().Be("Authored product"); name.Focused.Should().BeTrue(); name.SelectionStart.Should().Be(2); name.SelectionLength.Should().Be(5);
                rows.Current.Should().BeSameAs(selected); properties.SelectedObject.Should().BeSameAs(selected); InstallerScriptSerializer.Write(project).Should().Be(before);
            }
        });
    }

    [Fact]
    public void GuidedBorrowedEditorsTabsAndNewControlsFollowLiveThemesWhileKeepingTheirOwnershipAndSelection()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); using var editor = new PackageBuilderForm(controller); editor.OpenSection("layout");
            var shared = editor.ActiveSection!;
            using var wizard = new BuilderWizardForm(controller, editor); wizard.Show();
            var tabs = Find<TabControl>(wizard, "GuidedSections"); tabs.SelectedTab!.Name.Should().Be("layout");
            var path = Find<TextBox>(wizard, "DefaultDirName"); path.Text = @"C:\Authored\Destination"; path.Focus(); path.Select(3, 7);
            var before = InstallerScriptSerializer.Write(controller.Project);
            foreach (var theme in Themes())
            {
                Change(theme);
                wizard.BackColor.Should().Be(InstallerTheme.Shell); path.BackColor.Should().Be(InstallerTheme.Panel); path.ForeColor.Should().Be(InstallerTheme.Text);
                shared.FindForm().Should().BeSameAs(wizard); editor.ActiveSection.Should().BeSameAs(shared); tabs.SelectedTab.Name.Should().Be("layout");
                tabs.DrawMode.Should().Be(TabDrawMode.OwnerDrawFixed); tabs.TabPages["layout"]!.BackColor.Should().Be(InstallerTheme.Panel);
                path.Focused.Should().BeTrue(); path.SelectionStart.Should().Be(3); path.SelectionLength.Should().Be(7); path.Text.Should().Be(@"C:\Authored\Destination");
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            Change(Themes()[1]);
            using var font = new Font("Consolas", 10); var marker = new object();
            using var added = new Button { Text = "Authored action", Tag = marker, Font = font, BackColor = Color.Magenta };
            shared.Controls.Add(added); added.BackColor.Should().Be(InstallerTheme.Panel); added.ForeColor.Should().Be(InstallerTheme.Text);
            added.Text.Should().Be("Authored action"); added.Tag.Should().BeSameAs(marker); added.Font.Should().BeSameAs(font);
            using var image = new Bitmap(120, 28); using var graphics = Graphics.FromImage(image);
            typeof(AuthoringThemeBinding).GetMethod("DrawTab", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                new object[] { tabs, new DrawItemEventArgs(graphics, tabs.Font, new Rectangle(0, 0, 120, 28), tabs.SelectedIndex, DrawItemState.Selected) });
            image.GetPixel(0, 0).ToArgb().Should().Be(InstallerTheme.Accent.ToArgb());
            wizard.Dispose(); shared.FindForm().Should().BeSameAs(editor); shared.IsDisposed.Should().BeFalse();
        });
    }

    [Fact]
    public void DisposedControlsAndBindingsReleaseTheirSubscriptionsAndLaterThemeEventsAreSafe()
    {
        Sta(() =>
        {
            var original = Listeners(); using var owner = new Form(); using var binding = new AuthoringThemeBinding(owner);
            Listeners().Should().Be(original + 1); var watched = Field<HashSet<Control>>(binding, "_controls"); var initial = watched.Count;
            for (var i = 0; i < 20; i++)
            {
                using var panel = new Panel(); panel.Controls.Add(new TextBox()); owner.Controls.Add(panel);
                watched.Count.Should().Be(initial + 2); panel.Dispose(); watched.Count.Should().Be(initial);
            }
            owner.Dispose(); watched.Should().BeEmpty(); Listeners().Should().Be(original);
            Action refresh = () => BeepThemesManager.CurrentThemeName = BeepThemesManager.CurrentThemeName; refresh.Should().NotThrow();
        });
    }
}
