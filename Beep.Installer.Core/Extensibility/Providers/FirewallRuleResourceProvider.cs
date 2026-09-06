using Beep.Installer.Engine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record FirewallCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IFirewallCommandRunner
{
    bool IsSupported { get; }
    FirewallCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class FirewallRuleResourceProvider : IResourceProvider
{
    private readonly IFirewallCommandRunner _runner;

    public FirewallRuleResourceProvider()
        : this(new NetshFirewallCommandRunner())
    {
    }

    public FirewallRuleResourceProvider(IFirewallCommandRunner runner)
    {
        _runner = runner;
    }

    public string ResourceType => "firewall.rule";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = Input(operation, "name")
        };

        if (context.DryRun)
        {
            facts["mode"] = "dry-run";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        if (!_runner.IsSupported)
        {
            facts["platform"] = "unsupported";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        var query = _runner.Run(new[] { "advfirewall", "firewall", "show", "rule", $"name={Input(operation, "name")}" });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);
        return new ResourceDetectionResult { Exists = query.ExitCode == 0 && !IsNoRules(query), Facts = facts };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5301", $"{operation.Id}.name", "Firewall rule operation is missing name.");

        if (string.IsNullOrWhiteSpace(Input(operation, "program"))
            && string.IsNullOrWhiteSpace(Input(operation, "localPort"))
            && string.IsNullOrWhiteSpace(Input(operation, "remotePort"))
            && string.IsNullOrWhiteSpace(Input(operation, "service")))
        {
            return Error("BI5302", operation.Id, "Firewall rule must target a program, service, local port or remote port.");
        }

        if (Protocol(operation).Equals("any", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(Input(operation, "localPort"))
                || !string.IsNullOrWhiteSpace(Input(operation, "remotePort"))))
        {
            return Error("BI5303", $"{operation.Id}.protocol", "Firewall rules with Protocol=Any cannot specify explicit ports.");
        }

        if (!IsValidProfile(Input(operation, "profile")))
            return Error("BI5304", $"{operation.Id}.profile", "Firewall profile must be any, domain, private, public or a comma-separated combination.");

        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI5305", operation.Id, "Firewall rules require netsh advfirewall support.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: firewall rule would be created or updated."
            };

        var exists = Detect(operation, context).Exists;
        var arguments = exists ? BuildSetArguments(operation, context) : BuildAddArguments(operation, context);
        var result = _runner.Run(arguments);
        if (result.ExitCode != 0)
            return CommandFailure("BI5310", operation.Id, result, exists ? "update firewall rule" : "create firewall rule");

        return new ResourceProviderResult
        {
            Message = exists
                ? "Firewall rule updated through netsh advfirewall."
                : "Firewall rule created through netsh advfirewall."
        };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: firewall rule rollback would delete the rule."
            };

        if (!Detect(operation, context).Exists)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Firewall rule rollback skipped because the rule does not exist."
            };

        var delete = _runner.Run(new[] { "advfirewall", "firewall", "delete", "rule", $"name={Input(operation, "name")}" });
        if (delete.ExitCode != 0)
            return CommandFailure("BI5320", operation.Id, delete, "delete firewall rule");

        return new ResourceProviderResult { Message = "Firewall rule deleted through netsh advfirewall." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: firewall rule verification would query netsh advfirewall."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (!Detect(operation, context).Exists)
            return Error("BI5330", operation.Id, "Firewall rule verification failed because the rule does not exist.");

        return new ResourceProviderResult { Message = "Firewall rule exists in Windows Firewall." };
    }

    private static List<string> BuildAddArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = BaseArguments(operation, context);
        arguments.InsertRange(0, new[] { "advfirewall", "firewall", "add", "rule" });
        return arguments;
    }

    private static List<string> BuildSetArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = BaseArguments(operation, context);
        arguments.InsertRange(0, new[] { "advfirewall", "firewall", "set", "rule", $"name={Input(operation, "name")}", "new" });
        return arguments;
    }

    private static List<string> BaseArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = new List<string>
        {
            $"name={Input(operation, "name")}",
            $"dir={Direction(operation)}",
            $"action={Action(operation)}",
            $"enable={(BoolInput(operation, "enabled", defaultValue: true) ? "yes" : "no")}",
            $"profile={Profile(operation)}"
        };

        var description = Input(operation, "description");
        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"description={description}");

        var protocol = Protocol(operation);
        if (!string.Equals(protocol, "any", StringComparison.OrdinalIgnoreCase))
            arguments.Add($"protocol={protocol}");

        AddOptional(arguments, "localport", Input(operation, "localPort"));
        AddOptional(arguments, "remoteport", Input(operation, "remotePort"));
        AddOptional(arguments, "program", ResolveValue(Input(operation, "program"), context));
        AddOptional(arguments, "service", Input(operation, "service"));
        return arguments;
    }

    private static void AddOptional(List<string> arguments, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            arguments.Add($"{key}={value}");
    }

    private static string Direction(CompiledInstallOperation operation)
        => Input(operation, "direction").Equals("Out", StringComparison.OrdinalIgnoreCase)
           || Input(operation, "direction").Equals("out", StringComparison.OrdinalIgnoreCase)
            ? "out"
            : "in";

    private static string Action(CompiledInstallOperation operation)
        => Input(operation, "action").Equals("Block", StringComparison.OrdinalIgnoreCase)
           || Input(operation, "action").Equals("block", StringComparison.OrdinalIgnoreCase)
            ? "block"
            : "allow";

    private static string Protocol(CompiledInstallOperation operation)
        => Input(operation, "protocol").ToLowerInvariant() switch
        {
            "udp" => "udp",
            "any" => "any",
            _ => "tcp"
        };

    private static string Profile(CompiledInstallOperation operation)
        => string.IsNullOrWhiteSpace(Input(operation, "profile")) ? "any" : Input(operation, "profile").ToLowerInvariant();

    private static bool IsValidProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "any", "domain", "private", "public" };
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(allowed.Contains);
    }

    private static string ResolveValue(string value, ResourceProviderContext context)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var resolved = value;
        if (!string.IsNullOrWhiteSpace(context.InstallRoot))
            resolved = resolved.Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            resolved = resolved.Replace($"%{variable.Key}%", variable.Value, StringComparison.OrdinalIgnoreCase);
            resolved = resolved.Replace($"{{{variable.Key}}}", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool IsNoRules(FirewallCommandResult result)
        => result.StandardOutput.Contains("No rules match", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("No rules match", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static ResourceProviderResult Error(string code, string path, string message)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Diagnostics = new List<ProjectSchemaDiagnostic>
            {
                new(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };

    private static ResourceProviderResult CommandFailure(
        string code,
        string path,
        FirewallCommandResult result,
        string verb)
    {
        var message = $"Failed to {verb}. netsh.exe exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";

        return Error(code, path, message);
    }
}

internal sealed class NetshFirewallCommandRunner : IFirewallCommandRunner
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public FirewallCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new FirewallCommandResult(-1, "", "Windows Firewall netsh support is not available on this platform.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new FirewallCommandResult(process.ExitCode, standardOutput, standardError);
    }
}
