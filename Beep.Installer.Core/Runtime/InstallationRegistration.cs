using Microsoft.Win32;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>Existing Windows discovery/maintenance registrations; never creates another identity store.</summary>
public static class InstallationRegistration
{
    public static string UninstallKeyPath(string appId)
    {
        if (!Guid.TryParseExact(appId, "D", out var identity) || identity == Guid.Empty)
            throw new ArgumentException("Uninstall registration requires a nonzero AppId GUID.", nameof(appId));
        return $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{identity:D}";
    }

    internal static void SynchronizeVersion(string appId, string productName, string publisher, string scope,
        string installDirectory, string version, bool dryRun)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (string.IsNullOrWhiteSpace(productName) || productName.IndexOfAny(['\\', '/']) >= 0
            || string.IsNullOrWhiteSpace(publisher) || scope is not ("user" or "machine"))
            throw new InvalidDataException("Installed registration identity or scope is invalid.");

        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 };
        foreach (var view in views)
        {
            using var hive = RegistryKey.OpenBaseKey(InstallScope.HiveFor(scope == "user"), view);
            Update(hive, UpgradeEngine.RegistrationKeyPath(appId), "InstallPath", "Version");
            Update(hive, UninstallKeyPath(appId), "InstallLocation", "DisplayVersion");
        }

        void Update(RegistryKey hive, string keyPath, string pathValue, string versionValue)
        {
            // Read-only inspection avoids requesting write access to unrelated registrations.
            using var current = hive.OpenSubKey(keyPath);
            if (current is null || !Matches(current, pathValue)) return;
            if (!string.Equals(current.GetValue("Publisher") as string, publisher, StringComparison.Ordinal))
                throw new InvalidDataException("Matching installation registration belongs to a different publisher.");
            if (dryRun || string.Equals(current.GetValue(versionValue) as string, version, StringComparison.Ordinal)) return;
            using var writable = hive.OpenSubKey(keyPath, writable: true)
                ?? throw new IOException("Installation registration disappeared during reconciliation.");
            if (!Matches(writable, pathValue) || !string.Equals(writable.GetValue("Publisher") as string, publisher, StringComparison.Ordinal))
                throw new IOException("Installation registration changed during reconciliation.");
            writable.SetValue(versionValue, version, RegistryValueKind.String);
            writable.Flush();
        }

        bool Matches(RegistryKey key, string pathValue)
        {
            var location = key.GetValue(pathValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return !string.IsNullOrWhiteSpace(location) && Path.IsPathFullyQualified(location)
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory)), StringComparison.OrdinalIgnoreCase);
        }
    }
}
