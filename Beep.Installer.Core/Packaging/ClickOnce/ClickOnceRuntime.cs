using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Outcome of a per-user install (Track B2.2).</summary>
public class PerUserInstallResult
{
    public bool Success { get; set; }
    public string InstallRoot { get; set; } = "";
    public string ShortcutPath { get; set; } = "";
    public string UninstallKeyPath { get; set; } = "";
    public List<string> Warnings { get; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// Per-user (no-admin) ClickOnce install runtime (Track B2.2). Installs into
/// <c>%LocalAppData%\Apps\Beep\&lt;AppId&gt;\&lt;Version&gt;</c>, creates a Start Menu shortcut,
/// and writes an HKCU uninstall entry. The <paramref name="localAppDataRoot"/> override exists
/// for tests so installs stay hermetic.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClickOnceRuntime
{
    /// <summary>Default install root used when no override is given.</summary>
    public static string DefaultInstallRoot(string appId, string version)
        => ResolveInstallRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appId, version);

    private static string ResolveInstallRoot(string root, string appId, string version)
    {
        InstallationRegistration.UninstallKeyPath(appId);
        if (!Version.TryParse(version, out var parsed))
            throw new ArgumentException("A numeric product version is required.", nameof(version));
        return Path.GetFullPath(Path.Combine(root, "Apps", "Beep", Guid.Parse(appId).ToString("D"), parsed.ToString()));
    }

    public static PerUserInstallResult Install(
        string payloadDir, string productName, string version, string exeRelativePath, string appId,
        string? iconPath = null, string? publisher = null, string? localAppDataRoot = null)
    {
        var r = new PerUserInstallResult();
        try
        {
            r.UninstallKeyPath = InstallationRegistration.UninstallKeyPath(appId);
            if (string.IsNullOrWhiteSpace(productName)) { r.Error = "Product name required."; return r; }
            if (productName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || productName is "." or ".."
                || productName.EndsWith('.') || productName.EndsWith(' '))
            { r.Error = "Product name must be a valid shortcut name."; return r; }
            if (string.IsNullOrWhiteSpace(version)) { r.Error = "Version required."; return r; }
            if (string.IsNullOrWhiteSpace(exeRelativePath)) { r.Error = "Main executable required."; return r; }
            if (!Directory.Exists(payloadDir)) { r.Error = $"Payload directory not found: {payloadDir}"; return r; }

            var rootBase = string.IsNullOrWhiteSpace(localAppDataRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppDataRoot;
            var installRoot = ResolveInstallRoot(rootBase, appId, version);
            r.InstallRoot = installRoot;
            using var operationLock = InstallationOperationLock.Acquire(Path.GetDirectoryName(installRoot)!);
            var executable = Path.GetFullPath(Path.Combine(installRoot, exeRelativePath));
            if (Path.IsPathRooted(exeRelativePath) || !executable.StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { r.Error = "Main executable must stay inside the installation directory."; return r; }
            if (!File.Exists(Path.Combine(payloadDir, exeRelativePath)))
            { r.Error = "Main executable is missing from the payload."; return r; }

            // When localAppDataRoot is overridden (tests), place the Start Menu shortcut under
            // the same sandbox so the test stays hermetic; otherwise use the real user Start Menu.
            var startMenuBase = string.IsNullOrWhiteSpace(localAppDataRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Microsoft", "Windows", "Start Menu")
                : Path.Combine(localAppDataRoot, "StartMenu");
            var shortcutRoot = Path.GetFullPath(Path.Combine(startMenuBase, "Programs", Guid.Parse(appId).ToString("D")));
            r.ShortcutPath = Path.Combine(shortcutRoot, productName + ".lnk");
            string? previousShortcut;
            using (var existing = Registry.CurrentUser.OpenSubKey(r.UninstallKeyPath))
            {
                if (existing is not null && !string.Equals(existing.GetValue("Publisher") as string, publisher ?? "", StringComparison.Ordinal))
                { r.Error = "Installed AppId belongs to a different publisher."; return r; }
                previousShortcut = existing?.GetValue("ShortcutPath") as string;
            }


            CopyDirectory(payloadDir, installRoot);

            // HKCU uninstall entry (no admin required).
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(r.UninstallKeyPath, writable: true))
            {
                key!.SetValue("DisplayName", productName);
                key.SetValue("AppId", Guid.Parse(appId).ToString("D"));
                key.SetValue("DisplayVersion", version);
                key.SetValue("InstallLocation", installRoot);
                key.SetValue("Publisher", publisher ?? "");
                key.SetValue("UninstallString", $"\"{executable}\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            // Start Menu shortcut via WScript.Shell (PowerShell-hosted, no COM ref).
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.ShortcutPath)!);
                Shortcut.Create(r.ShortcutPath, executable, workDir: installRoot, iconPath: iconPath);
                if (File.Exists(r.ShortcutPath))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(r.UninstallKeyPath, writable: true);
                    key!.SetValue("ShortcutPath", r.ShortcutPath);
                    if (!string.IsNullOrEmpty(previousShortcut)
                        && string.Equals(Path.GetDirectoryName(Path.GetFullPath(previousShortcut)), shortcutRoot, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetExtension(previousShortcut), ".lnk", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(previousShortcut, r.ShortcutPath, StringComparison.OrdinalIgnoreCase))
                        File.Delete(previousShortcut);
                }
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"Could not create Start Menu shortcut: {ex.Message}");
                Diag.Debug("ClickOnceRuntime", "shortcut creation failed", ex);
            }

            // Tiny per-user manifest (per-user installs don't need admin to write the
            // standard Add/Remove Programs key under HKLM, so we write a per-user marker too).
            File.WriteAllText(Path.Combine(installRoot, "per-user-install.json"),
                $"{{\"appId\":\"{Guid.Parse(appId):D}\",\"product\":\"{Escape(productName)}\",\"version\":\"{Escape(version)}\"," +
                $"\"installedAt\":\"{DateTime.UtcNow:o}\",\"installRoot\":\"{Escape(installRoot)}\"}}");

            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Success = false;
            r.Error = ex.Message;
            Diag.Warn("ClickOnceRuntime", "per-user install failed", ex);
        }
        return r;
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
