using System.Reflection;
using System;
using System.IO;
using System.Linq;
using Beep.Installer;
using Beep.Installer.Core.Cli;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Ties the catalog (<c>cli-catalog.json</c>) to the three other places a verb is written down: the
/// dispatch table, the argument validator's vocabulary and the usage text. Each of those was
/// hand-maintained beside the others, and a verb present in one but not the next was rejected with
/// BI7005 or never documented. Every assertion here is over data, so a drift is a failing test, not a
/// silent gap.
/// </summary>
public class CliCatalogTests
{
    private static readonly CliVerbCatalog Catalog = CliVerbCatalog.Default;

    /// <summary>"qualify" is dispatched by position (the MSI matrix runner), not through the verb table.</summary>
    private const string MatrixRunnerToken = "qualify";

    private static readonly string LineFeed = ((char)10).ToString();

    [Fact]
    public void EveryDispatchedVerb_IsDescribedByTheCatalog_AndTheReverse()
    {
        var dispatched = Program.Verbs.SelectMany(v => v.Tokens).Append(MatrixRunnerToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var described = Catalog.Verbs.SelectMany(v => v.Aliases.Prepend(v.Token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        dispatched.Except(described, StringComparer.OrdinalIgnoreCase)
            .Should().BeEmpty("a verb the shell dispatches must be in cli-catalog.json");
        described.Except(dispatched, StringComparer.OrdinalIgnoreCase)
            .Should().BeEmpty("a verb in cli-catalog.json must be dispatched by the shell");
    }

    [Fact]
    public void CatalogHeadlessFlag_MatchesTheDispatchTable()
    {
        var wrong = Program.Verbs
            .Select(verb => (verb, entry: Catalog.Find(verb.Tokens[0])))
            .Where(x => x.entry is null || x.entry.Headless != x.verb.Headless)
            .Select(x => x.verb.Tokens[0])
            .ToList();

        wrong.Should().BeEmpty("the catalog says whether a verb opens a window; it must agree with the verb table");
    }

    [Fact]
    public void CatalogValueKind_MatchesTheDispatchTable()
    {
        var wrong = Program.Verbs
            .Select(verb => (verb, entry: Catalog.Find(verb.Tokens[0])!))
            .Where(x => (x.verb.Match == Cli.CliVerbMatch.Flag) != (x.entry.Kind == CliVerbKind.Flag))
            .Select(x => x.verb.Tokens[0])
            .ToList();

        wrong.Should().BeEmpty();
    }

    [Fact]
    public void EveryCatalogOption_IsAcceptedByTheArgumentValidator()
    {
        // The validator rejects unknown switches with BI7005 before a verb runs. Whenever a verb's
        // switch is missing from its vocabulary, the verb is unreachable from the command line.
        var rejected = (from verb in Catalog.Verbs
                        from option in verb.Options
                        where option.Name.StartsWith('/')
                        let probe = option.Kind switch
                        {
                            CliOptionKind.Flag => option.Name,
                            CliOptionKind.Property => option.Name + "Name=Value",
                            CliOptionKind.Value => option.Name + "x",
                            _ => option.Name,
                        }
                        let result = EnterpriseCommandLine.ExpandAndValidate(new[] { probe })
                        where result.Diagnostics.Any(d => d.Code == "BI7005")
                        select $"{verb.Id}: {option.Name}").Distinct().ToList();

        rejected.Should().BeEmpty("every documented switch must be in EnterpriseCommandLine's vocabulary:" + Environment.NewLine + string.Join(Environment.NewLine, rejected));
    }

    [Fact]
    public void EveryCatalogVerbToken_IsAcceptedByTheArgumentValidator()
    {
        var rejected = Catalog.Verbs
            .Where(v => v.Token.StartsWith('/'))
            .SelectMany(v => v.Aliases.Prepend(v.Token).Select(t => (v, t)))
            .Where(x => EnterpriseCommandLine.ExpandAndValidate(new[] { x.t + (x.v.Kind == CliVerbKind.Value ? "x" : "") })
                .Diagnostics.Any(d => d.Code == "BI7005"))
            .Select(x => x.t)
            .ToList();

        rejected.Should().BeEmpty();
    }

    [Fact]
    public void ExcludedVerbs_StateWhy_AndExposedVerbsDoNot()
    {
        foreach (var verb in Catalog.Verbs)
        {
            if (verb.Mcp.Exposed) verb.Mcp.Reason.Should().BeNull(verb.Id);
            else verb.Mcp.Reason.Should().NotBeNullOrWhiteSpace($"{verb.Id} is hidden from MCP and must say why");
        }
    }

    [Fact]
    public void InteractiveVerbs_AreNeverExposed_AndNonHeadlessMeansInteractive()
    {
        // A client can never answer a window. Spawning one would hang the call until its timeout.
        Catalog.Verbs.Where(v => v.Effect == CliEffect.Interactive).Should().OnlyContain(v => !v.Mcp.Exposed && !v.Headless);
        Catalog.Verbs.Where(v => !v.Headless).Should().OnlyContain(v => v.Effect == CliEffect.Interactive);
    }

    [Fact]
    public void ValueVerbs_NameTheirValue_AndFlagVerbsDoNot()
    {
        foreach (var verb in Catalog.Verbs)
        {
            if (verb.Kind == CliVerbKind.Value)
            {
                verb.Value.Should().NotBeNull(verb.Id);
                verb.Token.Should().EndWith("=", verb.Id);
            }
            else
            {
                verb.Value.Should().BeNull(verb.Id);
            }

            verb.Summary.Should().NotBeNullOrWhiteSpace(verb.Id);
        }
    }

    [Fact]
    public void DryRunAndJsonClaims_MatchTheDeclaredOptions()
    {
        foreach (var verb in Catalog.Verbs)
        {
            verb.Options.Any(o => o.Name == "/DRYRUN").Should().Be(verb.SupportsDryRun, $"{verb.Id} /DRYRUN");
            if (verb.SupportsJson) verb.Options.Any(o => o.Name == "/JSON").Should().BeTrue($"{verb.Id} says it supports /JSON");
        }
    }

    [Fact]
    public void CatalogIdsAndTokens_AreUnique()
    {
        Catalog.Verbs.Select(v => v.Id).GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty();
        Catalog.Verbs.SelectMany(v => v.Aliases.Prepend(v.Token)).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty();
    }

    [Fact]
    public void BlockedOptions_AlwaysSayWhy()
    {
        // Blocked options are raw-only credentials or policy overrides a person must pass (/ALLOWSCRIPTCMDS).
        foreach (var option in Catalog.Verbs.SelectMany(v => v.Options).Where(o => o.McpBlockedReason is not null))
            option.McpBlockedReason.Should().NotBeNullOrWhiteSpace(option.Name);
        Catalog.Find("silent-install")!.Options.Single(o => o.Name == "/ALLOWSCRIPTCMDS").McpBlockedReason.Should().NotBeNull();
    }

    [Fact]
    public void Uninstall_DoesNotClaimADryRun_BecauseTheShellIgnoresIt()
        => Catalog.Find("uninstall")!.SupportsDryRun.Should().BeFalse();

    [Fact]
    public void EffectsThatNeedConfirmation_AreMachineChangingAndPublishing()
    {
        Catalog.Find("publish-sdk")!.Effect.Should().Be(CliEffect.Publishes);
        foreach (var id in new[] { "silent-install", "uninstall", "repair", "qualify-delta", "qualify-upgrade", "qualify-update-channel-feed" })
            Catalog.Find(id)!.Effect.Should().Be(CliEffect.MachineChanging, id);
    }

    [Fact]
    public void EveryTokenTheValidatorKnows_IsCataloguedOrAGlobalSwitch()
    {
        // Reverse of VerbTokensAreAccepted: a switch the validator accepts but no verb lists is invisible to MCP.
        var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/RESPONSE=", "/CONFIG=", "/NOPROMPT", "/DIR=", "/JSON", "/DRYRUN", "/OUT=", "/STRICT",
            // Aliases of /D= (install directory).
            "/INSTALLDIR=", "/TARGETDIR=",
            // Written into generated VM/MSI command lines for a target, not typed by a person.
            "--os", "--arch", "--plan-hash",
        };
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Catalog.Verbs)
        {
            known.Add(v.Token);
            foreach (var a in v.Aliases) known.Add(a);
            foreach (var o in v.Options) known.Add(o.Name);
        }

        // The validator also spells switches in long form (/LAYOUTCACHE= is --layout-cache=): compare canonically.
        static string Canon(string t) => t.TrimStart('/', '-').Replace("-", "").TrimEnd('=', ':').ToLowerInvariant();
        var canonicalKnown = new HashSet<string>(known.Concat(global).Select(Canon));

        const BindingFlags all = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var vocabulary = new List<string>();
        foreach (var name in new[] { "KnownFlags", "KnownPrefixes" })
        {
            var member = typeof(EnterpriseCommandLine).GetField(name, all)?.GetValue(null)
                      ?? typeof(EnterpriseCommandLine).GetProperty(name, all)?.GetValue(null);
            member.Should().NotBeNull($"EnterpriseCommandLine.{name} must exist for this guard");
            vocabulary.AddRange(((System.Collections.IEnumerable)member!).Cast<object>().Select(o => o.ToString()!));
        }

        var uncatalogued = vocabulary.Where(t => !canonicalKnown.Contains(Canon(t))).Distinct().OrderBy(t => t).ToList();
        uncatalogued.Should().BeEmpty("add each to a verb's options in cli-catalog.json or to the documented global list: " + string.Join(", ", uncatalogued));
    }

    private static bool ContainsToken(string text, string token)
        => System.Text.RegularExpressions.Regex.IsMatch(text, "(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(token) + "(?![A-Za-z0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // ── usage text and docs ──────────────────────────────────────────────────

    [Fact]
    public void EveryVerb_IsListedInTheUsageText()
    {
        var usage = ReadRepoFile("Beep.Installer/Program.cs");
        var usageLines = usage.Split('\n').Where(l => l.Contains("Console.WriteLine")).ToList();

        var missing = Catalog.Verbs
            .Select(v => v.Token.TrimEnd('='))
            .Where(token => !usageLines.Any(l => ContainsToken(l, token)))
            .ToList();

        missing.Should().BeEmpty("PrintUsage must mention every command; missing:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryVerb_IsDocumentedInTheCliReference()
    {
        var page = ReadRepoFile("Help/cli-reference.html");

        var missing = Catalog.Verbs
            .Select(v => v.Token.TrimEnd('='))
            .Where(token => !ContainsToken(page, token))
            .ToList();

        missing.Should().BeEmpty("Help/cli-reference.html must cover every command; missing:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void CliReferencePage_GeneratedRegion_MatchesTheCatalog()
    {
        // The page's catalog section is generated (Update-CliReference.ps1 runs /CATALOG /HTML). Editing
        // the catalog without regenerating leaves a reference that no longer matches the verbs.
        var page = ReadRepoFile("Help/cli-reference.html").ReplaceLineEndings(LineFeed);
        var begin = page.IndexOf(CliVerbCatalog.HtmlBegin, StringComparison.Ordinal);
        var end = page.IndexOf(CliVerbCatalog.HtmlEnd, StringComparison.Ordinal);

        begin.Should().BeGreaterThanOrEqualTo(0, "cli-reference.html must contain the catalog region markers");
        end.Should().BeGreaterThan(begin);
        var region = page.Substring(begin, end + CliVerbCatalog.HtmlEnd.Length - begin);
        region.Should().Be(Catalog.ToHtml().ReplaceLineEndings(LineFeed),
            "run scripts/Update-CliReference.ps1 to regenerate Help/cli-reference.html");
    }

    [Fact]
    public void CliPromptGuide_OnlyUsesSwitchesTheCatalogKnows()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Catalog.Verbs)
        {
            known.Add(v.Token.TrimEnd('='));
            foreach (var a in v.Aliases) known.Add(a.TrimEnd('='));
            foreach (var o in v.Options) known.Add(o.Name.TrimEnd('='));
        }

        var text = ReadRepoFile("Help/cli-prompt.md");
        var used = System.Text.RegularExpressions.Regex.Matches(text, @"(?<![A-Za-z0-9.\\])/[A-Z][A-Z0-9]+(?![A-Za-z0-9])")
            .Select(m => m.Value).Distinct().ToList();
        used.Should().NotBeEmpty();
        used.Where(u => !known.Contains(u)).Should().BeEmpty("Help/cli-prompt.md must not name a switch the catalog lacks");
    }

    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run from a checkout containing Beep.Installer.slnx");
        return File.ReadAllText(Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
