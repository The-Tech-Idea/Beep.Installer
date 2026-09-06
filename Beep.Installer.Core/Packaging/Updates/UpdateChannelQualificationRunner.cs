using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine.Updates;

public sealed class UpdateChannelQualificationOptions
{
    public string FeedPath { get; init; } = "";
    public string TrustedPublicKeyPath { get; init; } = "";
    public string TrustedPublicKey { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string CurrentChannelId { get; init; } = "";
    public string TargetChannelId { get; init; } = "";
    public string InstalledVersion { get; init; } = "";
    public string CohortSeed { get; init; } = "";
    public bool VerifyLifecycle { get; init; }
    public bool DryRun { get; init; }
    public string ScriptPath { get; init; } = "";
    public string UpdatedScriptPath { get; init; } = "";
    public string DowngradeScriptPath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string InstallerExecutablePath { get; init; } = "";
    public IReadOnlyList<string> ExtraInstallArguments { get; init; } = Array.Empty<string>();
    public Func<UpdateChannelQualificationCommand, UpdateChannelQualificationCommandResult>? CommandRunner { get; init; }
}

public sealed class UpdateChannelQualificationReport
{
    public string FeedPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public bool DryRun { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<UpdateChannelQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class UpdateChannelQualificationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool ExpectedToFail { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public string CommandLine { get; init; } = "";
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public UpdateChannelTransitionDecision? TransitionDecision { get; init; }
}

public sealed class UpdateChannelQualificationCommand
{
    public string FileName { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = "";
    public string CommandLine => $"{FileName} {string.Join(" ", Arguments.Select(QuoteArgument))}";

    private static string QuoteArgument(string value)
        => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}

public sealed class UpdateChannelQualificationCommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

public sealed class UpdateChannelQualificationRunner
{
    public const string ReportFileName = "update-channel-qualification.json";

    public UpdateChannelQualificationReport Run(UpdateChannelQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var feedPath = Path.GetFullPath(Required(options.FeedPath, nameof(options.FeedPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(feedPath) ?? Environment.CurrentDirectory, "qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<UpdateChannelQualificationScenario>();
        var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = feedPath,
            TrustedPublicKeyPath = options.TrustedPublicKeyPath,
            TrustedPublicKey = options.TrustedPublicKey
        });
        scenarios.Add(Scenario(
            "verify-feed",
            "preflight",
            "Verify signed update channel feed metadata before evaluating transitions.",
            expectedToFail: false,
            success: verification.Success,
            verification.Success ? "Feed verification passed." : "Feed verification failed.",
            verification.Diagnostics));

        scenarios.Add(VerifyTamperedFeedFailure(feedPath, options));
        scenarios.Add(VerifyOfflineReconnectRecovery(feedPath, options));

        if (verification.Manifest is not null)
        {
            scenarios.Add(EvaluateTransition("transition", verification.Manifest, options, revoked: false, rolloutPercentage: null));
            scenarios.Add(EvaluateTransition("rollout-hold", verification.Manifest, options, revoked: false, rolloutPercentage: 0));
            scenarios.Add(EvaluateTransition("revoked-rollback", verification.Manifest, options, revoked: true, rolloutPercentage: null));
        }

        if (options.VerifyLifecycle)
            scenarios.AddRange(RunLifecycleEvidence(options, outputDirectory));

        var success = scenarios.All(s => s.Success);
        var report = new UpdateChannelQualificationReport
        {
            FeedPath = feedPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            DryRun = options.DryRun,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Update channel qualification completed." : "Update channel qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    public static void WriteReport(UpdateChannelQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, UpdateChannelQualificationJsonContext.Default.UpdateChannelQualificationReport));
    }

    private static UpdateChannelQualificationScenario VerifyTamperedFeedFailure(string feedPath, UpdateChannelQualificationOptions options)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "beep_update_channel_tamper_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var cloneFeed = Path.Combine(tempDir, UpdateChannelFeedPackageService.FeedFileName);
            var cloneSig = Path.Combine(tempDir, UpdateChannelFeedPackageService.SignatureFileName);
            File.Copy(feedPath, cloneFeed);
            File.Copy(Path.Combine(Path.GetDirectoryName(feedPath) ?? "", UpdateChannelFeedPackageService.SignatureFileName), cloneSig);
            File.AppendAllText(cloneFeed, $"{Environment.NewLine} ", Encoding.UTF8);

            var verification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
            {
                FeedPath = cloneFeed,
                TrustedPublicKeyPath = options.TrustedPublicKeyPath,
                TrustedPublicKey = options.TrustedPublicKey
            });
            var success = !verification.Success && verification.Diagnostics.Any(d => d.Code == "BI1567");
            return Scenario(
                "tampered-feed-failure",
                "negative-preflight",
                "Prove modified update channel feed metadata is rejected by detached signature verification.",
                expectedToFail: true,
                success,
                success ? "Tampered feed was rejected." : "Tampered feed was not rejected as expected.",
                verification.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { Beep.Installer.Engine.Diag.Debug("UpdateChannelQualificationRunner", "Temporary directory cleanup failed.", ex); }
        }
    }

    private static UpdateChannelQualificationScenario VerifyOfflineReconnectRecovery(string feedPath, UpdateChannelQualificationOptions options)
    {
        var missingFeedPath = Path.Combine(Path.GetDirectoryName(feedPath) ?? Environment.CurrentDirectory, "offline-" + Guid.NewGuid().ToString("N") + ".json");
        var offlineVerification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = missingFeedPath,
            TrustedPublicKeyPath = options.TrustedPublicKeyPath,
            TrustedPublicKey = options.TrustedPublicKey
        });
        var reconnectVerification = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            FeedPath = feedPath,
            TrustedPublicKeyPath = options.TrustedPublicKeyPath,
            TrustedPublicKey = options.TrustedPublicKey
        });

