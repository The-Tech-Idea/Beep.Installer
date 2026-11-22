using System.Text.Json.Serialization;

namespace Beep.Installer.Core.Cli;

/// <summary>What a verb does to the machine it runs on. Drives MCP confirmation, not dispatch.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CliEffect>))]
public enum CliEffect
{
    /// <summary>Reads inputs and reports; writes nothing but an optional /OUT= file.</summary>
    [JsonStringEnumMemberName("read-only")] ReadOnly,

    /// <summary>Produces or rewrites files (build output, evidence, packages) in a chosen directory.</summary>
    [JsonStringEnumMemberName("writes-files")] WritesFiles,

    /// <summary>Reaches the network (feeds, downloads, publishing).</summary>
    [JsonStringEnumMemberName("network")] Network,

    /// <summary>Pushes something to an external feed or registry. Confirmed like a machine change: it cannot be taken back.</summary>
    [JsonStringEnumMemberName("publishes")] Publishes,

    /// <summary>Installs, removes, repairs or updates software on this machine.</summary>
    [JsonStringEnumMemberName("machine-changing")] MachineChanging,

    /// <summary>Opens a window and waits for a person.</summary>
    [JsonStringEnumMemberName("interactive")] Interactive,
}

[JsonConverter(typeof(JsonStringEnumConverter<CliVerbKind>))]
public enum CliVerbKind
{
    /// <summary>A bare switch, matched exactly (<c>/SELFTEST</c>).</summary>
    [JsonStringEnumMemberName("flag")] Flag,

    /// <summary>A <c>PREFIX=value</c> switch (<c>/BUILD=project.bsetup</c>).</summary>
    [JsonStringEnumMemberName("value")] Value,
}

[JsonConverter(typeof(JsonStringEnumConverter<CliOptionKind>))]
public enum CliOptionKind
{
    [JsonStringEnumMemberName("flag")] Flag,
    [JsonStringEnumMemberName("value")] Value,

    /// <summary><c>/PROPERTY:Name=Value</c> — an unattended installer property.</summary>
    [JsonStringEnumMemberName("property")] Property,
}

/// <summary>The value a <see cref="CliVerbKind.Value"/> verb takes after its '='.</summary>
public sealed record CliVerbValue(string Name, string Description, bool IsPath);

/// <summary>One switch a verb reads. Names are exactly as typed, including a trailing '=' for value switches.</summary>
public sealed record CliOption
{
    public required string Name { get; init; }
    public CliOptionKind Kind { get; init; }
    public bool Required { get; init; }

    /// <summary>Carries a credential. Over MCP it must be a <c>secret://env/NAME</c> or <c>env:NAME</c> reference.</summary>
    public bool Secret { get; init; }

    /// <summary>Set when the switch takes only a raw secret, which MCP will not place on a command line.</summary>
    public string? McpBlockedReason { get; init; }
}

public sealed record CliMcpExposure(bool Exposed, string? Reason = null);

/// <summary>One CLI verb: the single description every consumer (help, docs, MCP) reads.</summary>
public sealed record CliVerbDescriptor
{
    public required string Id { get; init; }
    public required string Token { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public CliVerbKind Kind { get; init; }
    public required string Category { get; init; }
    public CliEffect Effect { get; init; }
    public required string Summary { get; init; }
    public bool Headless { get; init; }
    public bool SupportsJson { get; init; }
    public bool SupportsDryRun { get; init; }
    public CliVerbValue? Value { get; init; }
    public IReadOnlyList<CliOption> Options { get; init; } = Array.Empty<CliOption>();
    public required CliMcpExposure Mcp { get; init; }
}

/// <summary>The catalog document: verbs plus the exit-code contract.</summary>
public sealed record CliCatalogDocument
{
    public int Schema { get; init; }
    public required string Executable { get; init; }
    public required IReadOnlyDictionary<string, string> ExitCodes { get; init; }
    public required IReadOnlyList<CliVerbDescriptor> Verbs { get; init; }
}

public static class CliEffectNames
{
    /// <summary>The name a catalog, a report and an MCP client use for an effect. Every member is listed: a new one fails the build here.</summary>
    public static string ToWireName(this CliEffect effect) => effect switch
    {
        CliEffect.ReadOnly => "read-only",
        CliEffect.WritesFiles => "writes-files",
        CliEffect.Network => "network",
        CliEffect.Publishes => "publishes",
        CliEffect.MachineChanging => "machine-changing",
        CliEffect.Interactive => "interactive",
    };

    /// <summary>True for effects that must be confirmed (or previewed) before they run for real.</summary>
    public static bool NeedsConfirmation(this CliEffect effect) => effect switch
    {
        CliEffect.MachineChanging => true,
        CliEffect.Publishes => true,
        CliEffect.ReadOnly => false,
        CliEffect.WritesFiles => false,
        CliEffect.Network => false,
        CliEffect.Interactive => false,
    };
}
