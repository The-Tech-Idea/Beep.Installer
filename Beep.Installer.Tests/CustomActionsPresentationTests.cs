using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Beep.Installer.Models;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
/// <remarks>Blind spot: owned native controls and injected warning answers, not OS DPI, Narrator or installed payload execution.</remarks>
public sealed class CustomActionsPresentationTests
{
    [Fact]
    public void LayoutHasOneOkAllFieldsAndAThemedIconToolbar()
    {
        Sta(() =>
        {
            var actions = new List<CustomAction>
            {
                new() { Path = @"{app}\tools\configure.exe", Arguments = "--configure --quiet", WorkingDirectory = "{app}", Description = "Configure the installed application.", TimeoutMs = 30000 },
                new() { Path = @"{app}\cleanup.exe", Timing = CustomActionTiming.BeforeUninstall }
            };
            using var dialog = new CustomActionsDialog(actions); dialog.Show();
            Descendants(dialog).OfType<Button>().Count(button => button.Text == LanguageManager.GetString("Common_OK")).Should().Be(1);
            Find<Button>(dialog, "Actions_Add").Image.Should().NotBeNull(); Find<Button>(dialog, "Actions_Remove").Image.Should().NotBeNull();
            Find<Button>(dialog, "Actions_Cancel").Image.Should().NotBeNull();
            Find<DataGridView>(dialog, "Actions_Grid").Columns.Count.Should().Be(2);
            Find<TextBox>(dialog, "Actions_PathField").Text.Should().Be(actions[0].Path);
            Find<TextBox>(dialog, "Actions_DescriptionField").Text.Should().Be(actions[0].Description);
            Find<NumericUpDown>(dialog, "Actions_TimeoutField").Value.Should().Be(30000);
            var output = Environment.GetEnvironmentVariable("BEEP_ACTIONS_RENDER_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var image = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(Path.Combine(output, "implemented.png"));
            }
        });
    }

    [Theory]
    [InlineData("path")][InlineData("timeout")][InlineData("timing")][InlineData("missing")]
    public void RejectedDraftSelectsExactActionAndFieldAndCanBeCorrected(string kind)
    {
        Sta(() =>
        {
            var bad = kind == "missing" ? null! : new CustomAction { Path = kind == "path" ? "" : "owned-inert.exe", TimeoutMs = kind == "timeout" ? -17 : 0,
                Timing = kind == "timing" ? (CustomActionTiming)99 : CustomActionTiming.AfterInstall };
            var actions = new List<CustomAction> { new() { Path = "first-owned.exe" }, bad };
            var before = JsonSerializer.Serialize(actions);
            using var dialog = new CustomActionsDialog(actions); dialog.Show();
            var draft = JsonSerializer.Serialize(dialog.ResultActions); var applied = 0; dialog.ApplyDraft = () => { applied++; return true; };
            Find<Button>(dialog, "Actions_Ok").PerformClick();
            dialog.Visible.Should().BeTrue(); dialog.DialogResult.Should().Be(DialogResult.None); applied.Should().Be(0);
            var binding = (BindingSource)typeof(CustomActionsDialog).GetField("_binding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            binding.Position.Should().Be(1);
            JsonSerializer.Serialize(dialog.ResultActions).Should().Be(draft);
            var fieldName = kind switch { "path" => "Actions_PathField", "timeout" => "Actions_TimeoutField", "timing" => "Actions_TimingField", _ => "Actions_Grid" };
            dialog.Activate(); Application.DoEvents(); dialog.Controls.Find(fieldName, true).Single().Focused.Should().BeTrue();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                Find<Label>(dialog, "Actions_Validation").Text.Should().Contain(LanguageManager.GetString("Actions_CannotSave"));
                JsonSerializer.Serialize(dialog.ResultActions).Should().Be(draft);
            }
            if (kind == "path") Find<TextBox>(dialog, fieldName).Text = "corrected-owned.exe";
            else if (kind == "timeout") Find<NumericUpDown>(dialog, fieldName).Value = 4000;
            else if (kind == "timing") Find<ComboBox>(dialog, fieldName).SelectedItem = CustomActionTiming.AfterInstall;
            else { Find<Button>(dialog, "Actions_Remove").Enabled.Should().BeTrue(); Find<Button>(dialog, "Actions_Remove").PerformClick(); }
            Find<Button>(dialog, "Actions_Ok").PerformClick(); applied.Should().Be(1); dialog.DialogResult.Should().Be(DialogResult.OK);
            JsonSerializer.Serialize(actions).Should().Be(before);
        });
    }

