using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Beep.Installer.Models;
using Beep.Installer.Engine;
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
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
                // Some hosts / runtimes fail DPI init. Fall back to defaults.
                Console.Error.WriteLine($"Warning: UI init fallback ({ex.GetType().Name}: {ex.Message})");
            }
        }
        else
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

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
            catch { }
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
    }

    private static bool IsHeadlessCommand(string[] args)
    {
        return Has(args, "/?", "/H", "/HELP", "/VER")
            || IndexOf(args, "/BUILD=") >= 0
            || IndexOf(args, "/VALIDATE=") >= 0
            || IndexOf(args, "/PREVIEW=") >= 0
            || IndexOf(args, "/PUBLISH=") >= 0
            || Has(args, "/S", "/SILENT")
            || Has(args, "/UNINSTALL")
            || Has(args, "/SELFTEST");
    }

    // ── Dispatch ────────────────────────────────────────────────────────

    private static int Dispatch(string[] args)
    {
        // CLI flags that override everything
        if (Has(args, "/?", "/H", "/HELP"))
        {
            PrintUsage();
            return 0;
        }

        if (Has(args, "/VER"))
        {
            Console.WriteLine($"Beep Installer v{AppInfo.Version}");
            return 0;
        }

        // Silent install — used by the SHIPPED installer
        if (Has(args, "/S", "/SILENT"))
        {
            var project = LoadRuntimeProject(args);
            if (project == null) { Console.Error.WriteLine("No installer script was found in the executable."); return 2; }
            return RunSilentInstall(project, args);
        }

        // Uninstall — used by Add/Remove Programs
        if (Has(args, "/UNINSTALL"))
        {
            var project = LoadRuntimeProject(args);
            return project == null ? 2 : RunUninstall(project, args);
        }

        // Self-test
        if (Has(args, "/SELFTEST"))
        {
            return RunSelfTest();
        }

        // Headless build — used by CI pipelines
        var buildIdx = IndexOf(args, "/BUILD=");
        if (buildIdx >= 0)
        {
            var projectPath = args[buildIdx][7..];
            return RunHeadlessBuild(projectPath, args);
        }

        // CLI validate — check a .bsetup script for errors
        var validateIdx = IndexOf(args, "/VALIDATE=");
        if (validateIdx >= 0)
        {
            var projectPath = args[validateIdx][10..];
            return RunValidate(projectPath);
        }

        // CLI preview — render one wizard page to console
        var previewIdx = IndexOf(args, "/PREVIEW=");
        if (previewIdx >= 0)
        {
            var projectPath = args[previewIdx][9..];
            return RunPreview(projectPath);
        }

        // ClickOnce publish — CI-friendly
        var publishIdx = IndexOf(args, "/PUBLISH=");
        if (publishIdx >= 0)
        {
            var projectPath = args[publishIdx]["PUBLISH=".Length..];
            return RunPublish(projectPath, args);
        }

        // Standalone language manager tool
        if (Has(args, "/LANGMGR"))
        {
            Application.Run(new LanguageManagerForm());
            return 0;
        }

        // Default: detect runtime mode or open the generator
        if (IsRuntimeMode())
        {
            var project = LoadRuntimeProject(args);
            if (project == null) { ShowFatalMessage("Installer script could not be parsed — the installer is corrupted."); return 2; }
            Application.Run(new BeepModernInstallerForm(project));
            return 0;
        }

        // Generator UI
        var controller = new InstallerController();
        Application.Run(new PackageBuilderForm(controller));
        return 0;
    }

    // ── Mode detection ──────────────────────────────────────────────────

    /// <summary>
    /// True if this executable is acting as a shipped installer.
    /// </summary>
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
        RuntimeProjectContext.Current = project;
        return project;
    }

    // ── Silent install ──────────────────────────────────────────────────

    private static int RunSilentInstall(InstallProject project, string[] args)
    {
        RuntimeProjectContext.Current = project;
        var config = project;
        Console.WriteLine($"Installing {config.AppName} {config.AppVersion}…");

        var perUser = !(config.PrivilegesRequired == PrivilegeLevel.Admin || config.PrivilegesRequired == PrivilegeLevel.Lowest);
        var installPath = Engine.InstallScopeResolver.ResolveDefaultPath(config, perUser);
        var dArg = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase));
        if (dArg != null) installPath = dArg[3..];

        var componentsArg = args.FirstOrDefault(a => a.StartsWith("/COMPONENTS=", StringComparison.OrdinalIgnoreCase));
        if (componentsArg != null)
        {
            var ids = componentsArg[12..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var c in config.Components)
                c.Selected = ids.Contains(c.Id, StringComparer.OrdinalIgnoreCase) || c.Required;
        }

        Console.WriteLine($"  Path: {installPath}");

        var context = new SetupContext();
        context.Properties["InstallProject"] = project;
        context.Properties["InstallPath"] = installPath;
        context.Properties["PerUser"] = perUser;
