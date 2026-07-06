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
/// <c>%LocalAppData%\Apps\Beep\&lt;Product&gt;\&lt;Version&gt;</c>, creates a Start Menu shortcut,
/// and writes an HKCU uninstall entry. The <paramref name="localAppDataRoot"/> override exists
/// for tests so installs stay hermetic.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClickOnceRuntime
{
    /// <summary>Default install root used when no override is given.</summary>
    public static string DefaultInstallRoot(string productName, string version)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "Apps", "Beep", productName, version);

    public static PerUserInstallResult Install(
        string payloadDir, string productName, string version, string exeRelativePath,
        string? iconPath = null, string? publisher = null, string? localAppDataRoot = null)
    {
        var r = new PerUserInstallResult();
        try
        {
            if (string.IsNullOrWhiteSpace(productName)) { r.Error = "Product name required."; return r; }
            if (string.IsNullOrWhiteSpace(version)) { r.Error = "Version required."; return r; }
            if (string.IsNullOrWhiteSpace(exeRelativePath)) { r.Error = "Main executable required."; return r; }
            if (!Directory.Exists(payloadDir)) { r.Error = $"Payload directory not found: {payloadDir}"; return r; }

            var rootBase = string.IsNullOrWhiteSpace(localAppDataRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppDataRoot;
            var installRoot = Path.Combine(rootBase, "Apps", "Beep", productName, version);
            r.InstallRoot = installRoot;

            // When localAppDataRoot is overridden (tests), place the Start Menu shortcut under
            // the same sandbox so the test stays hermetic; otherwise use the real user Start Menu.
            var startMenuBase = string.IsNullOrWhiteSpace(localAppDataRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Microsoft", "Windows", "Start Menu")
                : Path.Combine(localAppDataRoot, "StartMenu");
            r.ShortcutPath = Path.Combine(startMenuBase, "Programs", productName, productName + ".lnk");

            r.UninstallKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{productName}";

            CopyDirectory(payloadDir, installRoot);

            // HKCU uninstall entry (no admin required).
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(r.UninstallKeyPath, writable: true))
            {
                key!.SetValue("DisplayName", productName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("InstallLocation", installRoot);
                key.SetValue("Publisher", publisher ?? "");
                key.SetValue("UninstallString", $"\"{Path.Combine(installRoot, exeRelativePath)}\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            // Start Menu shortcut via WScript.Shell (PowerShell-hosted, no COM ref).
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(r.ShortcutPath)!);
                Shortcut.Create(r.ShortcutPath, Path.Combine(installRoot, exeRelativePath), workDir: installRoot, iconPath: iconPath);
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"Could not create Start Menu shortcut: {ex.Message}");
                Diag.Debug("ClickOnceRuntime", "shortcut creation failed", ex);
            }

            // Tiny per-user manifest (per-user installs don't need admin to write the
            // standard Add/Remove Programs key under HKLM, so we write a per-user marker too).
            File.WriteAllText(Path.Combine(installRoot, "per-user-install.json"),
                $"{{\"product\":\"{Escape(productName)}\",\"version\":\"{Escape(version)}\"," +
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
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
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
