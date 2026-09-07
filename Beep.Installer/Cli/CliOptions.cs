using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

// The verb table is the thing worth testing here — a hand-counted slice shipped /PUBLISH= broken
// because nothing could assert over the verbs as data.
[assembly: InternalsVisibleTo("Beep.Installer.Tests")]

namespace Beep.Installer.Cli;

/// <summary>
/// The command line, parsed once and read through named accessors.
///
/// Verb values are always sliced by the prefix itself, never by a hand-counted offset. Dispatch
/// used to write <c>args[i][10..]</c> beside <c>"/VALIDATE="</c> and count the characters by eye;
/// <c>/PUBLISH=</c> was counted as 8 for a 9-character prefix, so every ClickOnce publish from the
/// CLI received <c>"=&lt;path&gt;"</c> and failed to load its own project. Nothing in the type
/// system objected, and no test covered the verb.
/// </summary>
internal sealed class CliOptions
{
    public CliOptions(string[]? args) => Args = args ?? Array.Empty<string>();

    /// <summary>The raw arguments, for handlers that still read their own switches from them.</summary>
    public string[] Args { get; }

    /// <summary>
    /// Exact, case-insensitive match on any of <paramref name="flags"/>. Exact rather than
    /// prefixed on purpose: <c>/UPDATE</c> must not swallow <c>/UPDATECHANNELFEED=</c>.
    /// </summary>
    public bool Has(params string[] flags)
        => Args.Any(a => flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Value of the <em>first</em> argument beginning with <paramref name="prefix"/>. First, not
    /// last — that is the precedence dispatch has always had.
    /// </summary>
    public bool TryFirstValue(string prefix, out string value)
    {
        for (var i = 0; i < Args.Length; i++)
        {
            if (!Args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            value = Args[i][prefix.Length..];
            return true;
        }

        value = "";
        return false;
    }

    /// <summary>
    /// Value of the <em>last</em> argument beginning with <paramref name="prefix"/>, matching the
    /// option-reading helpers so a repeated switch keeps its "last one wins" behaviour.
    /// </summary>
    public bool TryLastValue(string prefix, out string value)
    {
        var index = -1;
        for (var i = 0; i < Args.Length; i++)
            if (Args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) index = i;

        value = index >= 0 ? Args[index][prefix.Length..] : "";
        return index >= 0;
    }
}

/// <summary>How a verb recognises itself on the command line.</summary>
internal enum CliVerbMatch
{
    /// <summary>A bare switch, matched exactly (<c>/SELFTEST</c>).</summary>
    Flag,

    /// <summary>A <c>PREFIX=value</c> switch; the first occurrence wins.</summary>
    FirstValue,

    /// <summary>A <c>PREFIX=value</c> switch; the last occurrence wins.</summary>
    LastValue
}

/// <summary>
/// One CLI verb: how it is recognised, what runs it, and whether it is a console command.
///
/// The table these build is the single source of dispatch order <em>and</em> of the
/// console-attachment decision. Those were two hand-maintained lists of the same ~60 verbs, which
/// is the kind of duplication that drifts silently — a verb added to one and not the other either
/// never dispatches or dispatches with no console to print to.
/// </summary>
internal sealed class CliVerb
{
    private CliVerb(IReadOnlyList<string> tokens, CliVerbMatch match, Func<CliOptions, string, int> handler, bool headless)
    {
        Tokens = tokens;
        Match = match;
        Handler = handler;
        Headless = headless;
    }

    public IReadOnlyList<string> Tokens { get; }
    public CliVerbMatch Match { get; }
    public Func<CliOptions, string, int> Handler { get; }

    /// <summary>True when the verb runs headless and needs a console attached.</summary>
    public bool Headless { get; }

    /// <summary>A bare switch, optionally with aliases (<c>/?</c>, <c>/H</c>, <c>/HELP</c>).</summary>
    public static CliVerb Flag(Func<CliOptions, int> handler, bool headless = true, params string[] tokens)
        => new(tokens, CliVerbMatch.Flag, (options, _) => handler(options), headless);

    /// <summary>A <c>PREFIX=value</c> verb whose first occurrence wins.</summary>
    public static CliVerb Value(string token, Func<CliOptions, string, int> handler, bool headless = true)
        => new(new[] { Ends(token) }, CliVerbMatch.FirstValue, handler, headless);

    /// <summary>A <c>PREFIX=value</c> verb whose last occurrence wins.</summary>
    public static CliVerb LastValue(string token, Func<CliOptions, string, int> handler, bool headless = true)
        => new(new[] { Ends(token) }, CliVerbMatch.LastValue, handler, headless);

    private static string Ends(string token) => token.EndsWith('=')
        ? token
        : throw new ArgumentException($"A value verb's token must end with '=': '{token}'.", nameof(token));

    public bool TryMatch(CliOptions options, out string value)
    {
        switch (Match)
        {
            case CliVerbMatch.Flag:
                value = "";
                return options.Has(Tokens.ToArray());
            case CliVerbMatch.FirstValue:
                return options.TryFirstValue(Tokens[0], out value);
            default:
                return options.TryLastValue(Tokens[0], out value);
        }
    }
}
