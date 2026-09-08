using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Localization and layout-direction coverage.
///
/// The wizard shipped translations for 8 cultures including Arabic, but
/// <see cref="RtlHelper"/> had no callers — right-to-left users got a left-to-right layout.
/// These tests pin the culture classification the wizard now keys off, and assert every
/// culture resolves the shared keys so a missing translation surfaces here rather than as
/// English text in a shipped installer.
/// </summary>
// Shares the "Language" collection with LanguageSwitchTests: both mutate LanguageManager's static
// current culture, and xUnit runs different collections in parallel. Without this they race -- a
// SetLanguage("ar") here would be undone by the other class mid-assertion, which is exactly how it
// failed once the two were run together.
[Collection("Language")]
public class LocalizationTests
{
    [Theory]
    [InlineData("ar")]  // Arabic
    [InlineData("he")]  // Hebrew
    [InlineData("fa")]  // Persian
    [InlineData("ur")]  // Urdu
    public void RtlCultures_AreDetected(string culture)
        => RtlHelper.IsRtl(culture).Should().BeTrue();

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("pt")]
    [InlineData("")]
    public void LtrCultures_AreNotFlaggedRtl(string culture)
        => RtlHelper.IsRtl(culture).Should().BeFalse();

    [Fact]
    public void EverySupportedCulture_ResolvesNavigationLabels()
    {
        // The wizard's most visible strings. GetOrDefault falls back to English, so this
        // asserts resolution never throws and never yields blank chrome.
        var keys = new[] { "Btn_Back", "Btn_Next", "Btn_Cancel", "Btn_Install", "Btn_Finish" };

        var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture);
                foreach (var key in keys)
                {
                    LanguageManager.GetOrDefault(key, "fallback")
                        .Should().NotBeNullOrWhiteSpace($"'{key}' must resolve for culture '{culture}'");
                }
            }
        }
        finally
        {
            LanguageManager.SetLanguage(original);
        }
    }

    [Fact]
    public void SetLanguage_IsHonoured()
    {
        var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            LanguageManager.SetLanguage("ar");
            LanguageManager.CurrentCulture.TwoLetterISOLanguageName.Should().Be("ar");

            // This is what the wizard now uses to decide whether to mirror its layout.
            RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName)
                     .Should().BeTrue();
        }
        finally
        {
            LanguageManager.SetLanguage(original);
        }
    }

    [Fact]
    public void EveryCulture_ExposesTheSameKeySet()
    {
        // The resx files had drifted badly: English carried 38 keys and the other seven only
        // 30 — and the sets were not even subsets. English was missing all six Btn_* keys the
        // wizard asks for, while the other cultures were missing 14 content strings and so
        // silently fell back to English mid-wizard.
        // Goes through LanguageManager itself, so this also proves the resources are actually
        // reachable at runtime — they were not: the SDK compiles .resx to binary .resources
        // and the loader only probed .resx names, leaving all 8 translations dead.
        var byCulture = LanguageManager.SupportedCultures
            .ToDictionary(c => c, c => LanguageManager.GetKeys(c).ToHashSet());

        foreach (var (culture, keys) in byCulture)
            keys.Should().NotBeEmpty($"culture '{culture}' must resolve its embedded strings");

        var reference = byCulture["en"];
        foreach (var (culture, keys) in byCulture)
        {
            keys.Except(reference).Should().BeEmpty($"'{culture}' has keys English lacks");
            reference.Except(keys).Should().BeEmpty($"'{culture}' is missing keys English defines");
        }
    }

    [Fact]
    public void TranslatedStrings_AreActuallyReturned()
    {
        // Guards the loader fix: a real translation must come back, not the English default.
        var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            LanguageManager.SetLanguage("ar");
            LanguageManager.TryGetString("Btn_Next", out var arabicNext).Should().BeTrue();
            arabicNext.Should().NotBe("Next >", "the Arabic resource should supply its own text");

            LanguageManager.SetLanguage("de");
            LanguageManager.TryGetString("Btn_Cancel", out var germanCancel).Should().BeTrue();
            germanCancel.Should().NotBeNullOrWhiteSpace();
        }
        finally { LanguageManager.SetLanguage(original); }
    }

    [Fact]
    public void UnknownCulture_FallsBackWithoutThrowing()
    {
        var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            var act = () => LanguageManager.SetLanguage("zz");
            act.Should().NotThrow("an unrecognised system culture must not break the installer");

            LanguageManager.GetOrDefault("Btn_Next", "Next >").Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            LanguageManager.SetLanguage(original);
        }
    }
}
