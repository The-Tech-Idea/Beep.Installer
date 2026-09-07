using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Beep.Installer.Cli;
using Beep.Installer.Models;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Deployment;
using Beep.Installer.Policy;
using Beep.Installer.Quality;
using Beep.Installer.Security;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Forms;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer;

/// <summary>
/// Beep Installer — entry point.
///
/// This executable has TWO modes:
///
///  1. GENERATOR mode (default UI)
///        The Package Builder UI lets you author a .bsetup script and build
///        a self-contained Setup.exe for distribution.
///
///  2. RUNTIME mode (when shipped as the generated installer)
///        The Setup.exe carries embedded installer metadata and payload,
///        then shows the install wizard to the end user.
///
/// The mode is determined at startup by inspecting command-line arguments
/// and whether installer metadata exists beside or inside the executable.
/// </summary>
internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var commandLine = EnterpriseCommandLine.ExpandAndValidate(args);
        if (commandLine.HasErrors)
        {
            PrintDiagnostics("command-line", commandLine.Diagnostics);
            return 2;
        }
        args = commandLine.Args;

        // Defer WinForms init until we know we need a UI.
        // CLI-only commands (/BUILD, /S, /UNINSTALL, /SELFTEST, /PREVIEW, /?) run fully headless.
        var headless = IsHeadlessCommand(args);
        if (!headless)
        {
            try
            {
                ApplicationConfiguration.Initialize();
                BeepThemesManager.InitializeThemes();
                BeepThemesManager.SetCurrentTheme("ModernTheme");
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            }
            catch (Exception ex)
            {
                // Some hosts / runtimes fail DPI init; continue with framework defaults.
                Console.Error.WriteLine($"Warning: UI init continued with framework defaults ({ex.GetType().Name}: {ex.Message})");
            }
        }
        else
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        // Composition root. Built here rather than inside Dispatch so that diagnostics are routed
        // before any verb runs and drained after it returns — sinks batch, and a short headless run
        // would otherwise lose the entries explaining why it failed.
        var provider = Composition.InstallerServices.Build(args);
        using var diagnostics = Composition.InstallerServices.RouteDiagnostics(provider);
        Services = provider;

        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex}");
            var crashLog = Path.Combine(Path.GetTempPath(), $"Beep_Crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
            try
            {
                File.WriteAllText(crashLog,
                    $"Beep Installer — Fatal Error{Environment.NewLine}" +
                    $"Time   : {DateTime.UtcNow:u}{Environment.NewLine}" +
                    $"Machine: {Environment.MachineName}{Environment.NewLine}" +
                    $"Error  : {ex}{Environment.NewLine}");
            }
            catch (Exception logEx)
            {
                Engine.Diag.Debug("Program", "fatal-error crash log write failed", logEx);
                Console.Error.WriteLine($"Warning: crash log write failed ({logEx.GetType().Name}: {logEx.Message})");
            }
            if (!headless)
            {
                try
                {
                    MessageBox.Show($"Fatal error: {ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                                    $"A crash log was written to:{Environment.NewLine}{crashLog}",
                                    "Beep Installer — Fatal Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception mbx) { Engine.Diag.Debug("Program", "fatal-error MessageBox failed", mbx); }
            }
            return 99;
        }
        finally
        {
            Composition.InstallerServices.Shutdown(provider);
        }
    }

    /// <summary>
    /// The composition root's provider, for the few call sites that resolve rather than construct.
    /// Set once by <see cref="Main"/>; null under tests that call into the shell directly.
    /// </summary>
    internal static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Whether this invocation needs a console attached. Answered from the same verb table that
    /// dispatches, so the two can no longer disagree: a verb cannot be dispatchable but
    /// console-less, or attach a console and then never run.
    /// </summary>
    private static bool IsHeadlessCommand(string[] args)
    {
        if (IsMatrixQualificationCommand(args)) return true;

        var options = new CliOptions(args);
        foreach (var verb in Verbs)
            if (verb.TryMatch(options, out _))
                return verb.Headless;

        return false;
    }


    // ── Dispatch ────────────────────────────────────────────────────────

    private static int Dispatch(string[] args)
    {
        // The matrix runner reads its own switches and predates the verb table.
        if (IsMatrixQualificationCommand(args))
            return RunMsiMatrixQualificationRunner(args);

        var options = new CliOptions(args);
        foreach (var verb in Verbs)
            if (verb.TryMatch(options, out var value))
                return verb.Handler(options, value);

        return RunDefaultMode(args);
    }


    // ── Mode detection ──────────────────────────────────────────────────

    /// <summary>
    /// True if this executable is acting as a shipped installer.
    /// </summary>
    /// <summary>
    /// Records whether authored custom actions may run. Silence stays silence: with no flag and no
    /// policy nothing is written, and the step behaves as it always has.
    /// </summary>
    private static void ApplyScriptCommandConsent(SetupContext context, Policy.InstallerPolicy? policy, string[] args)
    {
        var decision = Engine.ScriptCommandConsent.Decide(
            Has(args, "/ALLOWSCRIPTCMDS"), Has(args, "/NOSCRIPTCMDS"), policy);
        if (decision is null) return;

        context.Properties[InstallContextKeys.AllowScriptCommands] = decision.Value;
        if (!decision.Value)
            Engine.Diag.Warn("Policy", "Authored custom actions are refused for this run.", eventId: "BI2620");
    }

    private static bool IsRuntimeMode()
    {
        var exeDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(exeDir, "script.bsetup"))
            || Engine.EmbeddedInstallerResources.Has("script.bsetup");
    }

    private static InstallProject? LoadRuntimeProject(string[]? args = null)
    {
        var exeDir = AppContext.BaseDirectory;
        var scriptIdx = args == null ? -1 : IndexOf(args, "/SCRIPT=");
        var explicitScript = scriptIdx >= 0 ? args![scriptIdx]["/SCRIPT=".Length..] : null;
        var path = !string.IsNullOrWhiteSpace(explicitScript)
            ? explicitScript
            : File.Exists(Path.Combine(exeDir, "script.bsetup"))
            ? Path.Combine(exeDir, "script.bsetup")
            : Engine.EmbeddedInstallerResources.PathFor("script.bsetup");
        if (path == null) return null;

        var (project, err) = InstallerScriptSerializer.Load(path);
        if (err != null)
        {
            Console.Error.WriteLine($"Script warning: {err}");
            return null;
        }

        project!.SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        return project;
    }

    // ── Silent install ──────────────────────────────────────────────────

    private static int RunSilentInstall(InstallProject project, string[] args)
    {
        var jsonMode = Has(args, "/JSON");
        var originalOut = Console.Out;
        using var jsonSink = jsonMode ? new StringWriter() : null;
        if (jsonMode) Console.SetOut(jsonSink!);
        var runCorrelationId = NewRunCorrelationId("install", project);
        using var diagScope = Engine.Diag.BeginScope("install", runCorrelationId);
        Engine.Diag.Info("Runtime", $"Starting silent install for {project.AppName} {project.AppVersion}.", "BI2401");

        var config = project;
        Console.WriteLine($"Installing {config.AppName} {config.AppVersion}…");

        var runtimeProperties = PropertyValues(args);
        AddPathRuntimeProperty(runtimeProperties, args, "/OFFLINELAYOUT=", "OfflineLayoutDirectory");
        var perUser = Engine.InstallScopeResolver.IsPerUser(config);
        if (TryRuntimeBool(runtimeProperties, "PerUser", out var perUserOverride))
        {
            try { perUser = Engine.InstallScopeResolver.IsPerUser(config, perUserOverride); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }
        var installPath = Engine.InstallScopeResolver.ResolveDefaultPath(config, perUser);
        var dArg = ArgValue(args, "/D=");
        if (dArg != null) installPath = dArg;

        using var operationLock = TryAcquireRuntimeOperation("install", project, args, installPath, originalOut, runCorrelationId);
        if (operationLock == null) return 2;

        if (runtimeProperties.TryGetValue("InstallType", out var installTypeValue)
            && Enum.TryParse<InstallationType>(installTypeValue, ignoreCase: true, out var installType))
        {
            config.DefaultInstallType = installType;
            ComponentSelection.ApplyInstallType(config, installType);
        }

        var componentsArg = ArgValue(args, "/COMPONENTS=");
        if (componentsArg != null)
        {
            var ids = componentsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var c in config.Components)
                c.Selected = ids.Contains(c.Id, StringComparer.OrdinalIgnoreCase) || c.Required;
        }

        Console.WriteLine($"  Path: {installPath}");

        // Transactional rollback: FileCopyStep registers each copied file; on failure we undo.
        var rollback = new RollbackManager();

        // The steps consume BeepDM's InstallConfig, not our authoring model — the builder
        // performs that projection and supplies every key the step graph reads.
        var context = Engine.InstallContextBuilder.ForInstall(
            project,
            installPath,
            perUser,
            rollback,
            customValues: runtimeProperties,
            force: Has(args, "/FORCE"),
            journalPath: ArgValue(args, "/JOURNAL="));
        context.Properties[InstallContextKeys.ResourceExecutionMode] = "install";
        ApplyBuiltInRuntimeProperties(context, runtimeProperties);

        // Same Core-hosted graph the wizard UI runs.
        var wizard = Hosting.InstallWizardGraph.BuildInstall(
            "beep-install-silent", new SetupOptions { Environment = "Production", DryRun = IsDryRun(args) });

        var logger = CreateRunLogger(args);
        logger.Info("Install", $"{config.AppName} {config.AppVersion} → {installPath} (perUser={perUser})");
        var policyEvaluation = EvaluateProjectPolicy(project, args);
        context.Properties[InstallContextKeys.ResourcePolicy] = policyEvaluation.Policy;
        ApplyScriptCommandConsent(context, policyEvaluation.Policy, args);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
        {
            const int blockedExit = 2;
            const string blockedMessage = "Installation blocked by enterprise policy.";
            logger.Warn("Policy", blockedMessage);
            Engine.Diag.Warn("Policy", blockedMessage, eventId: "BI2404");
            var blockedBundlePath = TryWriteSupportBundle("install", project, installPath, false, blockedExit, blockedMessage, logger.LogFilePath, context, policyEvaluation, args, runCorrelationId);
            EmitRuntimeTelemetry("install", project, installPath, false, blockedExit, blockedMessage, logger.LogFilePath, context, policyEvaluation, args, runCorrelationId);
            if (jsonMode)
            {
                Console.SetOut(originalOut);
                WriteRunResultJson("install", project, installPath, false, blockedExit, blockedMessage, logger.LogFilePath, context, blockedBundlePath, runCorrelationId);
            }
            return blockedExit;
        }
        if (!VerifyOfflineLayoutPreflight("install", project, installPath, args, context, policyEvaluation, logger.LogFilePath, jsonMode, originalOut, runCorrelationId))
            return 2;
        var progress = new SyncProgress(a =>
        {
            if (!string.IsNullOrEmpty(a.Messege)) logger.Info("Install", a.Messege);
        });

        var result = wizard.Run(context, progress);
        LogRunReport(logger, wizard);
        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        if (ok) rollback.Commit();
        else
        {
            Engine.Diag.Warn("Runtime", $"Install failed: {result.Message}", eventId: "BI2405");
            Console.WriteLine("Installation failed — rolling back changes…");
            rollback.Rollback();
            RestoreUpgradeBackupIfAny(context);
        }
        var restartOverride = ArgValue(args, "/RESTARTEXITCODE=") is string r
            && int.TryParse(r, out var code) ? code : (int?)null;
        var exit = Hosting.ExitCodes.ForInstallResult(ok, context, Has(args, "/NORESTART"), restartOverride);

        if (ok) RecordLogInArp(project, perUser, logger.LogFilePath);
        logger.Info("Install", ok ? $"Completed (exit {exit})." : $"Failed: {result.Message}");
        Engine.Diag.Info("Runtime", ok ? $"Install completed with exit code {exit}." : $"Install failed with exit code {exit}.", ok ? "BI2402" : "BI2405");

        Console.WriteLine(ok
            ? exit == Hosting.ExitCodes.Success
                ? "Installation completed successfully."
                : "Installation completed — a reboot is required to replace files that were in use."
            : $"Installation failed: {result.Message}");
        Console.WriteLine($"Log: {logger.LogFilePath}");
        var supportBundlePath = TryWriteSupportBundle("install", project, installPath, ok, exit, result.Message, logger.LogFilePath, context, policyEvaluation, args, runCorrelationId);
        EmitRuntimeTelemetry("install", project, installPath, ok, exit, result.Message, logger.LogFilePath, context, policyEvaluation, args, runCorrelationId);
        if (jsonMode)
        {
            Console.SetOut(originalOut);
            WriteRunResultJson("install", project, installPath, ok, exit, result.Message, logger.LogFilePath, context, supportBundlePath, runCorrelationId);
        }
        return exit;
    }

    // ── Repair ──────────────────────────────────────────────────────────

    private static int RunRepair(InstallProject project, string[] args)
    {
        var jsonMode = Has(args, "/JSON");
        var originalOut = Console.Out;
        using var jsonSink = jsonMode ? new StringWriter() : null;
        if (jsonMode) Console.SetOut(jsonSink!);
        var runCorrelationId = NewRunCorrelationId("repair", project);
        using var diagScope = Engine.Diag.BeginScope("repair", runCorrelationId);
        Engine.Diag.Info("Runtime", $"Starting repair for {project.AppName} {project.AppVersion}.", "BI2411");

        string installPath;
        try
        {
            installPath = Engine.InstallScopeResolver.ResolveMaintenancePath(project, ArgValue(args, "/D="));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return WriteMaintenancePreflightFailure("repair", project, args, "", ex.Message, originalOut, runCorrelationId);
        }

        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            var message = $"No installation of {project.AppName} was found to repair.";
            return WriteMaintenancePreflightFailure("repair", project, args, installPath ?? "", message, originalOut, runCorrelationId);
        }

        using var operationLock = TryAcquireRuntimeOperation("repair", project, args, installPath, originalOut, runCorrelationId);
        if (operationLock == null) return 2;

        Console.WriteLine($"Repairing {project.AppName} at {installPath}…");

        var repairRuntimeProperties = PropertyValues(args);
        AddPathRuntimeProperty(repairRuntimeProperties, args, "/OFFLINELAYOUT=", "OfflineLayoutDirectory");
        SetupContext context;
        try
        {
            context = Engine.InstallContextBuilder.ForRepair(
                project,
                installPath,
                runtimeVariables: repairRuntimeProperties,
                journalPath: ArgValue(args, "/JOURNAL="));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return WriteMaintenancePreflightFailure("repair", project, args, installPath, ex.Message, originalOut, runCorrelationId);
        }
        ApplyBuiltInRuntimeProperties(context, repairRuntimeProperties);
        var policyEvaluation = EvaluateProjectPolicy(project, args);
        context.Properties[InstallContextKeys.ResourcePolicy] = policyEvaluation.Policy;
        ApplyScriptCommandConsent(context, policyEvaluation.Policy, args);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
        {
            const int blockedExit = 2;
            const string blockedMessage = "Repair blocked by enterprise policy.";
            Engine.Diag.Warn("Policy", blockedMessage, eventId: "BI2414");
            var blockedBundlePath = TryWriteSupportBundle("repair", project, installPath, false, blockedExit, blockedMessage, "", context, policyEvaluation, args, runCorrelationId);
            EmitRuntimeTelemetry("repair", project, installPath, false, blockedExit, blockedMessage, "", context, policyEvaluation, args, runCorrelationId);
            if (jsonMode)
            {
                Console.SetOut(originalOut);
                WriteRunResultJson("repair", project, installPath, false, blockedExit, blockedMessage, "", context, blockedBundlePath, runCorrelationId);
            }
            return blockedExit;
        }
        if (!VerifyOfflineLayoutPreflight("repair", project, installPath, args, context, policyEvaluation, "", jsonMode, originalOut, runCorrelationId))
            return 2;
        var wizard = Hosting.InstallWizardGraph.BuildRepair(
            "beep-repair", new SetupOptions { Environment = "Production", DryRun = IsDryRun(args) });
        var result = wizard.Run(context);

        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        Console.WriteLine(ok ? $"Repair completed: {result.Message}" : $"Repair failed: {result.Message}");
        var exit = ok ? 0 : 1;
        Engine.Diag.Info("Runtime", ok ? "Repair completed." : $"Repair failed: {result.Message}", ok ? "BI2412" : "BI2415");
        var supportBundlePath = TryWriteSupportBundle("repair", project, installPath, ok, exit, result.Message, "", context, policyEvaluation, args, runCorrelationId);
        EmitRuntimeTelemetry("repair", project, installPath, ok, exit, result.Message, "", context, policyEvaluation, args, runCorrelationId);
        if (jsonMode)
        {
            Console.SetOut(originalOut);
            WriteRunResultJson("repair", project, installPath, ok, exit, result.Message, "", context, supportBundlePath, runCorrelationId);
        }
        return exit;
    }

    /// <summary>
    /// A failed upgrade must put the previous version back. UpgradeStep recorded the backup;
    /// CommitUpgradeStep only deletes it after a verified success, so on any failure the
    /// backup still exists here.
    /// </summary>
    private static void RestoreUpgradeBackupIfAny(SetupContext context)
    {
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        var installPath = context.TryGetProperty<string>("InstallPath");
        if (string.IsNullOrWhiteSpace(backup) || string.IsNullOrWhiteSpace(installPath)) return;

        Console.WriteLine($"Restoring previous version from backup…");
        var restored = new UpgradeEngine().RestoreFromBackup(backup!, installPath!, CancellationToken.None);
        Console.WriteLine(restored
            ? "Previous version restored."
            : $"Could not restore the previous version — backup preserved at: {backup}");
    }

    // ── Uninstall ───────────────────────────────────────────────────────

    private static int RunUninstall(InstallProject project, string[] args)
    {
        var jsonMode = Has(args, "/JSON");
        var originalOut = Console.Out;
        using var jsonSink = jsonMode ? new StringWriter() : null;
        if (jsonMode) Console.SetOut(jsonSink!);
        var runCorrelationId = NewRunCorrelationId("uninstall", project);
        using var diagScope = Engine.Diag.BeginScope("uninstall", runCorrelationId);
        Engine.Diag.Info("Runtime", $"Starting uninstall for {project.AppName} {project.AppVersion}.", "BI2421");

        var config = project;
        var perUser = Engine.InstallScopeResolver.IsPerUser(config);
        var installPath = "";
        var uninstallRuntimeProperties = PropertyValues(args);
        SetupContext context;
        try
        {
            installPath = Engine.InstallScopeResolver.ResolveMaintenancePath(config, ArgValue(args, "/D="));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return WriteMaintenancePreflightFailure("uninstall", project, args, installPath, ex.Message, originalOut, runCorrelationId);
        }
        using var operationLock = TryAcquireRuntimeOperation("uninstall", project, args, installPath, originalOut, runCorrelationId);
        if (operationLock == null) return 2;
        try
        {
            Console.WriteLine($"Uninstalling {config.AppName} from {installPath}…");
            context = Engine.InstallContextBuilder.ForUninstall(
                project,
                installPath,
                perUser,
                uninstallRuntimeProperties,
                journalPath: ArgValue(args, "/JOURNAL="));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return WriteMaintenancePreflightFailure("uninstall", project, args, installPath, ex.Message, originalOut, runCorrelationId);
        }
        ApplyBuiltInRuntimeProperties(context, uninstallRuntimeProperties);

        var wizard = Hosting.InstallWizardGraph.BuildUninstall();
        var policyEvaluation = EvaluateProjectPolicy(project, args);
        context.Properties[InstallContextKeys.ResourcePolicy] = policyEvaluation.Policy;
        ApplyScriptCommandConsent(context, policyEvaluation.Policy, args);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
        {
            const int blockedExit = 2;
            const string blockedMessage = "Uninstall blocked by enterprise policy.";
            Engine.Diag.Warn("Policy", blockedMessage, eventId: "BI2424");
            var blockedBundlePath = TryWriteSupportBundle("uninstall", project, installPath, false, blockedExit, blockedMessage, "", context, policyEvaluation, args, runCorrelationId);
            EmitRuntimeTelemetry("uninstall", project, installPath, false, blockedExit, blockedMessage, "", context, policyEvaluation, args, runCorrelationId);
            if (jsonMode)
            {
                Console.SetOut(originalOut);
                WriteRunResultJson("uninstall", project, installPath, false, blockedExit, blockedMessage, "", context, blockedBundlePath, runCorrelationId);
            }
            return blockedExit;
        }

        var result = wizard.Run(context);
        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        Console.WriteLine(ok ? "Uninstall completed." : $"Uninstall failed: {result.Message}");
        var exit = ok ? 0 : 1;
        Engine.Diag.Info("Runtime", ok ? "Uninstall completed." : $"Uninstall failed: {result.Message}", ok ? "BI2422" : "BI2425");
        var supportBundlePath = TryWriteSupportBundle("uninstall", project, installPath, ok, exit, result.Message, "", context, policyEvaluation, args, runCorrelationId);
        EmitRuntimeTelemetry("uninstall", project, installPath, ok, exit, result.Message, "", context, policyEvaluation, args, runCorrelationId);
        if (jsonMode)
        {
            Console.SetOut(originalOut);
            WriteRunResultJson("uninstall", project, installPath, ok, exit, result.Message, "", context, supportBundlePath, runCorrelationId);
        }
        return exit;
    }

    private static Engine.InstallationOperationLock? TryAcquireRuntimeOperation(string operation,
        InstallProject project, string[] args, string installPath, TextWriter originalOut, string correlationId)
    {
        try { return Engine.InstallationOperationLock.Acquire(installPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            WriteMaintenancePreflightFailure(operation, project, args, installPath, ex.Message, originalOut, correlationId);
            return null;
        }
    }

    private static int WriteMaintenancePreflightFailure(string operation, InstallProject project,
        string[] args, string installPath, string message, TextWriter originalOut, string correlationId)
    {
        Console.Error.WriteLine(message);
        Engine.Diag.Warn("Runtime", message, eventId: operation switch { "install" => "BI2403", "repair" => "BI2413", _ => "BI2423" });
        var context = new SetupContext();
        var policy = EvaluateProjectPolicy(project, args);
        if (policy.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policy.Diagnostics);
        var bundle = TryWriteSupportBundle(operation, project, installPath, false, 2, message, "", context, policy, args, correlationId);
        EmitRuntimeTelemetry(operation, project, installPath, false, 2, message, "", context, policy, args, correlationId);
        if (Has(args, "/JSON"))
        {
            Console.SetOut(originalOut);
            WriteRunResultJson(operation, project, installPath, false, 2, message, "", context, bundle, correlationId);
        }
        return 2;
    }

    // ── Self-test ───────────────────────────────────────────────────────

    private static int RunSelfTest()
    {
        Console.WriteLine("=== Beep Installer Self-Test ===");
        var testDir = Path.Combine(Path.GetTempPath(), $"BeepSelfTest_{Guid.NewGuid():N}");
        Console.WriteLine($"Test directory: {testDir}");

        try
        {
            var (project, sourceDir) = MakeSelfTestConfig(testDir);
            try
            {
                var context = Engine.InstallContextBuilder.ForInstall(project, testDir, perUser: true);

                var wizard = Hosting.InstallWizardGraph.BuildSelfTestInstall();

                var installResult = wizard.Run(context);
                var installOk = installResult.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
                Console.WriteLine($"Install: {(installOk ? "PASS" : "FAIL")}");

                var manifestOk = File.Exists(Path.Combine(testDir, "install-manifest.json"));
                Console.WriteLine($"Manifest: {(manifestOk ? "PASS" : "FAIL")}");

                var uninstallContext = Engine.InstallContextBuilder.ForUninstall(project, testDir, perUser: true);
                var uninstallWizard = Hosting.InstallWizardGraph.BuildSelfTestUninstall();
                var uninstallOk = uninstallWizard.Run(uninstallContext).Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
                Console.WriteLine($"Uninstall: {(uninstallOk ? "PASS" : "FAIL")}");

                var dirGone = !Directory.Exists(testDir);
                Console.WriteLine($"Cleanup: {(dirGone ? "PASS" : "FAIL")}");

                var allOk = installOk && manifestOk && uninstallOk && dirGone;
                Console.WriteLine($"=== Self-Test {(allOk ? "PASSED" : "FAILED")} ===");
                return allOk ? 0 : 1;
            }
            finally
            {
                try { if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true); }
                catch (Exception dx) { Engine.Diag.Debug("SelfTest", "source cleanup failed", dx); }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true); }
            catch (Exception dx) { Engine.Diag.Debug("SelfTest", "test-dir cleanup failed", dx); }
            return 1;
        }
    }

    private static (InstallProject project, string sourceDir) MakeSelfTestConfig(string testDir)
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"BeepSelfTestSrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "hello.txt");
        File.WriteAllText(sourceFile, $"Beep Installer self-test payload @ {DateTime.UtcNow:O}");

        var project = InstallerProjectFactory.CreateNew("BeepSelfTest", "0.0.0", "Beep Installer", sourceDir);
        project.DefaultDirName = testDir;
        project.PrivilegesRequired = PrivilegeLevel.User;
        project.OutputBaseFilename = "BeepSelfTest";
        project.CreateUninstallEntry = true;

        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            SizeBytes = new FileInfo(sourceFile).Length,
            Files = new List<FileCopyOperation>
            {
                new() { SourcePath = sourceFile, DestinationPath = "hello.txt", Description = "hello.txt" }
            }
        });

        return (project, sourceDir);
    }

    // ── Headless build ──────────────────────────────────────────────────

    private static int RunHeadlessBuild(string projectPath, string[] args)
    {
        Console.WriteLine($"Beep Installer — headless build");
        Console.WriteLine($"Script: {projectPath}");

        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"Error: {err}"); return 2; }

        // Paths in a .bsetup are relative to the script, not to wherever the build was
        // launched from. Resolved in memory only, so the script stays portable.
        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        ApplySigningOverrides(project, args);

        // CI gate: refuse to produce an unsigned installer when the pipeline demands signing.
        if (Has(args, "/REQUIRESIGNED") && !project.HasCodeSigningCertificate)
        {
            Console.Error.WriteLine("/REQUIRESIGNED: no code-signing certificate is configured. Configure PFX signing or a Windows certificate-store selector.");
            return 1;
        }

        // Allow overriding output via /OUT=
        var outIdx = IndexOf(args, "/OUT=");
        if (outIdx >= 0) project.OutputDir = args[outIdx][5..];

        // Allow overriding output format via /FORMAT=msix|msixbundle|exe (Track C).
        var formatValue = ArgValue(args, "/FORMAT=");
        if (formatValue != null && Enum.TryParse<InstallerOutputFormat>(formatValue, ignoreCase: true, out var fmt))
            project.OutputFormat = fmt;

        var updateUrl = ArgValue(args, "/UPDATEURL=");
        if (!string.IsNullOrWhiteSpace(updateUrl))
            project.AppUpdatesURL = updateUrl;

        if (IntArg(args, "/APPINSTALLERHOURS=") is int appInstallerHours)
            project.AppInstallerHoursBetweenUpdateChecks = appInstallerHours;

        if (Has(args, "/APPINSTALLERNOPROMPT"))
            project.AppInstallerShowPrompt = false;

        if (Has(args, "/APPINSTALLERFORCEUPDATEFROMANYVERSION"))
            project.AppInstallerForceUpdateFromAnyVersion = true;

        if (!EnforceProjectPolicy(project, args, "policy", out var policyEvaluation))
            return 1;

        var extensionDiagnostics = InstallerExtensionProjectValidationService.Validate(project, new InstallerExtensionProjectValidationOptions
        {
            ExtensionDirectories = ExtensionDirectoriesFromArgs(args),
            EngineVersion = AppInfo.Version,
            RequireSignedExtensions = Has(args, "/REQUIRESIGNED"),
            Policy = policyEvaluation.Policy,
            ProjectValidationOptions = new ProjectSchemaValidationOptions
            {
                Strict = Has(args, "/STRICT"),
                ForbidLiteralSecrets = Has(args, "/STRICT")
            }
        });
        if (extensionDiagnostics.Count > 0)
            PrintDiagnostics("extension", extensionDiagnostics);
        if (extensionDiagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return 1;

        if (!RunSupplyChainGate(project, args, policyEvaluation.Policy, "supply-chain", out var supplyChainReport))
            return 1;

        var progress = new Progress<BuildPipeline.BuildProgress>(p =>
            Console.WriteLine($"  [{p.Percent,3}%] {p.Message}"));

        var pipeline = new BuildPipeline
        {
            Progress = progress,
            ExtensionDirectories = ExtensionDirectoriesFromArgs(args),
            ExtensionPolicy = policyEvaluation.Policy,
            ExpectedSigningSubject = ArgValue(args, "/SIGNINGSUBJECT="),
            TimestampOutagePolicy = ResolveTimestampOutagePolicy(args, policyEvaluation.Policy),
            TimestampRetryCount = ResolveTimestampRetryCount(args)
        };
        var result = pipeline.Run(project);

        Console.WriteLine();
        Console.WriteLine(result.Summary);
        foreach (var w in result.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in result.Errors) Console.Error.WriteLine($"  ERR : {e}");

        if (result.Success && ShouldGenerateReleaseEvidence(args, policyEvaluation.Policy))
            WriteReleaseEvidence(project, result.OutputFile, ArgValue(args, "/OUT=") ?? Path.GetDirectoryName(result.OutputFile), args, policyEvaluation, supplyChainReport);

        return result.Success ? 0 : 1;
    }

    // ── Update-feed publish (Phase 11) ───────────────────────────────────

    /// <summary>
    /// Builds the installer and stages it into a static update feed: a versioned folder holding
    /// the full Setup.exe (+ the loose delta blob store when the payload is solid) and an
    /// atomically-updated <c>feed.json</c>. Usage:
    /// <c>/BUILD=&lt;project.bsetup&gt; /PUBLISHFEED=&lt;feedDir&gt; [/FEEDURL=&lt;baseUrl&gt;] [/CHANNEL=x] [/MINVERSION=x.y.z] [/REPUBLISH]</c>.
    /// </summary>
    private static int RunPublishFeed(string feedDir, string[] args)
    {
        var buildIdx = IndexOf(args, "/BUILD=");
        if (buildIdx < 0)
        {
            Console.Error.WriteLine("/PUBLISHFEED requires /BUILD=<project.bsetup> naming the project to build and publish.");
            return 2;
        }
        var projectPath = args[buildIdx][7..];

        Console.WriteLine("Beep Installer — publish update feed");
        Console.WriteLine($"Script: {projectPath}");
        Console.WriteLine($"Feed  : {feedDir}");

        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"Error: {err}"); return 2; }
        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);

        var outIdx = IndexOf(args, "/OUT=");
        if (outIdx >= 0) project.OutputDir = args[outIdx][5..];

        // Build first — keep intermediates so the solid payload.zip survives for the delta store.
        var buildProgress = new Progress<BuildPipeline.BuildProgress>(p =>
            Console.WriteLine($"  [{p.Percent,3}%] {p.Message}"));
        var pipeline = new BuildPipeline { Progress = buildProgress, KeepIntermediates = true };
        var build = pipeline.Run(project);
        Console.WriteLine();
        Console.WriteLine(build.Summary);
        foreach (var w in build.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in build.Errors) Console.Error.WriteLine($"  ERR : {e}");
        if (!build.Success) return 1;

        var baseUrl = Arg(args, "/FEEDURL=") ?? (string.IsNullOrWhiteSpace(project.AppUpdatesURL) ? null : project.AppUpdatesURL);
        var request = new Engine.FeedPublisher.Request
        {
            FeedDir = feedDir,
            Product = project.AppName,
            Version = project.AppVersion,
            Channel = Arg(args, "/CHANNEL=") ?? "stable",
            FullExePath = build.OutputFile,
            PayloadZipPath = build.PayloadPath,
            BaseUrl = baseUrl,
            MinSupportedVersion = Arg(args, "/MINVERSION="),
            Mode = project.AppUpdateMode,
            Republish = Has(args, "/REPUBLISH")
        };

        var publish = new Engine.FeedPublisher().Publish(request);
        Console.WriteLine();
        Console.WriteLine(publish.Summary);
        foreach (var w in publish.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in publish.Errors) Console.Error.WriteLine($"  ERR : {e}");
        return publish.Success ? 0 : 1;
    }

    /// <summary>Value of a <c>/KEY=value</c> flag, or null when absent.</summary>
    private static string? Arg(string[] args, string prefix)
    {
        var idx = IndexOf(args, prefix);
        return idx >= 0 ? args[idx][prefix.Length..] : null;
    }

    // ── App self-update (Phase 11) ───────────────────────────────────────

    private static TheTechIdea.Beep.Updates.UpdateSettings BuildUpdateSettings(string[] args)
    {
        var settings = TheTechIdea.Beep.Updates.UpdateServiceExtensions.LoadSettings();
        if (Arg(args, "/FEED=") is { } feed) settings.FeedUrl = feed;
        if (Arg(args, "/D=") is { } root) settings.InstallRoot = root;
        return settings;
    }

    /// <summary><c>/CHECKUPDATE [/FEED=url]</c> — report whether an app update (or stale module) is available.</summary>
    private static int RunCheckUpdate(string[] args)
    {
        var svc = new TheTechIdea.Beep.Updates.AppUpdateService(BuildUpdateSettings(args));
        var check = svc.CheckAsync().GetAwaiter().GetResult();
        if (!check.Succeeded) { Console.Error.WriteLine($"Update check failed: {check.Error}"); return 1; }

        Console.WriteLine($"Installed : {check.CurrentVersion}");
        Console.WriteLine($"Latest    : {check.LatestVersion ?? "(none)"}");
        Console.WriteLine(check.AppUpdateAvailable
            ? $"App update available: {check.LatestVersion}{(check.RequiresFullInstall ? " (full install required)" : " (delta)")}"
            : "The application is up to date.");
        if (check.StaleModules.Count > 0)
            Console.WriteLine($"Modules to update: {string.Join(", ", check.StaleModules.Select(m => $"{m.Id} {m.Version}"))}");
        return 0;
    }

    /// <summary><c>/UPDATE [/FEED=url] [/D=installRoot]</c> — apply an available app update side-by-side.</summary>
    private static int RunUpdate(string[] args)
    {
        var svc = new TheTechIdea.Beep.Updates.AppUpdateService(BuildUpdateSettings(args));
        var check = svc.CheckAsync().GetAwaiter().GetResult();
        if (!check.Succeeded) { Console.Error.WriteLine($"Update check failed: {check.Error}"); return 1; }
        if (!check.AnyUpdateAvailable) { Console.WriteLine("Already up to date."); return 0; }

        var progress = new SyncProgress(a => { if (!string.IsNullOrEmpty(a.Messege)) Console.WriteLine($"  {a.Messege}"); });
        int rc = 0;

        if (check.AppUpdateAvailable)
        {
            var result = svc.ApplyAppUpdateAsync(check, progress).GetAwaiter().GetResult();
            Console.WriteLine(result.Message);
            if (result.Flag != TheTechIdea.Beep.ConfigUtil.Errors.Ok) rc = 1;
        }

        return rc;
    }

    // ── CLI validate ─────────────────────────────────────────────────────

    private static int RunValidate(string projectPath, string[] args)
    {
        var strict = Has(args, "/STRICT");
        var result = HeadlessInstallerSdk.Validate(new HeadlessInstallerRequest
        {
            Strict = strict,
            ForbidLiteralSecrets = strict,
            ProjectPath = projectPath,
            PolicyEvaluator = project => EvaluateProjectPolicy(project, args),
            ExtensionDirectories = ExtensionDirectoriesFromArgs(args),
            RequireSignedExtensions = Has(args, "/REQUIRESIGNED"),
            ExtensionEngineVersion = AppInfo.Version
        });

        var validationJsonPath = ArgValue(args, "/OUT=");
        var validationJsonRequested = Has(args, "/JSON");
        if (validationJsonRequested || !string.IsNullOrWhiteSpace(validationJsonPath))
        {
            var report = HeadlessValidationReport.FromResult(result);
            if (!string.IsNullOrWhiteSpace(validationJsonPath))
                HeadlessValidationReport.WriteJson(report, validationJsonPath);

            if (validationJsonRequested)
            {
                Console.Write(HeadlessValidationReport.ToJson(report));
                return result.ExitCode;
            }
        }

        if (result.Project is not null)
        {
            Console.WriteLine($"Script  : {result.ProjectName} ({result.ProjectPath})");
            Console.WriteLine($"Product : {result.ProductName} {result.ProductVersion}");
            Console.WriteLine($"Source  : {result.SourceDirectory}");
            Console.WriteLine($"Components: {result.Project.Components.Count}");
        }

        if (result.LintDiagnostics.Count > 0)
            PrintDiagnostics("script", result.LintDiagnostics);

        if (result.SchemaDiagnostics.Count > 0)
            PrintDiagnostics("schema", result.SchemaDiagnostics);

        if (result.PolicyDiagnostics.Count > 0)
            PrintDiagnostics("policy", result.PolicyDiagnostics);

        if (result.ExtensionDiagnostics.Count > 0)
            PrintDiagnostics("extension", result.ExtensionDiagnostics);

        if (result.Project is null && result.Diagnostics.Count > 0)
            PrintDiagnostics("load", result.Diagnostics);

        if (result.BuildErrors.Count > 0)
        {
            Console.Error.WriteLine($"\n{result.BuildErrors.Count} error(s):");
            foreach (var e in result.BuildErrors) Console.Error.WriteLine($"  ERR : {e}");
        }

        if (result.BuildWarnings.Count > 0)
        {
            Console.WriteLine($"\n{result.BuildWarnings.Count} warning(s):");
            foreach (var w in result.BuildWarnings) Console.WriteLine($"  WARN: {w}");
        }

        if (result.Success
            && result.BuildWarnings.Count == 0
            && result.SchemaDiagnostics.Count == 0
            && result.LintDiagnostics.Count == 0
            && result.PolicyDiagnostics.Count == 0
            && result.ExtensionDiagnostics.Count == 0)
            Console.WriteLine("\nAll checks passed.");

        if (!string.IsNullOrWhiteSpace(validationJsonPath))
            Console.WriteLine($"\nValidation JSON written: {Path.GetFullPath(validationJsonPath)}");

        Console.WriteLine($"\nExit code: {result.ExitCode}");
        return result.ExitCode;
    }

    // ── CLI project canonicalization ────────────────────────────────────

    private static int RunCanonicalize(string projectPath, string[] args)
    {
        var lintBefore = ProjectScriptLinter.LintFile(projectPath);
        if (lintBefore.HasErrors)
        {
            PrintDiagnostics("script", lintBefore.Diagnostics);
            return 1;
        }

        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        var canonicalization = ProjectSchemaService.NormalizeInMemory(project);
        var schema = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });
        if (Has(args, "/JSON"))
        {
            if (schema.HasErrors)
            {
                foreach (var diagnostic in schema.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
                return 1;
            }

            if (!Has(args, "/WRITE"))
            {
                Console.Write(ProjectCanonicalJsonExporter.ToJson(project));
                return 0;
            }

            var jsonPath = ArgValue(args, "/OUT=") ?? Path.ChangeExtension(projectPath, ".bsetup.json");
            ProjectCanonicalJsonExporter.WriteJson(project, jsonPath);
            Console.WriteLine($"Canonical JSON written: {Path.GetFullPath(jsonPath)}");
            return 0;
        }

        Console.WriteLine($"Script  : {project.ProjectName} ({projectPath})");
        Console.WriteLine($"Schema  : {project.SchemaVersion}");
        if (lintBefore.Diagnostics.Count > 0)
            PrintDiagnostics("script", lintBefore.Diagnostics);
        if (canonicalization.Diagnostics.Count > 0)
            PrintDiagnostics("canonicalize", canonicalization.Diagnostics);
        if (schema.Diagnostics.Count > 0)
            PrintDiagnostics("schema", schema.Diagnostics);

        if (schema.HasErrors)
        {
            Console.WriteLine("\nCanonicalization stopped because the project still has schema errors.");
            return 1;
        }

        if (!Has(args, "/WRITE"))
        {
            Console.WriteLine("\nDry run only. Add /WRITE to rewrite the script in canonical .bsetup format.");
            return 0;
        }

        var backupPath = projectPath + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
        File.Copy(projectPath, backupPath, overwrite: false);
        var (ok, saveErr) = InstallerScriptSerializer.Save(project, projectPath);
        if (!ok)
        {
            Console.Error.WriteLine($"ERROR: {saveErr}");
            Console.Error.WriteLine($"Backup preserved: {backupPath}");
            return 1;
        }

        Console.WriteLine("\nCanonical script written.");
        Console.WriteLine($"Backup: {backupPath}");
        return 0;
    }

    // ── CLI compiled plan ───────────────────────────────────────────────

    private static int RunPlan(string projectPath, string[] args)
    {
        var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
        {
            ProjectPath = projectPath,
            Strict = Has(args, "/STRICT"),
            ForbidLiteralSecrets = Has(args, "/STRICT"),
            PolicyEvaluator = project => EvaluateProjectPolicy(project, args),
            ExtensionDirectories = ExtensionDirectoriesFromArgs(args),
            RequireSignedExtensions = Has(args, "/REQUIRESIGNED"),
            ExtensionEngineVersion = AppInfo.Version
        });

        if (!result.Success)
        {
            foreach (var d in result.Diagnostics)
                Console.Error.WriteLine($"{d.Severity.ToString().ToUpperInvariant()} {d.Code} {d.Path}: {d.Message}");
            return result.ExitCode;
        }

        var plan = result.Plan!;
        if (Has(args, "/JSON"))
        {
            var outputPath = ArgValue(args, "/OUT=");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var fullPath = Path.GetFullPath(outputPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(fullPath, result.PlanJson);
                Console.WriteLine($"Plan JSON written: {fullPath}");
            }
            else
            {
                Console.WriteLine(result.PlanJson);
            }
            return 0;
        }

        Console.WriteLine($"Script     : {result.ProjectName} ({result.ProjectPath})");
        Console.WriteLine($"Product    : {plan.ProductName} {plan.ProductVersion}");
        Console.WriteLine($"Plan hash  : {plan.PlanHash}");
        Console.WriteLine($"Operations : {plan.Operations.Count}");
        foreach (var group in plan.Operations.GroupBy(o => o.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        return 0;
    }

    // ── CLI enterprise property catalog ────────────────────────────────

    private static int RunEnterpriseProperties(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        var catalog = EnterprisePropertyCatalog.ForProject(project);
        if (Has(args, "/JSON"))
        {
            Console.WriteLine(EnterprisePropertyCatalog.ToJson(catalog));
            return 0;
        }

        Console.WriteLine($"Script    : {project.ProjectName} ({projectPath})");
        Console.WriteLine($"Product   : {project.AppName} {project.AppVersion}");
        Console.WriteLine($"Properties: {catalog.Properties.Count}");
        foreach (var group in catalog.Properties.GroupBy(p => p.Page).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine(group.Key);
            foreach (var property in group.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var required = property.Required ? "required" : "optional";
                Console.WriteLine($"  {property.Name} ({property.Type}, {required})");
                Console.WriteLine($"    {property.CommandLineSyntax}");
                Console.WriteLine($"    {property.ValidationRule}");
            }
        }
        return 0;
    }

    private static int RunFormatReadiness(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        var report = PackageFormatCapabilityReporter.Create(project);
        var outputPath = ArgValue(args, "/OUT=");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            File.WriteAllText(outputPath, PackageFormatCapabilityReporter.ToJson(report));
        }

        if (Has(args, "/JSON"))
        {
            Console.WriteLine(PackageFormatCapabilityReporter.ToJson(report));
            return report.BlockedFormatCount > 0 ? 1 : 0;
        }

        Console.WriteLine($"Format readiness: {project.ProjectName} ({projectPath})");
        Console.WriteLine($"Plan hash        : {report.PlanHash}");
        Console.WriteLine($"Release readiness: {report.ReleaseReadinessStatus}");
        Console.WriteLine($"Ready: {report.ReadyFormatCount}, Warnings: {report.WarningFormatCount}, Blocked: {report.BlockedFormatCount}");
        Console.WriteLine(report.ReleaseReadinessSummary);
        foreach (var format in report.Formats)
        {
            Console.WriteLine();
            Console.WriteLine($"{format.Format}: {format.Status}");
            Console.WriteLine($"  {format.Summary}");
            foreach (var finding in format.Findings.Take(8))
            {
                var code = string.IsNullOrWhiteSpace(finding.Code) ? "finding" : finding.Code;
                Console.WriteLine($"  [{finding.Severity}] {code}: {finding.Message}");
            }
            if (format.Findings.Count > 8)
                Console.WriteLine($"  ... {format.Findings.Count - 8} more finding(s)");
        }
        return report.BlockedFormatCount > 0 ? 1 : 0;
    }

    private static int RunExportTemplatePackage(string templateId, string[] args)
    {
        var outputDirectory = ArgValue(args, "/OUT=") ?? Path.Combine(Environment.CurrentDirectory, "template-packages", SafePathSegment(templateId));
        var result = ProjectTemplatePackageService.Export(new ProjectTemplatePackageOptions
        {
            TemplateId = templateId,
            ProductName = ArgValue(args, "/TEMPLATEPRODUCT=") ?? "TemplateApp",
            ProductVersion = ArgValue(args, "/TEMPLATEVERSION=") ?? "1.0.0",
            Publisher = ArgValue(args, "/TEMPLATEPUBLISHER=") ?? "The Tech Idea",
            SourceDirectory = ArgValue(args, "/TEMPLATESOURCEDIR=") ?? "",
            OutputDirectory = outputDirectory,
            Issuer = ArgValue(args, "/TEMPLATEISSUER=") ?? "",
            SigningPrivateKeyPath = ArgValue(args, "/TEMPLATESIGNKEY=") ?? ""
        });

        if (Has(args, "/JSON"))
        {
            Console.WriteLine(ProjectTemplatePackageService.ToJson(result));
            return result.Success ? 0 : 1;
        }

        if (result.Diagnostics.Count > 0)
            PrintDiagnostics("template package export", result.Diagnostics);
        if (!result.Success)
            return 1;

        Console.WriteLine($"Template package: {result.PackageDirectory}");
        Console.WriteLine($"Template id     : {result.TemplateId}");
        Console.WriteLine($"Manifest        : {result.ManifestPath}");
        Console.WriteLine($"Project         : {result.ProjectPath}");
        Console.WriteLine($"Signature       : {result.SignaturePath}");
        Console.WriteLine($"Project SHA-256 : {result.TemplateHash}");
        Console.WriteLine($"Public key SHA-256: {result.PublicKeySha256}");
        return 0;
    }

    private static int RunListTemplates(string[] args)
    {
        var templates = ProjectTemplates.Builtins
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new ProjectTemplateCatalogItem(t.Id, t.Name, t.Category, t.Description))
            .ToArray();
        var catalog = new ProjectTemplateCatalog(templates);
        var outputPath = ArgValue(args, "/OUT=");

        if (Has(args, "/JSON"))
        {
            var json = JsonSerializer.Serialize(catalog, ProjectTemplateCatalogJsonContext.Default.ProjectTemplateCatalog);
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Environment.CurrentDirectory);
                File.WriteAllText(outputPath, json);
            }
            Console.WriteLine(json);
            return 0;
        }

        Console.WriteLine("Built-in project templates:");
        foreach (var group in templates.GroupBy(t => t.Category))
        {
            Console.WriteLine();
            Console.WriteLine(group.Key);
            foreach (var template in group)
                Console.WriteLine($"  {template.Id,-10} {template.Name} — {template.Description}");
        }
        Console.WriteLine();
        Console.WriteLine("Use /EXPORTTEMPLATEPACKAGE=<template-id> with /TEMPLATESIGNKEY=<pem> to publish a signed reusable template package.");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var json = JsonSerializer.Serialize(catalog, ProjectTemplateCatalogJsonContext.Default.ProjectTemplateCatalog);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Environment.CurrentDirectory);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Catalog JSON: {Path.GetFullPath(outputPath)}");
        }
        return 0;
    }

    private static int RunVerifyTemplatePackage(string packageDirectory, string[] args)
    {
        var result = ProjectTemplatePackageService.Verify(new ProjectTemplatePackageVerificationOptions
        {
            PackageDirectory = packageDirectory,
            TrustedPublicKeyPath = ArgValue(args, "/TEMPLATETRUSTKEY=") ?? ""
        });

        if (Has(args, "/JSON"))
        {
            Console.WriteLine(ProjectTemplatePackageService.ToJson(result));
            return result.Success ? 0 : 1;
        }

        if (result.Diagnostics.Count > 0)
            PrintDiagnostics("template package verification", result.Diagnostics);
        if (!result.Success)
            return 1;

        Console.WriteLine($"Template package verified: {Path.GetFullPath(packageDirectory)}");
        Console.WriteLine($"Template id              : {result.Manifest?.TemplateId}");
        Console.WriteLine($"Project                  : {result.ProjectPath}");
        Console.WriteLine($"Project SHA-256          : {result.ActualProjectSha256}");
        Console.WriteLine($"Signature trusted        : {result.SignatureTrusted}");
        Console.WriteLine($"Trusted key SHA-256      : {result.TrustedKeySha256}");
        return 0;
    }

    // ── CLI prerequisite catalog export ────────────────────────────────

    private static int RunPrerequisiteCatalogExport(string catalog, string[] args)
    {
        var outputDirectory = ArgValue(args, "/OUT=")
            ?? Path.Combine(Environment.CurrentDirectory, "prerequisite-catalogs");
        var result = PrerequisiteCatalogService.ExportCatalog(new PrerequisiteCatalogExportOptions
        {
            Catalog = catalog,
            OutputDirectory = outputDirectory,
            SigningPrivateKeyPath = ArgValue(args, "/CATALOGSIGNKEY=") ?? "",
            KeyId = ArgValue(args, "/CATALOGKEYID=") ?? "",
            ApprovedBy = ArgValue(args, "/CATALOGAPPROVEDBY=") ?? "",
            ApprovalReason = ArgValue(args, "/CATALOGAPPROVALREASON=") ?? ""
        });

        foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var stream = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? Console.Error : Console.Out;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (!result.Success)
            return 1;

        Console.WriteLine($"Catalog  : {result.CatalogId} {result.Version}");
        Console.WriteLine($"File     : {result.CatalogPath}");
        Console.WriteLine($"Signature: {result.SignaturePath}");
        Console.WriteLine($"Approval : {result.ApprovalPath}");
        Console.WriteLine($"SHA-256  : {result.Sha256}");
        return 0;
    }

    private static int RunPrerequisiteCatalogQualification(string projectPath, string[] args)
    {
        var policyEvaluation = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyEvaluation);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
            return 2;

        try
        {
            var report = new PrerequisiteCatalogQualificationRunner().Run(new PrerequisiteCatalogQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                LayoutDirectory = ArgValue(args, "/QUALIFYCATALOGLAYOUT=") ?? "",
                LayoutSigningPrivateKeyPath = ArgValue(args, "/LAYOUTSIGNKEY=") ?? "",
                LayoutTrustedPublicKeyPath = ResolveOfflineLayoutTrustedPublicKeyPath(args, policy),
                LayoutTrustedPublicKey = ResolveOfflineLayoutTrustedPublicKey(args, policy),
                RequireSignedLayout = ShouldRequireOfflineLayoutSignature(args, policy),
                DownloadRemotePackages = Has(args, "/DOWNLOAD"),
                Policy = policy
            });

            Console.WriteLine($"Prerequisite catalog qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
            {
                var status = scenario.Success ? "passed" : "failed";
                Console.WriteLine($"  {scenario.Id,-36}: {status}");
            }
            Console.WriteLine($"  packages                            : {report.Packages.Count}");
            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Prerequisite catalog qualification failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI update channel feed export ─────────────────────────────────

    private static int RunUpdateChannelFeedExport(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        var formatValue = ArgValue(args, "/FORMAT=");
        if (formatValue != null && Enum.TryParse<InstallerOutputFormat>(formatValue, ignoreCase: true, out var format))
            project.OutputFormat = format;

        if (!EnforceProjectPolicy(project, args, "policy", out _))
            return 1;

        var outputDirectory = ArgValue(args, "/OUT=")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory, "update-channels");
        var result = UpdateChannelFeedPackageService.Export(new UpdateChannelFeedExportOptions
        {
            Project = project,
            OutputDirectory = outputDirectory,
            SigningPrivateKeyPath = ArgValue(args, "/UPDATECHANNELFEEDSIGNKEY=") ?? "",
            Issuer = ArgValue(args, "/UPDATECHANNELFEEDISSUER=") ?? "",
            DeltaPackageDirectory = ArgValue(args, "/DELTA=") ?? "",
            DeltaPackageBaseUrl = ArgValue(args, "/UPDATECHANNELDELTABASEURL=") ?? "",
            DeltaChannelId = ArgValue(args, "/UPDATECHANNELTARGET=") ?? ""
        });

        foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var stream = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? Console.Error : Console.Out;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (!result.Success)
            return 1;

        Console.WriteLine($"Update channel feed: {result.FeedPath}");
        Console.WriteLine($"Signature          : {result.SignaturePath}");
        Console.WriteLine($"Public key SHA-256 : {result.PublicKeySha256}");
        if (IndexOf(args, "/DELTA=") >= 0)
            Console.WriteLine($"Delta metadata     : attached");
        return 0;
    }

    internal static UpdateCenterForm CreateUpdateCenter(InstallProject project, string[] args)
        => UpdateCenterForm.Create(project, () => EvaluateProjectPolicy(project, args), CreateUpdateFeedOptions("", args));

    private static Engine.PackageDownloadTransportOptions CreateUpdateTransport(string[] args) => new()
    {
        AuthorizationOrigin = ArgValue(args, "/UPDATEAUTHORIGIN=") ?? "",
        BearerTokenReference = ArgValue(args, "/UPDATEBEARERREF=") ?? "",
        ProxyUri = ArgValue(args, "/UPDATEPROXY=") ?? "",
        ProxyUsername = ArgValue(args, "/UPDATEPROXYUSER=") ?? "",
        ProxyPasswordReference = ArgValue(args, "/UPDATEPROXYPASSWORDREF=") ?? ""
    };

    private static UpdateChannelFeedVerificationOptions CreateUpdateFeedOptions(string feedPath, string[] args)
        => new()
        {
            CacheDirectory = ArgValue(args, "/UPDATECACHE=") ?? "",
            MaximumCacheBytes = ReadPositiveUpdateLimit(args, "/UPDATECACHEMAXBYTES=", 4L * 1024 * 1024 * 1024),
            CacheRetention = TimeSpan.FromDays(ReadPositiveUpdateLimit(args, "/UPDATECACHERETENTIONDAYS=", 30, 36500)),
            ReplayStateDirectory = ArgValue(args, "/UPDATESTATE=") ?? "",
            ExpectedAppName = ArgValue(args, "/UPDATEAPPNAME=") ?? "",
            ExpectedAppId = ArgValue(args, "/UPDATEAPPID=") ?? "",
            ExpectedAppPublisher = ArgValue(args, "/UPDATEPUBLISHER=") ?? "",
            Transport = CreateUpdateTransport(args),
            FeedPath = feedPath,
            TrustedPublicKeyPath = ArgValue(args, "/UPDATECHANNELFEEDTRUSTKEY=") ?? ""
        };

    private static long ReadPositiveUpdateLimit(string[] args, string flag, long defaultValue, long maximum = long.MaxValue)
    {
        var value = ArgValue(args, flag);
        if (value is null) return defaultValue;
        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0 || parsed > maximum) throw new ArgumentException(flag + " requires a positive integer within its supported range.");
        return parsed;
    }

    private static int RunCheckUpdateChannel(string feedPath, string[] args, bool applyDelta = false)
    {
        var policyEvaluation = new InstallerPolicyEvaluation();
        policyEvaluation.Policy = LoadPolicyFromArgs(args, policyEvaluation);
        var feedOptions = CreateUpdateFeedOptions(feedPath, args);
        var installedVersion = ArgValue(args, "/UPDATECHANNELINSTALLEDVERSION=") ?? "";
        var currentChannel = ArgValue(args, "/UPDATECHANNELCURRENT=") ?? "";
        var targetChannel = ArgValue(args, "/UPDATECHANNELTARGET=") ?? "";
        var cohort = ArgValue(args, "/UPDATECHANNELCOHORT=") ?? "";
        var previewOnly = applyDelta && IsDryRun(args);
        UpdateChannelApplyResult? application = null;
        UpdateChannelCheckResult result;
        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (applyDelta && !previewOnly)
            {
                var deltaDirectory = ArgValue(args, "/DELTA=");
                var currentDirectory = ArgValue(args, "/DELTACURRENT=");
                var stageDirectory = ArgValue(args, "/DELTASTAGE=");
                if (string.IsNullOrWhiteSpace(currentDirectory)
                    || string.IsNullOrWhiteSpace(stageDirectory) || string.IsNullOrWhiteSpace(installedVersion))
                    throw new ArgumentException("Channel application requires /DELTACURRENT, /DELTASTAGE and /UPDATECHANNELINSTALLEDVERSION; omit /DELTA to acquire the signed remote package.");
                application = UpdateChannelFeedPackageService.ApplyDelta(feedOptions, new()
                {
                    CancellationToken = cancellation.Token,
                    DeltaDirectory = deltaDirectory ?? "", CurrentInstallDirectory = currentDirectory,
                    StageDirectory = stageDirectory, CurrentVersion = installedVersion,
                    JournalPath = ArgValue(args, "/DELTAJOURNAL=") ?? "",
                    TrustedPublicKeys = ReadTrustedKeys(ArgValue(args, "/DELTATRUSTKEY="))
                }, currentChannel, targetChannel, cohort, policyEvaluation: policyEvaluation);
                result = application.Check;
            }
            else
                result = UpdateChannelFeedPackageService.Check(feedOptions, installedVersion,
                    currentChannel, targetChannel, cohort, policyEvaluation: policyEvaluation, cancellationToken: cancellation.Token);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException or OperationCanceledException)
        {
            cancelled = ex is OperationCanceledException;
            result = new UpdateChannelCheckResult
            {
                Verification = new() { Diagnostics = { new(ProjectSchemaDiagnosticSeverity.Error, "BI1575", "UpdateChannels.Apply", ex.Message) } }
            };
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
        var exit = cancelled ? 1602 : result.PolicyEvaluation?.HasErrors == true ? 2 : !result.Success ? 1
            : (applyDelta && result.Decision!.Allowed != true) || result.Decision!.Action == UpdateChannelTransitionEvaluator.ActionBlocked ? 2
            : application is not null && !application.Success ? 1 : 0;
        var diagnostics = result.Verification.Diagnostics.Concat(result.PolicyEvaluation?.Diagnostics ?? new()).ToList();
        foreach (var diagnostic in diagnostics)
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        if (!string.IsNullOrWhiteSpace(application?.Error)) Console.Error.WriteLine(application.Error);
        if (Has(args, "/JSON"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = application?.Success ?? result.Success, exitCode = exit,
                action = applyDelta ? "apply-update-channel" : "check-update-channel",
                previewOnly, cancelled, applied = application?.Apply?.Success == true,
                application = application?.Apply, error = application?.Error,
                signatureTrusted = result.Verification.SignatureTrusted,
                decision = result.Decision, diagnostics,
                policy = result.PolicyEvaluation is null ? null : InstallerPolicyEvaluator.CreateEvidence(result.PolicyEvaluation)
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        else if (result.Decision is not null)
        {
            Console.WriteLine($"Update channel decision: {result.Decision.Action}");
            foreach (var reason in result.Decision.Reasons) Console.WriteLine(reason);
            if (previewOnly) Console.WriteLine("Eligibility preview only; delta contents were not staged or applied.");
            if (application?.Success == true) Console.WriteLine($"Update applied. Rollback journal: {application.Apply!.JournalPath}");
        }
        return exit;
    }

    private static int RunVerifyUpdateChannelFeed(string feedPath, string[] args)
    {
        var result = UpdateChannelFeedPackageService.Verify(new UpdateChannelFeedVerificationOptions
        {
            ExpectedAppName = ArgValue(args, "/UPDATEAPPNAME=") ?? "",
            ExpectedAppId = ArgValue(args, "/UPDATEAPPID=") ?? "",
            ExpectedAppPublisher = ArgValue(args, "/UPDATEPUBLISHER=") ?? "",
            FeedPath = feedPath,
            TrustedPublicKeyPath = ArgValue(args, "/UPDATECHANNELFEEDTRUSTKEY=") ?? ""
        });

        foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var stream = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? Console.Error : Console.Out;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (!result.Success)
            return 1;

        Console.WriteLine($"Update channel feed verified: {Path.GetFullPath(feedPath)}");
        Console.WriteLine($"Channels                    : {result.Manifest?.Channels.Count ?? 0}");
        Console.WriteLine($"Trusted key SHA-256         : {result.TrustedKeySha256}");
        return 0;
    }

    private static int RunQualifyUpdateChannelFeed(string feedPath, string[] args)
    {
        try
        {
            var report = new UpdateChannelQualificationRunner().Run(new UpdateChannelQualificationOptions
            {
                FeedPath = feedPath,
                TrustedPublicKeyPath = ArgValue(args, "/UPDATECHANNELFEEDTRUSTKEY=") ?? "",
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                CurrentChannelId = ArgValue(args, "/UPDATECHANNELCURRENT=") ?? "",
                TargetChannelId = ArgValue(args, "/UPDATECHANNELTARGET=") ?? "",
                InstalledVersion = ArgValue(args, "/UPDATECHANNELINSTALLEDVERSION=") ?? "",
                CohortSeed = ArgValue(args, "/UPDATECHANNELCOHORT=") ?? "",
                VerifyLifecycle = Has(args, "/UPDATECHANNELLIFECYCLE"),
                DryRun = IsDryRun(args),
                ScriptPath = ArgValue(args, "/UPDATECHANNELSCRIPT=") ?? "",
                UpdatedScriptPath = ArgValue(args, "/UPDATECHANNELUPDATEDSCRIPT=") ?? "",
                DowngradeScriptPath = ArgValue(args, "/UPDATECHANNELDOWNGRADESCRIPT=") ?? "",
                InstallDirectory = ArgValue(args, "/UPDATECHANNELINSTALLDIR=") ?? "",
                InstallerExecutablePath = ArgValue(args, "/UPDATECHANNELINSTALLER=") ?? ""
            });

            Console.WriteLine($"Update channel qualification: {report.ReportPath}");
            Console.WriteLine($"Success                     : {report.Success}");
            Console.WriteLine($"Scenarios                   : {report.Scenarios.Count}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {(scenario.Success ? "PASS" : "FAIL")} {scenario.Id}: {scenario.Message}");
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int RunDeltaBuild(string outputDirectory, string[] args)
    {
        var baseDirectory = ArgValue(args, "/DELTABASE=") ?? "";
        var targetDirectory = ArgValue(args, "/DELTATARGET=") ?? "";
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(targetDirectory))
        {
            Console.Error.WriteLine("ERROR: /DELTA requires /DELTABASE=<dir> and /DELTATARGET=<dir>.");
            return 2;
        }

        var project = IndexOf(args, "/SCRIPT=") >= 0 ? LoadRuntimeProject(args) : null;
        if (IndexOf(args, "/SCRIPT=") >= 0 && project is null)
        {
            Console.Error.WriteLine("ERROR: Delta image authoring requires a readable project when /SCRIPT is supplied.");
            return 2;
        }
        var signingPrivateKey = ReadOptionalFile(ArgValue(args, "/DELTASIGNKEY="));
        var result = new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            ExpectedAppId = project?.AppId ?? ArgValue(args, "/UPDATEAPPID=") ?? "",
            ExpectedProductName = project?.AppName ?? ArgValue(args, "/UPDATEAPPNAME=") ?? "",
            ExpectedPublisher = project?.AppPublisher ?? ArgValue(args, "/UPDATEPUBLISHER=") ?? "",
            BaseDirectory = baseDirectory,
            UpdatedDirectory = targetDirectory,
            OutputDirectory = outputDirectory,
            BaseVersion = ArgValue(args, "/DELTABASEVERSION=") ?? "",
            TargetVersion = ArgValue(args, "/DELTATARGETVERSION=") ?? project?.AppVersion ?? "",
            SigningPrivateKeyPem = signingPrivateKey
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"ERROR: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Delta package : {Path.GetFullPath(outputDirectory)}");
        Console.WriteLine($"Manifest      : {result.ManifestPath}");
        if (!string.IsNullOrWhiteSpace(result.SignaturePath))
            Console.WriteLine($"Signature     : {result.SignaturePath}");
        Console.WriteLine($"Base tree     : {result.BaseTreeSha256}");
        Console.WriteLine($"Target tree   : {result.TargetTreeSha256}");
        Console.WriteLine($"Files         : +{result.AddedFiles} ~{result.UpdatedFiles} -{result.RemovedFiles} ={result.UnchangedFiles}");
        Console.WriteLine($"Delta ratio   : {result.DeltaRatio:P2}");
        return 0;
    }

    private static int RunDeltaVerify(string deltaDirectory, string[] args)
    {
        var trustedKeys = ReadTrustedKeys(ArgValue(args, "/DELTATRUSTKEY="));
        var requireSignature = Has(args, "/REQUIRESIGNED") || trustedKeys.Count > 0;
        var result = new DeltaUpdatePackageService().Verify(deltaDirectory, requireSignature, trustedKeys);
        if (result.Manifest is null || !string.IsNullOrWhiteSpace(result.Error))
        {
            Console.Error.WriteLine($"ERROR: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Delta verified: {Path.GetFullPath(deltaDirectory)}");
        Console.WriteLine($"Base version  : {result.Manifest.BaseVersion}");
        Console.WriteLine($"Target version: {result.Manifest.TargetVersion}");
        Console.WriteLine($"Base tree     : {result.Manifest.BaseTreeSha256}");
        Console.WriteLine($"Target tree   : {result.Manifest.TargetTreeSha256}");
        Console.WriteLine($"Files         : {result.Manifest.Files.Count}");
        Console.WriteLine($"Signature     : {(result.Trusted ? "trusted" : "not required")}");
        return 0;
    }

    private static int RunQualifyDelta(string deltaDirectory, string[] args)
    {
        var currentDirectory = ArgValue(args, "/DELTACURRENT=") ?? "";
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            Console.Error.WriteLine("ERROR: /QUALIFYDELTA requires /DELTACURRENT=<dir>.");
            return 2;
        }

        try
        {
            var trustedKeys = ReadTrustedKeys(ArgValue(args, "/DELTATRUSTKEY="));
            var requireSignature = Has(args, "/REQUIRESIGNED") || trustedKeys.Count > 0;
            var report = new DeltaUpdateQualificationRunner().Run(new DeltaUpdateQualificationOptions
            {
                DeltaDirectory = deltaDirectory,
                CurrentInstallDirectory = currentDirectory,
                StageDirectory = ArgValue(args, "/DELTASTAGE=") ?? "",
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                JournalPath = ArgValue(args, "/DELTAJOURNAL=") ?? "",
                CurrentVersion = ArgValue(args, "/DELTACURRENTVERSION=") ?? "",
                RequireSignature = requireSignature,
                TrustedPublicKeys = trustedKeys
            });

            Console.WriteLine($"Delta qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
            {
                var status = scenario.Success ? "passed" : "failed";
                Console.WriteLine($"  {scenario.Id,-28}: {status}");
            }
            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Delta qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunDeltaApply(string deltaDirectory, string[] args)
    {
        var currentDirectory = ArgValue(args, "/DELTACURRENT=") ?? "";
        var stageDirectory = ArgValue(args, "/DELTASTAGE=") ?? "";
        if (string.IsNullOrWhiteSpace(currentDirectory) || string.IsNullOrWhiteSpace(stageDirectory))
        {
            Console.Error.WriteLine("ERROR: /APPLYDELTA requires /DELTACURRENT=<dir> and /DELTASTAGE=<dir>.");
            return 2;
        }

        var trustedKeys = ReadTrustedKeys(ArgValue(args, "/DELTATRUSTKEY="));
        var requireSignature = Has(args, "/REQUIRESIGNED") || trustedKeys.Count > 0;
        var result = new DeltaUpdatePackageService().ApplyAtomically(new DeltaUpdateAtomicApplyOptions
        {
            DeltaDirectory = deltaDirectory,
            CurrentInstallDirectory = currentDirectory,
            StageDirectory = stageDirectory,
            JournalPath = ArgValue(args, "/DELTAJOURNAL=") ?? "",
            CurrentVersion = ArgValue(args, "/DELTACURRENTVERSION=") ?? "",
            RequireSignature = requireSignature,
            TrustedPublicKeys = trustedKeys
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"ERROR: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Delta applied : {result.InstallDirectory}");
        Console.WriteLine($"Rollback      : {result.BackupDirectory}");
        Console.WriteLine($"Journal       : {result.JournalPath}");
        Console.WriteLine($"Manifest      : {result.ManifestPath}");
        Console.WriteLine($"Target tree   : {result.TargetTreeSha256}");
        Console.WriteLine($"Files         : +{result.AddedFiles} ~{result.UpdatedFiles} -{result.RemovedFiles} ={result.UnchangedFiles}");
        return 0;
    }

    private static int RunDeltaRollback(string journalPath, string[] args)
    {
        var result = new DeltaUpdatePackageService().RollbackAtomicApply(new DeltaUpdateRollbackOptions
        {
            JournalPath = journalPath
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"ERROR: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Delta rolled back: {result.InstallDirectory}");
        Console.WriteLine($"Journal          : {result.JournalPath}");
        Console.WriteLine($"Restored tree    : {result.BaseTreeSha256}");
        return 0;
    }

    private static string ReadOptionalFile(string? path)
        => string.IsNullOrWhiteSpace(path) ? "" : File.ReadAllText(Path.GetFullPath(path));

    private static List<string> ReadTrustedKeys(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? new List<string>()
            : new List<string> { File.ReadAllText(Path.GetFullPath(path)) };

    // ── CLI offline layout ─────────────────────────────────────────────

    private static int RunOfflineLayout(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        if (!EnforceProjectPolicy(project, args, "policy", out var policyEvaluation))
            return 1;
        var policy = policyEvaluation.Policy;

        var outputDirectory = ArgValue(args, "/OUT=")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory, "layout");
        var result = new OfflineLayoutBuilder().Build(project, outputDirectory, new OfflineLayoutOptions
        {
            BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory,
            DownloadRemotePackages = Has(args, "/DOWNLOAD"),
            ResumeDownloads = !Has(args, "/LAYOUTNORESUME"),
            CacheDirectory = ArgValue(args, "/LAYOUTCACHE=") ?? "",
            CacheRetentionDays = IntArg(args, "/LAYOUTCACHERETENTIONDAYS=") ?? policy?.OfflineLayoutCacheRetentionDays,
            ProxyUri = ArgValue(args, "/LAYOUTPROXY=") ?? policy?.OfflineLayoutProxyUri ?? "",
            ProxyUsername = ArgValue(args, "/LAYOUTPROXYUSER=") ?? policy?.OfflineLayoutProxyUsername ?? "",
            ProxyPassword = ArgValue(args, "/LAYOUTPROXYPASSWORD=") ?? policy?.OfflineLayoutProxyPassword ?? "",
            BearerToken = ArgValue(args, "/LAYOUTBEARERTOKEN=") ?? policy?.OfflineLayoutBearerToken ?? "",
            HeaderName = ArgValue(args, "/LAYOUTHEADERNAME=") ?? policy?.OfflineLayoutHeaderName ?? "",
            HeaderValue = ArgValue(args, "/LAYOUTHEADERVALUE=") ?? policy?.OfflineLayoutHeaderValue ?? "",
            SigningPrivateKeyPath = ArgValue(args, "/LAYOUTSIGNKEY=") ?? ""
        });

        foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var stream = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? Console.Error : Console.Out;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (!result.Success)
            return 1;

        Console.WriteLine($"Offline layout : {result.LayoutDirectory}");
        Console.WriteLine($"Inventory      : {result.InventoryPath}");
        if (!string.IsNullOrWhiteSpace(result.SignaturePath))
            Console.WriteLine($"Signature      : {result.SignaturePath}");
        return 0;
    }

    private static int RunVerifyOfflineLayout(string layoutPath, string[] args)
    {
        var policyEvaluation = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyEvaluation);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
            return 2;

        var result = new OfflineLayoutBuilder().Verify(layoutPath, new OfflineLayoutVerificationOptions
        {
            RequireSignature = ShouldRequireOfflineLayoutSignature(args, policy),
            TrustedPublicKeyPath = ResolveOfflineLayoutTrustedPublicKeyPath(args, policy),
            TrustedPublicKey = ResolveOfflineLayoutTrustedPublicKey(args, policy)
        });

        foreach (var diagnostic in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var stream = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? Console.Error : Console.Out;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (!result.Success)
            return 1;

        Console.WriteLine($"Offline layout verified: {result.LayoutDirectory}");
        Console.WriteLine($"Inventory              : {result.InventoryPath}");
        Console.WriteLine($"Signature              : {result.SignaturePath}");
        return 0;
    }

    private static int RunQualifyOfflineLayout(string layoutPath, string[] args)
    {
        var policyEvaluation = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyEvaluation);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
            return 2;

        try
        {
            var outputDirectory = ArgValue(args, "/OUT=")
                                  ?? Path.Combine(Path.GetFullPath(layoutPath), "qualification");
            var report = new OfflineLayoutQualificationRunner().Run(new OfflineLayoutQualificationOptions
            {
                LayoutDirectory = layoutPath,
                ScriptPath = ArgValue(args, "/SCRIPT=") ?? "",
                InstallDirectory = ArgValue(args, "/D=") ?? "",
                OutputDirectory = outputDirectory,
                InstallerExecutablePath = ArgValue(args, "/INSTALLER=") ?? "",
                DryRun = IsDryRun(args),
                RequireMixedPackageTypes = Has(args, "/REQUIREMIXEDPACKAGES"),
                RequiredPackageTypes = SplitList(ArgValue(args, "/REQUIREDPACKAGETYPES=") ?? "exe,msi,msp,msu"),
                VerificationOptions = new OfflineLayoutVerificationOptions
                {
                    RequireSignature = ShouldRequireOfflineLayoutSignature(args, policy),
                    TrustedPublicKeyPath = ResolveOfflineLayoutTrustedPublicKeyPath(args, policy),
                    TrustedPublicKey = ResolveOfflineLayoutTrustedPublicKey(args, policy)
                },
                ExtraInstallArguments = args
                    .Where(a => a.StartsWith("/PROPERTY:", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(a, "/JSON", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(a, "/SUPPORTBUNDLE", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            });

            Console.WriteLine($"Offline layout qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
            {
                var status = scenario.Success ? "passed" : "failed";
                Console.WriteLine($"  {scenario.Id,-24}: {status}");
            }
            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Offline layout qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyAccessibilityLocalization(string sourceRoot, string[] args)
    {
        try
        {
            var report = new AccessibilityLocalizationQualificationRunner().Run(new AccessibilityLocalizationQualificationOptions
            {
                SourceRoot = sourceRoot,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                ManualEvidenceDirectory = ArgValue(args, "/A11YEVIDENCE=") ?? "",
                RequireManualEvidence = Has(args, "/REQUIREA11YEVIDENCE")
            });

            Console.WriteLine($"Accessibility/localization qualification: {report.ReportPath}");
            Console.WriteLine($"Manual evidence checklist          : {report.ManualEvidenceChecklistPath}");
            foreach (var scenario in report.Scenarios)
            {
                var status = scenario.Success ? "passed" : "failed";
                Console.WriteLine($"  {scenario.Id,-34}: {status}");
            }
            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Accessibility/localization qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyVmReadiness(string evidenceRoot, string[] args)
    {
        try
        {
            var report = new VmQualificationReadinessEvaluator().Evaluate(new VmQualificationReadinessOptions
            {
                EvidenceRoot = evidenceRoot,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                RequiredTargets = VmQualificationReadinessEvaluator.ParseTargets(ArgValue(args, "/QUALIFYVMTARGETS=") ?? ""),
                RequiredScenarios = VmQualificationReadinessEvaluator.ParseList(ArgValue(args, "/QUALIFYVMSCENARIOS=") ?? ""),
                MaxEvidenceAgeDays = IntArg(args, "/MAXEVIDENCEAGEDAYS=") ?? 30,
                MaxFlakyFailuresPerTarget = IntArg(args, "/MAXFLAKYFAILURES=") ?? 0,
                MaxDurationSeconds = IntArg(args, "/MAXDURATIONSECONDS=") ?? 0,
                RequireNegativeSecurityEvidence = Has(args, "/REQUIRENEGATIVESECURITYEVIDENCE")
            });

            Console.WriteLine($"VM qualification readiness: {report.ReportPath}");
            Console.WriteLine($"VM evidence runbook       : {report.EvidenceRunbookPath}");
            Console.WriteLine($"VM evidence manifest      : {report.EvidenceManifestPath}");
            foreach (var cell in report.Cells)
            {
                var status = cell.Success ? "passed" : "failed";
                Console.WriteLine($"  {cell.EnvironmentId,-24}: {status} scenarios={cell.ScenarioIds.Count} failures={cell.FailureCount} ageDays={cell.AgeDays:F1}");
            }
            if (!report.Success)
            {
                foreach (var diagnostic in report.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"VM qualification readiness failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyReleasePortfolio(string evidenceRoot, string[] args)
    {
        try
        {
            var report = new ReleaseQualificationPortfolioRunner().Run(new ReleaseQualificationPortfolioOptions
            {
                EvidenceRoot = evidenceRoot,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                RequiredQualifications = SplitList(ArgValue(args, "/REQUIREDQUALIFICATIONS=") ?? "")
            });

            if (Has(args, "/JSON"))
            {
                Console.WriteLine(ReleaseQualificationPortfolioRunner.SerializeReport(report));
                return report.ExitCode;
            }

            Console.WriteLine($"Release qualification portfolio: {report.ReportPath}");
            Console.WriteLine($"Release evidence gap plan     : {report.GapPlanPath}");
            Console.WriteLine($"Release evidence gap manifest : {report.GapManifestPath}");
            foreach (var evidence in report.Evidence)
            {
                var status = evidence.Passed ? "passed" : evidence.Found ? "failed" : "missing";
                Console.WriteLine($"  {evidence.QualificationId,-24}: {status} ({evidence.ReportFileName})");
            }
            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Release qualification portfolio failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI deployment kit ──────────────────────────────────────────────

    private static int RunDeploymentKit(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        ApplySigningOverrides(project, args);
        if (!EnforceProjectPolicy(project, args, "policy", out _))
            return 1;

        var outputDirectory = ArgValue(args, "/OUT=")
                              ?? Path.Combine(
                                  Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory,
                                  "deployment-kit",
                                  SafePathSegment(project.AppName));
        var result = EnterpriseDeploymentKitGenerator.Generate(
            project,
            outputDirectory,
            new EnterpriseDeploymentKitOptions
            {
                InstallerFileName = ArgValue(args, "/INSTALLER="),
                InstallDirectory = ArgValue(args, "/D=")
            });

        Console.WriteLine($"Deployment kit: {result.OutputDirectory}");
        Console.WriteLine($"Manifest      : {result.ManifestPath}");
        Console.WriteLine($"Files         : {result.Files.Count}");
        foreach (var file in result.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {file}");
        return 0;
    }

    private static int RunDeploymentKitQualification(string kitRoot, string[] args)
    {
        try
        {
            var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
            {
                KitRoot = kitRoot,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                RequireManagedDeviceEvidence = Has(args, "/REQUIREMANAGEDEVIDENCE"),
                MaxEvidenceAgeDays = IntArg(args, "/MAXEVIDENCEAGEDAYS=") ?? 30
            });

            Console.WriteLine($"Deployment kit qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-28}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Deployment kit qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyUpgrade(string projectPath, string[] args)
    {
        try
        {
            var report = new UpgradeQualificationRunner().Run(new UpgradeQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Upgrade qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Upgrade qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyRecovery(string projectPath, string[] args)
    {
        try
        {
            var report = new RecoveryQualificationRunner().Run(new RecoveryQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Recovery qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Recovery qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyServices(string projectPath, string[] args)
    {
        try
        {
            var report = new WindowsServiceQualificationRunner().Run(new WindowsServiceQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Windows service qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Windows service qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifySigning(string[] args)
    {
        try
        {
            var report = new SigningQualificationRunner().Run(new SigningQualificationOptions
            {
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Signing qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Signing qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyIis(string projectPath, string[] args)
    {
        try
        {
            var report = new IisQualificationRunner().Run(new IisQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"IIS/web qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"IIS/web qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifySystemResources(string projectPath, string[] args)
    {
        try
        {
            var report = new SystemResourceQualificationRunner().Run(new SystemResourceQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"System-resource qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"System-resource qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyReleaseEvidence(string projectPath, string[] args)
    {
        try
        {
            var report = new ReleaseEvidenceQualificationRunner().Run(new ReleaseEvidenceQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                InstallerPath = ArgValue(args, "/INSTALLER=") ?? "",
                SourceRoot = ArgValue(args, "/SOURCEROOT=") ?? ""
            });

            Console.WriteLine($"Release-evidence qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or CryptographicException)
        {
            Console.Error.WriteLine($"Release-evidence qualification failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI MSI/WiX export ─────────────────────────────────────────────

    private static int RunMsiExport(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        if (!EnforceProjectPolicy(project, args, "policy", out var policyEvaluation))
            return 1;

        try
        {
            var outputDirectory = ArgValue(args, "/OUT=")
                                  ?? Path.Combine(
                                      Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory,
                                      "msi");
            var result = Engine.Msi.MsiPackageExporter.Generate(project, new Engine.Msi.MsiExportOptions
            {
                OutputDirectory = outputDirectory,
                FailOnUnsupportedOperations = policyEvaluation.Policy is null || InstallerPolicyEvaluator.IsUnsupportedMsiOperationError(policyEvaluation.Policy),
                ForbidCustomActions = policyEvaluation.Policy?.ForbidCustomActions == true,
                AllowedCustomActionFamilies = ResolveMsiCustomActionFamilies(args, policyEvaluation.Policy),
                BuildPackage = Has(args, "/MSIBUILD"),
                TransformPath = ArgValue(args, "/MST="),
                TransformTargetPackagePath = ArgValue(args, "/MSITARGET="),
                TransformUpdatedPackagePath = ArgValue(args, "/MSIUPDATED="),
                TransformType = ArgValue(args, "/MSTTYPE="),
                TransformValidationFlags = ArgValue(args, "/MSTVALIDATION="),
                TransformSuppressErrorFlags = ArgValue(args, "/MSTSUPPRESSERRORS="),
                PreserveUnchangedTransformRows = Has(args, "/MSTPRESERVE"),
                TransformProfile = ArgValue(args, "/MSTPROFILE="),
                TransformPropertyValues = ArgValue(args, "/MSTPROPERTY="),
                VerifyTransformLifecycle = Has(args, "/MSTLIFECYCLE"),
                TransformLifecyclePackagePath = ArgValue(args, "/MSTLIFECYCLEPACKAGE="),
                TransformLifecycleTransformPath = ArgValue(args, "/MSTLIFECYCLETRANSFORM="),
                TransformLifecycleLogDirectory = ArgValue(args, "/MSTLIFECYCLELOGDIR="),
                TransformLifecycleProperties = ArgValue(args, "/MSTLIFECYCLEPROPERTIES="),
                PatchPath = ArgValue(args, "/MSP="),
                PatchTargetPackagePath = ArgValue(args, "/MSPTARGET="),
                PatchUpdatedPackagePath = ArgValue(args, "/MSPUPDATED="),
                PatchBaselineId = ArgValue(args, "/MSPBASELINE="),
                PatchFamilyId = ArgValue(args, "/MSPFAMILY="),
                PatchVersion = ArgValue(args, "/MSPVERSION="),
                PatchClassification = ArgValue(args, "/MSPCLASSIFICATION="),
                PatchAllowRemoval = !Has(args, "/MSPNOREMOVAL"),
                PatchSupersede = !Has(args, "/MSPNOSUPERSEDE"),
                PatchRequireChangedPackages = !Has(args, "/MSPALLOWEMPTYDELTA"),
                VerifyPatchLifecycle = Has(args, "/MSPLIFECYCLE"),
                PatchLifecycleProductPackagePath = ArgValue(args, "/MSPPRODUCT="),
                PatchLifecycleLogDirectory = ArgValue(args, "/MSPLOGDIR="),
                PatchLifecycleProperties = ArgValue(args, "/MSPPROPERTIES="),
                ValidatePackage = Has(args, "/MSIVALIDATE"),
                ValidationPackagePath = ArgValue(args, "/MSIVALIDATEPACKAGE="),
                ValidationPdbPath = ArgValue(args, "/MSIVALIDATEPDB="),
                ValidationCubePath = ArgValue(args, "/MSIVALIDATECUB="),
                ValidationIceIds = ArgValue(args, "/MSIVALIDATEICE="),
                ValidationSuppressIceIds = ArgValue(args, "/MSIVALIDATESUPPRESSICE="),
                VerifyLifecycle = Has(args, "/MSILIFECYCLE"),
                LifecyclePackagePath = ArgValue(args, "/MSILIFECYCLEPACKAGE="),
                LifecycleLogDirectory = ArgValue(args, "/MSILIFECYCLELOGDIR="),
                LifecycleProperties = ArgValue(args, "/MSILIFECYCLEPROPERTIES="),
                VerifyLifecycleMatrix = Has(args, "/MSIMATRIX"),
                LifecycleMatrixTargets = ArgValue(args, "/MSIMATRIXTARGETS="),
                LifecycleMatrixRunnerPath = ArgValue(args, "/MSIMATRIXRUNNER="),
                LifecycleMatrixLogDirectory = ArgValue(args, "/MSIMATRIXLOGDIR="),
                LifecycleMatrixProperties = ArgValue(args, "/MSIMATRIXPROPERTIES="),
                LifecycleMatrixScenarioPackPath = ArgValue(args, "/MSIMATRIXSCENARIOS="),
                MsiexecToolPath = ArgValue(args, "/MSIEXEC="),
                SignOutput = !Has(args, "/NOSIGN") && project.HasCodeSigningCertificate,
                RequireSignedOutput = Has(args, "/REQUIRESIGNED"),
                ExpectedSigningSubject = ArgValue(args, "/SIGNINGSUBJECT="),
                TimestampOutagePolicy = ResolveTimestampOutagePolicy(args, policyEvaluation.Policy),
                TimestampRetryCount = ResolveTimestampRetryCount(args),
                RemoteSigningProvider = ArgValue(args, "/SIGNREMOTEPROVIDER="),
                RemoteSigningEndpoint = ArgValue(args, "/SIGNREMOTEENDPOINT="),
                RemoteSigningKeyId = ArgValue(args, "/SIGNREMOTEKEY="),
                RemoteSigningCredential = ArgValue(args, "/SIGNREMOTECREDENTIAL="),
                WixToolPath = ArgValue(args, "/WIX=")
            });

            Console.WriteLine($"MSI WiX source : {result.WixSourcePath}");
            if (!string.IsNullOrWhiteSpace(result.PackagePath))
                Console.WriteLine($"MSI package    : {result.PackagePath}");
            if (!string.IsNullOrWhiteSpace(result.TransformPath))
                Console.WriteLine($"MST transform  : {result.TransformPath}");
            if (!string.IsNullOrWhiteSpace(result.TransformLifecycleTransformPath))
                Console.WriteLine($"MST lifecycle  : {result.TransformLifecycleTransformPath}");
            if (!string.IsNullOrWhiteSpace(result.PatchSourcePath))
                Console.WriteLine($"MSP WiX source : {result.PatchSourcePath}");
            if (!string.IsNullOrWhiteSpace(result.PatchPath))
                Console.WriteLine($"MSP patch      : {result.PatchPath}");
            if (!string.IsNullOrWhiteSpace(result.PatchLifecycleProductPackagePath))
                Console.WriteLine($"MSP lifecycle  : {result.PatchLifecycleProductPackagePath}");
            if (!string.IsNullOrWhiteSpace(result.ValidationPackagePath))
                Console.WriteLine($"MSI validated  : {result.ValidationPackagePath}");
            if (!string.IsNullOrWhiteSpace(result.LifecyclePackagePath))
                Console.WriteLine($"MSI lifecycle  : {result.LifecyclePackagePath}");
            if (!string.IsNullOrWhiteSpace(result.LifecycleMatrixLogDirectory))
                Console.WriteLine($"MSI matrix     : {result.LifecycleMatrixLogDirectory}");
            Console.WriteLine($"Capability JSON: {result.CapabilityReportPath}");
            Console.WriteLine($"Plan hash      : {result.PlanHash}");
            Console.WriteLine($"UpgradeCode    : {result.UpgradeCode}");
            Console.WriteLine($"Components     : {result.Components.Count}");
            if (!string.IsNullOrWhiteSpace(result.WixCommandLine))
                Console.WriteLine($"WiX command    : {result.WixCommandLine}");
            if (!string.IsNullOrWhiteSpace(result.WixTransformUpdatedBuildCommandLine))
                Console.WriteLine($"MST profile MSI: {result.WixTransformUpdatedBuildCommandLine}");
            if (!string.IsNullOrWhiteSpace(result.WixTransformCommandLine))
                Console.WriteLine($"WiX transform  : {result.WixTransformCommandLine}");
            if (!string.IsNullOrWhiteSpace(result.WixPatchCommandLine))
                Console.WriteLine($"WiX patch      : {result.WixPatchCommandLine}");
            if (!string.IsNullOrWhiteSpace(result.WixValidationCommandLine))
                Console.WriteLine($"WiX validate   : {result.WixValidationCommandLine}");
            foreach (var lifecycle in result.LifecycleEvidence)
            {
                var status = lifecycle.Success ? "passed" : "failed";
                Console.WriteLine($"MSI {lifecycle.Action,-9}: {status} ({lifecycle.LogPath})");
            }
            foreach (var lifecycle in result.TransformLifecycleEvidence)
            {
                var status = lifecycle.Success ? "passed" : "failed";
                Console.WriteLine($"MST {lifecycle.Action,-18}: {status} ({lifecycle.LogPath})");
            }
            foreach (var lifecycle in result.PatchLifecycleEvidence)
            {
                var status = lifecycle.Success ? "passed" : "failed";
                Console.WriteLine($"MSP {lifecycle.Action,-12}: {status} ({lifecycle.LogPath})");
            }
            foreach (var matrix in result.LifecycleMatrixEvidence)
            {
                var status = matrix.Success ? "passed" : "failed";
                Console.WriteLine($"MSI matrix {matrix.EnvironmentId,-14}: {status} ({matrix.LogDirectory})");
            }
            foreach (var signing in result.SigningEvidence)
            {
                var status = signing.Success ? "signed" : "sign failed";
                Console.WriteLine($"{signing.ArtifactKind.ToUpperInvariant()} signing  : {status} ({signing.ArtifactPath})");
            }
            foreach (var finding in result.Findings)
                Console.WriteLine($"{finding.Severity.ToUpperInvariant(),-7} {finding.Code} {finding.OperationId}: {finding.Message}");
            return result.HasErrors ? 1 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int RunMsiMatrixQualificationRunner(string[] args)
    {
        try
        {
            var options = Engine.Msi.MsiLifecycleMatrixRunner.Parse(args);
            var report = Engine.Msi.MsiLifecycleMatrixRunner.Run(options);
            Engine.Msi.MsiLifecycleMatrixRunner.WriteReport(report);

            Console.WriteLine($"Matrix target : {report.EnvironmentId}");
            Console.WriteLine($"Channel       : {report.Channel}");
            Console.WriteLine($"Report        : {Path.Combine(report.LogDirectory, Engine.Msi.MsiLifecycleMatrixRunner.ReportFileName)}");
            foreach (var action in report.Actions)
            {
                var status = action.Success ? "passed" : "failed";
                Console.WriteLine($"  {action.Name,-14}: {status} ({action.LogPath})");
            }

            if (!report.Success)
                Console.Error.WriteLine(report.Message);
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Matrix qualification failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI WinGet manifest export ─────────────────────────────────────

    private static int RunWinGetExport(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        var formatValue = ArgValue(args, "/FORMAT=");
        if (formatValue != null && Enum.TryParse<InstallerOutputFormat>(formatValue, ignoreCase: true, out var format))
            project.OutputFormat = format;

        if (!EnforceProjectPolicy(project, args, "policy", out _))
            return 1;

        try
        {
            var outputDirectory = ArgValue(args, "/OUT=")
                                  ?? Path.Combine(
                                      Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory,
                                      "winget");
            var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
            {
                OutputDirectory = outputDirectory,
                InstallerPath = ArgValue(args, "/INSTALLER="),
                InstallerUrl = ArgValue(args, "/INSTALLERURL="),
                InstallerSha256 = ArgValue(args, "/SHA256="),
                SignatureSha256 = ArgValue(args, "/SIGNATURESHA256="),
                Installers = WinGetInstallersFromArgs(args),
                SbomPath = ArgValue(args, "/SBOMPATH="),
                ProvenancePath = ArgValue(args, "/PROVENANCEPATH="),
                SigningEvidencePath = ArgValue(args, "/SIGNINGEVIDENCE="),
                PackageIdentifier = ArgValue(args, "/PACKAGEID="),
                PackageLocale = ArgValue(args, "/PACKAGELOCALE=") ?? "en-US",
                License = ArgValue(args, "/LICENSE="),
                ShortDescription = ArgValue(args, "/DESCRIPTION="),
                Moniker = ArgValue(args, "/MONIKER=")
            });

            Console.WriteLine($"WinGet manifests: {result.ManifestDirectory}");
            Console.WriteLine($"Package ID      : {result.PackageIdentifier}");
            foreach (var installer in result.Installers)
                Console.WriteLine($"Installer       : {installer.Architecture} {installer.InstallerSha256} {installer.InstallerUrl}");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"WARN           : {warning}");
            Console.WriteLine($"Files          : {result.Files.Count}");
            foreach (var file in result.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"  {file}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static List<WinGetInstallerArtifact> WinGetInstallersFromArgs(string[] args)
    {
        var installers = new List<WinGetInstallerArtifact>();
        AddWinGetInstaller(installers, "x86", args, "/INSTALLERX86=", "/INSTALLERURLX86=", "/SHA256X86=");
        AddWinGetInstaller(installers, "x64", args, "/INSTALLERX64=", "/INSTALLERURLX64=", "/SHA256X64=");
        AddWinGetInstaller(installers, "arm64", args, "/INSTALLERARM64=", "/INSTALLERURLARM64=", "/SHA256ARM64=");
        AddWinGetInstaller(installers, "neutral", args, "/INSTALLERNEUTRAL=", "/INSTALLERURLNEUTRAL=", "/SHA256NEUTRAL=");
        return installers;
    }

    private static void AddWinGetInstaller(
        List<WinGetInstallerArtifact> installers,
        string architecture,
        string[] args,
        string installerPrefix,
        string urlPrefix,
        string shaPrefix)
    {
        var installerPath = ArgValue(args, installerPrefix);
        var installerUrl = ArgValue(args, urlPrefix);
        var sha256 = ArgValue(args, shaPrefix);
        var signatureSha256 = ArgValue(args, "/SIGNATURESHA256" + architecture.ToUpperInvariant() + "=");
        if (string.IsNullOrWhiteSpace(installerPath)
            && string.IsNullOrWhiteSpace(installerUrl)
            && string.IsNullOrWhiteSpace(sha256)
            && string.IsNullOrWhiteSpace(signatureSha256))
        {
            return;
        }

        installers.Add(new WinGetInstallerArtifact
        {
            Architecture = architecture,
            InstallerPath = installerPath,
            InstallerUrl = installerUrl,
            InstallerSha256 = sha256,
            SignatureSha256 = signatureSha256
        });
    }

    // ── CLI release evidence export ────────────────────────────────────

    private static int RunReleaseEvidence(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        if (!EnforceProjectPolicy(project, args, "policy", out var policyEvaluation))
            return 1;

        if (!RunSupplyChainGate(project, args, policyEvaluation.Policy, "supply-chain", out var supplyChainReport))
            return 1;

        try
        {
            WriteReleaseEvidence(
                project,
                ArgValue(args, "/INSTALLER="),
                ArgValue(args, "/OUT=")
                ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory, "evidence"),
                args,
                policyEvaluation,
                supplyChainReport);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int RunVerifyReleaseEvidence(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        if (!EnforceProjectPolicy(project, args, "policy", out var policyEvaluation))
            return 1;

        try
        {
            var result = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
            {
                EvidenceDirectory = ArgValue(args, "/EVIDENCEDIR=") ?? ArgValue(args, "/OUT="),
                SbomPath = ArgValue(args, "/SBOMPATH="),
                ProvenancePath = ArgValue(args, "/PROVENANCEPATH="),
                SigningEvidencePath = ArgValue(args, "/SIGNINGEVIDENCE="),
                InstallerPath = ArgValue(args, "/INSTALLER="),
                SourceRoot = ArgValue(args, "/SOURCEROOT="),
                ReportPath = ArgValue(args, "/EVIDENCEREPORT="),
                RequireAttestations = Has(args, "/REQUIREATTESTATIONS"),
                TrustedAttestationPublicKeyPath = ArgValue(args, "/ATTESTTRUSTKEY="),
                PolicyEvaluation = policyEvaluation
            });

            Console.WriteLine($"Evidence verification: {result.ReportPath}");
            Console.WriteLine($"Artifacts            : {result.Artifacts.Count}");
            Console.WriteLine($"Result               : {(result.Success ? "passed" : "failed")}");
            if (result.Diagnostics.Count > 0)
                PrintDiagnostics("release-evidence", result.Diagnostics);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int RunSupplyChainScan(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"Error: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        ApplySigningOverrides(project, args);
        var policyEvaluation = EvaluateProjectPolicy(project, args);
        if (policyEvaluation.Diagnostics.Count > 0)
            PrintDiagnostics("policy", policyEvaluation.Diagnostics);
        if (policyEvaluation.HasErrors)
            return 1;

        var reportPath = ArgValue(args, "/SECURITYREPORT=")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory, "supply-chain-report.json");
        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policyEvaluation.Policy,
            InstallerPath = ArgValue(args, "/INSTALLER="),
            OutputPath = reportPath
        });

        PrintSupplyChainReport(report, reportPath);
        return report.HasErrors ? 1 : 0;
    }

    private static int RunQualifySupplyChainSecurity(string projectPath, string[] args)
    {
        try
        {
            var report = new SupplyChainQualificationRunner().Run(new SupplyChainQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                InstallerPath = ArgValue(args, "/INSTALLER=") ?? "",
                SourceRoot = ArgValue(args, "/SOURCEROOT=") ?? ""
            });

            Console.WriteLine($"Supply-chain qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-38}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Supply-chain qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyDiagnostics(string projectPath, string[] args)
    {
        try
        {
            var report = new DiagnosticsQualificationRunner().Run(new DiagnosticsQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Diagnostics qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-34}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Diagnostics qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyHeadlessSdk(string projectPath, string[] args)
    {
        try
        {
            var report = new HeadlessSdkQualificationRunner().Run(new HeadlessSdkQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                SdkProjectPath = ArgValue(args, "/SDKPROJECT=") ?? "",
                PackageVersion = ArgValue(args, "/SDKPACKAGEVERSION=") ?? ""
            });

            Console.WriteLine($"Headless SDK qualification: {report.ReportPath}");
            Console.WriteLine($"  package: {report.PackageId} {report.PackageVersion}");
            Console.WriteLine($"  plan hash: {report.PlanHash}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-28}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Headless SDK qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunPublishSdkPackage(string packagePath, string[] args)
    {
        try
        {
            var result = new SdkPackagePublisher().Publish(new SdkPackagePublishOptions
            {
                PackagePath = packagePath,
                Source = ArgValue(args, "/SDKFEED=") ?? "",
                ApiKey = ArgValue(args, "/SDKAPIKEY=") ?? "",
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                DryRun = Has(args, "/DRYRUN")
            });

            if (Has(args, "/JSON"))
            {
                Console.Write(File.ReadAllText(result.ReportPath));
            }
            else
            {
                Console.WriteLine($"SDK package publish: {result.ReportPath}");
                Console.WriteLine($"  package : {result.PackagePath}");
                Console.WriteLine($"  source  : {result.Source}");
                Console.WriteLine($"  mode    : {(result.DryRun ? "dry-run" : "publish")}");
                Console.WriteLine($"  result  : {(result.Success ? "passed" : "failed")}");
                if (!result.Success)
                {
                    foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                        Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
                }
            }

            return result.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"SDK package publish failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyEnterpriseCli(string projectPath, string[] args)
    {
        try
        {
            var report = new EnterpriseCliQualificationRunner().Run(new EnterpriseCliQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Enterprise CLI qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-30}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Enterprise CLI qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyCompiledPlan(string projectPath, string[] args)
    {
        try
        {
            var report = new CompiledPlanQualificationRunner().Run(new CompiledPlanQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Compiled-plan qualification: {report.ReportPath}");
            Console.WriteLine($"  plan hash: {report.PlanHash}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-28}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Compiled-plan qualification failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunQualifyConfigTransforms(string projectPath, string[] args)
    {
        try
        {
            var report = new ConfigTransformQualificationRunner().Run(new ConfigTransformQualificationOptions
            {
                ProjectPath = projectPath,
                OutputDirectory = ArgValue(args, "/OUT=") ?? ""
            });

            Console.WriteLine($"Configuration-transform qualification: {report.ReportPath}");
            foreach (var scenario in report.Scenarios)
                Console.WriteLine($"  {scenario.Id,-42}: {(scenario.Success ? "passed" : "failed")}");
            if (!report.Success)
            {
                foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                    Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Configuration-transform qualification failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI resource recovery ──────────────────────────────────────────

    private static int RunRecovery(string projectPath, string[] args)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        var perUser = Engine.InstallScopeResolver.IsPerUser(project);
        var installPath = ArgValue(args, "/D=") ?? Engine.InstallScopeResolver.ResolveDefaultPath(project, perUser);
        string journalPath;
        try { journalPath = ResourceExecutionJournalStore.ResolvePath(installPath, project.AppId, ArgValue(args, "/JOURNAL=")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("ERROR: Cannot locate the installed journal: " + ex.Message);
            return 2;
        }

        if (Has(args, "/ROLLBACK"))
        {
            Console.WriteLine($"Rolling back typed resources for {project.AppName} from {journalPath}…");
            return RunUninstall(project, args);
        }

        if (Has(args, "/ABANDON"))
        {
            var abandoned = ResourceJournalRecoveryService.Abandon(journalPath);
            Console.WriteLine(abandoned.Message);
            if (!string.IsNullOrWhiteSpace(abandoned.ArchivedPath))
                Console.WriteLine($"Archive: {abandoned.ArchivedPath}");
            return abandoned.Succeeded ? 0 : 1;
        }

        var planResult = new InstallPlanCompiler().Compile(project);
        CompiledInstallPlan? plan = null;
        if (planResult.Success)
        {
            plan = planResult.Plan;
        }
        else
        {
            foreach (var d in planResult.Diagnostics)
                Console.Error.WriteLine($"{d.Severity.ToString().ToUpperInvariant()} {d.Code} {d.Path}: {d.Message}");
        }

        var summary = ResourceJournalRecoveryService.Inspect(journalPath, plan);
        PrintRecoverySummary(project, installPath, summary);
        return summary.Status == ResourceExecutionJournalLoadStatus.Loaded && summary.Warnings.Count == 0 ? 0 : 1;
    }

    private static void PrintRecoverySummary(
        InstallProject project,
        string installPath,
        ResourceJournalRecoverySummary summary)
    {
        Console.WriteLine($"Product      : {project.AppName} {project.AppVersion}");
        Console.WriteLine($"Install path : {installPath}");
        Console.WriteLine($"Journal      : {summary.JournalPath}");
        Console.WriteLine($"Status       : {summary.Status}");
        Console.WriteLine($"Message      : {summary.Message}");

        if (summary.Metadata != null)
        {
            Console.WriteLine($"Attempt      : {summary.Metadata.AttemptId}");
            Console.WriteLine($"Plan hash    : {summary.Metadata.PlanHash}");
            Console.WriteLine($"Updated      : {summary.Metadata.UpdatedAt:u}");
        }

        if (summary.Status == ResourceExecutionJournalLoadStatus.Loaded)
        {
            Console.WriteLine($"Entries      : {summary.EntryCount}");
            Console.WriteLine($"Applied      : {summary.AppliedCount}");
            Console.WriteLine($"Rolled back  : {summary.RolledBackCount}");
            Console.WriteLine($"Failed       : {summary.FailedCount}");
            Console.WriteLine($"Pending undo : {summary.PendingRollbackCount}");
            foreach (var op in summary.PendingRollbackOperations.Take(20))
                Console.WriteLine($"  {op.Id} [{op.Type}] {op.DisplayName}");
            if (summary.PendingRollbackOperations.Count > 20)
                Console.WriteLine($"  … {summary.PendingRollbackOperations.Count - 20} more");
        }

        foreach (var warning in summary.Warnings)
            Console.WriteLine($"WARN         : {warning}");
    }

    // ── CLI extension discovery ─────────────────────────────────────────

    private static int RunExtensionExport(string projectPath, string[] args)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var format = ArgValue(args, "/FORMAT=");
        var extensionValue = ArgValue(args, "/EXTENSIONS=");
        var outputDirectory = ArgValue(args, "/OUT=")
            ?? Path.Combine(Environment.CurrentDirectory, "extension-exports", SafePathSegment(format));

        if (string.IsNullOrWhiteSpace(projectPath))
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4150", "ExtensionExport.Script", "/EXTENSIONEXPORT requires a .bsetup script path."));
        if (string.IsNullOrWhiteSpace(extensionValue))
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4151", "ExtensionExport.Extensions", "/EXTENSIONEXPORT requires /EXTENSIONS=<dir[;dir]> so exporter code is always explicit."));
        if (string.IsNullOrWhiteSpace(format))
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4152", "ExtensionExport.Format", "/EXTENSIONEXPORT requires /FORMAT=<exporter-format>."));

        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
        {
            EmitExtensionExportResult(new ExtensionPackageExportCliResult
            {
                ScriptPath = projectPath ?? "",
                Format = format ?? "",
                OutputDirectory = outputDirectory,
                Diagnostics = diagnostics
            }, args);
            return 2;
        }

        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4153", projectPath, err ?? "Script could not be loaded."));
            EmitExtensionExportResult(new ExtensionPackageExportCliResult
            {
                ScriptPath = projectPath,
                Format = format!,
                OutputDirectory = outputDirectory,
                Diagnostics = diagnostics
            }, args);
            return 1;
        }
        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);

        var policyLoad = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyLoad);
        diagnostics.AddRange(policyLoad.Diagnostics);
        if (policyLoad.HasErrors)
        {
            EmitExtensionExportResult(new ExtensionPackageExportCliResult
            {
                ScriptPath = projectPath,
                Format = format!,
                OutputDirectory = outputDirectory,
                Diagnostics = diagnostics
            }, args);
            return 1;
        }

        var directories = extensionValue!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions
        {
            EngineVersion = AppInfo.Version,
            RequireSignature = Has(args, "/REQUIRESIGNED"),
            Policy = policy
        });
        var discoveryResult = discovery.DiscoverExplicitDirectories(directories);
        diagnostics.AddRange(discoveryResult.Diagnostics);

        var matches = discoveryResult.Extensions
            .SelectMany(extension => extension.PackageExporters
                .Where(exporter => string.Equals(exporter.Format, format, StringComparison.OrdinalIgnoreCase))
                .Select(exporter => new { Extension = extension, Exporter = exporter }))
            .ToList();

        if (matches.Count == 0)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI4154",
                "ExtensionExport.Format",
                $"No loaded extension exporter implements format '{format}'."));
        }
        else if (matches.Count > 1)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI4155",
                "ExtensionExport.Format",
                $"Exporter format '{format}' is ambiguous across {matches.Count} loaded extensions."));
        }

        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
        {
            EmitExtensionExportResult(new ExtensionPackageExportCliResult
            {
                ScriptPath = projectPath,
                Format = format!,
                OutputDirectory = outputDirectory,
                Diagnostics = diagnostics
            }, args);
            return 1;
        }

        var match = matches[0];
        InstallerExtensionExportResult? exportResult = null;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            exportResult = match.Exporter.Export(project, outputDirectory);
            if (exportResult == null)
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI4156",
                    match.Exporter.GetType().FullName ?? match.Exporter.GetType().Name,
                    "Extension exporter returned no result."));
            }
            else
            {
                diagnostics.AddRange(exportResult.Diagnostics);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI4157",
                match.Exporter.GetType().FullName ?? match.Exporter.GetType().Name,
                $"Extension exporter failed: {ex.GetBaseException().Message}"));
        }

        var success = exportResult?.Success == true
            && !diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
        var result = new ExtensionPackageExportCliResult
        {
            Success = success,
            ScriptPath = Path.GetFullPath(projectPath),
            Format = format!,
            OutputDirectory = Path.GetFullPath(outputDirectory),
            ExtensionId = match.Extension.Manifest.Id,
            ExtensionPublisher = match.Extension.Manifest.Publisher,
            ExtensionVersion = match.Extension.Manifest.Version,
            ExporterType = match.Exporter.GetType().FullName ?? match.Exporter.GetType().Name,
            Artifacts = exportResult?.Artifacts ?? new List<string>(),
            Diagnostics = diagnostics
                .OrderBy(d => d.Severity)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        EmitExtensionExportResult(result, args);
        return success ? 0 : 1;
    }

    private static IReadOnlyList<string> ExtensionDirectoriesFromArgs(string[] args)
    {
        var value = ArgValue(args, "/EXTENSIONS=");
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static void EmitExtensionExportResult(ExtensionPackageExportCliResult result, string[] args)
    {
        if (Has(args, "/JSON"))
        {
            Console.Write(JsonSerializer.Serialize(result, ExtensionPackageExportJsonContext.Default.ExtensionPackageExportCliResult));
            Console.WriteLine();
            return;
        }

        if (result.Diagnostics.Count > 0)
            PrintDiagnostics("extension-export", result.Diagnostics);

        Console.WriteLine($"\nExtension export: {(result.Success ? "completed" : "failed")}");
        Console.WriteLine($"Format          : {result.Format}");
        Console.WriteLine($"Output directory: {Path.GetFullPath(result.OutputDirectory)}");
        if (!string.IsNullOrWhiteSpace(result.ExtensionId))
            Console.WriteLine($"Extension       : {result.ExtensionId} {result.ExtensionVersion}");
        if (!string.IsNullOrWhiteSpace(result.ExporterType))
            Console.WriteLine($"Exporter        : {result.ExporterType}");
        foreach (var artifact in result.Artifacts)
            Console.WriteLine($"  {artifact}");
    }

    private static int RunExtensionDiscovery(string value, string[] args)
    {
        var directories = value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (directories.Count == 0)
        {
            Console.Error.WriteLine("ERROR: /EXTENSIONS requires one or more explicit directories.");
            return 2;
        }

        var policyLoad = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyLoad);
        if (policyLoad.HasErrors)
        {
            if (Has(args, "/JSON") || !string.IsNullOrWhiteSpace(ArgValue(args, "/OUT=")))
            {
                var policyReport = new InstallerExtensionConformanceReport
                {
                    EngineVersion = AppInfo.Version,
                    Diagnostics = policyLoad.Diagnostics
                };
                var outputPath = ArgValue(args, "/OUT=");
                if (!string.IsNullOrWhiteSpace(outputPath))
                    InstallerExtensionConformanceReport.WriteJson(policyReport, outputPath);
                if (Has(args, "/JSON"))
                    Console.Write(InstallerExtensionConformanceReport.ToJson(policyReport));
                else
                    Console.WriteLine($"Extension discovery report: {Path.GetFullPath(outputPath!)}");
                return 1;
            }

            PrintDiagnostics("policy", policyLoad.Diagnostics);
            return 1;
        }

        var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions
        {
            EngineVersion = AppInfo.Version,
            RequireSignature = Has(args, "/REQUIRESIGNED"),
            Policy = policy
        });
        var result = discovery.DiscoverExplicitDirectories(directories);

        if (Has(args, "/JSON") || !string.IsNullOrWhiteSpace(ArgValue(args, "/OUT=")))
        {
            var report = InstallerExtensionConformanceReport.FromDiscovery(result, AppInfo.Version);
            var outputPath = ArgValue(args, "/OUT=");
            if (!string.IsNullOrWhiteSpace(outputPath))
                InstallerExtensionConformanceReport.WriteJson(report, outputPath);

            if (Has(args, "/JSON"))
                Console.Write(InstallerExtensionConformanceReport.ToJson(report));
            else
                Console.WriteLine($"Extension discovery report: {Path.GetFullPath(outputPath!)}");

            return report.HasErrors ? 1 : 0;
        }

        if (result.Diagnostics.Count > 0)
            PrintDiagnostics("extension", result.Diagnostics);

        Console.WriteLine($"\nExtensions: {result.Extensions.Count}");
        foreach (var extension in result.Extensions.OrderBy(e => e.Manifest.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {extension.Manifest.Id} {extension.Manifest.Version}");
            Console.WriteLine($"    Publisher : {extension.Manifest.Publisher}");
            Console.WriteLine($"    Assembly  : {extension.EntryAssemblyPath}");
            Console.WriteLine($"    Resources : {string.Join(", ", extension.Manifest.ResourceTypes.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"    Validators: {string.Join(", ", extension.Manifest.ValidatorTypes.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"    Exporters : {string.Join(", ", extension.Manifest.ExporterFormats.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"    Permissions: {extension.Manifest.Permissions}");
            Console.WriteLine($"    Providers : {string.Join(", ", extension.ResourceProviders.Select(p => p.GetType().FullName).OrderBy(p => p, StringComparer.Ordinal))}");
            Console.WriteLine($"    Validator types: {string.Join(", ", extension.ProjectValidators.Select(p => p.GetType().FullName).OrderBy(p => p, StringComparer.Ordinal))}");
            Console.WriteLine($"    Exporter types : {string.Join(", ", extension.PackageExporters.Select(p => p.GetType().FullName).OrderBy(p => p, StringComparer.Ordinal))}");
        }

        return result.HasErrors ? 1 : 0;
    }

    private static int RunExtensionTemplate(string value, string[] args)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine("ERROR: /EXTENSIONTEMPLATE requires an output directory.");
            return 2;
        }

        var kind = (ArgValue(args, "/EXTENSIONKIND=") ?? "provider").Trim().ToLowerInvariant();
        var options = new InstallerExtensionTemplateOptions
        {
            ExtensionId = ArgValue(args, "/EXTENSIONID=") ?? DefaultExtensionId(kind),
            Publisher = ArgValue(args, "/EXTENSIONPUBLISHER=") ?? "Example Publisher",
            PackageVersion = ArgValue(args, "/EXTENSIONVERSION=") ?? "1.0.0",
            EngineVersion = ArgValue(args, "/EXTENSIONENGINEVERSION=") ?? AppInfo.Version,
            ResourceType = ArgValue(args, "/EXTENSIONRESOURCETYPE=") ?? "example.resource",
            ValidatorType = ArgValue(args, "/EXTENSIONVALIDATORTYPE=") ?? "example.validator",
            ExporterFormat = ArgValue(args, "/EXTENSIONEXPORTERFORMAT=") ?? "example-format",
            ProjectName = ArgValue(args, "/EXTENSIONPROJECT=") ?? DefaultExtensionProject(kind)
        };

        InstallerExtensionTemplateExportResult result;
        switch (kind)
        {
            case "provider":
                result = InstallerExtensionTemplateExporter.ExportProviderTemplate(value, options);
                break;
            case "validator":
                result = InstallerExtensionTemplateExporter.ExportValidatorTemplate(value, options);
                break;
            case "exporter":
                result = InstallerExtensionTemplateExporter.ExportExporterTemplate(value, options);
                break;
            default:
                Console.Error.WriteLine("ERROR: /EXTENSIONKIND must be provider, validator or exporter.");
                return 2;
        }

        var outputPath = ArgValue(args, "/OUT=");
        if (!string.IsNullOrWhiteSpace(outputPath))
            InstallerExtensionTemplateExporter.WriteJson(result, outputPath);

        if (Has(args, "/JSON"))
        {
            Console.WriteLine(InstallerExtensionTemplateExporter.ToJson(result));
            return 0;
        }

        Console.WriteLine($"{DisplayExtensionTemplateKind(kind)} SDK template exported.");
        Console.WriteLine($"Directory: {result.OutputDirectory}");
        foreach (var file in result.Files)
            Console.WriteLine($"  {file}");
        if (!string.IsNullOrWhiteSpace(outputPath))
            Console.WriteLine($"Template JSON: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"Next: .\\pack-extension.ps1 from {result.OutputDirectory}, then /EXTENSIONCONFORMANCE=<package> /JSON");
        return 0;
    }

    private static string DefaultExtensionId(string kind) => kind switch
    {
        "validator" => "com.example.beep.validator",
        "exporter" => "com.example.beep.exporter",
        _ => "com.example.beep.provider"
    };

    private static string DefaultExtensionProject(string kind) => kind switch
    {
        "validator" => "Example.Beep.Validator",
        "exporter" => "Example.Beep.Exporter",
        _ => "Example.Beep.Provider"
    };

    private static string DisplayExtensionTemplateKind(string kind) => kind switch
    {
        "validator" => "Validator",
        "exporter" => "Package exporter",
        _ => "Provider"
    };

    private static int RunExtensionConformance(string value, string[] args)
    {
        var directories = value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (directories.Count == 0)
        {
            Console.Error.WriteLine("ERROR: /EXTENSIONCONFORMANCE requires one or more explicit directories.");
            return 2;
        }

        var policyLoad = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, policyLoad);
        if (policyLoad.HasErrors)
        {
            if (Has(args, "/JSON"))
            {
                var policyReport = new InstallerExtensionConformanceReport
                {
                    EngineVersion = AppInfo.Version,
                    Diagnostics = policyLoad.Diagnostics
                };
                Console.Write(InstallerExtensionConformanceReport.ToJson(policyReport));
            }
            else
            {
                PrintDiagnostics("policy", policyLoad.Diagnostics);
            }
            return 1;
        }

        var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions
        {
            EngineVersion = AppInfo.Version,
            RequireSignature = Has(args, "/REQUIRESIGNED"),
            Policy = policy
        });
        var result = discovery.DiscoverExplicitDirectories(directories);
        var report = InstallerExtensionConformanceReport.FromDiscovery(result, AppInfo.Version);

        var outputPath = ArgValue(args, "/OUT=");
        if (!string.IsNullOrWhiteSpace(outputPath))
            InstallerExtensionConformanceReport.WriteJson(report, outputPath);

        if (Has(args, "/JSON"))
        {
            Console.Write(InstallerExtensionConformanceReport.ToJson(report));
        }
        else
        {
            if (report.Diagnostics.Count > 0)
                PrintDiagnostics("extension-conformance", report.Diagnostics);

            Console.WriteLine($"\nExtension conformance: {(report.HasErrors ? "failed" : "passed")}");
            Console.WriteLine($"Extensions           : {report.ExtensionCount}");
            Console.WriteLine($"Providers            : {report.ProviderCount}");
            if (!string.IsNullOrWhiteSpace(outputPath))
                Console.WriteLine($"Report               : {Path.GetFullPath(outputPath)}");
            foreach (var extension in report.Extensions)
            {
                Console.WriteLine($"  {extension.Id} {extension.Version}");
                foreach (var provider in extension.Providers)
                    Console.WriteLine($"    {provider.ResourceType} -> {provider.ProviderType}");
            }
        }

        return report.HasErrors ? 1 : 0;
    }

    private static int RunQualifyExtensionSdkCompatibility(string value, string[] args)
    {
        try
        {
            var directories = value
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (directories.Count == 0)
            {
                Console.Error.WriteLine("ERROR: /QUALIFYEXTENSIONSDK requires one or more explicit extension package directories.");
                return 2;
            }

            var engineVersions = (ArgValue(args, "/SDKENGINEVERSIONS=") ?? AppInfo.Version)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var policyLoad = new InstallerPolicyEvaluation();
            var policy = LoadPolicyFromArgs(args, policyLoad);
            if (policyLoad.HasErrors)
            {
                PrintDiagnostics("policy", policyLoad.Diagnostics);
                return 1;
            }
            var report = new ExtensionSdkCompatibilityQualificationRunner().Run(new ExtensionSdkCompatibilityQualificationOptions
            {
                ExtensionDirectories = directories,
                EngineVersions = engineVersions,
                OutputDirectory = ArgValue(args, "/OUT=") ?? "",
                RequireSignature = Has(args, "/REQUIRESIGNED"),
                Policy = policy
            });

            if (Has(args, "/JSON"))
            {
                Console.Write(ExtensionSdkCompatibilityQualificationRunner.ToJson(report));
            }
            else
            {
                Console.WriteLine($"Extension SDK compatibility qualification: {report.ReportPath}");
                foreach (var scenario in report.Scenarios)
                    Console.WriteLine($"  {scenario.EngineVersion,-16}: {(scenario.Success ? "passed" : "failed")} ({scenario.ExtensionCount} extension(s), {scenario.ProviderCount} provider(s))");
                if (!report.Success)
                {
                    foreach (var diagnostic in report.Scenarios.SelectMany(s => s.Diagnostics).Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                        Console.Error.WriteLine($"ERROR {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
                }
            }

            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Extension SDK compatibility qualification failed: {ex.Message}");
            return 2;
        }
    }

    // ── CLI preview ─────────────────────────────────────────────────────

    private static int RunPreview(string projectPath)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine(err); return 2; }

        Console.WriteLine($"Script: {project.ProjectName}");
        Console.WriteLine($"  Product   : {project.AppName} {project.AppVersion}");
        Console.WriteLine($"  Publisher : {project.AppPublisher}");
        Console.WriteLine($"  Source    : {project.SourceDirectory}");
        Console.WriteLine($"  Default   : {project.DefaultDirName}");
        Console.WriteLine($"  Components: {project.Components.Count}");
        Console.WriteLine($"  Output    : {Path.Combine(project.OutputDir, project.OutputBaseFilename)}");
        return 0;
    }

    // ── ClickOnce publish ──────────────────────────────────────────────

    private static int RunPublish(string projectPath, string[] args)
    {
        Console.WriteLine("Beep Installer — ClickOnce publish");
        Console.WriteLine($"Script: {projectPath}");

        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"Error: {err}"); return 2; }

        // ArgValue slices by the prefix itself. Both of these used to count the characters by hand
        // and drop the leading '/', so the publish directory and update URL each arrived with a
        // stray '=' in front — invisible until /PUBLISH= itself started passing its path through.
        var publishDir = ArgValue(args, "/OUT=")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "", "publish");

        var updateUrl = ArgValue(args, "/UPDATEURL=") ?? project.AppUpdatesURL;

        var noSign = Has(args, "/NOSIGN");

        var progress = new Progress<(int percent, string message)>(p =>
            Console.WriteLine($"  [{p.percent,3}%] {p.message}"));

        var publisher = new Engine.Publisher { Progress = progress };
        var result = publisher.Publish(project, publishDir, updateUrl, sign: !noSign);

        Console.WriteLine();
        Console.WriteLine(result.Summary);
        foreach (var w in result.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in result.Errors) Console.Error.WriteLine($"  ERR : {e}");
        return result.Success ? 0 : 1;
    }

    // ── Logging helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Creates the install log for a headless run. <c>/LOG=&lt;path&gt;</c> chooses the file;
    /// otherwise InstallLogger's default (%TEMP%\Beep_Install_*.log) is used — but now
    /// announced instead of silently written.
    /// </summary>
    private static InstallLogger CreateRunLogger(string[] args)
    {
        var custom = args.FirstOrDefault(a => a.StartsWith("/LOG=", StringComparison.OrdinalIgnoreCase))?[5..];
        return new InstallLogger(string.IsNullOrWhiteSpace(custom) ? null : custom);
    }

    /// <summary>
    /// Synchronous IProgress: Progress&lt;T&gt; posts to a SynchronizationContext and a console
    /// host has none, so its callbacks land on the thread pool and can arrive after Run()
    /// returned — lines would be missing from the log.
    /// </summary>
    private sealed class SyncProgress : IProgress<TheTechIdea.Beep.Addin.PassedArgs>
    {
        private readonly Action<TheTechIdea.Beep.Addin.PassedArgs> _handler;
        public SyncProgress(Action<TheTechIdea.Beep.Addin.PassedArgs> handler) => _handler = handler;
        public void Report(TheTechIdea.Beep.Addin.PassedArgs value) => _handler(value);
    }

    /// <summary>Writes each step result from the run report into the log.</summary>
    private static void LogRunReport(InstallLogger logger, TheTechIdea.Beep.SetUp.ISetupWizard wizard)
    {
        try
        {
            var report = wizard.GetReport();
            foreach (var step in report.StepResults)
                logger.StepComplete(step.StepId ?? "?", step.Succeeded, step.Message);
        }
        catch (Exception ex) { logger.Warn("Log", $"Could not append step report: {ex.Message}"); }
    }

    /// <summary>
    /// Records the log location in the ARP entry so support can find it later. Best-effort.
    /// </summary>
    private static void RecordLogInArp(InstallProject project, bool perUser, string logPath)
    {
        if (!project.CreateUninstallEntry) return;
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                TheTechIdea.Beep.Installer.InstallScope.HiveFor(perUser),
                TheTechIdea.Beep.Installer.InstallScope.ViewFor(project.Prefer64Bit));
            using var key = baseKey.OpenSubKey(
                InstallationRegistration.UninstallKeyPath(project.AppId), writable: true);
            key?.SetValue("LogFile", logPath);
        }
        catch (Exception ex) { Engine.Diag.Debug("Program", "ARP LogFile write skipped", ex); }
    }

    private static void WriteRunResultJson(
        string action,
        InstallProject project,
        string installPath,
        bool success,
        int exitCode,
        string message,
        string logPath,
        SetupContext context,
        string supportBundlePath = "",
        string correlationId = "")
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["correlationId"] = correlationId ?? "",
            ["productName"] = project.AppName,
            ["productVersion"] = project.AppVersion,
            ["installPath"] = installPath,
            ["success"] = success,
            ["exitCode"] = exitCode,
            ["rebootRequired"] = context.Properties.TryGetValue("RebootRequired", out var reboot) && reboot is true,
            ["message"] = message ?? "",
            ["logPath"] = logPath ?? "",
            ["journalPath"] = context.TryGetProperty<string>(InstallContextKeys.ResourceExecutionJournalPath) ?? "",
            ["supportBundlePath"] = supportBundlePath ?? ""
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
    }

    private static string TryWriteSupportBundle(
        string action,
        InstallProject project,
        string installPath,
        bool success,
        int exitCode,
        string message,
        string logPath,
        SetupContext context,
        InstallerPolicyEvaluation? policyEvaluation,
        string[] args,
        string correlationId)
    {
        if (!WantsSupportBundle(args))
            return "";

        try
        {
            var result = new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
            {
                Action = action,
                Project = project,
                InstallPath = installPath,
                Success = success,
                ExitCode = exitCode,
                Message = message ?? "",
                LogPath = logPath ?? "",
                Context = context,
                PolicyEvaluation = policyEvaluation,
                OutputPath = ArgValue(args, "/SUPPORTBUNDLE="),
                CorrelationId = correlationId,
                PreviewOnly = Has(args, "/SUPPORTBUNDLEPREVIEW"),
                ConsentGranted = !Has(args, "/SUPPORTBUNDLEPREVIEW"),
                RetentionDays = SupportBundleRetentionDays(args)
            });
            Console.WriteLine($"Support bundle: {result.Path}");
            return result.Path;
        }
        catch (Exception ex)
        {
            Engine.Diag.Warn("SupportBundle", "support bundle generation failed", ex);
            Console.WriteLine($"Support bundle failed: {ex.Message}");
            return "";
        }
    }

    private static bool WantsSupportBundle(string[] args)
        => Has(args, "/SUPPORTBUNDLE", "/SUPPORTBUNDLEPREVIEW")
           || IndexOf(args, "/SUPPORTBUNDLE=") >= 0
           || IndexOf(args, "/SUPPORTBUNDLERETENTIONDAYS=") >= 0;

    private static void EmitRuntimeTelemetry(
        string action,
        InstallProject project,
        string installPath,
        bool success,
        int exitCode,
        string message,
        string logPath,
        SetupContext context,
        InstallerPolicyEvaluation? policyEvaluation,
        string[] args,
        string correlationId)
    {
        try
        {
            var result = new RuntimeTelemetrySink().Emit(new RuntimeTelemetryOptions
            {
                Action = action,
                Project = project,
                InstallPath = installPath,
                Success = success,
                ExitCode = exitCode,
                Message = message ?? "",
                LogPath = logPath ?? "",
                Context = context,
                PolicyEvaluation = policyEvaluation,
                CorrelationId = correlationId,
                OutputPath = ArgValue(args, "/TELEMETRYOUT=") ?? ""
            });
            if (result.Emitted)
                Engine.Diag.Info("Telemetry", $"Runtime telemetry emitted in {result.Mode} mode. local={result.LocalWritten} remote={result.RemoteSent}", "BI2470");
        }
        catch (Exception ex)
        {
            Engine.Diag.Warn("Telemetry", "runtime telemetry emission failed", ex, "BI2471");
        }
    }

    private static int SupportBundleRetentionDays(string[] args)
    {
        var raw = ArgValue(args, "/SUPPORTBUNDLERETENTIONDAYS=");
        if (string.IsNullOrWhiteSpace(raw))
            return RuntimeSupportBundleOptions.DefaultRetentionDays;

        if (!int.TryParse(raw, out var days))
            return RuntimeSupportBundleOptions.DefaultRetentionDays;

        if (days < 0)
            return RuntimeSupportBundleOptions.DefaultRetentionDays;
        if (days > RuntimeSupportBundleOptions.MaximumRetentionDays)
            return RuntimeSupportBundleOptions.MaximumRetentionDays;
        return days;
    }

    private static string NewRunCorrelationId(string action, InstallProject project)
        => $"bi-{SafePathSegment(action)}-{SafePathSegment(project.AppName)}-{Guid.NewGuid():N}";

    // ── Arg helpers ─────────────────────────────────────────────────────

    private static bool Has(string[] args, params string[] flags)
        => args.Any(a => flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

    private static int IndexOf(string[] args, string prefix)
    {
        for (int i = 0; i < args.Length; i++)
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string? ArgValue(string[] args, string prefix)
    {
        var index = -1;
        for (int i = 0; i < args.Length; i++)
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) index = i;
        return index >= 0 ? args[index][prefix.Length..] : null;
    }

    private static int? IntArg(string[] args, string prefix)
        => ArgValue(args, prefix) is { } value && int.TryParse(value, out var number)
            ? number
            : null;

    private static Dictionary<string, string> PropertyValues(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("/PROPERTY:", StringComparison.OrdinalIgnoreCase))
                continue;

            var body = arg["/PROPERTY:".Length..];
            var split = body.IndexOf('=');
            if (split <= 0)
                continue;

            var name = body[..split].Trim();
            if (name.Length == 0)
                continue;

            values[name] = body[(split + 1)..];
        }

        return values;
    }

    private static void AddPathRuntimeProperty(
        IDictionary<string, string> properties,
        string[] args,
        string argumentPrefix,
        string propertyName)
    {
        var value = ArgValue(args, argumentPrefix);
        if (!string.IsNullOrWhiteSpace(value))
            properties[propertyName] = Path.GetFullPath(value);
    }

    private static bool VerifyOfflineLayoutPreflight(
        string operation,
        InstallProject project,
        string installPath,
        string[] args,
        SetupContext context,
        InstallerPolicyEvaluation policyEvaluation,
        string logFilePath,
        bool jsonMode,
        TextWriter originalOut,
        string runCorrelationId)
    {
        if (!context.Properties.TryGetValue(InstallContextKeys.RuntimeVariables, out var variablesValue)
            || variablesValue is not IReadOnlyDictionary<string, string> variables
            || !variables.TryGetValue("OfflineLayoutDirectory", out var layoutDirectory)
            || string.IsNullOrWhiteSpace(layoutDirectory))
            return true;

        var verification = new OfflineLayoutBuilder().Verify(layoutDirectory, new OfflineLayoutVerificationOptions
        {
            RequireSignature = ShouldRequireOfflineLayoutSignature(args, policyEvaluation.Policy),
            TrustedPublicKeyPath = ResolveOfflineLayoutTrustedPublicKeyPath(args, policyEvaluation.Policy),
            TrustedPublicKey = ResolveOfflineLayoutTrustedPublicKey(args, policyEvaluation.Policy)
        });
        if (verification.Diagnostics.Count > 0)
            PrintDiagnostics("offline-layout", verification.Diagnostics);
        if (verification.Success)
            return true;

        var message = $"{operation} blocked because offline layout preflight failed.";
        Engine.Diag.Warn("OfflineLayout", message, eventId: "BI2420");
        var bundlePath = TryWriteSupportBundle(operation, project, installPath, false, 2, message, logFilePath, context, policyEvaluation, args, runCorrelationId);
        if (jsonMode)
        {
            Console.SetOut(originalOut);
            WriteRunResultJson(operation, project, installPath, false, 2, message, logFilePath, context, bundlePath, runCorrelationId);
        }
        return false;
    }

    private static void ApplyBuiltInRuntimeProperties(SetupContext context, IReadOnlyDictionary<string, string> properties)
    {
        if (TryRuntimeBool(properties, "CreateDesktopIcon", out var createDesktopIcon))
            context.Properties["CreateDesktopIcon"] = createDesktopIcon;
        if (TryRuntimeBool(properties, "CreateStartMenu", out var createStartMenu))
            context.Properties["CreateStartMenu"] = createStartMenu;
        if (TryRuntimeBool(properties, "AutoStart", out var autoStart))
            context.Properties["AutoStart"] = autoStart;
        if (TryRuntimeBool(properties, "FileAssociations", out var fileAssociations))
            context.Properties["FileAssociations"] = fileAssociations;
        if (TryRuntimeBool(properties, "AcceptLicense", out var acceptLicense))
            context.Properties["AcceptLicense"] = acceptLicense;
        if (properties.TryGetValue("StartMenuFolder", out var startMenuFolder) && !string.IsNullOrWhiteSpace(startMenuFolder))
            context.Properties["StartMenuFolder"] = startMenuFolder;
        if (properties.TryGetValue("InstallType", out var installType) && !string.IsNullOrWhiteSpace(installType))
            context.Properties["InstallType"] = installType;
    }

    private static bool TryRuntimeBool(IReadOnlyDictionary<string, string> properties, string name, out bool value)
    {
        value = false;
        if (!properties.TryGetValue(name, out var raw))
            return false;

        if (bool.TryParse(raw, out value))
            return true;
        if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw.Equals("y", StringComparison.OrdinalIgnoreCase) || raw.Equals("1", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }
        if (raw.Equals("no", StringComparison.OrdinalIgnoreCase) || raw.Equals("n", StringComparison.OrdinalIgnoreCase) || raw.Equals("0", StringComparison.Ordinal))
        {
            value = false;
            return true;
        }
        return false;
    }

    private static bool IsMatrixQualificationCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], "qualify", StringComparison.OrdinalIgnoreCase);

    private static void ApplySigningOverrides(InstallProject project, string[] args)
    {
        var certificate = ArgValue(args, "/SIGNCERT=");
        if (!string.IsNullOrWhiteSpace(certificate))
            project.CodeSignCertificatePath = certificate;

        var password = ArgValue(args, "/SIGNPASSWORD=");
        if (!string.IsNullOrWhiteSpace(password))
            project.CodeSignCertificatePassword = password;

        var storeName = ArgValue(args, "/SIGNSTORE=");
        if (!string.IsNullOrWhiteSpace(storeName))
            project.CodeSignStoreName = storeName;

        var storeLocation = ArgValue(args, "/SIGNSTORELOCATION=");
        if (!string.IsNullOrWhiteSpace(storeLocation))
            project.CodeSignStoreLocation = storeLocation;

        var storeThumbprint = ArgValue(args, "/SIGNTHUMBPRINT=");
        if (!string.IsNullOrWhiteSpace(storeThumbprint))
            project.CodeSignStoreThumbprint = storeThumbprint;

        var storeSubject = ArgValue(args, "/SIGNSUBJECT=");
        if (!string.IsNullOrWhiteSpace(storeSubject))
            project.CodeSignStoreSubject = storeSubject;

        var timestamp = ArgValue(args, "/TIMESTAMP=");
        if (!string.IsNullOrWhiteSpace(timestamp))
            project.CodeSignTimestampUrl = timestamp;

        var remoteProvider = ArgValue(args, "/SIGNREMOTEPROVIDER=");
        if (!string.IsNullOrWhiteSpace(remoteProvider))
            project.CodeSignRemoteProvider = remoteProvider;

        var remoteEndpoint = ArgValue(args, "/SIGNREMOTEENDPOINT=");
        if (!string.IsNullOrWhiteSpace(remoteEndpoint))
            project.CodeSignRemoteEndpoint = remoteEndpoint;

        var remoteKey = ArgValue(args, "/SIGNREMOTEKEY=");
        if (!string.IsNullOrWhiteSpace(remoteKey))
            project.CodeSignRemoteKeyId = remoteKey;

        var remoteCredential = ArgValue(args, "/SIGNREMOTECREDENTIAL=");
        if (!string.IsNullOrWhiteSpace(remoteCredential))
            project.CodeSignRemoteCredential = remoteCredential;
    }

    private static string ResolveTimestampOutagePolicy(string[] args, InstallerPolicy? policy)
        => ArgValue(args, "/TIMESTAMPOUTAGE=")
           ?? policy?.TimestampOutagePolicy
           ?? "fail";

    private static int ResolveTimestampRetryCount(string[] args)
        => IntArg(args, "/TIMESTAMPRETRIES=") is int retries && retries >= 0 ? retries : 2;

    private static InstallerPolicy? LoadPolicyFromArgs(string[] args, InstallerPolicyEvaluation evaluation)
    {
        var resolution = InstallerPolicyResolver.Resolve(new InstallerPolicyResolutionOptions
        {
            ProjectPolicyPath = ArgValue(args, "/PROJECTPOLICY=") ?? "",
            ProfilePolicyPath = ArgValue(args, "/PROFILEPOLICY=") ?? ArgValue(args, "/POLICY=") ?? "",
            MachinePolicyPath = ArgValue(args, "/MACHINEPOLICY=") ?? ""
        });
        evaluation.Diagnostics.AddRange(resolution.Diagnostics);
        if (resolution.Policy is null)
            return null;

        evaluation.Sources.AddRange(resolution.Sources);
        evaluation.EffectivePolicySha256 = InstallerPolicyEvaluator.CreateEvidence(new InstallerPolicyEvaluation { Policy = resolution.Policy }).EffectivePolicySha256;
        return resolution.Policy;
    }

    private static InstallerPolicyEvaluation EvaluateProjectPolicy(InstallProject project, string[] args)
    {
        var evaluation = new InstallerPolicyEvaluation();
        var policy = LoadPolicyFromArgs(args, evaluation);
        if (policy == null)
            return evaluation;

        evaluation.Diagnostics.AddRange(InstallerPolicyEvaluator.EvaluateProject(policy, project).Diagnostics);
        evaluation.Policy = policy;
        return evaluation;
    }

    private static bool ShouldGenerateReleaseEvidence(string[] args, InstallerPolicy? policy)
        => Has(args, "/EVIDENCE", "/SBOM", "/PROVENANCE")
           || IndexOf(args, "/EVIDENCE=") >= 0
           || policy?.RequireSbom == true
           || policy?.RequireProvenance == true;

    private static IReadOnlyList<string> ResolveMsiCustomActionFamilies(string[] args, InstallerPolicy? policy)
    {
        var cliValue = ArgValue(args, "/MSICUSTOMACTIONS=");
        if (!string.IsNullOrWhiteSpace(cliValue))
            return SplitList(cliValue);

        return policy?.AllowedMsiCustomActionFamilies is { } families
            ? families
            : Array.Empty<string>();
    }

    private static string[] SplitList(string value)
        => value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ShouldRequireOfflineLayoutSignature(string[] args, InstallerPolicy? policy)
        => !Has(args, "/ALLOWUNSIGNEDLAYOUT") && policy?.AllowUnsignedOfflineLayouts != true;

    private static string ResolveOfflineLayoutTrustedPublicKeyPath(string[] args, InstallerPolicy? policy)
        => ArgValue(args, "/LAYOUTTRUSTKEY=")
           ?? policy?.OfflineLayoutTrustedPublicKeyPath
           ?? "";

    private static string ResolveOfflineLayoutTrustedPublicKey(string[] args, InstallerPolicy? policy)
        => string.IsNullOrWhiteSpace(ArgValue(args, "/LAYOUTTRUSTKEY="))
            ? policy?.OfflineLayoutTrustedPublicKey ?? ""
            : "";

    private static bool RunSupplyChainGate(
        InstallProject project,
        string[] args,
        InstallerPolicy? policy,
        string label,
        out SupplyChainSecurityReport? report)
    {
        report = null;
        if (policy?.RequireSupplyChainScan != true && IndexOf(args, "/SECURITYREPORT=") < 0)
            return true;

        var reportPath = ArgValue(args, "/SECURITYREPORT=")
            ?? Path.Combine(Path.GetFullPath(ArgValue(args, "/OUT=") ?? BuildPipeline.ResolveOutputDirectory(project)), "security", "supply-chain-report.json");
        report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            InstallerPath = ArgValue(args, "/INSTALLER="),
            OutputPath = reportPath
        });

        PrintSupplyChainReport(report, reportPath, label);
        return !report.HasErrors;
    }

    private static void PrintSupplyChainReport(SupplyChainSecurityReport report, string reportPath, string label = "supply-chain")
    {
        Console.WriteLine();
        Console.WriteLine($"Supply-chain report: {Path.GetFullPath(reportPath)}");
        Console.WriteLine($"Plan hash          : {report.PlanHash}");
        Console.WriteLine($"Artifacts          : {report.Artifacts.Count}");
        if (report.Findings.Count > 0)
            PrintDiagnostics(label, report.Findings.Select(f => new ProjectSchemaDiagnostic(
                f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
                    ? ProjectSchemaDiagnosticSeverity.Error
                    : ProjectSchemaDiagnosticSeverity.Warning,
                f.Code,
                f.Path,
                f.Message)));
        else
            Console.WriteLine("Supply-chain checks passed.");
    }

    private static void WriteReleaseEvidence(
        InstallProject project,
        string? installerPath,
        string? outputDirectory,
        string[] args,
        InstallerPolicyEvaluation? policyEvaluation = null,
        SupplyChainSecurityReport? supplyChainReport = null)
    {
        var evidenceDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(BuildPipeline.ResolveOutputDirectory(project), "evidence")
            : Path.Combine(Path.GetFullPath(outputDirectory!), "evidence");

        var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = evidenceDir,
            InstallerPath = installerPath,
            SourceRoot = ArgValue(args, "/SOURCEROOT="),
            SourceRevision = ArgValue(args, "/SOURCEREVISION="),
            BuildType = ArgValue(args, "/BUILDTYPE=") ?? "https://the-tech-idea.example/beep-installer/build/v1",
            AttestationPrivateKeyPath = ArgValue(args, "/ATTESTKEY="),
            AttestationKeyId = ArgValue(args, "/ATTESTKEYID="),
            PolicyEvaluation = policyEvaluation,
            SupplyChainReport = supplyChainReport
        });

        Console.WriteLine();
        Console.WriteLine($"Release evidence: {result.OutputDirectory}");
        Console.WriteLine($"Plan hash       : {result.PlanHash}");
        Console.WriteLine($"SBOM            : {result.SbomPath}");
        Console.WriteLine($"Provenance      : {result.ProvenancePath}");
        if (!string.IsNullOrWhiteSpace(result.SbomAttestationPath))
            Console.WriteLine($"SBOM DSSE       : {result.SbomAttestationPath}");
        if (!string.IsNullOrWhiteSpace(result.ProvenanceAttestationPath))
            Console.WriteLine($"Provenance DSSE : {result.ProvenanceAttestationPath}");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"WARN            : {warning}");
    }

    private static bool EnforceProjectPolicy(
        InstallProject project,
        string[] args,
        string label,
        out InstallerPolicyEvaluation evaluation)
    {
        evaluation = EvaluateProjectPolicy(project, args);
        if (evaluation.Diagnostics.Count > 0)
            PrintDiagnostics(label, evaluation.Diagnostics);
        return !evaluation.HasErrors;
    }

    private static string SafePathSegment(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? "App")
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "App" : result;
    }

    private static bool IsDryRun(string[] args)
        => Has(args, "--dry-run")
           || Has(args, "/DRYRUN")
           || string.Equals(ArgValue(args, "/DRYRUN="), "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(ArgValue(args, "/DRYRUN="), "yes", StringComparison.OrdinalIgnoreCase);

    private static void PrintDiagnostics(string label, IEnumerable<ProjectSchemaDiagnostic> diagnostics)
    {
        var items = diagnostics.ToList();
        Console.WriteLine($"\n{items.Count} {label} diagnostic(s):");
        foreach (var d in items)
            Console.WriteLine($"  {d.Severity.ToString().ToUpperInvariant(),-7} {d.Code} {d.Path}: {d.Message}");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Console.Error.WriteLine($"Unhandled: {ex}");
        try { MessageBox.Show($"Unexpected error: {ex?.Message}", "Beep Installer",
            MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch (Exception mbx) { Engine.Diag.Debug("Program", "unhandled-exception MessageBox failed", mbx); }
    }

    private static void ShowFatalMessage(string message)
{
    Console.Error.WriteLine(message);
    // Write a crash log so the user can inspect even after the dialog disappears.
    var crashLog = Path.Combine(Path.GetTempPath(), $"Beep_Crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
    try { File.WriteAllText(crashLog, $"Beep Installer — Runtime Error{Environment.NewLine}Time: {DateTime.UtcNow:u}{Environment.NewLine}{message}{Environment.NewLine}"); }
    catch (Exception logEx)
    {
        Engine.Diag.Debug("Program", "runtime-error crash log write failed", logEx);
        Console.Error.WriteLine($"Warning: crash log write failed ({logEx.GetType().Name}: {logEx.Message})");
    }
    // Try to show a dialog (may fail if WinForms init already blew up).
    try { MessageBox.Show(message + $"{Environment.NewLine}{Environment.NewLine}Log written to:{Environment.NewLine}{crashLog}", "Beep Installer — Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    catch (Exception mbx) { Engine.Diag.Debug("Program", "ShowFatalMessage dialog failed", mbx); }
}

    private static void PrintUsage()
    {
        Console.WriteLine(AppInfo.ProductName + " v" + AppInfo.Version);
        Console.WriteLine();
        Console.WriteLine("Usage:");

        Console.WriteLine();
        Console.WriteLine("Builder, validation and planning:");
        Console.WriteLine("  Beep.Installer.exe                         Open the Package Builder (generator UI)");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup>   Build the installer (headless)");
        Console.WriteLine("  Beep.Installer.exe /VALIDATE=<script.bsetup> [/JSON] [/OUT=<json>] Validate a script without building");
        Console.WriteLine("  Beep.Installer.exe /VALIDATE=<script.bsetup> /STRICT Fail strict warnings such as literal secrets");
        Console.WriteLine("  Beep.Installer.exe /CANONICALIZE=<script.bsetup> [/WRITE] Canonicalize a script");
        Console.WriteLine("  Beep.Installer.exe /PLAN=<script.bsetup> [/JSON] Compile and print the install plan");
        Console.WriteLine("  Beep.Installer.exe /FORMATREADINESS=<script.bsetup> [/JSON] [/OUT=<json>] Report EXE/MSI/MSIX/WinGet/management-kit release readiness");
        Console.WriteLine("  Beep.Installer.exe /LISTTEMPLATES [/JSON] [/OUT=<json>] List built-in project-template IDs for reusable package export");
        Console.WriteLine("  Beep.Installer.exe /EXPORTTEMPLATEPACKAGE=<template-id> /TEMPLATESIGNKEY=<pem> [/OUT=<dir>] [/JSON] Export a signed reusable project-template package");
        Console.WriteLine("  Beep.Installer.exe /VERIFYTEMPLATEPACKAGE=<dir> /TEMPLATETRUSTKEY=<pem> [/JSON] Verify a signed project-template package");

        Console.WriteLine();
        Console.WriteLine("SDK, extensions and unattended properties:");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYSDK=<script.bsetup> [/SDKPROJECT=<csproj>] [/SDKPACKAGEVERSION=<v>] [/OUT=<dir>] Pack and prove the headless SDK through an external PackageReference consumer");
        Console.WriteLine("  Beep.Installer.exe /PUBLISHSDK=<package.nupkg> /SDKFEED=<source> [/SDKAPIKEY=secret://env/NAME] [/DRYRUN] Publish the SDK package with secret-safe evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYPLAN=<script.bsetup> [/OUT=<dir>] Gate compiled-plan determinism, provider coverage, dependency integrity and redacted JSON");
        Console.WriteLine("  Beep.Installer.exe /PROPERTIES=<script.bsetup> [/JSON] Print unattended response-file and /PROPERTY names");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYCLI=<script.bsetup> [/OUT=<dir>] Gate enterprise CLI aliases, response files, property catalog and fail-closed argument behavior");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYCONFIG=<script.bsetup> [/OUT=<dir>] Gate JSON/XML/INI config transforms, rollback, conflict diagnostics and secret boundaries");
        Console.WriteLine("  Beep.Installer.exe /EXTENSIONS=<dir[;dir]> [/JSON] [/OUT=<json>] Validate explicit extension directories");
        Console.WriteLine("  Beep.Installer.exe /EXTENSIONEXPORT=<script.bsetup> /EXTENSIONS=<dir[;dir]> /FORMAT=<format> [/OUT=<dir>] [/JSON] Invoke a discovered extension package exporter");
        Console.WriteLine("  Beep.Installer.exe /EXTENSIONTEMPLATE=<dir> [/EXTENSIONKIND=provider|validator|exporter] [/JSON] [/OUT=<json>] Export provider/validator/exporter SDK template");
        Console.WriteLine("  Beep.Installer.exe /EXTENSIONCONFORMANCE=<dir[;dir]> [/JSON] [/OUT=<json>] Emit provider SDK conformance report");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYEXTENSIONSDK=<dir[;dir]> [/SDKENGINEVERSIONS=<v[;v]>] [/JSON] [/OUT=<dir>] Prove extension SDK compatibility across engine versions");

        Console.WriteLine();
        Console.WriteLine("Catalogs, updates and offline layouts:");
        Console.WriteLine("  Beep.Installer.exe /EXPORTCATALOG=builtin:microsoft-runtimes /CATALOGSIGNKEY=<pem> [/OUT=<dir>] Export signed prerequisite catalog approval bundle");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYCATALOG=<script.bsetup> [/QUALIFYCATALOGLAYOUT=<dir>] [/LAYOUTSIGNKEY=<pem> /LAYOUTTRUSTKEY=<pem>] [/DOWNLOAD] [/OUT=<dir>] Prove catalog resolution, package projection and air-gapped offline layout verification");
        Console.WriteLine("  Beep.Installer.exe /UPDATECHANNELFEED=<script.bsetup> /UPDATECHANNELFEEDSIGNKEY=<pem> [/OUT=<dir>] Export signed update channel feed metadata");
        Console.WriteLine("  Beep.Installer.exe /VERIFYUPDATECHANNELFEED=<beep-update-channels.json> /UPDATECHANNELFEEDTRUSTKEY=<pem> Verify signed update channel feed metadata");
        Console.WriteLine("  Beep.Installer.exe /CHECKUPDATECHANNEL=<feed.json> /UPDATECHANNELFEEDTRUSTKEY=<pem> /UPDATECHANNELINSTALLEDVERSION=<version> [/UPDATECHANNELCURRENT=<id>] [/UPDATECHANNELTARGET=<id>] [/UPDATECHANNELCOHORT=<seed>] [/JSON] Check device update eligibility without installing");
        Console.WriteLine("  Beep.Installer.exe /APPLYUPDATECHANNEL=<feed.json> [/DELTA=<package-dir>] /DELTACURRENT=<install-dir> /DELTASTAGE=<stage-dir> /UPDATECHANNELINSTALLEDVERSION=<version> /UPDATECHANNELFEEDTRUSTKEY=<pem> /DELTATRUSTKEY=<pem> [/JSON] [/DRYRUN] Apply a trusted channel delta; omit /DELTA for signed remote acquisition; dry-run previews eligibility only");
        Console.WriteLine("  Feed export: /UPDATECHANNELDELTABASEURL=<https://host/release/> signs the remote delta location when attaching /DELTA=<package-dir>");
        Console.WriteLine("  Channel check/apply also accept an HTTP(S) URL ending in /beep-update-channels.json; signed metadata is downloaded even for eligibility previews, but delta payloads are not.");
        Console.WriteLine("  Update transport: /UPDATEAUTHORIGIN=<https://host> /UPDATEBEARERREF=<env:NAME> [/UPDATEPROXY=<uri>] [/UPDATEPROXYUSER=<user> /UPDATEPROXYPASSWORDREF=<env:NAME>]; authenticated proxies require HTTPS.");
        Console.WriteLine("  Channel identity: /UPDATEAPPID=<persisted-guid> /UPDATEAPPNAME=<expected-name> /UPDATEPUBLISHER=<expected-publisher> from trusted deployment configuration, not values read from the incoming feed.");
        Console.WriteLine("  /UPDATECACHE=<directory> selects the persistent verified delta cache; default is the current user's local application data. Later runs resume matching partial blobs.");
        Console.WriteLine("  /UPDATESTATE=<directory> selects persistent product publication checkpoints. Checks and previews record accepted metadata to reject replay; keep this state separate from disposable caches.");
        Console.WriteLine("  /UPDATECACHEMAXBYTES=<positive bytes> and /UPDATECACHERETENTIONDAYS=<positive days> control cache reservations and retention (defaults: 4 GiB, 30 days).");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYUPDATECHANNELFEED=<beep-update-channels.json> /UPDATECHANNELFEEDTRUSTKEY=<pem> [/UPDATECHANNELCURRENT=<id>] [/UPDATECHANNELTARGET=<id>] [/UPDATECHANNELINSTALLEDVERSION=<version>] [/UPDATECHANNELCOHORT=<seed>] [/OUT=<dir>] Generate update-channel qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYUPDATECHANNELFEED=<feed.json> /UPDATECHANNELLIFECYCLE /UPDATECHANNELSCRIPT=<current.bsetup> [/UPDATECHANNELUPDATEDSCRIPT=<updated.bsetup>] [/UPDATECHANNELDOWNGRADESCRIPT=<older.bsetup>] [/UPDATECHANNELINSTALLDIR=<dir>] [/UPDATECHANNELINSTALLER=<exe>] Add install/update/downgrade/uninstall lifecycle evidence");
        Console.WriteLine("  Beep.Installer.exe /DELTA=<outdir> /DELTABASE=<dir> /DELTATARGET=<dir> [/DELTABASEVERSION=<v> /DELTATARGETVERSION=<v>] [/DELTASIGNKEY=<pem>] Build a signed content-addressed delta package");
        Console.WriteLine("  Installed-image authoring: add /SCRIPT=<target-project> or /UPDATEAPPID=<guid> /UPDATEAPPNAME=<name> /UPDATEPUBLISHER=<publisher> to validate both image journals before writing. /SCRIPT supplies the default target version.");
        Console.WriteLine("  Beep.Installer.exe /VERIFYDELTA=<dir> [/DELTATRUSTKEY=<pem> /REQUIRESIGNED] Verify a delta package manifest and signature");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYDELTA=<dir> /DELTACURRENT=<dir> [/OUT=<dir>] [/DELTATRUSTKEY=<pem> /REQUIRESIGNED] Generate valid/corrupt/interrupted delta lifecycle evidence");
        Console.WriteLine("  Beep.Installer.exe /APPLYDELTA=<dir> /DELTACURRENT=<dir> /DELTASTAGE=<dir> [/DELTAJOURNAL=<json>] [/DELTACURRENTVERSION=<v>] [/DELTATRUSTKEY=<pem> /REQUIRESIGNED] Verify, stage, atomically apply, and journal a delta");
        Console.WriteLine("  Beep.Installer.exe /ROLLBACKDELTA=<journal.json> Roll back the last committed delta using its journal");
        Console.WriteLine("  Beep.Installer.exe /RECOVERDELTA=<journal.json> [/DRYRUN] [/JSON] Inspect and recover an interrupted delta commit using verified tree state");
        Console.WriteLine("  Beep.Installer.exe /LAYOUT=<script.bsetup> [/OUT=<dir>] [/DOWNLOAD] [/LAYOUTSIGNKEY=<pem>] Build offline package layout");
        Console.WriteLine("  Beep.Installer.exe /LAYOUT=<script.bsetup> /DOWNLOAD [/LAYOUTCACHE=<dir>] [/LAYOUTCACHERETENTIONDAYS=<days>] [/LAYOUTPROXY=<uri>] [/LAYOUTBEARERTOKEN=secret://env/NAME] Download with cache/proxy/auth/resume");
        Console.WriteLine("  Beep.Installer.exe /VERIFYLAYOUT=<dir> /LAYOUTTRUSTKEY=<pem> Verify offline layout inventory and blobs");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYLAYOUT=<dir> /SCRIPT=<script.bsetup> [/OUT=<dir>] [/D=<path>] [/DRYRUN] [/REQUIREMIXEDPACKAGES] Prove valid/missing/tampered offline layout, suite inventory and install/repair/uninstall evidence");

        Console.WriteLine();
        Console.WriteLine("Qualification and release gates:");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYA11Y=<source-root> [/OUT=<dir>] [/A11YEVIDENCE=<dir> /REQUIREA11YEVIDENCE] Generate localization, RTL, DPI, keyboard, screen-reader and locale-invariant CLI evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYVM=<evidence-root> [/QUALIFYVMTARGETS=id|os|arch|channel;...] [/QUALIFYVMSCENARIOS=id,id] [/MAXEVIDENCEAGEDAYS=30] [/MAXFLAKYFAILURES=0] [/MAXDURATIONSECONDS=n] [/REQUIRENEGATIVESECURITYEVIDENCE] Gate release readiness from VM matrix evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYRELEASE=<evidence-root> [/REQUIREDQUALIFICATIONS=id,id|core|enterprise|release] [/OUT=<dir>] Aggregate existing qualification reports into one release portfolio gate");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYSIGNING [/OUT=<dir>] Generate signing and secret-provider qualification evidence without real certificates or HSM credentials");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYUPGRADE=<script.bsetup> [/OUT=<dir>] Generate upgrade/downgrade/failed-restore qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYRECOVERY=<script.bsetup> [/OUT=<dir>] Generate resource journal resume/recovery qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYSERVICES=<script.bsetup> [/OUT=<dir>] Generate Windows service provider qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYIIS=<script.bsetup> [/OUT=<dir>] Generate IIS/web deployment provider qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYSYSTEM=<script.bsetup> [/OUT=<dir>] Generate firewall, scheduled-task, association, certificate, COM and driver provider qualification evidence");
        Console.WriteLine("  Beep.Installer.exe /DEPLOYMENTKIT=<script.bsetup> [/OUT=<dir>] Export Intune/ConfigMgr kit");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYDEPLOYMENTKIT=<kit-root> [/REQUIREMANAGEDEVIDENCE] [/MAXEVIDENCEAGEDAYS=30] [/OUT=<dir>] Gate Intune/ConfigMgr kit completeness and managed-device evidence");

        Console.WriteLine();
        Console.WriteLine("Enterprise package formats:");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> [/OUT=<dir>] Export MSI WiX source and capability report");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSIBUILD [/WIX=<path>] [/SIGNCERT=<pfx> /SIGNPASSWORD=secret://env/NAME] Export, build and optionally sign with WiX");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSICUSTOMACTIONS=json-config-transform,pnp-driver,scheduled-task Restrict generated MSI custom-action families");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSIBUILD /MSIVALIDATE [/MSIVALIDATEICE=ICE03;ICE64] Validate built MSI with WiX ICEs");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSIBUILD /MSILIFECYCLE [/MSIEXEC=<path>] [/MSILIFECYCLELOGDIR=<dir>] Run silent install/repair/uninstall evidence");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSIMATRIX /MSIMATRIXTARGETS=<id|os|arch|channel;...> /MSIMATRIXRUNNER=<path> [/MSIMATRIXSCENARIOS=enterprise-default|driver-default|patch-default|<json>] Run lifecycle qualification matrix evidence");
        Console.WriteLine("  Beep.Installer.exe qualify --environment <id> --channel local|hyperv|azure --log-dir <dir> --msi <package.msi> [--updated-msi <updated.msi>] [--mst <transform.mst>] [--msp <patch.msp> --msp-product <product.msi>] [--scenario-pack enterprise-default --scenario <id>] First-party MSI matrix runner");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MST=<out.mst> /MSITARGET=<target.msi> [/MSIUPDATED=<updated.msi>] [/MSTPROFILE=<name> /MSTPROPERTY=PROP=VALUE;...] Generate an MST with WiX");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MST=<out.mst> /MSITARGET=<target.msi> /MSTLIFECYCLE [/MSTLIFECYCLELOGDIR=<dir>] Run silent MST install/uninstall evidence");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSP=<out.msp> /MSPTARGET=<target.msi|target.wixpdb> /MSPUPDATED=<updated.msi|updated.wixpdb> [/MSPNOSUPERSEDE] Generate a policy-checked MSP with WiX");
        Console.WriteLine("  Beep.Installer.exe /MSI=<script.bsetup> /MSP=<out.msp> /MSPLIFECYCLE /MSPPRODUCT=<product.msi|product-code> Run silent MSP apply/remove evidence");
        Console.WriteLine("  Beep.Installer.exe /WINGET=<script.bsetup> /INSTALLER=<file> [/INSTALLERURL=<url>] [/OUT=<dir>] Export WinGet manifests");
        Console.WriteLine("  Beep.Installer.exe /WINGET=<script.bsetup> /INSTALLERX64=<file> /INSTALLERURLX64=<url> [/INSTALLERX86=<file>] [/INSTALLERARM64=<file>] Export multi-architecture WinGet manifests");
        Console.WriteLine("  Beep.Installer.exe /WINGET=<script.bsetup> /INSTALLER=<app.msix> /SIGNATURESHA256=<hash> Export MSIX WinGet manifests with signature evidence");
        Console.WriteLine("  Beep.Installer.exe /WINGET=<script.bsetup> /INSTALLER=<file> /SBOMPATH=<spdx.json> /PROVENANCEPATH=<provenance.json> Link WinGet manifests to release evidence");

        Console.WriteLine();
        Console.WriteLine("Security, signing, evidence and policy:");
        Console.WriteLine("  Beep.Installer.exe /EVIDENCE=<script.bsetup> [/INSTALLER=<file>] [/OUT=<dir>] Export SPDX SBOM and provenance");
        Console.WriteLine("  Beep.Installer.exe /EVIDENCE=<script.bsetup> /ATTESTKEY=<private.pem> /ATTESTKEYID=<id> Export DSSE-signed SBOM/provenance attestations");
        Console.WriteLine("  Beep.Installer.exe /VERIFYEVIDENCE=<script.bsetup> /EVIDENCEDIR=<dir> [/INSTALLER=<file>] Verify SBOM/provenance hashes");
        Console.WriteLine("  Beep.Installer.exe /VERIFYEVIDENCE=<script.bsetup> /ATTESTTRUSTKEY=<public.pem> /REQUIREATTESTATIONS Verify DSSE attestations");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYEVIDENCE=<script.bsetup> [/INSTALLER=<file>] [/OUT=<dir>] Gate SBOM/provenance generation, verification, DSSE and tamper detection");
        Console.WriteLine("  Beep.Installer.exe /SECURITYSCAN=<script.bsetup> [/POLICY=<policy.json>] [/MACHINEPOLICY=<policy.json>] [/SECURITYREPORT=<json>] Scan release inputs");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYSECURITY=<script.bsetup> [/INSTALLER=<file>] [/OUT=<dir>] Gate supply-chain scanner, signature and release-evidence behavior");
        Console.WriteLine("  Beep.Installer.exe /QUALIFYDIAGNOSTICS=<script.bsetup> [/OUT=<dir>] Gate support bundle, structured diagnostics and telemetry evidence");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup> /SBOM /PROVENANCE Export release evidence beside build output");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup> /SIGNCERT=<pfx> /SIGNPASSWORD=secret://env/NAME [/TIMESTAMP=<url>] Sign with a secret reference");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup> /SIGNSTORE=My /SIGNSTORELOCATION=CurrentUser /SIGNTHUMBPRINT=<sha1> Sign from Windows certificate store");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup> /SIGNREMOTEENDPOINT=<url> /SIGNREMOTEKEY=<key> /SIGNREMOTECREDENTIAL=secret://env/NAME Sign through remote/cloud HSM provider");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup> /TIMESTAMPOUTAGE=fail|warn|retry [/TIMESTAMPRETRIES=2] Control timestamp server outage behavior");
        Console.WriteLine("  Beep.Installer.exe /POLICY=<policy.json> [/MACHINEPOLICY=<policy.json>] Enforce merged organization policy");
        Console.WriteLine("  Beep.Installer.exe /PROJECTPOLICY=<json> /PROFILEPOLICY=<json> /MACHINEPOLICY=<json> Resolve policy as machine > profile > project");
        Console.WriteLine("  Beep.Installer.exe /RECOVERY=<script.bsetup> [/D=<path>] [/JOURNAL=<path>] Inspect typed-resource recovery state");
        Console.WriteLine("  /S /JOURNAL=<path> selects the installation journal; subsequent repair/uninstall/recovery discover the recorded location automatically.");
        Console.WriteLine("  Beep.Installer.exe /RECOVERY=<script.bsetup> /ROLLBACK [/D=<path>] [/JOURNAL=<path>] Replay journal rollback");
        Console.WriteLine("  Beep.Installer.exe /RECOVERY=<script.bsetup> /ABANDON [/D=<path>] [/JOURNAL=<path>] Archive the active recovery journal");

        Console.WriteLine();
        Console.WriteLine("Runtime install, repair and uninstall:");
        Console.WriteLine("  Beep.Installer.exe /OUT=<dir>              Override output directory (with /BUILD)");
        Console.WriteLine("  Beep.Installer.exe /PREVIEW=<.bsetup>      Show project summary");
        Console.WriteLine("  Beep.Installer.exe /SCRIPT=<.bsetup> /S    Run installer from a script");
        Console.WriteLine("  Beep.Installer.exe /RESPONSE=<file>      Load JSON or .rsp command options");
        Console.WriteLine("  Beep.Installer.exe @install.rsp          Load command options from a response file");
        Console.WriteLine("  Beep.Installer.exe /S [/D=<path>]        Silent install (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /S /OFFLINELAYOUT=<dir> Install using verified offline layout package blobs");
        Console.WriteLine("  Beep.Installer.exe /S /JSON              Emit a stable machine-readable install result");
        Console.WriteLine("  Beep.Installer.exe /S /TELEMETRYOUT=<path> Write policy-controlled runtime telemetry evidence");
        Console.WriteLine("  Beep.Installer.exe /S /DRYRUN            Compile/run checks without applying changes");
        Console.WriteLine("  Beep.Installer.exe /S /FORCE             Allow installing an older version over a newer one");
        Console.WriteLine("  Beep.Installer.exe /REPAIR [/D=<path>]   Restore missing or modified files (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /S /NOSCRIPTCMDS      Install the files but refuse the authored custom actions");
        Console.WriteLine("  Beep.Installer.exe /S /ALLOWSCRIPTCMDS   Permit custom actions that policy would otherwise refuse");
        Console.WriteLine("  Beep.Installer.exe /S /NORESTART         Report success (0) even when a reboot is pending");
        Console.WriteLine("  Beep.Installer.exe /S /RESTARTEXITCODE=n Override the reboot-required exit code (default 3010)");
        Console.WriteLine("  Beep.Installer.exe /UNINSTALL [/D=<path>] Silent uninstall (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /SELFTEST             Install + verify + uninstall in %TEMP%");
        Console.WriteLine("  Beep.Installer.exe /LANGMGR              Open Language Manager");
        Console.WriteLine("  Beep.Installer.exe /UPDATEUI /SCRIPT=<path>  Open the update center for a trusted project");
        Console.WriteLine("  Beep.Installer.exe /PUBLISH=<.bsetup> [/OUT=<dir>] Publish as ClickOnce");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<.bsetup> /PUBLISHFEED=<dir> [/FEEDURL=<url>] [/MINVERSION=x.y.z] [/REPUBLISH]");
        Console.WriteLine("                                          Build + stage into a static update feed");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<.bsetup> /FORMAT=msix|msixbundle|exe Package as MSIX");
        Console.WriteLine("  Beep.Installer.exe /CHECKUPDATE [/FEED=<url>]           Report if an app/module update is available");
        Console.WriteLine("  Beep.Installer.exe /UPDATE [/FEED=<url>] [/D=<root>]    Apply an available app update (side-by-side)");
        Console.WriteLine("  Beep.Installer.exe /?                    Show this help");
    }
}

internal sealed record ProjectTemplateCatalog(ProjectTemplateCatalogItem[] Templates);

internal sealed record ProjectTemplateCatalogItem(string Id, string Name, string Category, string Description);

internal sealed class ExtensionPackageExportCliResult
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("o");
    public bool Success { get; init; }
    public string ScriptPath { get; init; } = "";
    public string Format { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ExtensionId { get; init; } = "";
    public string ExtensionPublisher { get; init; } = "";
    public string ExtensionVersion { get; init; } = "";
    public string ExporterType { get; init; } = "";
    public List<string> Artifacts { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectTemplateCatalog))]
internal sealed partial class ProjectTemplateCatalogJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExtensionPackageExportCliResult))]
[JsonSerializable(typeof(ProjectSchemaDiagnostic))]
internal sealed partial class ExtensionPackageExportJsonContext : JsonSerializerContext;

internal static class AppInfo
{
    public const string ProductName = "Beep Installer";
    public const string Version = "1.0.0";
}

