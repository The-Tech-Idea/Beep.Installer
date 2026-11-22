using System.Text.RegularExpressions;
using Beep.Installer.Engine;

namespace Beep.Installer.Core.Cli;

/// <summary>What a caller (an MCP tool, a script generator) asks for: a verb and its inputs.</summary>
public sealed record CliInvocationRequest
{
    /// <summary>Catalog id or token of the verb.</summary>
    public required string Command { get; init; }

    /// <summary>The verb's own value (<c>/BUILD=&lt;value&gt;</c>); null for flag verbs.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Switches by name. A flag switch takes <c>"true"</c> (or an empty string); a value switch takes its value.
    /// Names may be written <c>/OUT=</c>, <c>/OUT</c> or <c>OUT</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary><c>/PROPERTY:Name=Value</c> unattended properties.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }

    /// <summary>Append <c>/DRYRUN</c>. Refused for a verb that does not honour it.</summary>
    public bool DryRun { get; init; }

    /// <summary>Acknowledges a machine-changing verb will really change this machine.</summary>
    public bool AllowMachineChanges { get; init; }

    /// <summary>Append <c>/JSON</c> when the verb supports it and the caller did not choose.</summary>
    public bool PreferJson { get; init; } = true;
}

public sealed record CliInvocation(
    CliVerbDescriptor? Verb,
    IReadOnlyList<string> Arguments,
    bool JsonRequested,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Turns a <see cref="CliInvocationRequest"/> into the exact argument vector for the executable, or
/// a list of reasons it cannot. The one place the catalog's rules (required switches, secret
/// references, dry-run support, machine-change confirmation) are enforced, so the MCP server and
/// any other caller cannot disagree about them.
///
/// Arguments are produced as a vector and never joined into a command string, so a value containing
/// spaces or quotes cannot become a second argument.
/// </summary>
public static partial class CliInvocationBuilder
{
    [GeneratedRegex(@"^(secret://env/|env:)[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretReference();

    public static CliInvocation Build(CliVerbCatalog catalog, CliInvocationRequest request)
    {
        var problems = new List<string>();
        var verb = catalog.Find(request.Command);
        if (verb is null)
            return Fail(problems, $"Unknown command '{request.Command}'. Use installer_list_commands to see the available commands.");

        var args = new List<string>();
        if (!verb.Mcp.Exposed)
            problems.Add($"'{verb.Id}' is not available here: {verb.Mcp.Reason}");

        AddVerbToken(verb, request, args, problems);
        AddOptions(verb, request, args, problems);
        AddProperties(verb, request, args, problems);
        AddDryRun(verb, request, args, problems);
        EnforceMachineChangeConfirmation(verb, request, args, problems);

        var jsonRequested = args.Contains("/JSON", StringComparer.OrdinalIgnoreCase);
        if (!jsonRequested && verb.SupportsJson && request.PreferJson)
        {
            args.Add("/JSON");
            jsonRequested = true;
        }

        if (problems.Count == 0)
        {
            // The executable validates its own argument vector and answers exit 2; asking the same
            // validator first turns that into a readable reason before anything is spawned.
            var checkedArgs = EnterpriseCommandLine.ExpandAndValidate(args.ToArray());
            foreach (var d in checkedArgs.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                problems.Add($"{d.Code}: {d.Message}");
        }

        return new CliInvocation(verb, args, jsonRequested, problems);
    }

    private static void AddVerbToken(CliVerbDescriptor verb, CliInvocationRequest request, List<string> args, List<string> problems)
    {
        if (verb.Kind == CliVerbKind.Flag)
        {
            if (!string.IsNullOrEmpty(request.Value))
                problems.Add($"'{verb.Id}' takes no value; pass its inputs as options.");
            args.Add(verb.Token);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Value))
            problems.Add($"'{verb.Id}' needs a value: {verb.Value?.Name} — {verb.Value?.Description}.");
        else
            args.Add(verb.Token + request.Value);
    }

    private static void AddOptions(CliVerbDescriptor verb, CliInvocationRequest request, List<string> args, List<string> problems)
    {
        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawValue) in request.Options ?? new Dictionary<string, string>())
        {
            var option = FindOption(verb, rawName);
            if (option is null)
            {
                problems.Add($"'{verb.Id}' has no option '{rawName}'. Known: {string.Join(", ", verb.Options.Select(o => o.Name))}.");
                continue;
            }

            if (option.McpBlockedReason is not null)
            {
                problems.Add($"{option.Name} cannot be set here: {option.McpBlockedReason}");
                continue;
            }

            supplied.Add(option.Name);
            AddOption(option, rawValue, args, problems);
        }

        foreach (var missing in verb.Options.Where(o => o.Required && !supplied.Contains(o.Name)))
            problems.Add($"'{verb.Id}' requires option {missing.Name}.");
    }

    private static void AddOption(CliOption option, string value, List<string> args, List<string> problems)
    {
        switch (option.Kind)
        {
            case CliOptionKind.Flag:
                if (string.IsNullOrEmpty(value) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    args.Add(option.Name);
                else if (!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{option.Name} is a switch; pass \"true\" or leave it out.");
                break;

            case CliOptionKind.Value:
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"{option.Name} needs a value.");
                    break;
                }

                if (option.Secret && !SecretReference().IsMatch(value))
                {
                    // Never echo the offending value: it may be the secret itself.
                    problems.Add($"{option.Name} carries a credential; pass a reference such as secret://env/NAME or env:NAME, not the credential.");
                    break;
                }

                // The matrix runner's long options take a separate value argument, not NAME=value.
                if (option.Name.StartsWith("--", StringComparison.Ordinal))
                {
                    args.Add(option.Name.TrimEnd('='));
                    args.Add(value);
                }
                else
                {
                    args.Add(option.Name + value);
                }
                break;

            case CliOptionKind.Property:
                problems.Add($"{option.Name} is supplied through 'properties', not 'options'.");
                break;
        }
    }

    private static void AddProperties(CliVerbDescriptor verb, CliInvocationRequest request, List<string> args, List<string> problems)
    {
        if (request.Properties is not { Count: > 0 }) return;
        if (!verb.Options.Any(o => o.Kind == CliOptionKind.Property))
        {
            problems.Add($"'{verb.Id}' does not take /PROPERTY: values.");
            return;
        }

        foreach (var (name, value) in request.Properties)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Any(char.IsWhiteSpace))
                problems.Add($"Property name '{name}' is not valid; use a plain name without '=' or spaces.");
            else
                args.Add($"/PROPERTY:{name}={value}");
        }
    }

    private static void AddDryRun(CliVerbDescriptor verb, CliInvocationRequest request, List<string> args, List<string> problems)
    {
        if (!request.DryRun) return;
        if (!verb.SupportsDryRun)
        {
            problems.Add($"'{verb.Id}' does not support /DRYRUN.");
            return;
        }

        if (!args.Contains("/DRYRUN", StringComparer.OrdinalIgnoreCase))
            args.Add("/DRYRUN");
    }

    private static void EnforceMachineChangeConfirmation(CliVerbDescriptor verb, CliInvocationRequest request, List<string> args, List<string> problems)
    {
        if (!verb.Effect.NeedsConfirmation()) return;
        var dryRun = args.Contains("/DRYRUN", StringComparer.OrdinalIgnoreCase);
        if (dryRun || request.AllowMachineChanges) return;

        var what = verb.Effect == CliEffect.Publishes ? "publishes to an external feed" : "changes this machine";
        problems.Add(verb.SupportsDryRun
            ? $"'{verb.Id}' {what}. Pass dryRun=true to preview it, or allowMachineChanges=true to run it for real."
            : $"'{verb.Id}' {what} and has no dry-run. Pass allowMachineChanges=true to run it for real.");
    }

    private static CliOption? FindOption(CliVerbDescriptor verb, string rawName)
    {
        var name = rawName.Trim();
        if (name.Length == 0) return null;
        if (!name.StartsWith('/') && !name.StartsWith("--")) name = "/" + name;
        var bare = name.TrimEnd('=', ':');
        return verb.Options.FirstOrDefault(o =>
            string.Equals(o.Name.TrimEnd('=', ':'), bare, StringComparison.OrdinalIgnoreCase));
    }

    private static CliInvocation Fail(List<string> problems, string reason)
    {
        problems.Add(reason);
        return new CliInvocation(null, Array.Empty<string>(), false, problems);
    }
}
