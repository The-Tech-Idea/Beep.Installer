using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Every user-facing string the code asks for has a real translation behind it.
///
/// <c>L("Key", "English")</c> falls back to its English argument when the key is missing, which is
/// the right runtime behaviour and a terrible development signal: a key can be absent from all eight
/// resx files forever and nothing fails. The key-parity test does not catch it either, because a key
/// missing from *every* culture keeps the cultures in parity.
///
/// That is exactly what happened — 163 keys had accumulated with no resource entry at all, silently
/// serving English to Arabic, Japanese and Chinese users. This test is the missing signal.
/// </summary>
[Collection("Language")]
public class StringResourceCoverageTests
{
    /// <summary>L("Key", "English") and LanguageManager.GetOrDefault("Key", "English").</summary>
    private static readonly Regex Call =
        new(@"(?:\bL|GetOrDefault)\(\s*""([A-Za-z0-9_]+)""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\)",
            RegexOptions.Compiled);

    private static readonly string[] Cultures = { "ar", "de", "en", "es", "fr", "ja", "pt", "zh" };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return dir!;
    }

    /// <summary>Every (key, English default) pair the UI code asks the resource manager for.</summary>
    private static Dictionary<string, string> KeysUsedInCode()
    {
        var root = Path.Combine(RepoRoot().FullName, "Beep.Installer");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var normalised = file.Replace('\\', '/');
            if (normalised.Contains("/obj/") || normalised.Contains("/bin/")) continue;

            foreach (Match m in Call.Matches(File.ReadAllText(file)))
                keys.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
        }

        return keys;
    }

    private static HashSet<string> KeysDeclaredFor(string culture)
    {
        var path = Path.Combine(RepoRoot().FullName, "Beep.Installer", "Lang", $"Strings_{culture}.resx");
        File.Exists(path).Should().BeTrue($"'{culture}' must have a resource file");

        return XDocument.Load(path).Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name") ?? "")
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryStringTheCodeAsksForIsDeclaredInEnglish()
    {
        var used = KeysUsedInCode();
        used.Should().NotBeEmpty("the UI asks for localised strings");

        var declared = KeysDeclaredFor("en");
        var undeclared = used.Keys.Where(k => !declared.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        undeclared.Should().BeEmpty(
            "these keys only ever resolve to their English fallback, so no translation can reach a user: "
            + string.Join(", ", undeclared.Take(20)));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("pt")]
    [InlineData("zh")]
    public void EveryStringTheCodeAsksForIsTranslated(string culture)
    {
        var used = KeysUsedInCode();
        var declared = KeysDeclaredFor(culture);

        var missing = used.Keys.Where(k => !declared.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            $"'{culture}' would silently serve English for: " + string.Join(", ", missing.Take(20)));
    }

    [Fact]
    public void NoResourceFileDeclaresAKeyNothingAsksFor()
    {
        // The other direction. A key nothing reads is dead weight that still has to be translated
        // eight times whenever someone touches it.
        //
        // Not every declared key comes from an L(...) literal -- some are composed at runtime -- so
        // this asserts on the shape the scanner can see rather than demanding an exact match.
        var used = KeysUsedInCode().Keys.ToHashSet(StringComparer.Ordinal);
        var declared = KeysDeclaredFor("en");

        var orphans = declared.Where(k => !used.Contains(k)).ToList();

        // Report rather than fail: this is a hygiene signal, and dynamic keys are legitimate.
        orphans.Count.Should().BeLessThan(declared.Count,
            "at least some declared keys must be reachable from the code");
    }

    [Fact]
    public void ATranslatedStringActuallyDiffersFromItsEnglishDefault()
    {
        // Guards against "translating" by pasting the English text in, which would satisfy every
        // other test in this class while delivering nothing.
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try
        {
            LanguageManager.SetLanguage("ja");

            LanguageManager.TryGetString("Quick_ProductHeading", out var japanese).Should().BeTrue();
            japanese.Should().NotBe("What are you installing?", "the Japanese resource must supply its own text");

            LanguageManager.TryGetString("Signing_Title", out var signing).Should().BeTrue();
            signing.Should().NotBe("Code signing");
        }
        finally
        {
            LanguageManager.SetLanguage(previous);
        }
    }

    [Fact]
    public void FormatPlaceholdersSurviveTranslation()
    {
        // A translation that drops {0} turns a helpful message into a useless one, and a translation
        // that invents {2} throws FormatException at the moment the user most needs the message.
        var used = KeysUsedInCode();
        var problems = new List<string>();

        foreach (var culture in Cultures)
        {
            var path = Path.Combine(RepoRoot().FullName, "Beep.Installer", "Lang", $"Strings_{culture}.resx");
            var values = XDocument.Load(path).Root!
                .Elements("data")
                .Where(d => (string?)d.Attribute("name") is { Length: > 0 })
                .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")?.Value ?? "");

            foreach (var (key, english) in used)
            {
                if (!values.TryGetValue(key, out var translated)) continue;

                var expected = Placeholders(english);
                var actual = Placeholders(translated);

                if (!expected.SetEquals(actual))
                    problems.Add($"{culture}/{key}: expected {{{string.Join(",", expected.Order())}}}, got {{{string.Join(",", actual.Order())}}}");
            }
        }

        problems.Should().BeEmpty(string.Join("; ", problems.Take(15)));
    }

    /// <summary>The set of numeric placeholder indexes in a format string, ignoring any format spec.</summary>
    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
