using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Extensibility;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The provider names the resource wizard lists are translated, in every language.
///
/// These keys are invisible to <see cref="StringResourceCoverageTests"/> and always will be:
/// <c>ResourceTypeStrings</c> passes the catalog's own English as the fallback, so the two cannot
/// drift — but that also means there is no string literal in the code for that scanner to read. A
/// generic guard cannot see them, so they get a specific one.
///
/// The check avoids naming any key. It switches language and asserts the text actually changes: a
/// missing key falls back to the catalog's English, which is exactly what a missing translation
/// looks like, so absence and non-translation fail the same way.
/// </summary>
[Collection("Language")]
public class ResourceTypeStringsTests
{
    /// <summary>Cultures whose script guarantees a visible difference from English.</summary>
    private static readonly string[] DistinctScripts = { "ar", "ja", "zh" };

    [Fact]
    public void EveryDescribedProviderIsCovered()
    {
        var uncovered = ResourceInputCatalog.All
            .Where(d => !ResourceTypeStrings.IsCovered(d))
            .Select(d => d.Type)
            .ToList();

        uncovered.Should().BeEmpty(
            "a provider whose name cannot be translated shows English inside a translated wizard: "
            + string.Join(", ", uncovered));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("ja")]
    [InlineData("zh")]
    public void EveryProviderNameAndSummaryIsTranslated(string culture)
    {
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            LanguageManager.SetLanguage(culture);

            var untranslated = new List<string>();

            foreach (var descriptor in ResourceInputCatalog.All)
            {
                if (ResourceTypeStrings.Label(descriptor) == descriptor.Label)
                    untranslated.Add($"{descriptor.Type}.Label");

                if (ResourceTypeStrings.Summary(descriptor) == descriptor.Summary)
                    untranslated.Add($"{descriptor.Type}.Summary");
            }

            untranslated.Should().BeEmpty(
                $"'{culture}' falls back to English for: " + string.Join(", ", untranslated));
        }
        finally
        {
            LanguageManager.SetLanguage(previous);
        }
    }

    [Fact]
    public void EnglishResolvesToTheCatalogText()
    {
        // The English resource and the catalog must agree, or the wizard would show one thing and
        // the .bsetup would describe another.
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            LanguageManager.SetLanguage("en");

            foreach (var descriptor in ResourceInputCatalog.All)
            {
                ResourceTypeStrings.Label(descriptor).Should().Be(descriptor.Label, descriptor.Type);
                ResourceTypeStrings.Summary(descriptor).Should().Be(descriptor.Summary, descriptor.Type);
            }
        }
        finally
        {
            LanguageManager.SetLanguage(previous);
        }
    }

    [Fact]
    public void AnUndescribedProviderFallsBackToItsOwnText()
    {
        // A provider from a third-party extension has no translation and must still be listed.
        var alien = new ResourceTypeDescriptor(
            "contoso.custom", "Do the Contoso thing", "Whatever Contoso does.",
            Array.Empty<ResourceInput>());

        ResourceTypeStrings.Label(alien).Should().Be("Do the Contoso thing");
        ResourceTypeStrings.Summary(alien).Should().Be("Whatever Contoso does.");
        ResourceTypeStrings.IsCovered(alien).Should().BeFalse();
    }

    [Fact]
    public void TheDistinctScriptCulturesAreActuallyDistinct()
    {
        // Anti-vacuity for the theory above: if these cultures ever stopped differing from English,
        // "translated" would be indistinguishable from "missing" and the theory would pass blindly.
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            foreach (var culture in DistinctScripts)
            {
                LanguageManager.SetLanguage(culture);
                LanguageManager.TryGetString("Btn_Next", out var next).Should().BeTrue();
                next.Should().NotBe("Next >", $"'{culture}' must supply its own text");
            }
        }
        finally
        {
            LanguageManager.SetLanguage(previous);
        }
    }
}