context.Properties["CustomActions"] = RuntimeProjectContext.Current?.CustomActions;


        // Transactional rollback: FileCopyStep registers each copied file; on failure we undo.
        var rollback = new RollbackManager();
        context.Properties["RollbackManager"] = rollback;

        var wizard = new SetupWizardBuilder()
            .WithId("beep-install-silent")
            .WithOptions(new SetupOptions { Environment = "Production" })
            .AddStep(new PrerequisiteCheckStep())
            .AddStep(new DirectoryCreateStep("installer.prerequisites.check"))
            .AddStep(new CustomActionStep(CustomActionTiming.BeforeInstall, "installer.directory.create"))
            .AddStep(new Steps.PayloadDownloadStep("installer.custom.beforeinstall"))
            .AddStep(new Steps.PayloadPrepareStep("installer.payload.download"))
            .AddStep(new FileCopyStep("installer.payload.prepare"))
            .AddStep(new SharedFileCountStep("installer.files.copy"))
            .AddStep(new ComServerRegistrationStep("installer.files.copy"))
            .AddStep(new GacInstallStep("installer.files.copy"))
            .AddStep(new ShortcutCreateStep("installer.files.copy"))
            .AddStep(new RegistryWriteStep("installer.shortcuts.create"))
            .AddStep(new CustomActionStep(CustomActionTiming.AfterInstall, "installer.registry.write"))
            .AddStep(new VerifyInstallStep("installer.custom.afterinstall"))
            .Build();

        var result = wizard.Run(context);
        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        if (ok) rollback.Commit();
        else
        {
            Console.WriteLine("Installation failed — rolling back changes…");
            rollback.Rollback();
        }
        Console.WriteLine(ok ? "Installation completed successfully." : $"Installation failed: {result.Message}");
        return ok ? 0 : 1;
    }

    // ── Uninstall ───────────────────────────────────────────────────────

    private static int RunUninstall(InstallProject project, string[] args)
    {
        RuntimeProjectContext.Current = project;
        var config = project;
        var perUser = !(config.PrivilegesRequired == PrivilegeLevel.Admin || config.PrivilegesRequired == PrivilegeLevel.Lowest);
        var installPath = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))?[3..]
                          ?? Engine.InstallScopeResolver.ResolveDefaultPath(config, perUser);
        Console.WriteLine($"Uninstalling {config.AppName} from {installPath}…");

        var context = new SetupContext();
        context.Properties["InstallPath"] = installPath;
        context.Properties["InstallProject"] = project;
        context.Properties["PerUser"] = perUser;
