using Beep.Installer.Engine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record ScheduledTaskCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IScheduledTaskCommandRunner
{
    bool IsSupported { get; }
    ScheduledTaskCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class ScheduledTaskResourceProvider : IResourceProvider
{
    private readonly IScheduledTaskCommandRunner _runner;

    public ScheduledTaskResourceProvider()
        : this(new SchtasksCommandRunner())
    {
    }

    public ScheduledTaskResourceProvider(IScheduledTaskCommandRunner runner)
    {
        _runner = runner;
    }

    public string ResourceType => "scheduled-task.create";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["taskName"] = TaskName(operation, context)
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

        var query = _runner.Run(new[] { "/Query", "/TN", TaskName(operation, context) });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);

        return new ResourceDetectionResult { Exists = query.ExitCode == 0, Facts = facts };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5201", $"{operation.Id}.name", "Scheduled task operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "executablePath")))
            return Error("BI5202", $"{operation.Id}.executablePath", "Scheduled task operation is missing executablePath.");

        var trigger = Input(operation, "trigger");
        if (RequiresStartTime(trigger) && !IsValidTaskTime(Input(operation, "startTime")))
            return Error("BI5203", $"{operation.Id}.startTime", $"Scheduled task trigger '{trigger}' requires StartTime in HH:mm format.");

        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI5204", operation.Id, "Scheduled task operations require schtasks.exe support.");

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
                Message = "Dry run: scheduled task would be created or updated."
            };

        var create = _runner.Run(BuildCreateArguments(operation, context));
        if (create.ExitCode != 0)
            return CommandFailure("BI5210", operation.Id, create, "create or update scheduled task");

        if (!BoolInput(operation, "enabled", defaultValue: true))
        {
            var disable = _runner.Run(new[] { "/Change", "/TN", TaskName(operation, context), "/DISABLE" });
            if (disable.ExitCode != 0)
                return CommandFailure("BI5211", operation.Id, disable, "disable scheduled task");
        }

        return new ResourceProviderResult { Message = "Scheduled task created or updated through Task Scheduler." };
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
                Message = "Dry run: scheduled task rollback would end and delete the task."
            };

        if (!Detect(operation, context).Exists)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Scheduled task rollback skipped because the task does not exist."
            };

        if (BoolInput(operation, "stopOnUninstall", defaultValue: true))
        {
            var end = _runner.Run(new[] { "/End", "/TN", TaskName(operation, context) });
            if (end.ExitCode != 0 && !IsTaskNotRunning(end))
                return CommandFailure("BI5220", operation.Id, end, "end scheduled task");
        }

        var delete = _runner.Run(new[] { "/Delete", "/TN", TaskName(operation, context), "/F" });
        if (delete.ExitCode != 0)
            return CommandFailure("BI5221", operation.Id, delete, "delete scheduled task");

        return new ResourceProviderResult { Message = "Scheduled task ended and deleted through Task Scheduler." };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: scheduled task verification would query Task Scheduler."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var detection = Detect(operation, context);
        if (!detection.Exists)
            return Error("BI5230", operation.Id, "Scheduled task verification failed because the task does not exist.");

        return new ResourceProviderResult { Message = "Scheduled task exists in Task Scheduler." };
    }

    private static List<string> BuildCreateArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = new List<string>
        {
            "/Create",
            "/F",
            "/TN",
            TaskName(operation, context),
            "/TR",
            TaskRunCommand(operation, context),
            "/SC",
            ToSchedule(Input(operation, "trigger"))
        };

        if (RequiresStartTime(Input(operation, "trigger")))
        {
            arguments.Add("/ST");
            arguments.Add(Input(operation, "startTime"));
        }

        if (BoolInput(operation, "runElevated"))
        {
            arguments.Add("/RL");
            arguments.Add("HIGHEST");
        }

        var username = Input(operation, "username");
        if (!string.IsNullOrWhiteSpace(username))
        {
            arguments.Add("/RU");
            arguments.Add(username);
        }

        var password = Input(operation, "password");
        if (!string.IsNullOrWhiteSpace(password) && password != "<redacted>")
        {
            arguments.Add("/RP");
            arguments.Add(password);
        }

        return arguments;
    }

    private static string TaskRunCommand(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var executable = QuoteIfNeeded(ResolveValue(Input(operation, "executablePath"), context));
        var args = ResolveValue(Input(operation, "arguments"), context);
        var command = string.IsNullOrWhiteSpace(args) ? executable : $"{executable} {args}";

        var workingDirectory = ResolveValue(Input(operation, "workingDirectory"), context);
        return string.IsNullOrWhiteSpace(workingDirectory)
            ? command
            : $"cmd.exe /c cd /d {QuoteIfNeeded(workingDirectory)} && {command}";
    }

    private static string TaskName(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var name = ResolveValue(Input(operation, "name"), context).Trim();
        return name.StartsWith("\\", StringComparison.Ordinal) ? name : "\\" + name;
    }

    private static string ToSchedule(string trigger) => trigger.ToLowerInvariant() switch
    {
        "onstartup" => "ONSTART",
        "daily" => "DAILY",
        "once" => "ONCE",
        _ => "ONLOGON"
    };

    private static bool RequiresStartTime(string trigger)
        => trigger.Equals("daily", StringComparison.OrdinalIgnoreCase)
           || trigger.Equals("once", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidTaskTime(string value)
        => TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

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

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('"'))
            return value;

        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue = false)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsTaskNotRunning(ScheduledTaskCommandResult result)
        => result.StandardOutput.Contains("not currently running", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("not currently running", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("0x41303", StringComparison.OrdinalIgnoreCase);

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
        ScheduledTaskCommandResult result,
        string verb)
    {
        var message = $"Failed to {verb}. schtasks.exe exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";

        return Error(code, path, message);
    }
}

internal sealed class SchtasksCommandRunner : IScheduledTaskCommandRunner
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public ScheduledTaskCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new ScheduledTaskCommandResult(-1, "", "Task Scheduler is not available on this platform.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
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

        return new ScheduledTaskCommandResult(process.ExitCode, standardOutput, standardError);
    }
}
