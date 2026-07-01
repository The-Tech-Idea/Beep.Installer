using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer;

/// <summary>
/// Beep Installer — entry point.
///
/// This executable has TWO modes:
///
///  1. GENERATOR mode (default UI)
///        The Package Builder UI lets you author a .bpkg project and build
///        a self-contained Setup.exe for distribution.
///
///  2. RUNTIME mode (when shipped as the generated installer)
///        The Setup.exe carries an install-config.json + payload alongside it
///        and shows the install wizard to the end user.
///
/// The mode is determined at startup by inspecting command-line arguments
/// and whether an install-config.json exists beside the executable.
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
            if (!headless)
            {
                try { MessageBox.Show($"Fatal error: {ex.Message}", "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
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
            var config = LoadRuntimeConfig();
            var cfgIdxSl = IndexOf(args, "/CONFIG=");
            if (config == null && cfgIdxSl >= 0)
            {
                var (loaded, err) = ConfigManager.Load(args[cfgIdxSl][8..]);
                config = loaded;
                if (err != null) Console.Error.WriteLine(err);
            }
            if (config == null) { Console.Error.WriteLine("No install-config.json found beside the executable and no /CONFIG= was provided."); return 2; }
            return RunSilentInstall(config, args);
        }

        // Uninstall — used by Add/Remove Programs
        if (Has(args, "/UNINSTALL"))
        {
            var config = LoadRuntimeConfig();
            var cfgUIdx = IndexOf(args, "/CONFIG=");
            if (config == null && cfgUIdx >= 0)
            {
                var (loaded, err) = ConfigManager.Load(args[cfgUIdx][8..]);
                config = loaded;
                if (err != null) Console.Error.WriteLine(err);
            }
            return config == null ? 2 : RunUninstall(config, args);
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

        // CLI validate — check a .bpkg for errors
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

        // Direct install-config (legacy /CONFIG=)
        var cfgIdx = IndexOf(args, "/CONFIG=");
        if (cfgIdx >= 0)
        {
            // Explicit config means "run as runtime installer now"
            var (cfg, err) = ConfigManager.Load(args[cfgIdx][8..]);
            if (cfg == null) { Console.Error.WriteLine(err); return 2; }
            var branding = Engine.ThemeLoader.LoadBranding();
            Application.Run(new ThemedInstallerForm(cfg, branding));
            return 0;
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
            var config = LoadRuntimeConfig();
            if (config == null) { Console.Error.WriteLine("install-config.json found but could not be parsed."); return 2; }
            var branding = Engine.ThemeLoader.LoadBranding();
            Application.Run(new ThemedInstallerForm(config, branding));
            return 0;
        }

        // Generator UI
        Application.Run(new PackageBuilderForm());
        return 0;
    }

    // ── Mode detection ──────────────────────────────────────────────────

    /// <summary>
    /// True if this executable is acting as a SHIPPED installer (i.e. an
    /// install-config.json is sitting next to it).
    /// </summary>
    private static bool IsRuntimeMode()
    {
        var exeDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(exeDir, "install-config.json"));
    }

    private static InstallConfig? LoadRuntimeConfig()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "install-config.json");
        if (!File.Exists(path)) return null;
        var (cfg, err) = ConfigManager.Load(path);
        if (err != null) Console.Error.WriteLine($"Config warning: {err}");
        return cfg;
    }

    // ── Silent install ──────────────────────────────────────────────────

    private static int RunSilentInstall(InstallConfig config, string[] args)
    {
        Console.WriteLine($"Installing {config.ProductName} {config.ProductVersion}…");

        var installPath = config.DefaultInstallPath;
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
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = installPath;

        var wizard = new SetupWizardBuilder()
            .WithId("beep-install-silent")
            .WithOptions(new SetupOptions { Environment = "Production" })
            .AddStep(new PrerequisiteCheckStep())
            .AddStep(new DirectoryCreateStep("installer.prerequisites.check"))
            .AddStep(new Steps.PayloadDownloadStep("installer.directory.create"))
            .AddStep(new FileCopyStep("installer.payload.download"))
            .AddStep(new ShortcutCreateStep("installer.files.copy"))
            .AddStep(new RegistryWriteStep("installer.shortcuts.create"))
            .AddStep(new VerifyInstallStep("installer.registry.write"))
            .Build();

        var result = wizard.Run(context);
        var ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
        Console.WriteLine(ok ? "Installation completed successfully." : $"Installation failed: {result.Message}");
        return ok ? 0 : 1;
    }

    // ── Uninstall ───────────────────────────────────────────────────────

    private static int RunUninstall(InstallConfig config, string[] args)
    {
        var installPath = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))?[3..]
                          ?? config.DefaultInstallPath;
        Console.WriteLine($"Uninstalling {config.ProductName} from {installPath}…");

        var context = new SetupContext();
        context.Properties["InstallPath"] = installPath;
        context.Properties["InstallConfig"] = config;

        var wizard = new SetupWizardBuilder()
            .WithId("beep-uninstall")
            .AddStep(new UninstallStep())
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
            // Build a tiny self-contained config that requires no real source files
            var (cfg, sourceDir) = MakeSelfTestConfig(testDir);
            try
            {
            var context = new SetupContext();
            context.Properties["InstallConfig"] = cfg;
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
            uninstallContext.Properties["InstallConfig"] = cfg;
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
                try { if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true); } catch { }
            return 1;
        }
    }

    private static (InstallConfig config, string sourceDir) MakeSelfTestConfig(string testDir)
    {
        // Create a tiny source file in a sibling temp dir, so the file copy has something real to copy
        var sourceDir = Path.Combine(Path.GetTempPath(), $"BeepSelfTestSrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "hello.txt");
        File.WriteAllText(sourceFile, $"Beep Installer self-test payload @ {DateTime.UtcNow:O}");

        var config = new InstallConfig
        {
            ProductName = "BeepSelfTest",
            ProductVersion = "0.0.0",
            Publisher = "Beep Installer",
            DefaultInstallPath = testDir,
            Components = new List<InstallComponent>
            {
                new()
                {
                    Id = "core", Name = "Core", Required = true, Selected = true,
                    SizeBytes = new FileInfo(sourceFile).Length,
                    Files = new List<FileCopyOperation>
                    {
                        new() { SourcePath = sourceFile, DestinationPath = "hello.txt", Description = "hello.txt" }
                    }
                }
            }
        };
        return (config, sourceDir);
    }

    // ── Headless build ──────────────────────────────────────────────────

    private static int RunHeadlessBuild(string projectPath, string[] args)
    {
        Console.WriteLine($"Beep Installer — headless build");
        Console.WriteLine($"Project: {projectPath}");

        var (project, err) = ProjectSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"Error: {err}"); return 2; }

        // Allow overriding output via /OUT=
        var outIdx = IndexOf(args, "/OUT=");
        if (outIdx >= 0) project.Build.OutputDirectory = args[outIdx][5..];

        var progress = new Progress<BuildProgress>(p =>
            Console.WriteLine($"  [{p.Percent,3}%] {p.Message}"));

        var builder = new InstallerBuilder { Progress = progress };
        var result = builder.Build(project);

        Console.WriteLine();
        Console.WriteLine(result.Summary);
        foreach (var w in result.Warnings) Console.WriteLine($"  WARN: {w}");
        foreach (var e in result.Errors) Console.Error.WriteLine($"  ERR : {e}");

        return result.Success ? 0 : 1;
    }

    // ── CLI validate ─────────────────────────────────────────────────────

    private static int RunValidate(string projectPath)
    {
        var (project, err) = ProjectSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine($"ERROR: {err}"); return 2; }

        var result = new InstallerBuilder().Validate(project);
        Console.WriteLine($"Project : {project.ProjectName} ({projectPath})");
        Console.WriteLine($"Product : {project.InstallConfig.ProductName} {project.InstallConfig.ProductVersion}");
        Console.WriteLine($"Source  : {project.SourceDirectory}");
        Console.WriteLine($"Components: {project.InstallConfig.Components.Count}");

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
        var (project, err) = ProjectSerializer.Load(projectPath);
        if (project == null) { Console.Error.WriteLine(err); return 2; }

        Console.WriteLine($"Project: {project.ProjectName}");
        Console.WriteLine($"  Product   : {project.InstallConfig.ProductName} {project.InstallConfig.ProductVersion}");
        Console.WriteLine($"  Publisher : {project.InstallConfig.Publisher}");
        Console.WriteLine($"  Source    : {project.SourceDirectory}");
        Console.WriteLine($"  Default   : {project.InstallConfig.DefaultInstallPath}");
        Console.WriteLine($"  Components: {project.InstallConfig.Components.Count}");
        Console.WriteLine($"  Output    : {Path.Combine(project.Build.OutputDirectory, project.Build.OutputFileName)}");
        return 0;
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
            MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(AppInfo.ProductName + " v" + AppInfo.Version);
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Beep.Installer.exe                         Open the Package Builder (generator UI)");
        Console.WriteLine("  Beep.Installer.exe /BUILD=<project.bpkg>   Build the installer (headless)");
        Console.WriteLine("  Beep.Installer.exe /VALIDATE=<project.bpkg> Validate a project without building");
        Console.WriteLine("  Beep.Installer.exe /OUT=<dir>              Override output directory (with /BUILD)");
        Console.WriteLine("  Beep.Installer.exe /PREVIEW=<.bpkg>        Show project summary");
        Console.WriteLine("  Beep.Installer.exe /CONFIG=<config.json> Run as installer with given config");
        Console.WriteLine("  Beep.Installer.exe /S [/D=<path>]        Silent install (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /UNINSTALL [/D=<path>] Silent uninstall (runtime mode)");
        Console.WriteLine("  Beep.Installer.exe /SELFTEST             Install + verify + uninstall in %TEMP%");
        Console.WriteLine("  Beep.Installer.exe /LANGMGR              Open Language Manager");
        Console.WriteLine("  Beep.Installer.exe /?                    Show this help");
    }
}

internal static class AppInfo
{
    public const string ProductName = "Beep Installer";
    public const string Version = "1.0.0";
}
