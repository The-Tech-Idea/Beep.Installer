using System;
using System.IO;
using System.Linq;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public static class InstallScopeResolver
{
    public static string ProgramFilesFolder(bool prefer64Bit)
    {
        if (prefer64Bit)
            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrEmpty(x86)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : x86;
    }

    /// <summary>
    /// The single decision point for per-user vs per-machine install scope.
    ///
    /// This matters more than it looks: the resulting flag travels as the <c>PerUser</c>
    /// context key and selects the registry hive for the registry, COM, shared-file and
    /// uninstall steps. Getting it wrong silently sends every write to HKLM.
    ///
    /// DefaultScope controls ownership; PrivilegesRequired controls elevation independently.
    /// An explicit selection is accepted only when the project allows scope selection.
    /// </summary>
    public static bool IsPerUser(InstallProject project, bool? selection = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var authored = project.DefaultScope == InstallationScope.User;
        if (selection.HasValue && selection.Value != authored && !project.AllowScopeSelection)
            throw new ArgumentException("This project does not allow changing the authored installation scope.", nameof(selection));
        return selection ?? authored;
    }

    public static string DefaultBase(bool prefer64Bit, bool perUser)
        => perUser
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : ProgramFilesFolder(prefer64Bit);

    public static bool? ReadInstalledScope(InstallProject project, string installPath, string? journalPath = null)
    {
        string path;
        try { path = Beep.Installer.Extensibility.ResourceExecutionJournalStore.ResolvePath(installPath, project.AppId, journalPath); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Cannot determine installed scope: " + ex.Message, ex); }
        var loaded = new Beep.Installer.Extensibility.ResourceExecutionJournalStore(path).TryLoad();
        if (loaded.Status == Beep.Installer.Extensibility.ResourceExecutionJournalLoadStatus.Missing) return null;
        if (!loaded.Success || loaded.Journal is null)
            throw new InvalidOperationException("Cannot determine installed scope: " + loaded.Message);
        var metadata = loaded.Journal.Metadata;
        var appIdError = Beep.Installer.Extensibility.ResourceJournalRecoveryService.ValidateAppId(metadata, project.AppId);
        if (appIdError.Length > 0) throw new InvalidOperationException(appIdError);
        var identityError = Beep.Installer.Extensibility.ResourceJournalRecoveryService.ValidatePublisher(metadata, project.AppPublisher);
        if (identityError.Length > 0) throw new InvalidOperationException(identityError);
        return metadata.InstallScope switch
        {
            "user" => true,
            "machine" => false,
            _ => throw new InvalidOperationException("Installed scope journal contains an invalid scope.")
        };
    }

    public static string ResolveDefaultPath(InstallProject project, bool perUser)
    {
        var raw = project?.DefaultDirName ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var folder = DefaultBase(project?.Prefer64Bit ?? true, perUser);
        var resolved = raw
            .Replace("%ProgramFilesX86%", folder)
            .Replace("%ProgramFiles%", folder);
        return TheTechIdea.Beep.Installer.ConfigManager.ExpandVariables(resolved);
    }

    /// <summary>Resolves one maintenance target; multiple installations require an explicit path.</summary>
    public static string ResolveMaintenancePath(InstallProject project, string? explicitPath = null,
        Func<bool, string?>? registrationLookup = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
        registrationLookup ??= perUser =>
        {
            using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(
                TheTechIdea.Beep.Installer.InstallScope.HiveFor(perUser),
                TheTechIdea.Beep.Installer.InstallScope.ViewFor(project.Prefer64Bit));
            return new TheTechIdea.Beep.Installer.UpgradeEngine().DetectExisting(project.AppId, hive)?.InstallPath;
        };
        var candidates = new[] { registrationLookup(true), registrationLookup(false) }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length > 1)
            throw new InvalidOperationException("Multiple installations were found. Specify /D=<install-directory> to select the maintenance target.");
        return candidates.Length == 1 ? candidates[0] : ResolveDefaultPath(project, IsPerUser(project));
    }

    public static string ResolveOutputDirectory(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.OutputDir))
            return project.OutputDir;

        var product = SafeFileName(project.AppName);
        var version = SafeFileName(project.AppVersion);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BeepInstaller", "Builds", $"{product}-{version}");
    }

    private static string SafeFileName(string name, string fallback = "Application")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? fallback : clean;
    }
}
