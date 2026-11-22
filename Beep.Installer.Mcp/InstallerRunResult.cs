using System.Text.Json;

namespace Beep.Installer.Mcp;

/// <summary>What a tool call returns: the exit code as-is, plus enough to act on it.</summary>
public sealed record InstallerRunResult
{
    public required string Command { get; init; }

    /// <summary>The argument vector that was (or would have been) run. Credentials appear only as references.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>False when nothing ran: <see cref="Problems"/> says why.</summary>
    public bool Ran { get; init; }

    public int? ExitCode { get; init; }
    public string? ExitMeaning { get; init; }
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public bool OutputTruncated { get; init; }
    public long DurationMs { get; init; }

    /// <summary>Parsed standard output when the command was run with /JSON and printed valid JSON.</summary>
    public JsonElement? Json { get; init; }

    /// <summary>Standard output as text; omitted when <see cref="Json"/> carries it.</summary>
    public string? Stdout { get; init; }

    public string? Stderr { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    public static InstallerRunResult Refused(string command, IReadOnlyList<string> arguments, IReadOnlyList<string> problems)
        => new() { Command = command, Arguments = arguments, Ran = false, Problems = problems };

    /// <summary>The documented exit-code contract; 3010 is success-with-reboot, not an error.</summary>
    public static (string Meaning, bool Success) Interpret(int exitCode) => exitCode switch
    {
        0 => ("Success.", true),
        3010 => ("Success; a reboot is required to finish.", true),
        1 => ("The operation ran and failed, or a gate reported a blocking finding. See stdout/stderr.", false),
        2 => ("The command line, script or response file was rejected before anything ran. See stderr.", false),
        _ => ("The command failed with a non-standard exit code. See stdout/stderr.", false),
    };
}