context.Properties["CustomActions"] = RuntimeProjectContext.Current?.CustomActions;


        var wizard = new SetupWizardBuilder()
            .WithId("beep-uninstall")
            .AddStep(new CustomActionStep(CustomActionTiming.BeforeUninstall))
            .AddStep(new UninstallStep())
            .AddStep(new CustomActionStep(CustomActionTiming.AfterUninstall, "installer.uninstall"))
            .Build();

        var result = wizard.Run(context);
        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        Console.WriteLine(ok ? "Uninstall completed." : $"Uninstall failed: {result.Message}");
        return ok ? 0 : 1;
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
                RuntimeProjectContext.Current = project;
                var context = new SetupContext();
                context.Properties["InstallProject"] = project;
                context.Properties["InstallPath"] = testDir;

                var wizard = new SetupWizardBuilder()
                    .WithId("beep-selftest")
                    .WithOptions(new SetupOptions { Environment = "Test" })
                    .AddStep(new DirectoryCreateStep())
                    .AddStep(new FileCopyStep("installer.directory.create"))
                    .AddStep(new VerifyInstallStep("installer.files.copy"))
                    .Build();

                var installResult = wizard.Run(context);
                var installOk = installResult.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
                Console.WriteLine($"Install: {(installOk ? "PASS" : "FAIL")}");

                var manifestOk = File.Exists(Path.Combine(testDir, "install-manifest.json"));
                Console.WriteLine($"Manifest: {(manifestOk ? "PASS" : "FAIL")}");

                var uninstallContext = new SetupContext();
                uninstallContext.Properties["InstallPath"] = testDir;
                uninstallContext.Properties["InstallProject"] = project;
                var uninstallWizard = new SetupWizardBuilder()
                    .WithId("beep-selftest-uninstall")
                    .AddStep(new UninstallStep())
                    .Build();
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

        // Allow overriding output via /OUT=
        var outIdx = IndexOf(args, "/OUT=");
        if (outIdx >= 0) project.OutputDir = args[outIdx][5..];

        // Allow overriding output format via /FORMAT=msix|msixbundle|exe (Track C).
        var fmtIdx = IndexOf(args, "/FORMAT=");
        if (fmtIdx >= 0 && Enum.TryParse<InstallerOutputFormat>(args[fmtIdx]["FORMAT=".Length..], ignoreCase: true, out var fmt))
            project.OutputFormat = fmt;

        var progress = new Progress<BuildPipeline.BuildProgress>(p =>
            Console.WriteLine($"  [{p.Percent,3}%] {p.Message}"));

        var pipeline = new BuildPipeline { Progress = progress };
        var result = pipeline.Run(project);

        Console.WriteLine();
        Console.WriteLine(result.Summary);
        foreach (var w in result.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in result.Errors) Console.Error.WriteLine($"  ERR : {e}");

        return result.Success ? 0 : 1;
    }

    // ── CLI validate ─────────────────────────────────────────────────────

    private static int RunValidate(string projectPath)
    {
        var (project, err) = InstallerScriptSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        var result = new BuildPipeline().Validate(project);
        Console.WriteLine($"Script  : {project.ProjectName} ({projectPath})");
        Console.WriteLine($"Product : {project.AppName} {project.AppVersion}");
        Console.WriteLine($"Source  : {project.SourceDirectory}");
        Console.WriteLine($"Components: {project.Components.Count}");

        if (result.Errors.Count > 0)
        {
            Console.Error.WriteLine($"\n{result.Errors.Count} error(s):");
            foreach (var e in result.Errors) Console.Error.WriteLine($"  ERR : {e}");
        }
        if (result.Warnings.Count > 0)
        {
            Console.WriteLine($"\n{result.Warnings.Count} warning(s):");
            foreach (var w in result.Warnings) Console.WriteLine($"  WARN: {w}");
        }
        if (result.Errors.Count == 0 && result.Warnings.Count == 0)
            Console.WriteLine("\nAll checks passed.");

        Console.WriteLine($"\nExit code: {(result.Errors.Count > 0 ? 1 : 0)}");
        return result.Errors.Count > 0 ? 1 : 0;
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

        var outIdx = IndexOf(args, "/OUT=");
        var publishDir = outIdx >= 0
            ? args[outIdx]["OUT=".Length..]
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "", "publish");

        var urlIdx = IndexOf(args, "/UPDATEURL=");
        var updateUrl = urlIdx >= 0 ? args[urlIdx]["UPDATEURL=".Length..] : project.AppUpdatesURL;

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

    // ── Arg helpers ─────────────────────────────────────────────────────

    private static bool Has(string[] args, params string[] flags)
        => args.Any(a => flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

    private static int IndexOf(string[] args, string prefix)
    {
        for (int i = 0; i < args.Length; i++)
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
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
    catch { }
    // Try to show a dialog (may fail if WinForms init already blew up).
    try { MessageBox.Show(message + $"{Environment.NewLine}{Environment.NewLine}Log written to:{Environment.NewLine}{crashLog}", "Beep Installer — Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    catch (Exception mbx) { Engine.Diag.Debug("Program", "ShowFatalMessage dialog failed", mbx); }
}

    private static void PrintUsage()
    {
        Console.WriteLine(AppInfo.ProductName + " v" + AppInfo.Version);
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Beep.Installer.exe                         Open the Package Builder (generator UI)");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<script.bsetup>   Build the installer (headless)");
        Console.WriteLine("  Beep.Installer.exe /VALIDATE=<script.bsetup> Validate a script without building");
        Console.WriteLine("  Beep.Installer.exe /OUT=<dir>              Override output directory (with /BUILD)");
        Console.WriteLine("  Beep.Installer.exe /PREVIEW=<.bsetup>      Show project summary");
        Console.WriteLine("  Beep.Installer.exe /SCRIPT=<.bsetup> /S    Run installer from a script");
        Console.WriteLine("  Beep.Installer.exe /S [/D=<path>]        Silent install (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /UNINSTALL [/D=<path>] Silent uninstall (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /SELFTEST             Install + verify + uninstall in %TEMP%");
        Console.WriteLine("  Beep.Installer.exe /LANGMGR              Open Language Manager");
        Console.WriteLine("  Beep.Installer.exe /PUBLISH=<.bsetup> [/OUT=<dir>] Publish as ClickOnce");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<.bsetup> /FORMAT=msix|msixbundle|exe Package as MSIX");
        Console.WriteLine("  Beep.Installer.exe /?                    Show this help");
    }
}

internal static class AppInfo
{
    public const string ProductName = "Beep Installer";
    public const string Version = "1.0.0";
}

