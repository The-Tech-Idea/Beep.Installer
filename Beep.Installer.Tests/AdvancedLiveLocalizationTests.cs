using System.Reflection;
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
public sealed class AdvancedLiveLocalizationTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Find<T>(Control owner, string name) where T : Control => owner.Controls.Find(name, true).OfType<T>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void Sta(Action action)
    {
        UiStaTest.Run(() =>
        {
            var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            finally { LanguageManager.SetLanguage(culture); }
        });
    }
    [Fact]
    public void GroupHeadersTranslateWithoutReopeningTheEditorOrExpandingCollapsedGroups()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
            var panel = editor.ActiveSection; var name = Find<TextBox>(panel!, "AppName"); name.Text = "Unchanged draft"; name.Focus();
            var nav = Field<LeftNavPanel>(editor, "_nav"); var list = Field<ListView>(nav, "_list");
            list.Groups.Cast<ListViewGroup>().Single(group => group.Name == "advanced").CollapsedState = ListViewGroupCollapsedState.Collapsed;
            name.Focus(); name.Focused.Should().BeTrue("the field starts focused before language changes");
            name.Select(3, 7);
            var events = 0; nav.SectionSelected += (_, _) => events++;
            var keys = new Dictionary<string, string> { ["project"] = "Nav_Project", ["source"] = "Nav_Source", ["features"] = "Nav_Features", ["advanced"] = "Nav_AdvancedResources",
                ["customize"] = "Nav_Customize", ["output"] = "Nav_Output", ["deployment"] = "Nav_Deployment", ["build"] = "Nav_Build" };
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                foreach (ListViewGroup group in list.Groups) group.Header.Should().Be(LanguageManager.GetString(keys[group.Name!]));
                list.Groups.Cast<ListViewGroup>().Single(group => group.Name == "advanced").CollapsedState.Should().Be(ListViewGroupCollapsedState.Collapsed);
                editor.ActiveSection.Should().BeSameAs(panel); name.Text.Should().Be("Unchanged draft"); name.Focused.Should().BeTrue("culture {0}, active control {1}", culture, editor.ActiveControl?.Name);
                name.SelectionStart.Should().Be(3); name.SelectionLength.Should().Be(7);
                name.AccessibleName.Should().Be(LanguageManager.GetString("Field_ProductName").TrimEnd(':'));
                editor.RightToLeft.Should().Be(LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No);
                list.RightToLeftLayout.Should().Be(LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
            }
            events.Should().Be(0); nav.SelectedSectionId.Should().Be("identity");
        });
    }
    [Fact]
    public void FocusRestorationRespectsLaterNavigationOtherWindowsAndControlOwnership()
    {
        Sta(() =>
        {
            using var owner = new Form(); using var other = new Form();
            using var first = new TextBox { Text = "Authored value", Top = 10 };
            using var second = new TextBox { Top = 50 };
            using var otherField = new TextBox(); other.Controls.Add(otherField);
            owner.Controls.AddRange(new Control[] { first, second }); owner.Show(); first.Focus();
            var bookmark = UiFocusBookmark.Capture(owner); bookmark.Should().NotBeNull();
            owner.RightToLeft = RightToLeft.Yes; owner.RightToLeftLayout = true; bookmark!.Restore();
            second.Focus(); Application.DoEvents(); second.Focused.Should().BeTrue("a later field choice supersedes queued restoration");

            first.Focus(); bookmark = UiFocusBookmark.Capture(owner); bookmark!.Restore();
            first.Select(7, 2); Application.DoEvents();
            first.SelectionStart.Should().Be(7); first.SelectionLength.Should().Be(2);

            first.Focus(); bookmark = UiFocusBookmark.Capture(owner); bookmark!.Restore();
            other.Show(); other.Activate(); otherField.Focus(); otherField.Focused.Should().BeTrue(); Application.DoEvents();
            otherField.Focused.Should().BeTrue("queued restoration must not reactivate a different window");

            owner.Activate(); first.Focus(); bookmark = UiFocusBookmark.Capture(owner);
            other.Controls.Add(first); owner.Activate(); second.Focus(); bookmark!.Restore(); Application.DoEvents();
            second.Focused.Should().BeTrue("a bookmark cannot follow a field to a different form");
            owner.Controls.Add(first); first.Focus(); bookmark = UiFocusBookmark.Capture(owner);
            first.Dispose(); second.Focus(); bookmark!.Restore(); Application.DoEvents(); second.Focused.Should().BeTrue();
            owner.Hide(); UiFocusBookmark.Capture(owner).Should().BeNull();
        });
    }
    [Fact]
    public void BoundEnumCheckboxFileAndFolderCaptionsRefreshWithoutChangingValuesOrPanels()
    {
        Sta(() =>
        {
            using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.OpenSection("layout");
            var layout = editor.ActiveSection!; var folder = Find<TextBox>(layout, "DefaultDirName"); folder.Text = @"C:\Authored\Path";
            var scope = Find<ComboBox>(layout, "DefaultScope"); var selection = scope.SelectedItem;
            var checkbox = Find<CheckBox>(layout, "AllowScopeSelection"); checkbox.Checked = true;
            editor.OpenSection("codesign"); var signing = editor.ActiveSection!; var certificate = Find<TextBox>(signing, "CodeSignCertificatePath"); certificate.Text = @"C:\Authored\test.pfx";
            var before = InstallerScriptSerializer.Write(editor.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                folder.AccessibleName.Should().Be(LanguageManager.GetString("Field_DefaultInstallPath").TrimEnd(':'));
                scope.AccessibleName.Should().Be(LanguageManager.GetString("Field_Scope").TrimEnd(':')); scope.SelectedItem.Should().Be(selection);
                checkbox.Text.Should().Be(LanguageManager.GetString("Field_AllowScopeSelection").TrimEnd(':')); checkbox.Checked.Should().BeTrue();
                certificate.AccessibleName.Should().Be(LanguageManager.GetString("Field_CertificatePfx").TrimEnd(':'));
                Descendants(signing).OfType<Button>().Should().Contain(button => button.Text == "..." && button.AccessibleName!.Contains(certificate.AccessibleName!));
                folder.Text.Should().Be(@"C:\Authored\Path"); certificate.Text.Should().Be(@"C:\Authored\test.pfx");
                InstallerScriptSerializer.Write(editor.Project).Should().Be(before);
                editor.ActiveSection.Should().BeSameAs(signing);
            }
            editor.OpenSection("layout"); editor.ActiveSection.Should().BeSameAs(layout);
        });
    }
    [Fact]
    public void CachedResourceHeadingsActionsAndAccessibleNamesTranslateWithoutLosingSelection()
    {
        Sta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
            project.WindowsServices.Add(new() { Name = "First", ExecutablePath = "first.exe" }); project.WindowsServices.Add(new() { Name = "Second", ExecutablePath = "second.exe" });
            using var editor = new PackageBuilderForm(new InstallerController(project)); editor.Show(); editor.OpenSection("windowsservices");
            var panel = editor.ActiveSection!; var grid = Descendants(panel).OfType<DataGridView>().Single(); var binding = (BindingSource)grid.DataSource!; binding.Position = 1;
            var selected = binding.Current; var before = InstallerScriptSerializer.Write(project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                Find<Label>(panel, "SectionTitle").Text.Should().Be(LanguageManager.GetString("Section_WindowsServices"));
                Find<Label>(panel, "SectionHelp").Text.Should().Be(LanguageManager.GetString("Section_WindowsServicesHelp"));
                grid.AccessibleName.Should().Be(LanguageManager.GetString("Section_WindowsServices"));
                Find<PropertyGrid>(panel, "ResourceProperties").AccessibleName.Should().Be(LanguageManager.GetString("Resources_Properties") + ": " + grid.AccessibleName);
                foreach (var key in new[] { "Btn_Add", "Builder_Duplicate", "Btn_Remove" }) Descendants(panel).OfType<Button>().Should().Contain(button => button.Text == LanguageManager.GetString(key));
                editor.ActiveSection.Should().BeSameAs(panel); binding.Current.Should().BeSameAs(selected); InstallerScriptSerializer.Write(project).Should().Be(before);
            }
        });
    }
    [Theory]
    [InlineData("scheduledtasks", "Section_ScheduledTasks")]
    [InlineData("firewallrules", "Section_FirewallRules")]
    [InlineData("certificates", "Section_Certificates")]
    [InlineData("comregistrations", "Section_ComRegistrations")]
    [InlineData("driverpackages", "Section_DriverPackages")]
    [InlineData("configtransforms", "Section_ConfigTransforms")]
    [InlineData("iisapppools", "Section_IisAppPools")]
    [InlineData("iissites", "Section_IisSites")]
    [InlineData("webdeploy", "Section_WebDeploy")]
    public void PopulatedSystemResourceEditorsTranslateAndRetainSelectedAuthoredRows(string section, string titleKey)
    {
        Sta(() =>
        {
            int Listeners() => ((MulticastDelegate?)typeof(LanguageManager).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
            var original = Listeners();
            using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.OpenSection(section);
            var panel = editor.ActiveSection!;
            var add = Descendants(panel).OfType<Button>().Single(button => button.Text == LanguageManager.GetString("Btn_Add"));
            add.PerformClick(); add.PerformClick(); Application.DoEvents();
            var grid = Descendants(panel).OfType<DataGridView>().Single();
            var binding = (BindingSource)grid.DataSource!; binding.Position = 1;
            var selected = binding.Current!;
            var property = new[] { "Name", "Description", "SourcePath" }.Select(name => selected.GetType().GetProperty(name))
                .First(info => info?.PropertyType == typeof(string) && info.CanWrite)!;
            property.SetValue(selected, "Authored resource Ω"); binding.ResetCurrentItem();
            var props = Find<PropertyGrid>(panel, "ResourceProperties");
            props.SelectedObject.Should().BeSameAs(selected); grid.Rows.Count.Should().Be(2);
            Application.DoEvents(); editor.Activate(); Application.DoEvents();
            grid.Focus(); grid.Focused.Should().BeTrue();
            var before = InstallerScriptSerializer.Write(editor.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                var beforeFocus = editor.ContainsFocus; var beforeActive = Form.ActiveForm == editor; var beforeHandle = editor.Handle;
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                var title = Find<Label>(panel, "SectionTitle"); var help = Find<Label>(panel, "SectionHelp");
                title.Text.Should().Be(LanguageManager.GetString(titleKey)); title.AccessibleName.Should().Be(title.Text);
                help.Text.Should().Be(LanguageManager.GetString(titleKey + "Help")); help.AccessibleName.Should().Be(help.Text);
                grid.AccessibleName.Should().Be(title.Text);
                props.AccessibleName.Should().Be(LanguageManager.GetString("Resources_Properties") + ": " + title.Text);
                binding.Position.Should().Be(1); binding.Current.Should().BeSameAs(selected); props.SelectedObject.Should().BeSameAs(selected);
                if (!editor.ContainsFocus && Form.ActiveForm != editor)
                {
                    editor.ActiveControl.Should().NotBeNull("the focused target is retained when Windows deactivates a recreated form");
                    editor.Activate(); Application.DoEvents();
                }
                grid.Focused.Should().BeTrue("the selected resource editor retains keyboard focus in {0}; owner focus={1}, active owner={2}, focused controls={3}, active control={4}; before focus={5}, before active={6}, handle recreated={7}",
                    culture, editor.ContainsFocus, Form.ActiveForm == editor,
                    string.Join(", ", Descendants(editor).Where(control => control.Focused).Select(control => control.Name + ":" + control.GetType().Name)), editor.ActiveControl?.Name,
                    beforeFocus, beforeActive, beforeHandle != editor.Handle);
                grid.Rows.Count.Should().Be(2); property.GetValue(selected).Should().Be("Authored resource Ω");
                InstallerScriptSerializer.Write(editor.Project).Should().Be(before); editor.ActiveSection.Should().BeSameAs(panel);
            }
            editor.OpenSection("identity"); LanguageManager.SetLanguage("de"); Application.DoEvents();
            Find<Label>(panel, "SectionHelp").Text.Should().Be(LanguageManager.GetString(titleKey + "Help"));
            editor.OpenSection(section); editor.ActiveSection.Should().BeSameAs(panel); binding.Current.Should().BeSameAs(selected);
            editor.Dispose(); panel.IsDisposed.Should().BeTrue(); Listeners().Should().Be(original);
        });
    }
    [Fact]
    public void ComEditorFocusAndSubscriptionsSurviveRepeatedWindowAndLanguageCycles()
    {
        for (var cycle = 0; cycle < 4; cycle++)
            PopulatedSystemResourceEditorsTranslateAndRetainSelectedAuthoredRows("comregistrations", "Section_ComRegistrations");
    }

    [Fact]
    public void CaptionSubscriptionsEndWithTheirControlsAndAccessibleBindingNeverChangesInputText()
    {
        Sta(() =>
        {
            int Listeners() => ((MulticastDelegate?)typeof(LanguageManager).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
            var original = Listeners();
            using var box = new TextBox { Text = "authored text" };
            new LiveUiCaption("Field_ProductName", "Product name:").Bind(box, text: false, trimColon: true);
            Listeners().Should().Be(original + 1); LanguageManager.SetLanguage("ar"); box.Text.Should().Be("authored text");
            box.Dispose(); Listeners().Should().Be(original); Action change = () => LanguageManager.SetLanguage("de"); change.Should().NotThrow();
        });
    }
    [Fact]
    public void ToolbarSearchTitleAndRecentMenusTranslateWithoutChangingTheProjectOrLeakingBindings()
    {
        Sta(() =>
        {
            int Listeners() => ((MulticastDelegate?)typeof(LanguageManager).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;
            var original = Listeners();
            using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.OpenSection("identity");
            var before = InstallerScriptSerializer.Write(editor.Project); var panel = editor.ActiveSection;
            var toolbar = Field<ToolStrip>(editor, "_toolbar"); var search = Find<TextBox>(editor, "AuthoringSectionSearch");
            var recent = Field<ToolStripMenuItem>(editor, "_recentsBtn");
            var hints = new Dictionary<string, string> { ["Toolbar_New"] = "Toolbar_NewHint", ["Toolbar_Open"] = "Toolbar_OpenHint", ["Common_Save"] = "Toolbar_SaveHint",
                ["Builder_SaveAsTitle"] = "Toolbar_SaveAsHint", ["Packaging_Button"] = "Packaging_ButtonHint", ["Signing_Button"] = "Signing_ButtonHint", ["Toolbar_Guided"] = "Toolbar_GuidedHint",
                ["Quick_Button"] = "Quick_ButtonHint", ["Toolbar_Preview"] = "Toolbar_PreviewHint", ["Builder_BuildTitle"] = "Toolbar_BuildHint", ["Builder_PublishTitle"] = "Toolbar_PublishHint",
                ["Update_Title"] = "Update_Instructions", ["Toolbar_Templates"] = "Toolbar_TemplatesHint", ["Toolbar_Actions"] = "Toolbar_ActionsHint", ["Recipe_Title"] = "Recipe_Preview",
                ["Release_Title"] = "Release_Notice", ["Comparison_Title"] = "Comparison_Select", ["Toolbar_Conditions"] = "Toolbar_ConditionsHint", ["Toolbar_Languages"] = "AuthoringLanguage_Title",
                ["Toolbar_About"] = "Builder_AboutTitle", ["Toolbar_Help"] = "Toolbar_HelpHint", ["Recovery_Title"] = "Recovery_Help" };
            search.Text = "identity"; search.Focus(); search.Select(2, 4);
            var recentPaths = recent.DropDownItems.Cast<ToolStripItem>().Where(item => string.IsNullOrEmpty(item.Name) && item is ToolStripMenuItem).ToDictionary(item => item, item => item.Text);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                foreach (var button in toolbar.Items.OfType<ToolStripButton>())
                {
                    button.Name.Should().NotBeNullOrWhiteSpace(); button.Text.Should().Be(LanguageManager.GetString(button.Name!)); button.AccessibleName.Should().Be(button.Text);
                    if (hints.TryGetValue(button.Name!, out var key)) button.ToolTipText.Should().Be(LanguageManager.GetString(key));
                }
                recent.Text.Should().Be(LanguageManager.GetString("Builder_Recent")); recent.AccessibleName.Should().Be(recent.Text);
                recent.ToolTipText.Should().Be(LanguageManager.GetString("Builder_RecentlyOpenedInstallerScripts"));
                foreach (var item in recent.DropDownItems.Cast<ToolStripItem>().Where(item => !string.IsNullOrEmpty(item.Name))) item.Text.Should().Be(LanguageManager.GetString(item.Name!));
                foreach (var pair in recentPaths) pair.Key.Text.Should().Be(pair.Value);
                search.PlaceholderText.Should().Be(LanguageManager.GetString("Builder_SearchSections")); search.AccessibleName.Should().Be(search.PlaceholderText);
                search.Text.Should().Be("identity"); search.Focused.Should().BeTrue(); search.SelectionStart.Should().Be(2); search.SelectionLength.Should().Be(4);
                editor.Text.Should().Contain(LanguageManager.GetString("Builder_BeepInstallerPackageBuilder")).And.Contain(LanguageManager.GetString("SaveState_Untitled"));
                editor.ActiveSection.Should().BeSameAs(panel); InstallerScriptSerializer.Write(editor.Project).Should().Be(before);
            }
            var bound = Listeners();
            for (var i = 0; i < 5; i++) typeof(PackageBuilderForm).GetMethod("RefreshRecents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            Listeners().Should().Be(bound, "replaced Recent menu items must dispose their translation subscriptions");
            editor.Dispose(); Listeners().Should().Be(original);
        });
    }
    [Fact]
    public void GuidedLanguageSwitchPreservesFocusedRebuiltFieldsTemplateAndStepper()
    {
        Sta(() =>
        {
            using var wizard = new BuilderWizardForm(new InstallerController()); wizard.Show();
            var name = Find<TextBox>(wizard, "AppName"); name.Text = "Pending project name";
            Find<TextBox>(wizard, "AppVersion").Text = "3.2.1";
            var templates = Find<ComboBox>(wizard, "CreationTemplate"); templates.SelectedIndex = 2;
            var templateId = ((ProjectTemplate)templates.SelectedItem!).Id;
            name.Focus(); name.Select(3, 8);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                var oldName = name; var changed = LanguageManager.CurrentCulture.TwoLetterISOLanguageName != culture;
                LanguageManager.SetLanguage(culture); Application.DoEvents(); name = Find<TextBox>(wizard, "AppName");
                if (changed) oldName.IsDisposed.Should().BeTrue();
                name.Text.Should().Be("Pending project name"); name.Focused.Should().BeTrue("culture {0}", culture); name.SelectionStart.Should().Be(3); name.SelectionLength.Should().Be(8);
                Find<TextBox>(wizard, "AppVersion").Text.Should().Be("3.2.1"); ((ProjectTemplate)Find<ComboBox>(wizard, "CreationTemplate").SelectedItem!).Id.Should().Be(templateId);
            }
            Find<ComboBox>(wizard, "CreationTemplate").Focus(); LanguageManager.SetLanguage("ar"); Application.DoEvents();
            Find<ComboBox>(wizard, "CreationTemplate").Focused.Should().BeTrue(); ((ProjectTemplate)Find<ComboBox>(wizard, "CreationTemplate").SelectedItem!).Id.Should().Be(templateId);
            Find<Button>(wizard, "GuidedStep_project").Focus(); LanguageManager.SetLanguage("de"); Application.DoEvents();
            Find<Button>(wizard, "GuidedStep_project").Focused.Should().BeTrue();
        });
    }
    [Fact]
    public void NewAdvancedGroupAndEnumCaptionKeysAreEmbeddedInAllEightLanguages()
    {
        var keys = new[] { "Nav_AdvancedResources", "Field_UpdateMode", "Field_InstallType", "Field_Privileges", "Field_Scope", "Field_Theme", "Field_TargetArchitecture",
            "Field_PayloadSource", "Field_CompressionFormat", "Field_CompressionStrength", "Field_OutputFormat", "Builder_NoRecentScripts", "Builder_ClearRecent",
            "Builder_ResultMetrics", "Builder_ResultTime", "Builder_ResultIssues", "Builder_LastBuildArtifact",
            "Builder_IncludePatterns", "Builder_ExcludePatterns", "Builder_MoveUp", "Builder_MoveDown", "Builder_ScanningSource", "Builder_ScanComplete", "Builder_ScanFailedStatus",
            "Builder_KeyboardTitle", "Builder_ValidationTitle", "Builder_FormatReadinessTitle", "Builder_KeyboardSearchHelp", "Builder_KeyboardSections", "Builder_KeyboardFields",
            "Section_ScheduledTasksHelp", "Section_FirewallRulesHelp", "Section_CertificatesHelp", "Section_ComRegistrationsHelp", "Section_DriverPackagesHelp",
            "Section_ConfigTransformsHelp", "Section_IisAppPoolsHelp", "Section_IisSitesHelp", "Section_WebDeployHelp", "Builder_ScanDiscarded" };
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            var values = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string);
            foreach (var key in keys) { values.Should().ContainKey(key); values[key].Should().NotBeNullOrWhiteSpace(); }
        }
    }
}
