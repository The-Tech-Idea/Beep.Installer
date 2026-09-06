using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine.Msi;

public sealed class MsiLifecycleMatrixRunnerOptions
{
    public string EnvironmentId { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Channel { get; init; } = "local";
    public string PlanHash { get; init; } = "";
    public string LogDirectory { get; init; } = "";
    public string MsiPath { get; init; } = "";
    public string UpdatedMsiPath { get; init; } = "";
    public string TransformPath { get; init; } = "";
    public string PatchPath { get; init; } = "";
    public string PatchProductPath { get; init; } = "";
    public string ScenarioPackPath { get; init; } = "";
    public string MsiexecToolPath { get; init; } = "msiexec";
    public bool DryRun { get; init; }
    public IReadOnlyList<string> Properties { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ScenarioIds { get; init; } = Array.Empty<string>();
    public Func<MsiToolInvocation, MsiToolResult>? ToolRunner { get; init; }
}

public sealed class MsiLifecycleMatrixScenarioPack
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;
    [JsonPropertyName("scenarios")]
    public List<MsiLifecycleMatrixScenario> Scenarios { get; init; } = new();
}

public sealed class MsiLifecycleMatrixScenario
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("category")]
    public string Category { get; init; } = "lifecycle";
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    [JsonPropertyName("actions")]
    public List<string> Actions { get; init; } = new();
    [JsonPropertyName("requiredArtifacts")]
    public List<string> RequiredArtifacts { get; init; } = new();
    [JsonPropertyName("properties")]
    public List<string> Properties { get; init; } = new();
}

public sealed class MsiLifecycleMatrixRunnerReport
{
    [JsonPropertyName("environmentId")]
    public string EnvironmentId { get; init; } = "";
    [JsonPropertyName("operatingSystem")]
    public string OperatingSystem { get; init; } = "";
    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = "";
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "";
    [JsonPropertyName("planHash")]
    public string PlanHash { get; init; } = "";
    [JsonPropertyName("hostMachineName")]
    public string HostMachineName { get; init; } = "";
    [JsonPropertyName("hostOperatingSystem")]
    public string HostOperatingSystem { get; init; } = "";
    [JsonPropertyName("hostArchitecture")]
    public string HostArchitecture { get; init; } = "";
    [JsonPropertyName("logDirectory")]
    public string LogDirectory { get; init; } = "";
    [JsonPropertyName("startedUtc")]
    public DateTimeOffset StartedUtc { get; init; }
    [JsonPropertyName("completedUtc")]
    public DateTimeOffset CompletedUtc { get; init; }
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; init; }
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
    [JsonPropertyName("scenarioPackPath")]
    public string ScenarioPackPath { get; init; } = "";
    [JsonPropertyName("scenarios")]
    public List<MsiLifecycleMatrixScenarioEvidence> Scenarios { get; init; } = new();
    [JsonPropertyName("actions")]
    public List<MsiLifecycleMatrixRunnerAction> Actions { get; init; } = new();
}

public sealed class MsiLifecycleMatrixScenarioEvidence
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("category")]
    public string Category { get; init; } = "";
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    [JsonPropertyName("actions")]
    public List<string> Actions { get; init; } = new();
    [JsonPropertyName("success")]
    public bool Success { get; init; }
}

public sealed class MsiLifecycleMatrixRunnerAction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("scenarioId")]
    public string ScenarioId { get; init; } = "";
    [JsonPropertyName("scenarioCategory")]
    public string ScenarioCategory { get; init; } = "";
    [JsonPropertyName("artifactPath")]
    public string ArtifactPath { get; init; } = "";
    [JsonPropertyName("logPath")]
    public string LogPath { get; init; } = "";
    [JsonPropertyName("commandLine")]
    public string CommandLine { get; init; } = "";
    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("standardOutput")]
    public string StandardOutput { get; init; } = "";
    [JsonPropertyName("standardError")]
    public string StandardError { get; init; } = "";
}

public static class MsiLifecycleMatrixRunner
{
    public const string ReportFileName = "matrix-runner-report.json";
    public const string EnterpriseDefaultScenarioPack = "enterprise-default";
    public const string DriverDefaultScenarioPack = "driver-default";
    public const string PatchDefaultScenarioPack = "patch-default";

