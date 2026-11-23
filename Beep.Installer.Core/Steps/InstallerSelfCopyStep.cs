using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Engine;
using Beep.Installer.Hosting;
using Beep.Installer.Models;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Steps;

/// <summary>
/// Stages the installer executable inside the install folder.
///
/// The uninstall/repair registration written by <c>BuildPipeline</c> points at
/// <c>%InstallPath%&lt;setup exe name&gt;</c>. Nothing used to put a file there, so the
/// Apps &amp; Features entry referenced a path that never existed and uninstall could
/// only be run from the original download. Copying the running installer keeps the ARP
/// verbs working after the download is gone.
/// </summary>
public sealed class InstallerSelfCopyStep : ISetupStep
{
    public string StepId => StepIds.InstallerSelfCopy;
    public string StepName => "Stage uninstaller";
    public string Description => "Copies the installer into the install folder so uninstall and repair keep working.";
    public IReadOnlyList<string> DependsOn { get; }

    public InstallerSelfCopyStep(string? dependsOn = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
    }

    public bool CanSkip(SetupContext context)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null || !project.CreateUninstallEntry)
            return true;
        if (context.Options?.DryRun == true)
            return true;

        var installPath = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(source))
            return true;

        // Repair runs from the staged copy; copying it onto itself would fail.
        return SamePath(source, StagedInstallerPath(project, installPath));
    }

    public IErrorsInfo Validate(SetupContext context)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null)
            return StepErrorHelpers.Fail("InstallProject not found in context.");
        if (string.IsNullOrWhiteSpace(context.TryGetProperty<string>(InstallContextKeys.InstallPath)))
            return StepErrorHelpers.Fail("InstallPath not set.");

        return StepErrorHelpers.Ok("Uninstaller staging target is ready.");
    }

    public IErrorsInfo Execute(SetupContext context, IProgress<PassedArgs>? progress = null)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null)
            return StepErrorHelpers.Fail("InstallProject not found in context.");

        var installPath = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        if (string.IsNullOrWhiteSpace(installPath))
            return StepErrorHelpers.Fail("InstallPath not set.");

        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return StepErrorHelpers.Fail("The running installer executable could not be located; uninstall registration would point at a missing file.");

        var destination = StagedInstallerPath(project, installPath);
        if (SamePath(source, destination))
            return StepErrorHelpers.Ok("Installer is already staged.");

        progress?.Report(new PassedArgs { Messege = $"Staging uninstaller: {Path.GetFileName(destination)}" });
        RemoveSupersededInstallers(installPath, destination, source, progress);
        var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(installPath);
            File.Copy(source, staging, overwrite: true);
            File.Move(staging, destination, overwrite: true);

            context.Properties["InstallerSelfCopyPath"] = destination;
            return StepErrorHelpers.Ok($"Uninstaller staged: {destination}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(staging);
            return StepErrorHelpers.Fail($"Failed to stage the uninstaller at '{destination}': {ex.Message}");
        }
    }

    public Task<IErrorsInfo> ExecuteAsync(SetupContext context, IProgress<PassedArgs>? progress = null, CancellationToken token = default)
        => Task.FromResult(Execute(context, progress));

    /// <summary>The path <see cref="TheTechIdea.Beep.Installer.BuildPipeline"/> names in the ARP registration.</summary>
    internal static string StagedInstallerPath(InstallProject project, string installPath)
        => Path.Combine(installPath, InstallerFileName(project));

    /// <summary>Mirrors <c>BuildPipeline.EnsureExeFileName</c> so the copy and the registry agree.</summary>
    internal static string InstallerFileName(InstallProject project)
    {
        var baseName = string.IsNullOrWhiteSpace(project.OutputBaseFilename)
            ? $"Setup-{project.AppName}-{project.AppVersion}"
            : project.OutputBaseFilename.Trim();

        var invalid = Path.GetInvalidFileNameChars();
        baseName = new string(baseName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(baseName))
            baseName = "Setup";

        return baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : baseName + ".exe";
    }

    /// <summary>
    /// Removes the previous version's staged installer before staging the new one. The payload
    /// does not contain the installer, so an in-place upgrade would otherwise leave every old
    /// <c>Setup-*.exe</c> behind and uninstall would only remove the current version's copy.
    /// </summary>
    private static void RemoveSupersededInstallers(
        string installPath,
        string destination,
        string runningInstaller,
        IProgress<PassedArgs>? progress)
    {
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(installPath, "Setup-*.exe", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(candidate, destination) || SamePath(candidate, runningInstaller))
                    continue;

                try
                {
                    File.Delete(candidate);
                    progress?.Report(new PassedArgs { Messege = $"Removed superseded installer: {Path.GetFileName(candidate)}" });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    progress?.Report(new PassedArgs { Messege = $"Left superseded installer '{candidate}': {ex.Message}" });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress?.Report(new PassedArgs { Messege = $"Could not inspect superseded installers: {ex.Message}" });
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
