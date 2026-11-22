using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class CustomActionsPresentationTests
{
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
    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });
}
