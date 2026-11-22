using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class SharedFieldLayoutPresentationTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static TableLayoutPanel Table(Control field)
    {
        for (var parent = field.Parent; parent is not null; parent = parent.Parent)
            if (parent is TableLayoutPanel table) return table;
        throw new InvalidOperationException("Bound field must have a containing table.");
    }
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

    [Theory]
    [InlineData("identity", "AppName")]
    [InlineData("layout", "DefaultDirName")]
    [InlineData("eula", "LicenseFile")]
    [InlineData("output", "OutputDir")]
    [InlineData("package", "CodeSignCertificatePath")]
    [InlineData("codesign", "CodeSignCertificatePath")]
    public void PopulatedFieldRowsFitTranslationsAndLargerMultilineCaptionsWithoutLosingValues(string section, string property)
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show(); editor.Size = editor.MinimumSize; editor.OpenSection(section);
            var panel = editor.ActiveSection!; var field = Find<TextBox>(panel, property); field.Text = "Authored Ω value";
            var table = Table(field); var rowControl = field.Parent == table ? field : field.Parent!;
            var row = table.GetPositionFromControl(rowControl).Row; var caption = (Label)table.GetControlFromPosition(0, row)!;
            Application.DoEvents(); editor.Activate(); Application.DoEvents(); field.Focus(); field.Select(2, 5);
            var before = InstallerScriptSerializer.Write(controller.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                caption.Height.Should().BeGreaterThanOrEqualTo(caption.GetPreferredSize(new Size(caption.Width, 0)).Height, "the complete {0} field label fits in {1}", property, culture);
                field.Width.Should().BeGreaterThan(100); field.Text.Should().Be("Authored Ω value"); field.Focused.Should().BeTrue(); field.SelectionStart.Should().Be(2); field.SelectionLength.Should().Be(5);
                editor.ActiveSection.Should().BeSameAs(panel); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            LanguageManager.SetLanguage("en"); Application.DoEvents();
            var originalText = caption.Text; var originalFont = caption.Font;
            using var largeFont = new Font(caption.Font.FontFamily, 15, FontStyle.Bold);
            caption.Font = largeFont;
            caption.Text = "Long translated field caption\r\nSecond explanatory line\r\nThird explanatory line\r\nFourth explanatory line";
            table.PerformLayout(); Application.DoEvents();
            caption.Height.Should().BeGreaterThanOrEqualTo(caption.GetPreferredSize(new Size(caption.Width, 0)).Height, "larger multi-line captions must grow their row rather than clip");
            field.Focused.Should().BeTrue(); field.SelectionStart.Should().Be(2); field.SelectionLength.Should().Be(5);
            field.Text.Should().Be("Authored Ω value"); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            if (section == "eula") ((SplitterPanel)table.Parent!).AutoScroll.Should().BeTrue();
            caption.Text = originalText; caption.Font = originalFont; table.PerformLayout(); Application.DoEvents();
            caption.Height.Should().BeLessThan(70, "ordinary field rows return to their compact size after larger text is removed");
        });
    }

    [Fact]
    public void PackageSectionHeadingCanWrapAndGrowWithoutChangingConfiguredValues()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show(); editor.Size = editor.MinimumSize; editor.OpenSection("package");
            var field = Find<TextBox>(editor.ActiveSection!, "CodeSignCertificatePath"); field.Text = "Authored certificate.pfx";
            var table = Table(field); var heading = (Label)table.GetControlFromPosition(0, 0)!;
            var originalFont = heading.Font; var originalText = heading.Text; var before = InstallerScriptSerializer.Write(controller.Project);
            using var largeFont = new Font(heading.Font.FontFamily, 15, FontStyle.Bold);
            heading.Font = largeFont; heading.Text = "Long section heading\r\nSecond explanatory line\r\nThird explanatory line\r\nFourth explanatory line";
            table.PerformLayout(); Application.DoEvents();
            heading.Height.Should().BeGreaterThanOrEqualTo(heading.GetPreferredSize(new Size(heading.Width, 0)).Height);
            field.Text.Should().Be("Authored certificate.pfx"); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            heading.Font = originalFont; heading.Text = originalText;
        });
    }

    [Fact]
    public void CheckboxCaptionCanGrowAndReturnToNormalSpacingWithoutChangingItsValue()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show(); editor.Size = editor.MinimumSize; editor.OpenSection("layout");
            var checkbox = Find<CheckBox>(editor.ActiveSection!, "AllowScopeSelection"); checkbox.Checked = false;
            var table = Table(checkbox); var originalText = checkbox.Text; var originalFont = checkbox.Font; var before = InstallerScriptSerializer.Write(controller.Project);
            using var largeFont = new Font(checkbox.Font.FontFamily, 15);
            checkbox.Font = largeFont; checkbox.Text = "Long option caption\r\nSecond explanatory line\r\nThird explanatory line\r\nFourth explanatory line";
            table.PerformLayout(); Application.DoEvents();
            checkbox.Height.Should().BeGreaterThanOrEqualTo(checkbox.GetPreferredSize(new Size(checkbox.Width, 0)).Height); checkbox.Checked.Should().BeFalse();
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            checkbox.Font = originalFont; checkbox.Text = originalText; table.PerformLayout(); Application.DoEvents();
            checkbox.Height.Should().BeGreaterThanOrEqualTo(26).And.BeLessThan(70);
        });
    }
}
