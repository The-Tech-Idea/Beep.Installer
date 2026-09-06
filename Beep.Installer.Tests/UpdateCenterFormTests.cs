using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using FluentAssertions;
using TheTechIdea.Beep.Winform.Controls;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateCenterFormTests
{
    [Fact]
    public void UpdateTranslations_AreEmbeddedAndPreserveFormatArgumentsInEveryShippedLanguage()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(UpdateCenterForm).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            stream.Should().NotBeNull();
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>()
                .Where(e => ((string)e.Key).StartsWith("Update_", StringComparison.Ordinal))
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        }
        var english = Read("en");
        english.Should().HaveCount(39);
        foreach (var culture in Beep.Installer.Lang.LanguageManager.SupportedCultures)
        {
            var translated = Read(culture);
            translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var pair in english)
            {
                translated[pair.Key].Should().NotBeNullOrWhiteSpace();
                System.Text.CompositeFormat.Parse(translated[pair.Key]).MinimumArgumentCount.Should()
                    .Be(System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount, $"{culture}/{pair.Key} must preserve formatting arguments");
            }
        }
    }

    [Fact]
    public void RequestedDialog_HasTrustedIdentityAndGatedAccessibleActions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = UpdateCenterForm.Create(new InstallProject
                { AppId = "a34321a2-680b-43a8-af88-c56d6afab012", AppName = "Example", AppPublisher = "Publisher", AppUpdateChannel = "stable" }, () => new InstallerPolicyEvaluation());
                typeof(UpdateCenterForm).GetField("_appId", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(form).Should().Be("a34321a2-680b-43a8-af88-c56d6afab012");
                var controls = Descendants(form).ToArray();
                controls.OfType<BeepLabel>().Should().Contain(c => c.Text.Contains("Example") && c.Text.Contains("Publisher"));
                var apply = controls.OfType<BeepButton>().Single(b => b.Text == "Apply update");
                apply.Enabled.Should().BeFalse();
                controls.OfType<BeepButton>().Should().OnlyContain(b => !string.IsNullOrEmpty(b.AccessibleName));
                controls.OfType<BeepTextBox>().Should().OnlyContain(b => !string.IsNullOrEmpty(b.AccessibleName));
                form.AcceptButton.Should().NotBeNull();
                form.CancelButton.Should().NotBeNull();
                controls.OfType<BeepButton>().Count(b => b.Text == Beep.Installer.Lang.LanguageManager.T("Btn_Browse")).Should().Be(6);
                typeof(UpdateCenterForm).GetMethod("RefreshInstalledState", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(form, new object[] { new Beep.Installer.Engine.Updates.DeltaUpdatePackageResult
                    { InstalledVersion = "2.0", InstallDirectory = "C:\\Example", JournalPath = "C:\\Example-journal.json" } });
                controls.OfType<BeepTextBox>().Single(c => c.AccessibleName == "Installed version").Text.Should().Be("2.0");
                typeof(UpdateCenterForm).GetField("_eligible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, true);
                apply.Enabled = true;
                controls.OfType<BeepTextBox>().Single(c => c.AccessibleName == "Target channel").Text = "preview";
                apply.Enabled.Should().BeFalse("changing inputs invalidates the previous check");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control root)
        => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
}
