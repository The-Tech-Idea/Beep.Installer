using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Xunit;

namespace Beep.Installer.Tests;

/// <remarks>Owned native controls/files only; not OS DPI, Narrator or release acceptance.</remarks>
[Collection("Language")]
public sealed class LanguageManagerPresentationTests
{
    [Fact]
    public void MenusAndUiLanguageChangesPreserveActiveTranslationDraft()
    {
        Run((form, dir) =>
        {
            var picker = Find<ComboBox>(form, "Languages_Picker"); picker.SelectedItem = "fr";
            var grid = Find<DataGridView>(form, "Languages_Grid");
            grid.CurrentCell = grid.Rows[0].Cells[1]; grid.BeginEdit(true);
            var editor = (TextBox)grid.EditingControl!; editor.Text = "Authored draft 原文"; editor.Focus(); editor.Select(2, 5);
            var original = JsonSerializer.Serialize(Strings(form)); var preview = Find<RichTextBox>(form, "Languages_Preview").Text;
            var originalFile = File.ReadAllText(Path.Combine(dir, "Strings_fr.resx"));
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                form.Activate(); editor = (TextBox)grid.EditingControl!; editor.Focus(); editor.Select(2, 5);
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                picker.SelectedItem.Should().Be("fr"); grid.CurrentCell!.RowIndex.Should().Be(0);
                grid.IsCurrentCellInEditMode.Should().BeTrue();
                ((TextBox)grid.EditingControl!).Text.Should().Be("Authored draft 原文");
                ((TextBox)grid.EditingControl!).SelectionStart.Should().Be(2);
                ((TextBox)grid.EditingControl!).SelectionLength.Should().Be(5);
                JsonSerializer.Serialize(Strings(form)).Should().Be(original);
                Find<RichTextBox>(form, "Languages_Preview").Text.Should().Be(preview);
                File.ReadAllText(Path.Combine(dir, "Strings_fr.resx")).Should().Be(originalFile);
                Item(form, "LangMgr_AddLanguage").Text.Should().Be(LanguageManager.GetString("LangMgr_AddLanguage"));
                Item(form, "LangMgr_RemoveLanguage").Text.Should().NotBe(Item(form, "LangMgr_AddLanguage").Text);
                Item(form, "LangMgr_AddKey").Text.Should().NotBe(Item(form, "LangMgr_RemoveKey").Text);
                grid.Columns[1].HeaderText.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("LangMgr_ValueHeader"), "fr"));
                form.RightToLeft.Should().Be(culture == "ar" ? RightToLeft.Yes : RightToLeft.No);
            }
            grid.EndEdit(); Strings(form)["fr"]["Common_Cancel"].Should().Be("Authored draft 原文");
            var output = Environment.GetEnvironmentVariable("BEEP_LANGUAGES_RENDER_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                LanguageManager.SetLanguage("en"); Directory.CreateDirectory(output);
                using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(output, "implemented.png"));
            }
        });
    }

    [Fact]
    public void FileCommandsCommitActiveCellAndCsvRoundTripsQuotedMultilineTranslations()
    {
        Run((form, dir) =>
        {
            var picker = Find<ComboBox>(form, "Languages_Picker"); picker.SelectedItem = "fr";
            var grid = Find<DataGridView>(form, "Languages_Grid"); grid.CurrentCell = grid.Rows[0].Cells[1]; grid.BeginEdit(true);
            const string value = "French, \"quoted\"\r\nالعربية 原文";
            ((TextBox)grid.EditingControl!).Text = value;
            form.SaveAllLanguages();
            XDocument.Load(Path.Combine(dir, "Strings_fr.resx")).Descendants("data").Single(e => (string?)e.Attribute("name") == "Common_Cancel").Element("value")!.Value.Should().Be(value.Replace("\r\n", "\n"));
            var csv = Path.Combine(dir, "translations.csv"); form.ExportCsv(csv);
            grid.Rows[0].Cells[1].Value = "Changed";
            form.ImportCsv(csv);
            Strings(form)["fr"]["Common_Cancel"].Should().Be(value);
            picker.SelectedItem.Should().Be("fr"); LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be("en");
            Find<Label>(form, "Languages_Status").Text.Should().Contain(csv);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture);
                Find<Label>(form, "Languages_Status").Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("LangMgr_ImportedStatus"), csv));
            }
        });
    }

    [Theory]
    [InlineData("Key,fr,fr\nK,one,two")]
    [InlineData("Key,../../outside\nK,value")]
    [InlineData("Key,fr\nK,good\nInvalid,too,many")]
    public void InvalidCsvCannotPartiallyReplaceDraft(string content)
    {
        Run((form, dir) =>
        {
            var before = JsonSerializer.Serialize(Strings(form)); var path = Path.Combine(dir, "invalid.csv"); File.WriteAllText(path, content);
            ((Action)(() => form.ImportCsv(path))).Should().Throw<FormatException>();
            JsonSerializer.Serialize(Strings(form)).Should().Be(before);
            Find<ComboBox>(form, "Languages_Picker").SelectedItem.Should().Be("en");
        });
    }

    [Fact]
    public void LanguageAndKeyMenuCommandsKeepFileAndDisplayLanguageSeparate()
    {
        Run((form, dir) =>
        {
            form.PromptInput = (_, _) => "it"; Item(form, "LangMgr_AddLanguage").PerformClick();
            Find<ComboBox>(form, "Languages_Picker").SelectedItem.Should().Be("it");
            Strings(form)["it"].Should().HaveCount(2); File.Exists(Path.Combine(dir, "Strings_it.resx")).Should().BeFalse();
            form.PromptInput = (_, _) => "Owned_NewKey"; Item(form, "LangMgr_AddKey").PerformClick();
            Strings(form).Values.Should().OnlyContain(values => values.ContainsKey("Owned_NewKey"));
            var grid = Find<DataGridView>(form, "Languages_Grid");
            grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(row => (string?)row.Cells[0].Value == "Owned_NewKey").Cells[1];
            Item(form, "LangMgr_RemoveKey").PerformClick(); Strings(form).Values.Should().OnlyContain(values => !values.ContainsKey("Owned_NewKey"));
            form.PromptInput = (_, _) => "pt-BR"; Item(form, "LangMgr_DuplicateLanguage").PerformClick();
            Strings(form)["pt-br"].Should().Equal(Strings(form)["it"]);
            Find<ComboBox>(form, "Languages_Picker").SelectedItem = "it";
            form.SaveAllLanguages(); File.Exists(Path.Combine(dir, "Strings_it.resx")).Should().BeTrue();
            Item(form, "LangMgr_RemoveLanguage").PerformClick();
            File.Exists(Path.Combine(dir, "Strings_it.resx")).Should().BeFalse(); Strings(form).Should().NotContainKey("it");
            Find<ComboBox>(form, "Languages_Picker").SelectedItem.Should().Be("en");
            LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be("en");
        });
    }

    [Fact]
    public void MinimumEnlargedLayoutAndThemeSubscriptionsRemainUsable()
    {
        Run((form, _) =>
        {
            using var enlarged = new Font(form.Font.FontFamily, 15); form.Font = enlarged; form.Size = form.MinimumSize;
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                foreach (var name in new[] { "Languages_Picker", "Languages_Grid", "Languages_Preview", "Languages_Status" })
                {
                    var control = form.Controls.Find(name, true).Single();
                    var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    bounds.Top.Should().BeGreaterThanOrEqualTo(form.MainMenuStrip!.Bottom);
                    bounds.Bottom.Should().BeLessThanOrEqualTo(form.ClientSize.Height);
                    bounds.Width.Should().BeGreaterThan(30); bounds.Height.Should().BeGreaterThan(10);
                }
            }
            var original = JsonSerializer.Serialize(Strings(form));
            BeepThemesManager.SetCurrentTheme("DarkTheme"); Application.DoEvents();
            JsonSerializer.Serialize(Strings(form)).Should().Be(original);
            Find<DataGridView>(form, "Languages_Grid").BackgroundColor.Should().Be(Beep.Installer.Ui.InstallerTheme.Panel);
        });
        // Run disposes the form before restoring globals; named handlers must not retain disposed forms.
    }

    private static Dictionary<string, Dictionary<string, string>> Strings(LanguageManagerForm form) =>
        (Dictionary<string, Dictionary<string, string>>)typeof(LanguageManagerForm).GetField("_allStrings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static T Find<T>(Control form, string name) where T : Control => form.Controls.Find(name, true).OfType<T>().Single();
    private static ToolStripMenuItem Item(LanguageManagerForm form, string name) => form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
        .SelectMany(item => item.DropDownItems.OfType<ToolStripMenuItem>()).Single(item => item.Name == name);
    private static void Run(Action<LanguageManagerForm, string> action) => UiStaTest.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "beep-languages-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName; var theme = BeepThemesManager.CurrentThemeName;
        Exception? uiFailure = null;
        ThreadExceptionEventHandler report = (_, e) => { uiFailure ??= e.Exception; Beep.Installer.Engine.Diag.Debug("LanguageManagerPresentationTests", "Unexpected owned UI exception", e.Exception); };
        Application.ThreadException += report;
        try
        {
            foreach (var code in new[] { "en", "fr", "ar" })
                new XDocument(new XElement("root", new XElement("data", new XAttribute("name", "Common_Cancel"), new XElement("value", code == "fr" ? "Annuler" : "Cancel")),
                    new XElement("data", new XAttribute("name", "Common_OK"), new XElement("value", "OK")))).Save(Path.Combine(dir, "Strings_" + code + ".resx"));
            Application.EnableVisualStyles(); LanguageManager.SetLanguage("en"); BeepThemesManager.SetCurrentTheme("ModernTheme");
            static int Listeners(Type type, string field) => ((MulticastDelegate?)type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
            var languages = Listeners(typeof(LanguageManager), "LanguageChanged"); var themes = Listeners(typeof(BeepThemesManager), "ThemeChanged");
            using (var form = new LanguageManagerForm(dir) { ShowMessage = (_, _, buttons) => buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK })
            { form.Show(); Application.DoEvents(); action(form, dir); }
            Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languages);
            Listeners(typeof(BeepThemesManager), "ThemeChanged").Should().Be(themes);
        }
        finally
        {
            Application.ThreadException -= report; LanguageManager.SetLanguage(culture); BeepThemesManager.CurrentThemeName = theme;
            foreach (var file in Directory.EnumerateFiles(dir)) File.Delete(file); Directory.Delete(dir);
            if (uiFailure is not null) ExceptionDispatchInfo.Capture(uiFailure).Throw();
        }
    });
}
