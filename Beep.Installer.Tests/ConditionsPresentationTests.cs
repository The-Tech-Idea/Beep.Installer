using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ConditionsPresentationTests
{
    [Fact]
    public void MissingRuleCanBeRemovedAfterSaveRoutesToItsRow()
    {
        Sta(() =>
        {
            var component = Component();
            using var dialog = new ComponentConditionsDialog(new[] { component }, component);
            dialog.Show();
            component.Conditions[1] = null!;
            var grid = Descendants(dialog).OfType<DataGridView>().Single();
            ((BindingSource)grid.DataSource!).ResetBindings(false);
            var applied = 0; dialog.ApplyDraft = () => { applied++; return true; };
            dialog.Controls.Find("ConditionOk", true).OfType<Button>().Single().PerformClick();
            applied.Should().Be(0); dialog.DialogResult.Should().Be(DialogResult.None);
            grid.CurrentCell!.RowIndex.Should().Be(1);
            dialog.Controls.Find("ConditionPreview", true).OfType<TextBox>().Single().Text
                .Should().Contain(LanguageManager.GetString("Conditions_PreviewNull"));
            var remove = dialog.Controls.Find("ConditionRemove", true).OfType<Button>().Single();
            remove.Enabled.Should().BeTrue(); remove.PerformClick();
            component.Conditions.Should().ContainSingle().Which.Should().NotBeNull();
            dialog.Controls.Find("ConditionOk", true).OfType<Button>().Single().PerformClick();
            applied.Should().Be(1); dialog.DialogResult.Should().Be(DialogResult.OK);
        });
    }

    [Fact]
    public void PreviewKeepsSelectedAuthoredCommandAcrossCultureRefresh()
    {
        Sta(() =>
        {
            var component = Component();
            using var dialog = new ComponentConditionsDialog(new[] { component }, component);
            dialog.Show(); dialog.Activate();
            var preview = dialog.Controls.Find("ConditionPreview", true).OfType<TextBox>().Single();
            var before = JsonSerializer.Serialize(component);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                dialog.Activate(); preview.Focus();
                preview.Select(preview.Text.IndexOf("command.exe", StringComparison.Ordinal), "command.exe".Length);
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                preview.SelectedText.Should().Be("command.exe");
                dialog.Activate(); Application.DoEvents(); preview.Focused.Should().BeTrue();
                JsonSerializer.Serialize(component).Should().Be(before);
            }
        });
    }

    [Fact]
    public void EveryPreviewRuleAndExpressionUsesResourcesAndRetainsAuthoredAndDiagnosticFacts()
    {
        Sta(() =>
        {
            var component = new InstallComponent { Id = "facts" };
            foreach (var type in Enum.GetValues<ConditionType>())
                component.Conditions.Add(new InstallCondition { Type = type, Value = "authored{0}_" + type,
                    Value2 = "expected{1}_" + type, Operator = "%%" });
            component.Conditions.Add(new InstallCondition { Type = ConditionType.Architecture, Value = "" });
            var empty = new InstallComponent { Id = "empty" };
            using var dialog = new ComponentConditionsDialog(new[] { component, empty }, component);
            dialog.Show();
            var preview = dialog.Controls.Find("ConditionPreview", true).OfType<TextBox>().Single();
            var expression = dialog.Controls.Find("ConditionExpression", true).OfType<ComboBox>().Single();
            var picker = dialog.Controls.Find("ConditionComponent", true).OfType<ComboBox>().Single();
            foreach (var mode in Enum.GetValues<ConditionExpressionMode>())
            {
                expression.SelectedItem = mode;
                var before = JsonSerializer.Serialize(component);
                var issues = Beep.Installer.Engine.ConditionListValidator.Validate(component.Conditions);
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    preview.Text.Should().Contain(LanguageManager.GetString("Conditions_Preview" + mode))
                        .And.Contain(LanguageManager.GetString("Conditions_PreviewIssues"));
                    foreach (var condition in component.Conditions)
                    {
                        var key = "Conditions_Preview" + (condition.Type == ConditionType.IsAdmin ? "Admin" : condition.Type.ToString());
                        preview.Text.Should().Contain(string.Format(LanguageManager.CurrentCulture,
                            LanguageManager.GetString(key), condition.Operator, condition.Value, condition.Value2));
                    }
                    foreach (var issue in issues)
                        preview.Text.Should().Contain(AuthoringReportPresentation.Status(issue.Severity.ToString()) +
                            " #" + (issue.Index + 1).ToString(LanguageManager.CurrentCulture) + ": " + issue.Message);
                    JsonSerializer.Serialize(component).Should().Be(before);
                }
            }
            picker.SelectedIndex = 1;
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                preview.Text.Should().Be(LanguageManager.GetString("Conditions_PreviewEmpty"));
                empty.Conditions.Should().BeEmpty();
            }
        });
    }

    [Fact]
    public void FrenchPreviewTranslatesExplanationWhilePreservingRuleFactsAndSelection()
    {
        Sta(() =>
        {
            var component = Component();
            component.Conditions[1].Operator = "%%";
            using var dialog = new ComponentConditionsDialog(new[] { component }, component);
            dialog.Show();
            var preview = dialog.Controls.Find("ConditionPreview", true).OfType<TextBox>().Single();
            preview.Focus(); preview.Select(2, 4);
            var before = JsonSerializer.Serialize(component);
            LanguageManager.SetLanguage("fr"); Application.DoEvents();
            preview.Text.Should().Contain("au moins une règle").And.Contain("command.exe").And.Contain("%%");
            preview.SelectionStart.Should().Be(2); preview.SelectionLength.Should().Be(4);
            JsonSerializer.Serialize(component).Should().Be(before);
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
    public static IEnumerable<object[]> ModalCultures => LanguageManager.SupportedCultures
        .SelectMany(culture => new[] { new object[] { culture, false }, new object[] { culture, true } });

    [Theory]
    [MemberData(nameof(ModalCultures))]
    public void OwnedModalRoutesBlockingRuleThenAllowsCorrectionOrCancel(string culture, bool cancel)
    {
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture);
            var valid = Component(); var invalid = Component(); invalid.Id = "invalid";
            invalid.Conditions[1].Value = "";
            var before = JsonSerializer.Serialize(new[] { valid, invalid });
            var applied = 0;
            using var owner = new Form(); owner.Show();
            using var dialog = new ComponentConditionsDialog(new[] { valid, invalid }, valid);
            dialog.ApplyDraft = () => { applied++; return true; };
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    IsWindowEnabled(owner.Handle).Should().BeFalse();
                    var ok = dialog.Controls.Find("ConditionOk", true).OfType<Button>().Single();
                    ok.PerformClick(); Application.DoEvents();
                    applied.Should().Be(0); dialog.DialogResult.Should().Be(DialogResult.None);
                    dialog.Controls.Find("ConditionComponent", true).OfType<ComboBox>().Single().SelectedIndex.Should().Be(1);
                    Descendants(dialog).OfType<DataGridView>().Single().CurrentCell!.RowIndex.Should().Be(1);
                    ValueField(dialog).Focused.Should().BeTrue();
                    var status = dialog.Controls.Find("ConditionValidation", true).OfType<Label>().Single();
                    status.Text.Should().Contain(LanguageManager.GetString("Conditions_CannotSave"))
                        .And.Contain("invalid").And.Contain("#2");
                    status.AccessibleName.Should().Be(status.Text);
                    JsonSerializer.Serialize(new[] { valid, invalid }).Should().Be(before);
                    if (cancel) dialog.Controls.Find("ConditionCancel", true).OfType<Button>().Single().PerformClick();
                    else
                    {
                        ValueField(dialog).Text = "arm64";
                        dialog.Controls.Find("ConditionOperator", true).OfType<TextBox>().Single().Text = "%%";
                        status.Text.Should().NotContain(LanguageManager.GetString("Conditions_CannotSave"));
                        ok.PerformClick();
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    if (failure is not null && !dialog.IsDisposed)
                    { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                }
            };
            timer.Start();
            dialog.ShowDialog(owner).Should().Be(cancel || failure is not null ? DialogResult.Cancel : DialogResult.OK);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            applied.Should().Be(cancel ? 0 : 1);
            IsWindowEnabled(owner.Handle).Should().BeTrue();
            if (cancel) JsonSerializer.Serialize(new[] { valid, invalid }).Should().Be(before);
            else { invalid.Conditions[1].Value.Should().Be("arm64"); invalid.Conditions[1].Operator.Should().Be("%%"); }
        });
    }

    [Fact]
    public void SaveFindsAnErrorInAnotherComponentAndKeepsTheDraftOpen()
    {
        Sta(() =>
        {
            var valid = Component();
            var invalid = Component(); invalid.Id = "invalid";
            invalid.Conditions[1].Value = "";
            var before = JsonSerializer.Serialize(new[] { valid, invalid });
            var applied = 0;
            using var dialog = new ComponentConditionsDialog(new[] { valid, invalid }, valid);
            dialog.ApplyDraft = () => { applied++; return true; };
            dialog.Show();
            dialog.Controls.Find("ConditionOk", true).OfType<Button>().Single().PerformClick();
            applied.Should().Be(0);
            dialog.Visible.Should().BeTrue();
            dialog.DialogResult.Should().Be(DialogResult.None);
            dialog.Controls.Find("ConditionComponent", true).OfType<ComboBox>().Single().SelectedIndex.Should().Be(1);
            Descendants(dialog).OfType<DataGridView>().Single().CurrentCell!.RowIndex.Should().Be(1);
            ValueField(dialog).Focused.Should().BeTrue();
            JsonSerializer.Serialize(new[] { valid, invalid }).Should().Be(before);
        });
    }

    [Fact]
    public void PopulatedGridSelectsAndEditsTheActualConditionAndRemoveRefreshesRows()
    {
        Sta(() =>
        {
            var component = Component();
            using var dialog = new ComponentConditionsDialog(new[] { component }, component);
            dialog.Show();
            var grid = Descendants(dialog).OfType<DataGridView>().Single();
            grid.Rows.Count.Should().Be(2);
            grid.CurrentCell = grid.Rows[1].Cells[0];
            var field = ValueField(dialog);
            field.Text.Should().Be("x64");
            field.Text = "arm64";
            component.Conditions[1].Value.Should().Be("arm64");
            Descendants(dialog).OfType<Button>().Single(b => b.Text == LanguageManager.GetString("Common_Remove")).PerformClick();
            grid.Rows.Count.Should().Be(1);
            component.Conditions.Should().ContainSingle().Which.Type.Should().Be(ConditionType.CommandReturns);
        });
    }

    [Fact]
    public void CaptionRefreshPreservesSelectedDraftAndNeverEvaluatesCommandCondition()
    {
        Sta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepConditionCaption_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var marker = Path.Combine(root, "evaluated.txt");
            try
            {
                var component = Component();
                using var dialog = new ComponentConditionsDialog(new[] { new InstallComponent { Id = "first" }, component }, component);
                dialog.Show();
                var field = ValueField(dialog);
                field.Text = "cmd.exe /c echo evaluated>\"" + marker + "\"";
                dialog.Activate(); field.Focus(); field.Select(2, 5);
                var before = JsonSerializer.Serialize(component);
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    dialog.Activate(); field.Focus(); field.Select(2, 5);
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    field.Text.Should().Contain(marker);
                    field.SelectionStart.Should().Be(2); field.SelectionLength.Should().Be(5);
                    JsonSerializer.Serialize(component).Should().Be(before);
                    File.Exists(marker).Should().BeFalse();
                    dialog.Text.Should().Be(LanguageManager.GetString("Conditions_ComponentConditionBuilder"));
                    field.AccessibleName.Should().Be(LanguageManager.GetString("Common_Value"));
                    dialog.Controls.Find("ConditionComponent", true).OfType<ComboBox>().Single().SelectedIndex.Should().Be(1);
                    dialog.Controls.Find("ConditionExpression", true).OfType<ComboBox>().Single().SelectedItem.Should().Be(ConditionExpressionMode.Any);
                    dialog.Controls.Find("ConditionPreview", true).OfType<TextBox>().Single().Text.Should().Contain(marker);
                    dialog.RightToLeftLayout.Should().Be(culture == "ar");
                    dialog.Activate(); Application.DoEvents(); field.Focused.Should().BeTrue();
                }
            }
            finally { if (File.Exists(marker)) File.Delete(marker); Directory.Delete(root); }
        });
    }

    [Fact]
    public void PopulatedStatusesFollowCulturePaletteAndDisposeSubscriptions()
    {
        Sta(() =>
        {
            var languageListeners = Listeners(typeof(LanguageManager), "LanguageChanged");
            var paletteListeners = Listeners(typeof(BeepThemesManager), "ThemeChanged");
            var theme = BeepThemesManager.CurrentThemeName;
            var themes = BeepThemesManager.GetThemes();
            var choices = new[] { themes.First(t => t.PanelBackColor.GetBrightness() > .75).ThemeName,
                themes.First(t => t.PanelBackColor.GetBrightness() < .25).ThemeName };
            try
            {
                var component = Component();
                using (var dialog = new ComponentConditionsDialog(new[] { component }, component))
                {
                    dialog.Show();
                    var grid = Descendants(dialog).OfType<DataGridView>().Single();
                    grid.CurrentCell = grid.Rows[1].Cells[0];
                    var value = ValueField(dialog);
                    var op = dialog.Controls.Find("ConditionOperator", true).OfType<TextBox>().Single();
                    var status = dialog.Controls.Find("ConditionValidation", true).OfType<Label>().Single();
                    foreach (var state in new[] { "clean", "warning", "error" })
                    {
                        value.Text = state == "error" ? "" : "x64";
                        op.Text = state == "warning" ? "%%" : "==";
                        var issues = Beep.Installer.Engine.ConditionListValidator.Validate(component.Conditions);
                        var errors = issues.Count(i => i.Severity == Beep.Installer.Engine.ConditionListValidator.IssueSeverity.Error);
                        var warnings = issues.Count - errors;
                        foreach (var culture in LanguageManager.SupportedCultures)
                        {
                            LanguageManager.SetLanguage(culture); Application.DoEvents();
                            status.Text.Should().Contain(LanguageManager.GetString("Common_Component")).And.Contain(component.Id);
                            status.AccessibleName.Should().Be(status.Text);
                            if (state != "clean") status.Text.Should().Contain(string.Format(LanguageManager.CurrentCulture,
                                LanguageManager.GetString("Report_DiagnosticCounts"), errors, warnings));
                            foreach (var choice in choices)
                            {
                                BeepThemesManager.CurrentThemeName = choice; Application.DoEvents();
                                status.ForeColor.Should().Be(InstallerTheme.AccessibleForeground(errors > 0 ? InstallerTheme.Danger :
                                    warnings > 0 ? InstallerTheme.Warning : InstallerTheme.Success, InstallerTheme.Panel));
                            }
                        }
                    }
                }
                Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languageListeners);
                Listeners(typeof(BeepThemesManager), "ThemeChanged").Should().Be(paletteListeners);
            }
            finally { BeepThemesManager.CurrentThemeName = theme; }
        });
    }

    [Fact]
    public void MinimumWindowAndLargeMultilineCaptionsRetainAccessibleEditors()
    {
        Sta(() =>
        {
            using var font = new System.Drawing.Font("Segoe UI", 15);
            var component = Component();
            using var dialog = new ComponentConditionsDialog(new[] { component }, component);
            dialog.Font = font; dialog.Size = dialog.MinimumSize; dialog.Show();
            var caption = dialog.Controls.Find("Caption_Common_Value", true).OfType<Label>().Single();
            caption.Text = "A long translated field caption\nwith a second line\nand a third line";
            Application.DoEvents(); dialog.PerformLayout();
            caption.Height.Should().BeGreaterOrEqualTo(caption.GetPreferredSize(new System.Drawing.Size(caption.Width, 0)).Height);
            var editor = ValueField(dialog);
            editor.Width.Should().BeGreaterThan(100);
            editor.Focus().Should().BeTrue();
            dialog.Controls.Find("ConditionOk", true).Single().Visible.Should().BeTrue();
            dialog.Controls.Find("ConditionCancel", true).Single().Visible.Should().BeTrue();
        });
    }

    private static int Listeners(Type type, string name) => ((MulticastDelegate?)type
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    private static InstallComponent Component()
    {
        var component = new InstallComponent { Id = "second", Name = "Authored component", ConditionExpression = ConditionExpressionMode.Any };
        component.Conditions.Add(new InstallCondition { Type = ConditionType.CommandReturns, Value = "command.exe", Value2 = "0", Operator = "==" });
        component.Conditions.Add(new InstallCondition { Type = ConditionType.Architecture, Value = "x64", Operator = "==" });
        return component;
    }

    private static TextBox ValueField(Form dialog) => Descendants(dialog).OfType<TextBox>().Single(box => box.DataBindings.Cast<Binding>()
        .Any(binding => binding.BindingMemberInfo.BindingField == "Value"));

    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
}
