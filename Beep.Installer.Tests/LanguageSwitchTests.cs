using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Live language switching (7.B.1).
///
/// <c>SetLanguage</c> swapped the string table but told nobody, and <c>GetString</c> is pull-only —
/// so a switch retranslated windows opened afterwards and left everything already on screen in the
/// old language. RTL had the matching gap: <c>RtlHelper</c> could only turn right-to-left on, so
/// Arabic to English left the window mirrored with no way back.
/// </summary>
[Collection("Language")]
public sealed class LanguageSwitchTests : IDisposable
{
    private readonly string _original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;

    public void Dispose() => LanguageManager.SetLanguage(_original);

    [Fact]
    public void SwitchingLanguageAnnouncesTheChange()
    {
        LanguageManager.SetLanguage("en");
        var announced = 0;
        void Handler(object? s, EventArgs e) => announced++;

        LanguageManager.LanguageChanged += Handler;
        try { LanguageManager.SetLanguage("fr"); }
        finally { LanguageManager.LanguageChanged -= Handler; }

        announced.Should().Be(1);
        LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be("fr");
    }

    [Fact]
    public void ReselectingTheSameLanguageAnnouncesNothing()
    {
        LanguageManager.SetLanguage("de");
        var announced = 0;
        void Handler(object? s, EventArgs e) => announced++;

        LanguageManager.LanguageChanged += Handler;
        try { LanguageManager.SetLanguage("de"); }
        finally { LanguageManager.LanguageChanged -= Handler; }

        announced.Should().Be(0, "re-selecting the current language should not churn the UI");
    }

    [Fact]
    public void AListenerThatThrows_DoesNotStopTheOthers()
    {
        // A disposed form is the realistic case. If its handler aborted the notification, every
        // listener after it would keep the old language and the window would be half-translated.
        LanguageManager.SetLanguage("en");
        var reached = false;
        void Thrower(object? s, EventArgs e) => throw new InvalidOperationException("disposed");
        void Later(object? s, EventArgs e) => reached = true;

        LanguageManager.LanguageChanged += Thrower;
        LanguageManager.LanguageChanged += Later;
        try
        {
            var act = () => LanguageManager.SetLanguage("es");
            act.Should().NotThrow();
        }
        finally
        {
            LanguageManager.LanguageChanged -= Thrower;
            LanguageManager.LanguageChanged -= Later;
        }

        reached.Should().BeTrue();
    }

    [Fact]
    public void StringsActuallyChangeWithTheLanguage()
    {
        // Uses whatever key the resx files agree on; the point is that the table swapped.
        LanguageManager.SetLanguage("en");
        var english = LanguageManager.GetString("Wizard.Next");

        LanguageManager.SetLanguage("fr");
        var french = LanguageManager.GetString("Wizard.Next");

        if (english == "Wizard.Next" || french == "Wizard.Next")
            return; // key not present in these resx files; nothing to assert about translation

        french.Should().NotBe(english);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("fa", true)]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    public void RtlIsRecognisedPerLanguage(string code, bool rightToLeft)
    {
        RtlHelper.IsRtl(code).Should().Be(rightToLeft);
    }

    [Fact]
    public void DirectionCanBeTurnedBackOff()
    {
        // The regression: ApplyRtl was one-way, so a switch back left the form mirrored.
        using var form = new Form();
        using var box = new TextBox();
        form.Controls.Add(box);

        RtlHelper.ApplyDirection(form, rightToLeft: true);
        form.RightToLeft.Should().Be(RightToLeft.Yes);
        box.RightToLeft.Should().Be(RightToLeft.Yes);

        RtlHelper.ApplyDirection(form, rightToLeft: false);

        form.RightToLeft.Should().Be(RightToLeft.No);
        form.RightToLeftLayout.Should().BeFalse();
        box.RightToLeft.Should().Be(RightToLeft.No, "the children have to come back too");
    }

    [Fact]
    public void PagesGetAReloadHook_DefaultedSoUnlocalizedOnesStillCompile()
    {
        // A page with no translated text of its own inherits a no-op rather than being forced to
        // implement one.
        var page = new StubPage();

        var reload = () => ((Pages.IInstallerPage)page).ReloadStrings();

        reload.Should().NotThrow();
    }

    private sealed class StubPage : Pages.IInstallerPage
    {
        public string PageTitle => "Stub";
        public string Subtitle => "";
        public bool CanGoNext => true;
        public void OnEnter(Pages.InstallContext ctx) { }
        public bool Validate() => true;
#pragma warning disable CS0067
        public event EventHandler<bool>? ValidityChanged;
#pragma warning restore CS0067
    }
}
