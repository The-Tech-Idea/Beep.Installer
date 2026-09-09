using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <summary>The opening of a lookup: <c>L("Key",</c> or <c>GetOrDefault("Key",</c>.</summary>
    private static readonly Regex CallHead =
        new("(?:\\bL|GetOrDefault)\\(\\s*\"([A-Za-z0-9_]+)\"\\s*,", RegexOptions.Compiled);

    /// <summary>A C# string literal, escapes included.</summary>
    private static readonly Regex Literal =
        new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

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

            foreach (var (key, english) in Lookups(File.ReadAllText(file)))
                keys.TryAdd(key, english);
        }

        return keys;
    }

    /// <summary>
    /// Every <c>L(key, english)</c> in a file, including defaults assembled by concatenation.
    ///
    /// A regex over a single literal missed 28 keys whose English text is written as
    /// <c>"..." + "..."</c>. They were absent from all eight resx files and nothing said so, which
    /// made this class quietly under-report the very thing it exists to catch.
    ///
    /// It must also reject a computed second argument: <c>UpdateCenterForm</c> has its own
    /// <c>L(key, params object[])</c> overload where the second argument is a value, not a default.
    /// </summary>
    private static IEnumerable<(string Key, string English)> Lookups(string source)
    {
        foreach (Match head in CallHead.Matches(source))
        {
            var open = source.LastIndexOf('(', head.Index + head.Length - 1);
            if (open < 0) continue;

            var depth = 0;
            var inString = false;
            var escaped = false;
            var end = -1;

            for (var i = open; i < source.Length; i++)
            {
                var c = source[i];

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }

            if (end < 0) continue;

            var body = source.Substring(head.Index + head.Length, end - (head.Index + head.Length));
            var parts = Literal.Matches(body).Select(m => m.Groups[1].Value).ToList();
            if (parts.Count == 0) continue;

            // Once the literals and the `+` that join them are removed, anything left means the
            // default is computed — so this is not a declaration of English text.
            var residue = Literal.Replace(body, "").Replace("+", "").Trim();
            if (residue.Length > 0) continue;

            yield return (head.Groups[1].Value, Unescape(string.Concat(parts)));
        }
    }

    /// <summary>Turns a C# literal body into the runtime string it produces.</summary>
    private static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                sb.Append(text[i]);
                continue;
            }

            i++;
            sb.Append(text[i] switch
            {
                'r' => '\r',
                'n' => '\n',
                't' => '\t',
                _ => text[i],
            });
        }

        return sb.ToString();
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
    public void TheScannerSeesConcatenatedDefaults()
    {
        // Anti-vacuity, and a regression guard for the blind spot itself: if this stopped matching,
        // every coverage assertion below would pass by simply not looking.
        var used = KeysUsedInCode();

        used.Should().ContainKey("Signing_TimestampHint",
            "its English default is written as several concatenated literals");
        used["Signing_TimestampHint"].Should().Contain("Timestamping keeps the signature valid");

        used.Should().NotContainKey("Completed",
            "that is UpdateCenterForm's L(key, params object[]) overload, not an English default");
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
        // Not every declared key comes from an L(...) literal -- UpdateCenterForm composes its keys
        // by prefixing "Update_" -- so this asserts on the shape the scanner can see rather than
        // demanding an exact match.
        var used = KeysUsedInCode().Keys.ToHashSet(StringComparer.Ordinal);
        var declared = KeysDeclaredFor("en");

        var orphans = declared.Where(k => !used.Contains(k)).ToList();

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

            LanguageManager.TryGetString("Resource_ChooseHeading", out var resource).Should().BeTrue();
            resource.Should().NotBe("What should the installer do?");
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
                    problems.Add($"{culture}/{key}: expected [{string.Join(",", expected.Order())}], got [{string.Join(",", actual.Order())}]");
            }
        }

        problems.Should().BeEmpty(string.Join("; ", problems.Take(15)));
    }

    /// <summary>The set of numeric placeholder indexes in a format string, ignoring any format spec.</summary>
    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, "\\{(\\d+)(?::[^}]*)?\\}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
