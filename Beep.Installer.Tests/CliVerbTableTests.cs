using System;
using System.Linq;
using Beep.Installer;
using Beep.Installer.Cli;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Guards the CLI verb table.
///
/// Dispatch used to hand-count each verb's value offset — <c>args[i][10..]</c> written beside
/// <c>"/VALIDATE="</c> — across some sixty verbs, with a second hand-kept copy of the same list
/// deciding console attachment. <c>/PUBLISH=</c> was counted as 8 for a 9-character prefix, so
/// every ClickOnce publish from the CLI received <c>"=&lt;path&gt;"</c> and could not load its
/// project. Nothing failed to compile and no test covered the verb. These assert over the verbs as
/// data, which is the only way that class of error becomes visible at all.
/// </summary>
public class CliVerbTableTests
{
    private static CliVerb[] ValueVerbs => Program.Verbs
        .Where(v => v.Match != CliVerbMatch.Flag)
        .ToArray();

    [Fact]
    public void EveryValueVerb_ReturnsItsOwnValue_NotAnOffByOneSliceOfIt()
    {
        const string sentinel = @"C:\some\path\project.bsetup";

        var wrong = ValueVerbs
            .Select(verb =>
            {
                var token = verb.Tokens[0];
                verb.TryMatch(new CliOptions(new[] { token + sentinel }), out var value);
                return (token, value);
            })
            .Where(x => x.value != sentinel)
            .ToList();

        wrong.Should().BeEmpty(
            "a verb must slice its value by its own prefix; got:" + Environment.NewLine +
            string.Join(Environment.NewLine, wrong.Select(x => $"  {x.token} -> '{x.value}'")));
    }

    [Fact]
    public void PublishVerb_PassesThePathThrough()
    {
        // The regression itself: /PUBLISH= is nine characters and was sliced as eight.
        var publish = ValueVerbs.Single(v => v.Tokens[0] == "/PUBLISH=");

        publish.TryMatch(new CliOptions(new[] { @"/PUBLISH=D:\src\App.bsetup" }), out var value).Should().BeTrue();
        value.Should().Be(@"D:\src\App.bsetup");
    }

    [Fact]
    public void ValueVerbTokens_AreUnique()
    {
        // Two verbs sharing a token means the later one is unreachable, silently.
        ValueVerbs.Select(v => v.Tokens[0])
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void LongerVerbs_ComeBeforeThePrefixesTheyStartWith()
    {
        // Value matching is StartsWith, so /PUBLISH= placed ahead of /PUBLISHFEED= would swallow it.
        var tokens = ValueVerbs.Select(v => v.Tokens[0]).ToList();

        var shadowed = (from i in Enumerable.Range(0, tokens.Count)
                        from j in Enumerable.Range(i + 1, tokens.Count - i - 1)
                        where tokens[j].StartsWith(tokens[i].TrimEnd('='), StringComparison.OrdinalIgnoreCase)
                              && tokens[j].Length > tokens[i].Length
                              && tokens[j].StartsWith(tokens[i], StringComparison.OrdinalIgnoreCase)
                        select $"{tokens[i]} (position {i}) shadows {tokens[j]} (position {j})").ToList();

        shadowed.Should().BeEmpty(string.Join(Environment.NewLine, shadowed));
    }

    [Theory]
    // Precedence that is behaviour, not accident, and would be lost by "just append the new verb".
    [InlineData("/PUBLISHFEED=", "/BUILD=")]      // /BUILD=x /PUBLISHFEED=y must publish, not only build
    [InlineData("/RECOVERDELTA=", "/ROLLBACKDELTA=")]
    [InlineData("/APPLYUPDATECHANNEL=", "/VERIFYUPDATECHANNELFEED=")]
    [InlineData("/CHECKUPDATECHANNEL=", "/VERIFYUPDATECHANNELFEED=")]
    [InlineData("/QUALIFYDEPLOYMENTKIT=", "/DEPLOYMENTKIT=")]
    [InlineData("/QUALIFYEVIDENCE=", "/EVIDENCE=")]
    [InlineData("/QUALIFYSECURITY=", "/SECURITYSCAN=")]
    public void VerbOrder_KeepsTheDocumentedPrecedence(string first, string second)
    {
        var tokens = ValueVerbs.Select(v => v.Tokens[0]).ToList();

        tokens.IndexOf(first).Should().BeLessThan(tokens.IndexOf(second),
            $"{first} has to be matched before {second}");
    }

    [Fact]
    public void OnlyTheWindowedTools_AreNonHeadless()
    {
        Program.Verbs.Where(v => !v.Headless)
            .SelectMany(v => v.Tokens)
            .Should().BeEquivalentTo(new[] { "/UPDATEUI", "/LANGMGR" },
                "everything else prints to a console and must have one attached");
    }

    [Fact]
    public void FlagVerbs_MatchExactly_SoUpdateDoesNotSwallowUpdateChannelFeed()
    {
        var update = Program.Verbs.Single(v => v.Match == CliVerbMatch.Flag && v.Tokens.Contains("/UPDATE"));

        update.TryMatch(new CliOptions(new[] { "/UPDATECHANNELFEED=app.bsetup" }), out _).Should().BeFalse();
        update.TryMatch(new CliOptions(new[] { "/update" }), out _).Should().BeTrue("flags are case-insensitive");
    }

    [Fact]
    public void FirstOccurrenceWins_ForValueVerbs()
    {
        var build = ValueVerbs.Single(v => v.Tokens[0] == "/BUILD=");

        build.TryMatch(new CliOptions(new[] { "/BUILD=first.bsetup", "/BUILD=second.bsetup" }), out var value);
        value.Should().Be("first.bsetup");
    }

    [Fact]
    public void LastOccurrenceWins_ForTheChannelVerbs()
    {
        // These read through the option helper, which has always taken the last occurrence.
        var apply = ValueVerbs.Single(v => v.Tokens[0] == "/APPLYUPDATECHANNEL=");

        apply.Match.Should().Be(CliVerbMatch.LastValue);
        apply.TryMatch(new CliOptions(new[] { "/APPLYUPDATECHANNEL=one", "/APPLYUPDATECHANNEL=two" }), out var value);
        value.Should().Be("two");
    }

    [Fact]
    public void ValueVerb_RequiresATokenEndingInEquals()
    {
        var build = () => CliVerb.Value("/BUILD", (_, _) => 0);

        build.Should().Throw<ArgumentException>("a value verb without '=' would match its own prefix and slice nothing");
    }

    [Fact]
    public void NoVerbMatches_AnEmptyOrUnrelatedCommandLine()
    {
        foreach (var argv in new[] { Array.Empty<string>(), new[] { "/NOSUCHVERB=x" }, new[] { @"C:\project.bsetup" } })
        {
            var options = new CliOptions(argv);
            Program.Verbs.Any(v => v.TryMatch(options, out _))
                .Should().BeFalse("'{0}' should fall through to the default mode", string.Join(' ', argv));
        }
    }
}