        var success = !offlineVerification.Success
            && offlineVerification.Diagnostics.Any(d => d.Code == "BI1561")
            && reconnectVerification.Success;
        var diagnostics = offlineVerification.Diagnostics
            .Concat(reconnectVerification.Diagnostics)
            .ToList();
        return Scenario(
            "offline-reconnect",
            "recovery",
            "Prove an unavailable update-channel feed fails closed, then the same trusted feed verifies after reconnect.",
            expectedToFail: false,
            success,
            success ? "Offline feed miss failed closed and reconnect verification passed." : "Offline reconnect behavior did not match the expected fail-closed/recover sequence.",
            diagnostics);
    }

    private static UpdateChannelQualificationScenario EvaluateTransition(
        string id,
        UpdateChannelFeedManifest manifest,
        UpdateChannelQualificationOptions options,
        bool revoked,
        int? rolloutPercentage)
    {
        var feed = CloneManifest(manifest);
        var targetChannelId = string.IsNullOrWhiteSpace(options.TargetChannelId)
            ? feed.SelectedChannelId
            : options.TargetChannelId;
        var channel = feed.Channels.FirstOrDefault(c => string.Equals(c.Id, targetChannelId, StringComparison.OrdinalIgnoreCase))
            ?? feed.Channels.First();
        var replacement = new UpdateChannelFeedEntry
        {
            Id = channel.Id,
            Name = channel.Name,
            Ring = channel.Ring,
            FeedUrl = channel.FeedUrl,
            RolloutPercentage = rolloutPercentage ?? channel.RolloutPercentage,
            MinimumVersion = channel.MinimumVersion,
            DeadlineUtc = channel.DeadlineUtc,
            Critical = channel.Critical,
            MaintenanceWindow = channel.MaintenanceWindow,
            RollbackVersion = string.IsNullOrWhiteSpace(channel.RollbackVersion) ? channel.MinimumVersion : channel.RollbackVersion,
            Revoked = revoked
        };
        feed.Channels.Remove(channel);
        feed.Channels.Add(replacement);

        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = feed,
            CurrentChannelId = options.CurrentChannelId,
            TargetChannelId = replacement.Id,
            InstalledVersion = options.InstalledVersion,
            CohortSeed = string.IsNullOrWhiteSpace(options.CohortSeed) ? Environment.MachineName : options.CohortSeed
        });

        var expectedAction = id switch
        {
            "rollout-hold" => UpdateChannelTransitionEvaluator.ActionHold,
            "revoked-rollback" => UpdateChannelTransitionEvaluator.ActionRollback,
            _ => string.Equals(options.CurrentChannelId, replacement.Id, StringComparison.OrdinalIgnoreCase)
                ? UpdateChannelTransitionEvaluator.ActionUpdate
                : UpdateChannelTransitionEvaluator.ActionTransition
        };
        var success = string.Equals(decision.Action, expectedAction, StringComparison.OrdinalIgnoreCase);
        if (id == "transition")
            success = success && decision.Allowed;
        else
            success = success && !decision.Allowed;

        return new UpdateChannelQualificationScenario
        {
            Id = id,
            Category = "transition",
            Description = id switch
            {
                "rollout-hold" => "Prove staged rollout exclusion produces a hold decision.",
                "revoked-rollback" => "Prove revoked channel metadata produces a rollback decision when rollback metadata exists.",
                _ => "Prove target channel transition is allowed when feed, minimum-version and cohort gates pass."
            },
            ExpectedToFail = id != "transition",
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? $"Transition decision was {decision.Action}." : $"Unexpected transition decision: {decision.Action}.",
            TransitionDecision = decision
        };
    }

    private static IReadOnlyList<UpdateChannelQualificationScenario> RunLifecycleEvidence(
        UpdateChannelQualificationOptions options,
        string outputDirectory)
    {
        var scenarios = new List<UpdateChannelQualificationScenario>();
        if (string.IsNullOrWhiteSpace(options.ScriptPath))
        {
            scenarios.Add(new UpdateChannelQualificationScenario
            {
                Id = "lifecycle-inputs",
                Category = "lifecycle",
                Description = "Validate update-channel lifecycle qualification inputs.",
                Success = false,
                ExitCode = 1,
                Message = "Lifecycle qualification requires UPDATECHANNELSCRIPT."
            });
            return scenarios;
        }

        scenarios.Add(RunLifecycleCommand("install-current", "Run silent install for the current channel package.", options, outputDirectory, Required(options.ScriptPath, nameof(options.ScriptPath)), "/S", expectedToFail: false));
        scenarios.Add(RunLifecycleCommand("update-target", "Run silent update/upgrade for the target channel package.", options, outputDirectory, FirstNonEmpty(options.UpdatedScriptPath, options.ScriptPath), "/S", expectedToFail: false, force: true));

        if (!string.IsNullOrWhiteSpace(options.DowngradeScriptPath))
            scenarios.Add(RunLifecycleCommand("downgrade-blocked", "Prove downgrade without FORCE is blocked by the runtime lifecycle gate.", options, outputDirectory, options.DowngradeScriptPath, "/S", expectedToFail: true));

        scenarios.Add(RunLifecycleCommand("uninstall-current", "Run silent uninstall after update-channel lifecycle qualification.", options, outputDirectory, options.ScriptPath, "/UNINSTALL", expectedToFail: false));
        return scenarios;
    }

    private static UpdateChannelQualificationScenario RunLifecycleCommand(
        string id,
        string description,
        UpdateChannelQualificationOptions options,
        string outputDirectory,
        string scriptPath,
        string modeFlag,
        bool expectedToFail,
        bool force = false)
    {
        var executable = string.IsNullOrWhiteSpace(options.InstallerExecutablePath)
            ? Environment.ProcessPath ?? "Beep.Installer.exe"
            : options.InstallerExecutablePath;
        var installDirectory = string.IsNullOrWhiteSpace(options.InstallDirectory)
            ? Path.Combine(outputDirectory, "update-channel-install")
            : Path.GetFullPath(options.InstallDirectory);
        var args = new List<string>
        {
            $"/SCRIPT={Path.GetFullPath(scriptPath)}",
            modeFlag,
            $"/D={installDirectory}",
            "/JSON",
            "/NORESTART",
            "/SUPPRESSMSGBOXES"
        };
        if (force)
            args.Add("/FORCE");
        args.AddRange(options.ExtraInstallArguments);

        var command = new UpdateChannelQualificationCommand
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
        var result = options.DryRun
            ? new UpdateChannelQualificationCommandResult { ExitCode = expectedToFail ? 1 : 0, StandardOutput = "Dry run: command not executed." }
            : RunCommand(command, options.CommandRunner);
        var success = expectedToFail ? result.ExitCode != 0 : result.ExitCode == 0;
        var evidencePath = WriteCommandEvidence(outputDirectory, id, command, result);

        return new UpdateChannelQualificationScenario
        {
            Id = id,
            Category = "lifecycle",
            Description = description,
            ExpectedToFail = expectedToFail,
            Success = success,
            ExitCode = result.ExitCode,
            Message = success
                ? expectedToFail ? "Lifecycle command failed as expected." : "Lifecycle command passed."
                : expectedToFail ? "Lifecycle command unexpectedly passed." : "Lifecycle command failed.",
            EvidencePath = evidencePath,
            CommandLine = command.CommandLine,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError
        };
    }

    private static UpdateChannelQualificationCommandResult RunCommand(
        UpdateChannelQualificationCommand command,
        Func<UpdateChannelQualificationCommand, UpdateChannelQualificationCommandResult>? commandRunner)
    {
        if (commandRunner != null)
            return commandRunner(command);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? Directory.GetCurrentDirectory() : command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in command.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new UpdateChannelQualificationCommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };
    }

    private static string WriteCommandEvidence(
        string outputDirectory,
        string id,
        UpdateChannelQualificationCommand command,
        UpdateChannelQualificationCommandResult result)
    {
        var path = Path.Combine(outputDirectory, SafeFileName(id) + ".command.json");
        var evidence = new UpdateChannelQualificationCommandEvidence
        {
            CommandLine = command.CommandLine,
            FileName = command.FileName,
            Arguments = command.Arguments.ToList(),
            WorkingDirectory = command.WorkingDirectory,
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError
        };
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, UpdateChannelQualificationJsonContext.Default.UpdateChannelQualificationCommandEvidence));
        return path;
    }

    private static UpdateChannelQualificationScenario Scenario(
        string id,
        string category,
        string description,
        bool expectedToFail,
        bool success,
        string message,
        List<ProjectSchemaDiagnostic> diagnostics)
        => new()
        {
            Id = id,
            Category = category,
            Description = description,
            ExpectedToFail = expectedToFail,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = message,
            Diagnostics = diagnostics
        };

    private static UpdateChannelFeedManifest CloneManifest(UpdateChannelFeedManifest manifest)
        => new()
        {
            SchemaVersion = manifest.SchemaVersion,
            AppName = manifest.AppName,
            AppPublisher = manifest.AppPublisher,
            AppVersion = manifest.AppVersion,
            SelectedChannelId = manifest.SelectedChannelId,
            Issuer = manifest.Issuer,
            CreatedUtc = manifest.CreatedUtc,
            SignatureFile = manifest.SignatureFile,
            SignatureAlgorithm = manifest.SignatureAlgorithm,
            PublicKeySha256 = manifest.PublicKeySha256,
            Channels = manifest.Channels.Select(c => new UpdateChannelFeedEntry
            {
                Id = c.Id,
                Name = c.Name,
                Ring = c.Ring,
                FeedUrl = c.FeedUrl,
                RolloutPercentage = c.RolloutPercentage,
                MinimumVersion = c.MinimumVersion,
                DeadlineUtc = c.DeadlineUtc,
                Critical = c.Critical,
                MaintenanceWindow = c.MaintenanceWindow,
                RollbackVersion = c.RollbackVersion,
                Revoked = c.Revoked
            }).ToList()
        };

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}

public sealed class UpdateChannelQualificationCommandEvidence
{
    public string CommandLine { get; init; } = "";
    public string FileName { get; init; } = "";
    public List<string> Arguments { get; init; } = new();
    public string WorkingDirectory { get; init; } = "";
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

[JsonSerializable(typeof(UpdateChannelQualificationReport))]
[JsonSerializable(typeof(UpdateChannelQualificationScenario))]
[JsonSerializable(typeof(UpdateChannelQualificationCommandEvidence))]
[JsonSerializable(typeof(UpdateChannelTransitionDecision))]
[JsonSerializable(typeof(UpdateRolloutDecision))]
[JsonSerializable(typeof(ProjectSchemaDiagnostic))]
internal partial class UpdateChannelQualificationJsonContext : JsonSerializerContext
{
}
