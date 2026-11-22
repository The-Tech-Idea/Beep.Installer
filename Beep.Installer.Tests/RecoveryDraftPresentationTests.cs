using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class RecoveryDraftPresentationTests
{
    [Fact]
    public void PickerSelectsReadableDraftPreservesSelectionAcrossLanguageAndRejectsUnreadableDraft()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            var root = Path.Combine(Path.GetTempPath(), "RecoveryPicker_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root); LanguageManager.SetLanguage("en");
                var valid = Path.Combine(root, "valid.bsetup");
                InstallerScriptSerializer.Save(InstallerProjectFactory.CreateNew("Draft project", "1.0.0", "Publisher", ""), valid).ok.Should().BeTrue();
                var invalid = Path.Combine(root, "invalid.bsetup"); File.WriteAllText(invalid, "invalid script");
                var original = Path.Combine(root, "original.bsetup");
                using var dialog = new RecoveryDraftDialog(new[] { new RecoveryDraftEntry(invalid), new RecoveryDraftEntry(valid, original) }); dialog.Show(); Application.DoEvents();
                var list = dialog.Controls.Find("RecoveryDrafts", true).OfType<ListView>().Single();
                var recover = dialog.Controls.Find("RecoverDraft", true).OfType<Button>().Single();
                list.Items[0].Selected = true; Application.DoEvents(); recover.Enabled.Should().BeFalse();
                list.Items[0].Selected = false; list.Items[1].Selected = true; Application.DoEvents();
                recover.Enabled.Should().BeTrue(); dialog.SelectedRecoveryPath.Should().Be(valid);
                dialog.SelectedEntry!.DocumentPath.Should().Be(original); list.Items[1].SubItems[2].Text.Should().Be(original);
                LanguageManager.SetLanguage("ar"); dialog.RightToLeftLayout.Should().BeTrue(); dialog.SelectedRecoveryPath.Should().Be(valid);
                recover.AccessibleName.Should().Be(LanguageManager.GetString("Recovery_Recover"));
                LanguageManager.SetLanguage("en"); recover.PerformClick(); dialog.DialogResult.Should().Be(DialogResult.OK);
                File.Exists(invalid).Should().BeTrue(); File.Exists(valid).Should().BeTrue();
            }
            catch (Exception ex) { error = ex; }
            finally { LanguageManager.SetLanguage(language); Directory.Delete(root, true); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
