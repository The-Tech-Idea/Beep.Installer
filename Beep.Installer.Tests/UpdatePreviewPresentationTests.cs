using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Winform.Controls;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class UpdatePreviewPresentationTests
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

    private static BeepTextBox Input(Form form, string key) => form.Controls.Find("UpdateInput_" + key, true).OfType<BeepTextBox>().Single();
    private static TextBox View(Form form) => form.Controls.Find("UpdatePreview", true).OfType<TextBox>().Single();
    private static BeepButton Apply(Form form) => (BeepButton)typeof(UpdateCenterForm).GetField("_apply", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static void Check(Form form)
    {
        var task = (Task)typeof(UpdateCenterForm).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, new object[] { "check" })!;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        task.IsCompleted.Should().BeTrue(); task.GetAwaiter().GetResult();
    }
    private static UpdateCenterForm Create(UpdatePreviewTests.SignedFixture fixture)
    {
        var project = InstallerProjectFactory.CreateNew("ChannelApp", "2.0", "Acme", "src");
        project.AppId = UpdateChannelFeedPackageServiceTests.FixtureAppId; project.AppUpdateChannel = "stable";
        var form = UpdateCenterForm.Create(project, () => new(), fixture.Options);
        var key = Path.Combine(fixture.Root, "public.pem"); File.WriteAllText(key, fixture.PublicKey);
        Input(form, "feed").Text = fixture.Options.FeedPath; Input(form, "deltaKey").Text = key;
        Input(form, "version").Text = "1.0"; Input(form, "install").Text = fixture.Current; Input(form, "delta").Text = fixture.Delta;
        return form;
    }

    [Fact]
    public void RealSignedCheckShowsPayloadAndRollbackConstraintsBeforeEnablingApply()
    {
        RunSta(() =>
        {
            using var fixture = new UpdatePreviewTests.SignedFixture(); using var form = Create(fixture); form.Show();
            Apply(form).Enabled.Should().BeFalse(); Check(form); Apply(form).Enabled.Should().BeTrue();
            View(form).ReadOnly.Should().BeTrue(); View(form).ScrollBars.Should().Be(ScrollBars.Vertical);
            View(form).Text.Should().Contain("2.0").And.Contain(fixture.Delta).And.Contain(LanguageManager.GetString("UpdatePreview_NoFallback"));
            Input(form, "version").Text = "3.0"; Apply(form).Enabled.Should().BeFalse();
            View(form).Text.Should().Be(LanguageManager.GetString("UpdatePreview_CheckRequired"));
            Check(form); Apply(form).Enabled.Should().BeFalse(); View(form).Text.Should().Contain(LanguageManager.GetString("UpdatePreview_MissingDelta"));
            File.ReadAllText(Path.Combine(fixture.Current, "app.txt")).Should().Be("old");
        });
    }

    [Fact]
    public void MissingInstallOrDeltaKeyCannotEnableApplyAndLiveTranslationPreservesInputs()
    {
        RunSta(() =>
        {
            using var fixture = new UpdatePreviewTests.SignedFixture(); using var form = Create(fixture); form.Show();
            Input(form, "deltaKey").Text = "missing.pem"; Check(form); Apply(form).Enabled.Should().BeFalse();
            View(form).Text.Should().Contain(LanguageManager.GetString("UpdatePreview_MissingKey"));
            LanguageManager.SetLanguage("ar"); form.RightToLeft.Should().Be(RightToLeft.Yes);
            Apply(form).Text.Should().Be(LanguageManager.GetString("Update_Apply"));
            Input(form, "version").AccessibleName.Should().Be(LanguageManager.GetString("Update_Version"));
            View(form).Text.Should().Contain(LanguageManager.GetString("UpdatePreview_MissingKey"));
            Input(form, "version").Text.Should().Be("1.0"); Input(form, "deltaKey").Text.Should().Be("missing.pem");
            Input(form, "deltaKey").Text = Path.Combine(fixture.Root, "public.pem"); Input(form, "install").Text = "missing-install";
            Check(form); Apply(form).Enabled.Should().BeFalse();
            View(form).Text.Should().Contain(LanguageManager.GetString("Update_MissingInstall"));
            form.Close(); LanguageManager.SetLanguage("de"); // A disposed dialog must not receive callbacks.
        });
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void ReviewRemainsScrollableAndApplyFooterVisibleAtSupportedScales(string culture, float scale)
    {
        RunSta(() =>
        {
            using var fixture = new UpdatePreviewTests.SignedFixture(); LanguageManager.SetLanguage(culture);
            using var form = Create(fixture); form.Size = form.MinimumSize; form.Show(); form.Scale(new SizeF(scale, scale)); form.PerformLayout(); Application.DoEvents();
            Check(form); Apply(form).Enabled.Should().BeTrue(); View(form).Text.Should().Contain("2.0");
            var bounds = form.RectangleToClient(Apply(form).RectangleToScreen(Apply(form).ClientRectangle));
            form.ClientRectangle.Contains(bounds).Should().BeTrue($"footer must fit: form={form.ClientRectangle}, apply={bounds}, parent={Apply(form).Parent!.Bounds}");
            View(form).Parent.Should().BeOfType<FlowLayoutPanel>().Which.AutoScroll.Should().BeTrue();
        });
    }

    [Fact]
    public void EditingInputsDuringCheckCannotPublishAStalePreview()
    {
        RunSta(() =>
        {
            using var fixture = new UpdatePreviewTests.SignedFixture(); using var form = Create(fixture); form.Show();
            var task = (Task)typeof(UpdateCenterForm).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(form, new object[] { "check" })!;
            Input(form, "target").Text = "other-channel";
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            task.IsCompleted.Should().BeTrue(); task.GetAwaiter().GetResult();
            Apply(form).Enabled.Should().BeFalse(); View(form).Text.Should().Be(LanguageManager.GetString("UpdatePreview_CheckRequired"));
        });
    }

    [Fact]
    public void PreviewCaptionsArePresentAndKeepFormatArgumentsAcrossEightLanguages()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(UpdateCenterForm).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>().Where(e => ((string)e.Key).StartsWith("UpdatePreview_"))
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        }
        var english = Read("en"); english.Should().HaveCount(22);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var pair in english)
                System.Text.CompositeFormat.Parse(translated[pair.Key]).MinimumArgumentCount.Should()
                    .Be(System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount);
        }
    }

    [Fact]
    public void DisposingDuringCheckCancelsWithoutPublishingToDisposedControls()
    {
        RunSta(() =>
        {
            using var fixture = new UpdatePreviewTests.SignedFixture(); using var form = Create(fixture); form.Show();
            var task = (Task)typeof(UpdateCenterForm).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(form, new object[] { "check" })!;
            form.Dispose();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            task.IsCompleted.Should().BeTrue(); task.GetAwaiter().GetResult();
            form.IsDisposed.Should().BeTrue(); LanguageManager.SetLanguage("ar");
        });
    }
}