    public static MsiLifecycleMatrixRunnerOptions Parse(IReadOnlyList<string> args)
    {
        var properties = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dryRun = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "qualify", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/DRYRUN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/DRYRUN=true", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (string.Equals(arg, "--property", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "/PROPERTY", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                    properties.Add(args[++i]);
                continue;
            }

            if (string.Equals(arg, "--scenario", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "/SCENARIO", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                    values["scenario"] = AppendValue(Value(values, "scenario"), args[++i]);
                continue;
            }

            if ((arg.StartsWith("--", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal)) && i + 1 < args.Count)
            {
                values[arg.TrimStart('-', '/')] = args[++i];
            }
        }

        return new MsiLifecycleMatrixRunnerOptions
        {
            EnvironmentId = Value(values, "environment"),
            OperatingSystem = Value(values, "os"),
            Architecture = Value(values, "arch"),
            Channel = Value(values, "channel", "local"),
            PlanHash = Value(values, "plan-hash"),
            LogDirectory = Value(values, "log-dir"),
            MsiPath = Value(values, "msi"),
            UpdatedMsiPath = Value(values, "updated-msi"),
            TransformPath = Value(values, "mst"),
            PatchPath = Value(values, "msp"),
            PatchProductPath = Value(values, "msp-product"),
            ScenarioPackPath = Value(values, "scenario-pack"),
            MsiexecToolPath = Value(values, "msiexec", "msiexec"),
            DryRun = dryRun,
            Properties = properties,
            ScenarioIds = SplitList(Value(values, "scenario")).ToArray()
        };
    }

    public static MsiLifecycleMatrixRunnerReport Run(MsiLifecycleMatrixRunnerOptions options)
    {
        var started = DateTimeOffset.UtcNow;
        var logDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.LogDirectory)
            ? Path.Combine(Path.GetTempPath(), "BeepMsiMatrix", SafePathSegment(options.EnvironmentId))
            : options.LogDirectory);
        Directory.CreateDirectory(logDirectory);

        var actions = new List<MsiLifecycleMatrixRunnerAction>();
        var scenarios = new List<MsiLifecycleMatrixScenarioEvidence>();
        var validationMessage = Validate(options);
        if (!string.IsNullOrWhiteSpace(validationMessage))
            return Complete(options, logDirectory, started, actions, scenarios, false, 2, validationMessage);

        if (!IsSupportedChannel(options.Channel))
            return Complete(options, logDirectory, started, actions, scenarios, false, 3, $"Channel '{options.Channel}' is not supported by the first-party matrix runner. Supported channels: local, hyperv, azure.");

        IReadOnlyList<MsiLifecycleMatrixScenario> scenarioPlan;
        try
        {
            scenarioPlan = LoadScenarioPlan(options);
        }
        catch (InvalidOperationException ex)
        {
            return Complete(options, logDirectory, started, actions, scenarios, false, 4, ex.Message);
        }

        foreach (var scenario in scenarioPlan)
        {
            var before = actions.Count;
            foreach (var action in scenario.Actions)
            {
                var step = CreateScenarioAction(options, scenario, action, logDirectory);
                actions.Add(options.DryRun
                    ? DryRunAction(options, scenario, step.Name, step.ArtifactPath, step.LogPath, step.Arguments, step.ExpectFailure, step.ToolPath, step.AddMsiExecutionOptions)
                    : RunAction(options, scenario, step.Name, step.ArtifactPath, step.LogPath, step.Arguments, step.ExpectFailure, step.ToolPath, step.AddMsiExecutionOptions));
                if (!actions[^1].Success)
                    break;
            }

            var scenarioActions = actions.Skip(before).ToList();
            scenarios.Add(new MsiLifecycleMatrixScenarioEvidence
            {
                Id = scenario.Id,
                Category = scenario.Category,
                Description = scenario.Description,
                Actions = scenario.Actions.ToList(),
                Success = scenarioActions.Count > 0 && scenarioActions.All(a => a.Success)
            });

            if (scenarioActions.Any(a => !a.Success))
                break;
        }

        var success = actions.Count > 0 && actions.All(a => a.Success);
        return Complete(options, logDirectory, started, actions, scenarios, success, success ? 0 : actions.First(a => !a.Success).ExitCode, success ? "Matrix qualification completed." : "Matrix qualification failed.");
    }

    public static void WriteReport(MsiLifecycleMatrixRunnerReport report)
    {
        Directory.CreateDirectory(report.LogDirectory);
        File.WriteAllText(
            Path.Combine(report.LogDirectory, ReportFileName),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Validate(MsiLifecycleMatrixRunnerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EnvironmentId))
            return "--environment is required.";
        if (string.IsNullOrWhiteSpace(options.LogDirectory))
            return "--log-dir is required.";
        if (string.IsNullOrWhiteSpace(options.MsiPath))
            return "--msi is required for local matrix qualification.";
        if (!File.Exists(options.MsiPath))
            return $"MSI package '{Path.GetFullPath(options.MsiPath)}' was not found.";
        if (!string.IsNullOrWhiteSpace(options.UpdatedMsiPath) && !File.Exists(options.UpdatedMsiPath))
            return $"Updated MSI package '{Path.GetFullPath(options.UpdatedMsiPath)}' was not found.";
        if (!string.IsNullOrWhiteSpace(options.TransformPath) && !File.Exists(options.TransformPath))
            return $"MST transform '{Path.GetFullPath(options.TransformPath)}' was not found.";
        if (!string.IsNullOrWhiteSpace(options.PatchPath) && !File.Exists(options.PatchPath))
            return $"MSP patch '{Path.GetFullPath(options.PatchPath)}' was not found.";
        if (!string.IsNullOrWhiteSpace(options.PatchPath) && string.IsNullOrWhiteSpace(options.PatchProductPath))
            return "--msp-product is required when --msp is supplied.";
        return "";
    }

    private sealed record PlannedAction(
        string Name,
        string ArtifactPath,
        string LogPath,
        IReadOnlyList<string> Arguments,
        bool ExpectFailure = false,
        string ToolPath = "",
        bool AddMsiExecutionOptions = true);

    private static IReadOnlyList<MsiLifecycleMatrixScenario> LoadScenarioPlan(MsiLifecycleMatrixRunnerOptions options)
    {
        var scenarios = string.IsNullOrWhiteSpace(options.ScenarioPackPath)
            ? DefaultScenarios(options)
            : LoadScenarioPack(options);

        if (options.ScenarioIds.Count == 0)
            return scenarios;

        var selected = new List<MsiLifecycleMatrixScenario>();
        foreach (var id in options.ScenarioIds)
        {
            var match = scenarios.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new InvalidOperationException($"Scenario '{id}' was not found in the matrix scenario pack.");
            selected.Add(match);
        }

        return selected;
    }

    private static IReadOnlyList<MsiLifecycleMatrixScenario> DefaultScenarios(MsiLifecycleMatrixRunnerOptions options)
    {
        var scenarios = new List<MsiLifecycleMatrixScenario>();
        if (!string.IsNullOrWhiteSpace(options.TransformPath))
        {
            scenarios.Add(new MsiLifecycleMatrixScenario
            {
                Id = "mst-lifecycle",
                Category = "transform",
                Description = "Install MSI with MST transform and then uninstall.",
                RequiredArtifacts = new() { "msi", "mst" },
                Actions = new() { "mst-install", "mst-uninstall" }
            });
        }
        else
        {
            scenarios.Add(new MsiLifecycleMatrixScenario
            {
                Id = "msi-lifecycle",
                Category = "lifecycle",
                Description = "Install, repair and uninstall MSI package.",
                RequiredArtifacts = new() { "msi" },
                Actions = new() { "msi-install", "msi-repair", "msi-uninstall" }
            });
        }

        if (!string.IsNullOrWhiteSpace(options.PatchPath))
        {
            scenarios.Add(new MsiLifecycleMatrixScenario
            {
                Id = "msp-lifecycle",
                Category = "patch",
                Description = "Apply and remove MSP patch.",
                RequiredArtifacts = new() { "msp", "msp-product" },
                Actions = new() { "msp-apply", "msp-remove" }
            });
        }

        return scenarios;
    }

    private static IReadOnlyList<MsiLifecycleMatrixScenario> LoadScenarioPack(MsiLifecycleMatrixRunnerOptions options)
    {
        var path = ResolveScenarioPackPath(options.ScenarioPackPath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Matrix scenario pack '{path}' was not found.");

        var pack = JsonSerializer.Deserialize<MsiLifecycleMatrixScenarioPack>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Matrix scenario pack '{path}' is empty.");

        if (pack.Scenarios.Count == 0)
            throw new InvalidOperationException($"Matrix scenario pack '{path}' does not define scenarios.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in pack.Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.Id))
                throw new InvalidOperationException($"Matrix scenario pack '{path}' contains a scenario without an id.");
            if (!ids.Add(scenario.Id))
                throw new InvalidOperationException($"Matrix scenario pack '{path}' contains duplicate scenario id '{scenario.Id}'.");
            ValidateScenarioArtifacts(options, scenario);
            foreach (var action in scenario.Actions)
                _ = CreateScenarioAction(options, scenario, action, options.LogDirectory);
        }

        return pack.Scenarios;
    }

    private static void ValidateScenarioArtifacts(MsiLifecycleMatrixRunnerOptions options, MsiLifecycleMatrixScenario scenario)
    {
        foreach (var artifact in scenario.RequiredArtifacts)
        {
            var missing = artifact.ToLowerInvariant() switch
            {
                "msi" => string.IsNullOrWhiteSpace(options.MsiPath),
                "msi-updated" => string.IsNullOrWhiteSpace(options.UpdatedMsiPath),
                "mst" => string.IsNullOrWhiteSpace(options.TransformPath),
                "msp" => string.IsNullOrWhiteSpace(options.PatchPath),
                "msp-product" => string.IsNullOrWhiteSpace(options.PatchProductPath),
                _ => throw new InvalidOperationException($"Scenario '{scenario.Id}' requires unknown artifact '{artifact}'.")
            };
            if (missing)
                throw new InvalidOperationException($"Scenario '{scenario.Id}' requires artifact '{artifact}', but it was not provided.");
        }
    }

    private static PlannedAction CreateScenarioAction(MsiLifecycleMatrixRunnerOptions options, MsiLifecycleMatrixScenario scenario, string action, string logDirectory)
    {
        var safeScenarioId = SafePathSegment(scenario.Id);
        var name = action.Trim().ToLowerInvariant();
        return name switch
        {
            "msi-install" => new PlannedAction("msi-install", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-msi-install.log"), new[] { "/i", options.MsiPath }),
            "msi-repair" => new PlannedAction("msi-repair", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-msi-repair.log"), new[] { "/famus", options.MsiPath }),
            "msi-uninstall" => new PlannedAction("msi-uninstall", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-msi-uninstall.log"), new[] { "/x", options.MsiPath }),
            "msi-upgrade" => new PlannedAction("msi-upgrade", options.UpdatedMsiPath, Path.Combine(logDirectory, safeScenarioId + "-msi-upgrade.log"), new[] { "/i", options.UpdatedMsiPath }),
            "msi-downgrade-blocked" => new PlannedAction("msi-downgrade-blocked", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-msi-downgrade-blocked.log"), new[] { "/i", options.MsiPath }, ExpectFailure: true),
            "mst-install" => new PlannedAction("mst-install", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-mst-install.log"), new[] { "/i", options.MsiPath, "TRANSFORMS=" + Path.GetFullPath(options.TransformPath) }),
            "mst-uninstall" => new PlannedAction("mst-uninstall", options.MsiPath, Path.Combine(logDirectory, safeScenarioId + "-mst-uninstall.log"), new[] { "/x", options.MsiPath }),
            "msp-apply" => new PlannedAction("msp-apply", options.PatchPath, Path.Combine(logDirectory, safeScenarioId + "-msp-apply.log"), new[] { "/p", options.PatchPath, "REINSTALL=ALL", "REINSTALLMODE=ecmus" }),
            "msp-remove" => new PlannedAction("msp-remove", options.PatchPath, Path.Combine(logDirectory, safeScenarioId + "-msp-remove.log"), new[] { "/package", options.PatchProductPath, "/uninstall", options.PatchPath }),
            "msp-inventory-before" => new PlannedAction("msp-inventory-before", "windows-installer-patches", Path.Combine(logDirectory, safeScenarioId + "-msp-inventory-before.log"), PatchInventoryArguments(), ToolPath: "powershell", AddMsiExecutionOptions: false),
            "msp-inventory-after-apply" => new PlannedAction("msp-inventory-after-apply", "windows-installer-patches", Path.Combine(logDirectory, safeScenarioId + "-msp-inventory-after-apply.log"), PatchInventoryArguments(), ToolPath: "powershell", AddMsiExecutionOptions: false),
            "msp-inventory-after-remove" => new PlannedAction("msp-inventory-after-remove", "windows-installer-patches", Path.Combine(logDirectory, safeScenarioId + "-msp-inventory-after-remove.log"), PatchInventoryArguments(), ToolPath: "powershell", AddMsiExecutionOptions: false),
            "driver-store-before" => new PlannedAction("driver-store-before", "pnputil", Path.Combine(logDirectory, safeScenarioId + "-driver-store-before.log"), new[] { "/enum-drivers" }, ToolPath: "pnputil", AddMsiExecutionOptions: false),
            "driver-store-after-install" => new PlannedAction("driver-store-after-install", "pnputil", Path.Combine(logDirectory, safeScenarioId + "-driver-store-after-install.log"), new[] { "/enum-drivers" }, ToolPath: "pnputil", AddMsiExecutionOptions: false),
            "driver-store-after-uninstall" => new PlannedAction("driver-store-after-uninstall", "pnputil", Path.Combine(logDirectory, safeScenarioId + "-driver-store-after-uninstall.log"), new[] { "/enum-drivers" }, ToolPath: "pnputil", AddMsiExecutionOptions: false),
            _ => throw new InvalidOperationException($"Scenario '{scenario.Id}' uses unsupported matrix action '{action}'.")
        };
    }

    private static IReadOnlyList<string> PatchInventoryArguments()
        => new[]
        {
            "-NoProfile",
            "-Command",
            "$ErrorActionPreference = 'Stop'; $installer = New-Object -ComObject WindowsInstaller.Installer; @($installer.Patches) | ForEach-Object { [pscustomobject]@{ PatchCode = [string]$_ } } | ConvertTo-Json -Depth 4"
        };

    private static MsiLifecycleMatrixRunnerAction DryRunAction(MsiLifecycleMatrixRunnerOptions options, MsiLifecycleMatrixScenario scenario, string name, string artifactPath, string logPath, IReadOnlyList<string> actionArguments, bool expectFailure, string toolPath, bool addMsiExecutionOptions)
    {
        var invocation = CreateInvocation(options, scenario, actionArguments, logPath, toolPath, addMsiExecutionOptions);
        return new MsiLifecycleMatrixRunnerAction
        {
            Name = name,
            ScenarioId = scenario.Id,
            ScenarioCategory = scenario.Category,
            ArtifactPath = Path.GetFullPath(artifactPath),
            LogPath = logPath,
            CommandLine = invocation.CommandLine,
            ToolVersion = "dry-run",
            ExitCode = 0,
            Success = true,
            StandardOutput = expectFailure ? "dry-run expected-failure" : "dry-run"
        };
    }

    private static MsiLifecycleMatrixRunnerAction RunAction(MsiLifecycleMatrixRunnerOptions options, MsiLifecycleMatrixScenario scenario, string name, string artifactPath, string logPath, IReadOnlyList<string> actionArguments, bool expectFailure, string toolPath, bool addMsiExecutionOptions)
    {
        var invocation = CreateInvocation(options, scenario, actionArguments, logPath, toolPath, addMsiExecutionOptions);
        var result = options.ToolRunner?.Invoke(invocation) ?? RunTool(invocation);
        return new MsiLifecycleMatrixRunnerAction
        {
            Name = name,
            ScenarioId = scenario.Id,
            ScenarioCategory = scenario.Category,
            ArtifactPath = Path.GetFullPath(artifactPath),
            LogPath = logPath,
            CommandLine = invocation.CommandLine,
            ToolVersion = result.ToolVersion,
            ExitCode = result.ExitCode,
            Success = expectFailure ? result.ExitCode != 0 : result.ExitCode == 0,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError
        };
    }

    private static MsiToolInvocation CreateInvocation(MsiLifecycleMatrixRunnerOptions options, MsiLifecycleMatrixScenario scenario, IReadOnlyList<string> actionArguments, string logPath, string toolPath, bool addMsiExecutionOptions)
    {
        var arguments = new List<string>(actionArguments.Select(FullPathIfFile));
        if (addMsiExecutionOptions)
        {
            foreach (var property in options.Properties.Concat(scenario.Properties).Where(p => !string.IsNullOrWhiteSpace(p)))
                arguments.Add(property);
            arguments.Add("/qn");
            arguments.Add("/norestart");
            arguments.Add("/L*v");
            arguments.Add(logPath);
        }

        var resolvedToolPath = string.IsNullOrWhiteSpace(toolPath) ? options.MsiexecToolPath : toolPath;
        if (IsHyperVChannel(options.Channel))
            return CreateHyperVInvocation(options, resolvedToolPath, arguments);
        if (IsAzureChannel(options.Channel))
            return CreateAzureInvocation(options, resolvedToolPath, arguments);
        return new MsiToolInvocation
        {
            ToolPath = resolvedToolPath,
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(options.LogDirectory)
        };
    }

    private static MsiToolInvocation CreateHyperVInvocation(MsiLifecycleMatrixRunnerOptions options, string toolPath, IReadOnlyList<string> toolArguments)
    {
        var script = """
            param([string]$VmName, [string]$MsiexecPath, [string]$ArgumentsJson)
            $ErrorActionPreference = 'Stop'
            $msiArguments = @(ConvertFrom-Json -InputObject $ArgumentsJson | ForEach-Object { [string]$_ })
            Invoke-Command -VMName $VmName -ScriptBlock {
                param([string]$GuestMsiexecPath, [string[]]$GuestMsiArguments)
                $process = Start-Process -FilePath $GuestMsiexecPath -ArgumentList $GuestMsiArguments -Wait -PassThru
                exit $process.ExitCode
            } -ArgumentList $MsiexecPath, $msiArguments
            """;

        return new MsiToolInvocation
        {
            ToolPath = "powershell",
            Arguments = new[]
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-Command", script,
                options.EnvironmentId,
                toolPath,
                JsonSerializer.Serialize(toolArguments)
            },
            WorkingDirectory = Path.GetFullPath(options.LogDirectory)
        };
    }

    private static MsiToolInvocation CreateAzureInvocation(MsiLifecycleMatrixRunnerOptions options, string toolPath, IReadOnlyList<string> toolArguments)
    {
        var target = ParseAzureTarget(options.EnvironmentId);
        var script = "$ErrorActionPreference = 'Stop'; "
                     + "$msiArguments = @(ConvertFrom-Json -InputObject '" + PowerShellSingleQuote(JsonSerializer.Serialize(toolArguments)) + "' | ForEach-Object { [string]$_ }); "
                     + "$process = Start-Process -FilePath '" + PowerShellSingleQuote(toolPath) + "' -ArgumentList $msiArguments -Wait -PassThru; "
                     + "exit $process.ExitCode";

        var arguments = new List<string>
        {
            "vm",
            "run-command",
            "invoke",
            "--name",
            target.VmName,
            "--command-id",
            "RunPowerShellScript",
            "--scripts",
            script
        };
        if (!string.IsNullOrWhiteSpace(target.ResourceGroup))
        {
            arguments.Add("--resource-group");
            arguments.Add(target.ResourceGroup);
        }
        if (!string.IsNullOrWhiteSpace(target.Subscription))
        {
            arguments.Add("--subscription");
            arguments.Add(target.Subscription);
        }

        return new MsiToolInvocation
        {
            ToolPath = "az",
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(options.LogDirectory)
        };
    }

    private static MsiToolResult RunTool(MsiToolInvocation invocation)
    {
        var start = new ProcessStartInfo
        {
            FileName = invocation.ToolPath,
            WorkingDirectory = string.IsNullOrWhiteSpace(invocation.WorkingDirectory) ? Environment.CurrentDirectory : invocation.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in invocation.Arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{invocation.ToolPath}'.");
        if (!process.WaitForExit((int)TimeSpan.FromHours(1).TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { Beep.Installer.Engine.Diag.Debug("MsiLifecycleMatrixRunner", "Timed-out tool process kill failed.", ex); }
            throw new InvalidOperationException($"Tool '{invocation.ToolPath}' timed out.");
        }

        return new MsiToolResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = process.StandardOutput.ReadToEnd(),
            StandardError = process.StandardError.ReadToEnd(),
            ToolVersion = ToolFileVersion(invocation.ToolPath)
        };
    }

    private static MsiLifecycleMatrixRunnerReport Complete(
        MsiLifecycleMatrixRunnerOptions options,
        string logDirectory,
        DateTimeOffset started,
        List<MsiLifecycleMatrixRunnerAction> actions,
        List<MsiLifecycleMatrixScenarioEvidence> scenarios,
        bool success,
        int exitCode,
        string message)
        => new()
        {
            EnvironmentId = options.EnvironmentId,
            OperatingSystem = options.OperatingSystem,
            Architecture = options.Architecture,
            Channel = options.Channel,
            PlanHash = options.PlanHash,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            LogDirectory = logDirectory,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            DryRun = options.DryRun,
            ExitCode = exitCode,
            Message = message,
            ScenarioPackPath = ScenarioPackDisplayPath(options.ScenarioPackPath),
            Scenarios = scenarios,
            Actions = actions
        };

    private static string Value(IReadOnlyDictionary<string, string> values, string name, string fallback = "")
        => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string AppendValue(string current, string value)
        => string.IsNullOrWhiteSpace(current) ? value : current + ";" + value;

    private static IEnumerable<string> SplitList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;
        foreach (var item in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return item;
    }

    private static string ScenarioPackDisplayPath(string scenarioPackPath)
    {
        if (string.IsNullOrWhiteSpace(scenarioPackPath))
            return "";
        try
        {
            return ResolveScenarioPackPath(scenarioPackPath);
        }
        catch
        {
            return scenarioPackPath;
        }
    }

    public static string ResolveScenarioPackPath(string scenarioPackPath)
    {
        if (string.IsNullOrWhiteSpace(scenarioPackPath))
            return "";

        var alias = scenarioPackPath.Trim();
        var fileName = alias.Equals(EnterpriseDefaultScenarioPack, StringComparison.OrdinalIgnoreCase)
            ? "enterprise-default.matrix.json"
            : alias.Equals(DriverDefaultScenarioPack, StringComparison.OrdinalIgnoreCase)
                ? "driver-default.matrix.json"
                : alias.Equals(PatchDefaultScenarioPack, StringComparison.OrdinalIgnoreCase)
                    ? "patch-default.matrix.json"
                    : "";

        if (string.IsNullOrWhiteSpace(fileName))
            return Path.GetFullPath(scenarioPackPath);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Packaging", "Msi", "ScenarioPacks", fileName),
            Path.Combine(AppContext.BaseDirectory, "ScenarioPacks", fileName),
            Path.Combine(Environment.CurrentDirectory, "Beep.Installer.Core", "Packaging", "Msi", "ScenarioPacks", fileName),
            Path.Combine(Environment.CurrentDirectory, "Packaging", "Msi", "ScenarioPacks", fileName)
        };

        var match = candidates.FirstOrDefault(File.Exists);
        return match == null ? candidates[0] : Path.GetFullPath(match);
    }

    private static bool IsLocalChannel(string channel)
        => string.IsNullOrWhiteSpace(channel)
           || channel.Equals("local", StringComparison.OrdinalIgnoreCase)
           || channel.Equals("host", StringComparison.OrdinalIgnoreCase)
           || channel.Equals("self", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedChannel(string channel)
        => IsLocalChannel(channel) || IsHyperVChannel(channel) || IsAzureChannel(channel);

    private static bool IsHyperVChannel(string channel)
        => channel.Equals("hyperv", StringComparison.OrdinalIgnoreCase)
           || channel.Equals("hyper-v", StringComparison.OrdinalIgnoreCase);

    private static bool IsAzureChannel(string channel)
        => channel.Equals("azure", StringComparison.OrdinalIgnoreCase)
           || channel.Equals("az", StringComparison.OrdinalIgnoreCase);

    private static AzureTarget ParseAzureTarget(string environmentId)
    {
        var parts = environmentId
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 3 => new AzureTarget(parts[^1], parts[^2], string.Join('/', parts.Take(parts.Length - 2))),
            2 => new AzureTarget(parts[1], parts[0], ""),
            _ => new AzureTarget(environmentId, "", "")
        };
    }

    private static string PowerShellSingleQuote(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FullPathIfFile(string value)
        => File.Exists(value) ? Path.GetFullPath(value) : value;

    private static string ToolFileVersion(string toolPath)
    {
        try
        {
            var fullPath = File.Exists(toolPath) ? Path.GetFullPath(toolPath) : toolPath;
            var info = FileVersionInfo.GetVersionInfo(fullPath);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "" : info.FileVersion!;
        }
        catch
        {
            return "";
        }
    }

    private static string SafePathSegment(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? "target")
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "target" : result;
    }

    private sealed record AzureTarget(string VmName, string ResourceGroup, string Subscription);
}
