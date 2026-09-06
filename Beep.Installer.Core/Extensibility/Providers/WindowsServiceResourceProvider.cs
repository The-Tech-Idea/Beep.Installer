using Beep.Installer.Engine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beep.Installer.Extensibility.Providers;

public sealed record WindowsServiceCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IWindowsServiceCommandRunner
{
    bool IsSupported { get; }
    WindowsServiceCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class WindowsServiceResourceProvider : IResourceProvider
{
    private readonly IWindowsServiceCommandRunner _runner;

    public WindowsServiceResourceProvider()
        : this(new ScWindowsServiceCommandRunner())
    {
    }

    public WindowsServiceResourceProvider(IWindowsServiceCommandRunner runner)
    {
        _runner = runner;
    }

    public string ResourceType => "service.install";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.Process | InstallerExtensionPermission.MachineScope | InstallerExtensionPermission.Secrets;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["serviceName"] = Input(operation, "name")
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

        var query = _runner.Run(new[] { "query", Input(operation, "name") });
        facts["queryExitCode"] = query.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["queryOutput"] = Truncate(query.StandardOutput, 500);
        facts["queryError"] = Truncate(query.StandardError, 500);

        if (query.ExitCode == 0)
        {
            var configuration = _runner.Run(new[] { "qc", Input(operation, "name") });
            facts["configurationExitCode"] = configuration.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            facts["configurationOutput"] = Truncate(configuration.StandardOutput, 1000);
            facts["configurationError"] = Truncate(configuration.StandardError, 500);
            if (configuration.ExitCode == 0)
                CaptureServiceConfigurationFacts(configuration.StandardOutput, facts);

            var description = _runner.Run(new[] { "qdescription", Input(operation, "name") });
            facts["descriptionExitCode"] = description.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            facts["descriptionOutput"] = Truncate(description.StandardOutput, 500);
            facts["descriptionError"] = Truncate(description.StandardError, 500);
            if (description.ExitCode == 0)
                facts["description"] = ParseScValue(description.StandardOutput, "DESCRIPTION");
        }

        return new ResourceDetectionResult
        {
            Exists = query.ExitCode == 0,
            Facts = facts
        };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "name")))
            return Error("BI5001", $"{operation.Id}.name", "Windows service operation is missing name.");
        if (string.IsNullOrWhiteSpace(Input(operation, "executablePath")))
            return Error("BI5002", $"{operation.Id}.executablePath", "Windows service operation is missing executablePath.");

        var account = Input(operation, "account");
        if (account.Equals("User", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(Input(operation, "username")))
            return Error("BI5003", $"{operation.Id}.username", "Windows service uses a user account but username is missing.");

        var password = Input(operation, "password");
        if (!string.IsNullOrWhiteSpace(password)
            && password != "<redacted>"
            && !SecretReference.IsReference(password))
            return Error("BI5005", $"{operation.Id}.password", "Windows service password must be an opaque secret reference such as env:NAME or secret://env/NAME.");

        if (SecretReference.IsReference(password)
            && !SecretReference.TryParse(password, out _, out var parseError))
            return Error("BI5006", $"{operation.Id}.password", $"Windows service password secret reference is invalid: {parseError}");

        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI5004", operation.Id, "Windows service operations require Windows Service Control Manager support.");

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection,
        ResourceProviderContext context)
    {
        StampPreviousConfiguration(operation, detection);

        return new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
            Operations = new List<CompiledInstallOperation> { operation }
        };
    }

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: Windows service would be installed or updated."
            };

        var passwordResolution = ResolvePassword(operation, context);
        if (passwordResolution.Result.Code == ResourceProviderResultCode.Failed)
            return passwordResolution.Result;

        var exists = Detect(operation, context).Exists;
        var configure = _runner.Run(BuildConfigureArguments(operation, context, exists, passwordResolution.Password));
        if (configure.ExitCode != 0)
            return CommandFailure("BI5010", operation.Id, configure, exists ? "update" : "create");

        var description = Input(operation, "description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descriptionResult = _runner.Run(new[] { "description", Input(operation, "name"), description });
            if (descriptionResult.ExitCode != 0)
                return CommandFailure("BI5011", operation.Id, descriptionResult, "set description for");
        }

        if (int.TryParse(Input(operation, "failureRestartDelaySeconds"), out var delaySeconds) && delaySeconds > 0)
        {
            var failureResult = _runner.Run(new[]
            {
                "failure",
                Input(operation, "name"),
                "reset=",
                "86400",
                "actions=",
                $"restart/{delaySeconds * 1000}"
            });
            if (failureResult.ExitCode != 0)
                return CommandFailure("BI5012", operation.Id, failureResult, "configure failure recovery for");
        }

        if (BoolInput(operation, "startAfterInstall"))
        {
            var startResult = _runner.Run(new[] { "start", Input(operation, "name") });
            if (startResult.ExitCode != 0)
                return CommandFailure("BI5013", operation.Id, startResult, "start");
        }

        return new ResourceProviderResult
        {
            Message = exists
                ? "Windows service updated through Service Control Manager."
                : "Windows service created through Service Control Manager."
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
                Message = "Dry run: Windows service rollback would stop and delete the service."
            };

        if (!Detect(operation, context).Exists)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Windows service rollback skipped because the service does not exist."
            };

        if (BoolInput(operation, "previous.exists"))
        {
            var restore = _runner.Run(BuildRestoreArguments(operation, context));
            if (restore.ExitCode != 0)
                return CommandFailure("BI5022", operation.Id, restore, "restore previous configuration for");

            var description = Input(operation, "previous.description");
            var descriptionResult = _runner.Run(new[] { "description", Input(operation, "name"), description });
            if (descriptionResult.ExitCode != 0)
                return CommandFailure("BI5023", operation.Id, descriptionResult, "restore previous description for");

            return new ResourceProviderResult
            {
                Message = "Windows service previous configuration restored through Service Control Manager."
            };
        }

        if (BoolInput(operation, "stopOnUninstall"))
        {
            var stopResult = _runner.Run(new[] { "stop", Input(operation, "name") });
            if (stopResult.ExitCode != 0 && !IsAlreadyStopped(stopResult))
                return CommandFailure("BI5020", operation.Id, stopResult, "stop");
        }

        var deleteResult = _runner.Run(new[] { "delete", Input(operation, "name") });
        if (deleteResult.ExitCode != 0)
            return CommandFailure("BI5021", operation.Id, deleteResult, "delete");

        return new ResourceProviderResult
        {
            Message = "Windows service stopped and deleted through Service Control Manager."
        };
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = "Dry run: Windows service verification would query Service Control Manager."
            };

        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        var detection = Detect(operation, context);
        if (!detection.Exists)
            return Error("BI5030", operation.Id, "Windows service verification failed because the service does not exist.");

        return new ResourceProviderResult
        {
            Message = "Windows service exists in Service Control Manager."
        };
    }

    private static List<string> BuildConfigureArguments(
        CompiledInstallOperation operation,
        ResourceProviderContext context,
        bool exists,
        string password)
    {
        var arguments = new List<string>
        {
            exists ? "config" : "create",
            Input(operation, "name"),
            "binPath=",
            BuildBinaryPath(operation, context),
            "start=",
            ToScStartMode(Input(operation, "startMode"))
        };

        var displayName = Input(operation, "displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            arguments.Add(exists ? "DisplayName=" : "displayName=");
            arguments.Add(displayName);
        }

        var account = ToScAccount(Input(operation, "account"), Input(operation, "username"));
        if (!string.IsNullOrWhiteSpace(account))
        {
            arguments.Add("obj=");
            arguments.Add(account);
        }

        if (!string.IsNullOrWhiteSpace(password) && password != "<redacted>")
        {
            arguments.Add("password=");
            arguments.Add(password);
        }

        var dependsOn = Input(operation, "dependsOn");
        if (!string.IsNullOrWhiteSpace(dependsOn))
        {
            arguments.Add("depend=");
            arguments.Add(dependsOn.Replace(",", "/", StringComparison.OrdinalIgnoreCase));
        }

        return arguments;
    }

    private static List<string> BuildRestoreArguments(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var arguments = new List<string>
        {
            "config",
            Input(operation, "name"),
            "binPath=",
            ResolveValue(Input(operation, "previous.binaryPath"), context),
            "start=",
            ToScStartMode(Input(operation, "previous.startMode"))
        };

        var displayName = Input(operation, "previous.displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            arguments.Add("DisplayName=");
            arguments.Add(displayName);
        }

        var account = Input(operation, "previous.account");
        if (!string.IsNullOrWhiteSpace(account))
        {
            arguments.Add("obj=");
            arguments.Add(account);
        }

        var dependencies = Input(operation, "previous.dependsOn");
        if (!string.IsNullOrWhiteSpace(dependencies))
        {
            arguments.Add("depend=");
            arguments.Add(dependencies);
        }

        return arguments;
    }

    private static void StampPreviousConfiguration(
        CompiledInstallOperation operation,
        ResourceDetectionResult detection)
    {
        operation.Inputs["previous.exists"] = detection.Exists ? "true" : "false";
        if (!detection.Exists)
            return;

        CopyFact("binaryPath", "previous.binaryPath");
        CopyFact("startMode", "previous.startMode");
        CopyFact("serviceStartName", "previous.account");
        CopyFact("displayName", "previous.displayName");
        CopyFact("dependsOn", "previous.dependsOn");
        CopyFact("description", "previous.description");

        void CopyFact(string source, string target)
        {
            if (detection.Facts.TryGetValue(source, out var value))
                operation.Inputs[target] = value;
        }
    }

    private static void CaptureServiceConfigurationFacts(string output, SortedDictionary<string, string> facts)
    {
        Add("BINARY_PATH_NAME", "binaryPath");
        Add("START_TYPE", "startMode", ParseStartMode);
        Add("SERVICE_START_NAME", "serviceStartName");
        Add("DISPLAY_NAME", "displayName");
        Add("DEPENDENCIES", "dependsOn", value => value.Replace('\n', '/').Replace('\r', '/').Trim('/'));

        void Add(string label, string key, Func<string, string>? transform = null)
        {
            var value = ParseScValue(output, label);
            if (!string.IsNullOrWhiteSpace(value))
                facts[key] = transform?.Invoke(value) ?? value;
        }
    }

    private static string ParseScValue(string output, string label)
    {
        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon < 0 || colon == trimmed.Length - 1)
                return "";

            return trimmed[(colon + 1)..].Trim();
        }

        return "";
    }

    private static string ParseStartMode(string value)
    {
        var upper = value.ToUpperInvariant();
        if (upper.Contains("DELAYED", StringComparison.OrdinalIgnoreCase))
            return "delayed-auto";
        if (upper.Contains("AUTO", StringComparison.OrdinalIgnoreCase))
            return "auto";
        if (upper.Contains("DEMAND", StringComparison.OrdinalIgnoreCase))
            return "demand";
        if (upper.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
            return "disabled";
        return value;
    }

    private static (string Password, ResourceProviderResult Result) ResolvePassword(
        CompiledInstallOperation operation,
        ResourceProviderContext context)
    {
        var value = Input(operation, "password");
        if (string.IsNullOrWhiteSpace(value) || value == "<redacted>")
            return ("", new ResourceProviderResult());

        if (!SecretReference.TryParse(value, out var reference, out var parseError))
            return ("", Error("BI5006", $"{operation.Id}.password", $"Windows service password secret reference is invalid: {parseError}"));

        var resolved = context.SecretProvider.Resolve(reference);
        if (!resolved.Success || resolved.Value is null)
            return ("", Error("BI5007", $"{operation.Id}.password", resolved.Error ?? $"Windows service password secret reference '{value}' could not be resolved."));

        return (resolved.Value, new ResourceProviderResult());
    }

    private static string BuildBinaryPath(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var normalizedExecutable = ResolveValue(Input(operation, "executablePath"), context);
        var binaryPath = normalizedExecutable.Contains(' ') && !normalizedExecutable.StartsWith('"')
            ? $"\"{normalizedExecutable}\""
            : normalizedExecutable;
        var args = ResolveValue(Input(operation, "arguments"), context);
        return string.IsNullOrWhiteSpace(args) ? binaryPath : $"{binaryPath} {args}";
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

    private static string ToScStartMode(string value) => value.ToLowerInvariant() switch
    {
        "delayedauto" or "delayed-auto" or "delayed_auto" => "delayed-auto",
        "manual" or "demand" => "demand",
        "disabled" => "disabled",
        _ => "auto"
    };

    private static string ToScAccount(string account, string username)
        => account.ToLowerInvariant() switch
        {
            "localservice" => "NT AUTHORITY\\LocalService",
            "networkservice" => "NT AUTHORITY\\NetworkService",
            "user" => username,
            _ => "LocalSystem"
        };

    private static bool BoolInput(CompiledInstallOperation operation, string key)
        => bool.TryParse(Input(operation, key), out var value) && value;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsAlreadyStopped(WindowsServiceCommandResult result)
        => result.StandardOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("has not been started", StringComparison.OrdinalIgnoreCase)
           || result.StandardError.Contains("1062", StringComparison.OrdinalIgnoreCase);

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
        WindowsServiceCommandResult result,
        string verb)
    {
        var message = $"Failed to {verb} Windows service. sc.exe exited with code {result.ExitCode}.";
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {Truncate(detail.Trim(), 500)}";

        return Error(code, path, message);
    }
}

internal sealed class ScWindowsServiceCommandRunner : IWindowsServiceCommandRunner
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public WindowsServiceCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (!IsSupported)
            return new WindowsServiceCommandResult(-1, "", "Windows Service Control Manager is not available on this platform.");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
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

        return new WindowsServiceCommandResult(process.ExitCode, standardOutput, standardError);
    }
}
