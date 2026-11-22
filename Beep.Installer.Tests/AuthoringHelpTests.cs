using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringHelpTests
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(culture); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(45)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static TextBox Body(Form form) => form.Controls.Find("HelpBody", true).OfType<TextBox>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Theory]
    [InlineData("open port", "firewallrules")]
    [InlineData("scheduled job", "scheduledtasks")]
    [InlineData("default program", "fileassociations")]
    [InlineData("digital signature", "codesign")]
    [InlineData("web server", "iissites")]
    [InlineData("background process", "windowsservices")]
    [InlineData("installation location", "layout")]
    public void UsefulAliasesMatchActualDestinations(string query, string id)
    {
        RunSta(() =>
        {
            using var form = new PackageBuilderForm(new InstallerController());
            var nav = Field<LeftNavPanel>(form, "_nav"); nav.FilterText = query; nav.VisibleSectionIds.Should().Contain(id);
            var topic = AuthoringHelpCatalog.Find(id); topic.Availability.Should().NotBeNullOrWhiteSpace();
            nav.SelectSection(id); form.ActiveSectionId.Should().Be(id);
        });
    }

    [Fact]
    public void AllNavigationTopicsHavePackagedLocalizedGuidanceAndDeclaredViewAvailability()
    {
        RunSta(() =>
        {
            using var form = new PackageBuilderForm(new InstallerController());
            AuthoringHelpCatalog.Topics.Select(topic => topic.Id).Should().BeEquivalentTo(form.SectionIds);
            AuthoringHelpCatalog.Find("codesign").GuidedKey.Should().Be("Guided_Signing");
            AuthoringHelpCatalog.Find("windowsservices").GuidedKey.Should().BeNull();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture);
                foreach (var topic in AuthoringHelpCatalog.Topics)
                { topic.Title.Should().NotBe(topic.TitleKey); topic.Body.Should().NotBe(topic.BodyKey); topic.Body.Should().NotBeNullOrWhiteSpace(); }
            }
            LanguageManager.SetLanguage("ar");
            Field<LeftNavPanel>(form, "_nav").FilterText = "المنافذ";
            Field<LeftNavPanel>(form, "_nav").VisibleSectionIds.Should().Contain("firewallrules");
        });
    }

    [Fact]
    public void FilteringAndLanguageChangesDoNotReopenTheActiveEditorOrLoseEdits()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var form = new PackageBuilderForm(controller); form.Show();
            form.OpenSection("identity"); var editor = form.ActiveSection; var nav = Field<LeftNavPanel>(form, "_nav");
            var name = editor!.Controls.Find("AppName", true).OfType<TextBox>().Single(); name.Text = "edited name";
            var events = 0; nav.SectionSelected += (_, _) => events++;
            nav.FilterText = "product name"; nav.FilterText = "unmatched-text-123"; nav.FilterText = "";
            LanguageManager.SetLanguage("ar"); nav.SelectedSectionId.Should().Be("identity"); events.Should().Be(0);
            form.ActiveSection.Should().BeSameAs(editor); name.Text.Should().Be("edited name");
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void HelpIsReadOnlySearchableAndTranslatesWithoutChangingTheSelectedTopic()
    {
        RunSta(() =>
        {
            using var dialog = new AuthoringHelpDialog("codesign"); dialog.Show();
            Body(dialog).ReadOnly.Should().BeTrue(); Body(dialog).Text.Should().Contain(AuthoringHelpCatalog.Find("codesign").Title).And.Contain("Signing");
            LanguageManager.SetLanguage("ar"); Body(dialog).Text.Should().Contain(AuthoringHelpCatalog.Find("codesign").Body); dialog.RightToLeft.Should().Be(RightToLeft.Yes);
            var search = dialog.Controls.Find("HelpSearch", true).OfType<TextBox>().Single(); search.Text = "open port";
            Body(dialog).Text.Should().Contain(AuthoringHelpCatalog.Find("firewallrules").Body);
            var topics = dialog.Controls.Find("HelpTopics", true).OfType<ListView>().Single();
            topics.Items.Cast<ListViewItem>().Should().Contain(item => item.Tag!.ToString() == "firewallrules" && item.SubItems[1].Text == LanguageManager.GetString("Help_Advanced"));
            search.Text = "no-matching-topic-123"; Body(dialog).Text.Should().Be(LanguageManager.GetString("Help_NoMatches"));
            dialog.Close(); LanguageManager.SetLanguage("de");
        });
    }

    [Theory]
    [InlineData("en", "open port", "scheduled job")]
    [InlineData("ar", "فتح منفذ", "مهمة مجدولة")]
    [InlineData("de", "Port öffnen", "geplanter Auftrag")]
    [InlineData("es", "abrir puerto", "trabajo programado")]
    [InlineData("fr", "ouvrir un port", "tâche planifiée")]
    [InlineData("ja", "ポートを開く", "定期ジョブ")]
    [InlineData("pt", "abrir porta", "tarefa agendada")]
    [InlineData("zh", "开放端口", "计划任务")]
    public void SuggestedLocalizedTasksMatchInAllEightLanguages(string culture, string port, string job)
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage(culture);
            AuthoringHelpCatalog.Matches(AuthoringHelpCatalog.Find("firewallrules"), port).Should().BeTrue();
            AuthoringHelpCatalog.Matches(AuthoringHelpCatalog.Find("scheduledtasks"), job).Should().BeTrue();
        });
    }

    [Fact]
    public void NewHelpResourcesAreEmbeddedAndKeepTheirFormatArguments()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(AuthoringHelpDialog).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>()
                .Where(entry => new[] { "Help_", "HelpBody_", "HelpAliases_" }.Any(prefix => ((string)entry.Key).StartsWith(prefix, StringComparison.Ordinal)))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
        var english = Read("en"); english.Should().HaveCount(43);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var pair in english)
            {
                translated[pair.Key].Should().NotBeNullOrWhiteSpace();
                System.Text.CompositeFormat.Parse(translated[pair.Key]).MinimumArgumentCount.Should()
                    .Be(System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount);
            }
        }
    }

    [Fact]
    public void SearchShowsViewAvailabilityAndEmptyStateAndSurvivesThemeChanges()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var form = new PackageBuilderForm(controller); form.Show(); form.OpenSection("identity"); // the section panel is hidden until a project is open
            var nav = Field<LeftNavPanel>(form, "_nav"); nav.FilterText = "digital signature";
            var list = nav.Controls.OfType<ListView>().Single(); list.Columns[1].Width.Should().BeGreaterThan(0);
            var row = list.Items.Cast<ListViewItem>().Single(item => item.Tag!.ToString() == "codesign");
            row.SubItems[1].Text.Should().Be(LanguageManager.GetString("Help_GuidedAdvanced")); row.ToolTipText.Should().Contain(LanguageManager.GetString("Guided_Signing"));
            nav.FilterText = "no-result-123"; nav.Controls.OfType<Label>().Should().Contain(label => label.Visible && label.Text == LanguageManager.GetString("Help_NoMatches"));
            var theme = BeepThemesManager.CurrentThemeName; nav.BackColor = Color.Magenta; BeepThemesManager.CurrentThemeName = theme;
            nav.BackColor.Should().Be(InstallerTheme.Sidebar); nav.FilterText.Should().Be("no-result-123");
            using var help = new AuthoringHelpDialog("codesign"); help.Show(); help.BackColor = Color.Magenta;
            BeepThemesManager.CurrentThemeName = theme; help.BackColor.Should().Be(InstallerTheme.Panel); Body(help).Text.Should().Contain(AuthoringHelpCatalog.Find("codesign").Body);
            help.Close(); controller.Project.MarkClean();
        });
    }

    [Fact]
    public void ActualF1EntryPointsShowTheCurrentTopicAndPreserveAnUncreatedGuidedDraft()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var advanced = new PackageBuilderForm(controller); advanced.Show(); advanced.OpenSection("firewallrules");
            void Opens(Action open, string expected, bool search = false)
            {
                var seen = false; Exception? dialogFailure = null; using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<AuthoringHelpDialog>().SingleOrDefault(); if (dialog is null) return;
                    seen = true; timer.Stop();
                    try { Body(dialog).Text.Should().Contain(AuthoringHelpCatalog.Find(expected).Body);
                        if (search) dialog.Controls.Find("HelpSearch", true).Single().Focused.Should().BeTrue(); }
                    catch (Exception ex) { dialogFailure = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start(); open(); seen.Should().BeTrue(); if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
            }
            var keys = new KeyEventArgs(Keys.F1);
            Opens(() => typeof(PackageBuilderForm).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(object), typeof(KeyEventArgs) }, null)!.Invoke(advanced, new object[] { advanced, keys }), "firewallrules");
            using var guided = new BuilderWizardForm(controller, advanced, startNew: true); guided.Show();
            Field<TextBox>(guided, "_nameBox").Text = "unsaved draft"; var before = JsonSerializer.Serialize(controller.Project);
            void Key(Keys key) => typeof(BuilderWizardForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(guided, new object[] { default(Message), key });
            Opens(() => Key(Keys.F1), "identity"); Opens(() => Key(Keys.Control | Keys.F), "identity", search: true);
            Field<TextBox>(guided, "_nameBox").Text.Should().Be("unsaved draft"); JsonSerializer.Serialize(controller.Project).Should().Be(before); controller.Project.MarkClean();
        });
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void HelpRemainsUsableAtSupportedScales(string culture, float scale)
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage(culture); using var dialog = new AuthoringHelpDialog("codesign");
            dialog.Size = dialog.MinimumSize; dialog.Show(); dialog.Scale(new SizeF(scale, scale)); dialog.PerformLayout(); Application.DoEvents();
            foreach (var control in Descendants(dialog).Where(control => control is Button or TextBox or ListView))
                dialog.ClientRectangle.Contains(dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))).Should().BeTrue(control.AccessibleName);
            Body(dialog).ScrollBars.Should().Be(ScrollBars.Vertical); Body(dialog).Text.Should().Contain(AuthoringHelpCatalog.Find("codesign").Body);
        });
    }
}
