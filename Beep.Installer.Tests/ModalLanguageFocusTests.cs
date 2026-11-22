using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ModalLanguageFocusTests
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(previous); }
    });

    private static void InModal(Form owner, Action<Form, TextBox> action, bool ownerEnabledOnReturn = true)
    {
        using var dialog = new Form();
        var field = new TextBox { Text = "Dialog input", Dock = DockStyle.Top };
        dialog.Controls.Add(field);
        Exception? failure = null;
        dialog.Shown += (_, _) => dialog.BeginInvoke(() =>
        {
            try
            {
                dialog.Activate(); field.Focus(); Application.DoEvents();
                IsWindowEnabled(owner.Handle).Should().BeFalse("ShowDialog disables its native owner window");
                field.Focused.Should().BeTrue();
                action(dialog, field);
            }
            catch (Exception ex) { failure = ex; }
            finally { dialog.Close(); }
        });
        dialog.ShowDialog(owner);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        IsWindowEnabled(owner.Handle).Should().Be(ownerEnabledOnReturn);
        if (ownerEnabledOnReturn) owner.Activate();
        Application.DoEvents();
    }

    [Fact]
    public void BookmarkRetainsDisabledModalOwnerTargetAndSelectionWithoutTakingDialogFocus()
    {
        Sta(() =>
        {
            using var owner = new Form();
            var field = new TextBox { Text = "Authored selection", Dock = DockStyle.Top };
            owner.Controls.Add(field); owner.Show(); owner.Activate(); field.Focus(); field.Select(3, 6);
            InModal(owner, (_, modalField) =>
            {
                var bookmark = UiFocusBookmark.Capture(owner); bookmark.Should().NotBeNull();
                owner.RightToLeft = RightToLeft.Yes; owner.RightToLeftLayout = true;
                owner.ActiveControl = null; field.Select(0, 0);
                bookmark!.Restore(); Application.DoEvents();
                modalField.Focused.Should().BeTrue(); owner.ContainsFocus.Should().BeFalse();
                owner.ActiveControl.Should().BeSameAs(field);
                field.SelectionStart.Should().Be(3); field.SelectionLength.Should().Be(6);
            });
            field.Focused.Should().BeTrue(); field.SelectionStart.Should().Be(3); field.SelectionLength.Should().Be(6);
        });
    }

    [Theory]
    [InlineData(false, "AppName")]
    [InlineData(true, "AppName")]
    [InlineData(true, "AppVersion")]
    [InlineData(true, "AppPublisher")]
    [InlineData(true, "SourceDirectory")]
    public void LanguageChangesInsideOwnedModalPreserveAuthoringTargetDraftAndSelection(bool guided, string property)
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using Form owner = guided ? new BuilderWizardForm(controller) : new PackageBuilderForm(controller);
            owner.Show();
            if (owner is PackageBuilderForm advanced) advanced.OpenSection("identity");
            TextBox Field() => owner.Controls.Find(property, true).OfType<TextBox>().Single();
            var original = Field(); original.Text = "Authored Ω draft";
            owner.Activate(); original.Focus(); original.Select(2, 5); Application.DoEvents();
            original.Focused.Should().BeTrue();
            var before = InstallerScriptSerializer.Write(controller.Project);
            InModal(owner, (_, modalField) =>
            {
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    IsWindowEnabled(owner.Handle).Should().BeFalse("the recreated parent must remain disabled until its modal closes");
                    modalField.Focused.Should().BeTrue($"a parent refresh must not take modal focus; culture={culture}, ownerActive={Form.ActiveForm == owner}, nativeOwnerEnabled={IsWindowEnabled(owner.Handle)}, modalActive={Form.ActiveForm == modalField.FindForm()}, activeControl={owner.ActiveControl?.Name}");
                    var current = Field(); current.Text.Should().Be("Authored Ω draft");
                    current.SelectionStart.Should().Be(2); current.SelectionLength.Should().Be(5);
                    InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                }
            });
            var restored = Field(); restored.Focused.Should().BeTrue();
            restored.SelectionStart.Should().Be(2); restored.SelectionLength.Should().Be(5);
            restored.Text.Should().Be("Authored Ω draft");
        });
    }

    [Fact]
    public void NestedModalRefreshRetainsNestedEditingTargetAndPreservesExplicitOwnerDisable()
    {
        Sta(() =>
        {
            using var owner = new Form();
            var host = new SplitContainer { Dock = DockStyle.Fill };
            var field = new TextBox { Text = "Nested authored value" };
            host.Panel1.Controls.Add(field); owner.Controls.Add(host);
            owner.Show(); owner.Activate(); field.Focus(); field.Select(2, 4);
            InModal(owner, (outer, _) =>
            {
                InModal(outer, (_, innerField) =>
                {
                    var bookmark = UiFocusBookmark.Capture(owner); bookmark.Should().NotBeNull();
                    using (ModalOwnerRefresh.Begin(owner))
                    {
                        owner.RightToLeft = RightToLeft.Yes; owner.RightToLeftLayout = true;
                        owner.ActiveControl = null; field.Select(0, 0); bookmark!.Restore();
                    }
                    Application.DoEvents(); innerField.Focused.Should().BeTrue();
                    IsWindowEnabled(owner.Handle).Should().BeFalse();
                    field.SelectionStart.Should().Be(2); field.SelectionLength.Should().Be(4);
                });
                IsWindowEnabled(owner.Handle).Should().BeFalse("closing an inner modal leaves the outer owner disabled");
                owner.Enabled = false;
            }, ownerEnabledOnReturn: false);
            owner.Enabled.Should().BeFalse();
        });
    }
}
