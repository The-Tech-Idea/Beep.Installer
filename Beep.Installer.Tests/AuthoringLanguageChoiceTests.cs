using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringLanguageChoiceTests
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr handle);

    private static int Listeners() => ((MulticastDelegate?)typeof(LanguageManager)
        .GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleLanguageActionAppliesAllCulturesAndPreservesProjectAndDraft(bool guided)
    {
        UiStaTest.Run(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            var controller = new InstallerController(); controller.StopAutoSave();
            try
            {
                LanguageManager.SetLanguage("en");
                using Form owner = guided ? new BuilderWizardForm(controller) : new PackageBuilderForm(controller);
                owner.Show();
                if (owner is PackageBuilderForm editor) editor.OpenSection("identity");
                TextBox Field() => owner.Controls.Find("AppName", true).OfType<TextBox>().Single();
                owner.Activate(); Field().Text = "Author Ω draft"; Field().Focus(); Field().Select(2, 5); Application.DoEvents();
                var before = InstallerScriptSerializer.Write(controller.Project);
                Exception? failure = null; bool seen = false;
                using var timer = new System.Windows.Forms.Timer { Interval = 25 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<AuthoringLanguageDialog>().SingleOrDefault(form => !form.InvokeRequired && form.Modal);
                    if (dialog is null) return;
                    timer.Stop(); seen = true;
                    try
                    {
                        var choices = dialog.Controls.Find("AuthoringUiLanguage", true).OfType<ComboBox>().Single();
                        var apply = dialog.Controls.Find("AuthoringApplyLanguage", true).OfType<Button>().Single();
                        choices.Items.Cast<object>().Select(item => item.ToString()).Should().Equal(LanguageManager.SupportedCultures.Select(LanguageManager.NativeNameOf));
                        for (var index = 0; index < LanguageManager.SupportedCultures.Length; index++)
                        {
                            choices.SelectedIndex = index;
                            apply.PerformClick(); Application.DoEvents();
                            var culture = LanguageManager.SupportedCultures[index];
                            LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be(culture);
                            choices.SelectedIndex.Should().Be(index);
                            dialog.Text.Should().Be(LanguageManager.GetString("AuthoringLanguage_Title"));
                            dialog.RightToLeftLayout.Should().Be(culture == "ar");
                            choices.AccessibleName.Should().Be(dialog.Text);
                            IsWindowEnabled(owner.Handle).Should().BeFalse();
                            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                            Field().Text.Should().Be("Author Ω draft");
                            Field().SelectionStart.Should().Be(2); Field().SelectionLength.Should().Be(5);
                            dialog.Controls.Find("AuthoringEditTranslations", true).Single().Text.Should().Be(LanguageManager.GetString("AuthoringLanguage_Edit"));
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                if (guided) owner.Controls.Find("GuidedLanguages", true).OfType<Button>().Single().PerformClick();
                else Descendants(owner).OfType<ToolStrip>()
                    .SelectMany(strip => strip.Items.OfType<ToolStripButton>().Concat(strip.Items.OfType<ToolStripDropDownButton>().SelectMany(more => more.DropDownItems.OfType<ToolStripButton>())))
                    .Single(button => button.Name == "Toolbar_Languages").PerformClick();
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                seen.Should().BeTrue(); IsWindowEnabled(owner.Handle).Should().BeTrue();
                Field().Text.Should().Be("Author Ω draft");
                Field().SelectionStart.Should().Be(2); Field().SelectionLength.Should().Be(5);
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            finally { controller.StopAutoSave(); LanguageManager.SetLanguage(previous); }
        });
    }

    [Fact]
    public void ChangingChoiceWithoutApplyDoesNotChangeCultureAndDisposalDetachesListeners()
    {
        UiStaTest.Run(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try
            {
                LanguageManager.SetLanguage("fr"); var baseline = Listeners();
                using (var dialog = new AuthoringLanguageDialog())
                {
                    dialog.Show(); Application.DoEvents();
                    var choices = dialog.Controls.Find("AuthoringUiLanguage", true).OfType<ComboBox>().Single();
                    choices.SelectedIndex.Should().Be(Array.IndexOf(LanguageManager.SupportedCultures, "fr"));
                    choices.SelectedIndex = Array.IndexOf(LanguageManager.SupportedCultures, "ar");
                    LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be("fr");
                    dialog.Controls.Find("AuthoringCloseLanguages", true).OfType<Button>().Single().PerformClick();
                }
                Listeners().Should().Be(baseline);
                LanguageManager.SetLanguage("de");
            }
            finally { LanguageManager.SetLanguage(previous); }
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