    [Fact]
    public void WarningConfirmationUsesTheCurrentLanguageAndNoKeepsTheDraft()
    {
        Sta(() =>
        {
            var actions = new List<CustomAction> { new() { Path = "", Required = false, Description = "Keep authored draft" } };
            var before = JsonSerializer.Serialize(actions);
            using var dialog = new CustomActionsDialog(actions); dialog.Show(); var applied = 0;
            dialog.ApplyDraft = () => { applied++; return true; };
            var answer = DialogResult.No; var calls = 0;
            dialog.ShowWarningPrompt = (owner, message, title) =>
            {
                owner.Should().BeSameAs(dialog); title.Should().Be(LanguageManager.GetString("Common_Warnings"));
                message.Should().Contain(LanguageManager.GetString("Actions_SaveWarnings")).And.Contain("Path is empty.");
                calls++; return answer;
            };
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Find<Button>(dialog, "Actions_Ok").PerformClick();
                dialog.Visible.Should().BeTrue(); applied.Should().Be(0); dialog.ResultActions[0].Description.Should().Be("Keep authored draft");
            }
            answer = DialogResult.Yes; Find<Button>(dialog, "Actions_Ok").PerformClick(); applied.Should().Be(1); calls.Should().Be(9);
            JsonSerializer.Serialize(actions).Should().Be(before);
        });
    }

    [Fact]
    public void EmptyListAddRemoveAndCancelStaySafeAndTimingCaptionsTranslate()
    {
        Sta(() =>
        {
            var actions = new List<CustomAction>(); using var dialog = new CustomActionsDialog(actions); dialog.Show();
            Find<Button>(dialog, "Actions_Remove").Enabled.Should().BeFalse(); Find<TextBox>(dialog, "Actions_PathField").Enabled.Should().BeFalse();
            Find<Button>(dialog, "Actions_Add").PerformClick(); dialog.ResultActions.Should().ContainSingle();
            Find<TextBox>(dialog, "Actions_PathField").Text = "inert-owned.exe";
            var timing = Find<ComboBox>(dialog, "Actions_TimingField");
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                timing.Text.Should().Be(LanguageManager.GetString("Actions_AfterInstall"));
                dialog.ResultActions[0].Timing.Should().Be(CustomActionTiming.AfterInstall);
                Find<Button>(dialog, "Actions_Add").AccessibleName.Should().Be(LanguageManager.GetString("Actions_AddAction"));
            }
            Find<Button>(dialog, "Actions_Remove").PerformClick(); dialog.ResultActions.Should().BeEmpty();
            Find<Button>(dialog, "Actions_Cancel").PerformClick(); actions.Should().BeEmpty(); dialog.DialogResult.Should().Be(DialogResult.Cancel);
        });
    }

    [Fact]
    public void EditingAndApplyingCommandMetadataDoesNotRunTheCommand()
    {
        Sta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "BeepActionMarker_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); var marker = Path.Combine(directory, "must-not-exist.txt");
            try
            {
                // This is authored metadata only. The fixture never launches a process.
                var actions = new List<CustomAction> { new() { Path = "cmd.exe", Arguments = "/d /c echo unexpected > \"" + marker + "\"" } };
                using var dialog = new CustomActionsDialog(actions); dialog.Show();
                foreach (var culture in LanguageManager.SupportedCultures) { LanguageManager.SetLanguage(culture); Application.DoEvents(); }
                Find<TextBox>(dialog, "Actions_DescriptionField").Text = "Metadata edit";
                var applied = 0; dialog.ApplyDraft = () => { applied++; return true; };
                Find<Button>(dialog, "Actions_Ok").PerformClick(); Application.DoEvents();
                applied.Should().Be(1); File.Exists(marker).Should().BeFalse();
                actions[0].Description.Should().BeNull();
            }
            finally { if (File.Exists(marker)) File.Delete(marker); Directory.Delete(directory); }
        });
    }

    [Fact]
    public void ThemeAndCaptionSubscriptionsEndWithTheDialog()
    {
        Sta(() =>
        {
            static int Listeners(Type type, string field) => ((MulticastDelegate?)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null))?.GetInvocationList().Length ?? 0;
            var languages = Listeners(typeof(LanguageManager), "LanguageChanged"); var themes = Listeners(typeof(BeepThemesManager), "ThemeChanged");
            using var dialog = new CustomActionsDialog(new List<CustomAction> { new() { Path = "owned.exe" } }); dialog.Show();
            var draft = JsonSerializer.Serialize(dialog.ResultActions);
            foreach (var theme in new[] { BeepThemesManager.GetThemes().First(t => t.PanelBackColor.GetBrightness() > .75),
                BeepThemesManager.GetThemes().First(t => t.PanelBackColor.GetBrightness() < .25) })
            {
                BeepThemesManager.CurrentThemeName = theme.ThemeName; Application.DoEvents();
                var expected = Beep.Installer.Ui.InstallerTheme.AccessibleForeground(Beep.Installer.Ui.InstallerTheme.Text, Beep.Installer.Ui.InstallerTheme.Panel).ToArgb();
                var bitmap = (Bitmap)Find<Button>(dialog, "Actions_Add").Image!;
                Enumerable.Range(0, bitmap.Width).Any(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).ToArgb() == expected)).Should().BeTrue();
                JsonSerializer.Serialize(dialog.ResultActions).Should().Be(draft);
            }
            dialog.Dispose(); Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languages);
            Listeners(typeof(BeepThemesManager), "ThemeChanged").Should().Be(themes);
        });
    }

    [Fact]
    public void MinimumEnlargedWindowKeepsFooterInsideEveryLanguage()
    {
        Sta(() =>
        {
            using var large = new Font("Segoe UI", 15);
            using var dialog = new CustomActionsDialog(new List<CustomAction> { new() { Path = "owned.exe" } });
            dialog.Font = large; dialog.Size = dialog.MinimumSize; dialog.Show();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                foreach (var name in new[] { "Actions_Ok", "Actions_Cancel", "Actions_Add", "Actions_Remove" })
                {
                    var button = Find<Button>(dialog, name);
                    var bounds = dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    dialog.ClientRectangle.Contains(bounds).Should().BeTrue($"{name} in {culture}: {bounds}, client {dialog.ClientRectangle}");
                }
            }
        });
    }

    [Fact]
    public void LiveCaptionsPreserveSelectedActionAndAuthoredText()
    {
        Sta(() =>
        {
            var actions = new List<CustomAction> { new() { Path = "owned-inert.exe", Description = "Original" } };
            var canonical = JsonSerializer.Serialize(actions);
            using var dialog = new CustomActionsDialog(actions); dialog.Show();
            var description = Descendants(dialog).OfType<TextBox>().Single(box => box.DataBindings.Cast<Binding>()
                .Any(binding => binding.BindingMemberInfo.BindingField == nameof(CustomAction.Description)));
            description.Text = "Authored draft 原文"; description.Focus(); description.Select(2, 4);
            var draft = JsonSerializer.Serialize(dialog.ResultActions);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                dialog.Activate(); description.Focus(); description.Select(2, 4);
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                dialog.Text.Should().Be(LanguageManager.GetString("Actions_CustomActionsA11"));
                description.Text.Should().Be("Authored draft 原文"); description.SelectionStart.Should().Be(2); description.SelectionLength.Should().Be(4);
                JsonSerializer.Serialize(dialog.ResultActions).Should().Be(draft);
            }
            JsonSerializer.Serialize(actions).Should().Be(canonical);
        });
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        var theme = BeepThemesManager.CurrentThemeName;
        Exception? uiFailure = null;
        ThreadExceptionEventHandler reporter = (_, e) =>
        {
            uiFailure ??= e.Exception;
            Beep.Installer.Engine.Diag.Debug("CustomActionsPresentationTests", "Unexpected owned UI exception", e.Exception);
        };
        Application.ThreadException += reporter;
        try { Application.EnableVisualStyles(); LanguageManager.SetLanguage("en"); BeepThemesManager.SetCurrentTheme("ModernTheme"); action(); }
        finally
        {
            Application.ThreadException -= reporter; LanguageManager.SetLanguage(culture); BeepThemesManager.CurrentThemeName = theme;
            if (uiFailure is not null) ExceptionDispatchInfo.Capture(uiFailure).Throw();
        }
    });
}
