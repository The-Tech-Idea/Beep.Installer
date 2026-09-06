using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine.Msix;

/// <summary>
/// Pre-package MSIX capability gate. The custom EXE/MSI engines can execute rich classic
/// Windows installer resources; the current MSIX exporter packages application payload and
/// AppInstaller update metadata. This analyzer prevents an MSIX build from silently implying
/// support for resources that would not be represented in the generated package.
/// </summary>
public static class MsixProjectCapabilityAnalyzer
{
    public static IReadOnlyList<CheckResult> Analyze(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var results = new List<CheckResult>
        {
            Info("Payload", "MSIX output packages staged application files and generated manifest metadata.")
        };
        var appInstallerFeedUrl = EffectiveAppInstallerFeedUrl(project);
        results.Add(StoreReadinessChecker.CheckIdentityName(project.MsixIdentity));
        if (string.IsNullOrWhiteSpace(project.MsixPublisher))
            results.Add(Error("MSIX publisher", "Set the explicit certificate subject in MsixPublisher; display publisher is not package identity."));

        AddUnsupported(results, "Prerequisites", project.Prerequisites.Count,
            "Prerequisite chaining is not represented in current MSIX output. Use EXE/MSI suite output or remove prerequisites for MSIX.");
        AddUnsupported(results, "Prerequisite catalogs", project.PrerequisiteCatalogs.Count,
            "Prerequisite catalog expansion produces package.install resources, which are not represented in current MSIX output.");
        AddUnsupported(results, "Package nodes", project.Packages.Count,
            "Explicit package nodes are suite/chainer resources and are not represented in current MSIX output.");
        AddUnsupported(results, "Supersedence", project.DeploymentSupersedence.Count,
            "Deployment supersedence rules are not represented in current MSIX output.");
        AddUnsupported(results, "Registry", project.RegistryEntries.Count,
            "Classic registry writes are not represented in current MSIX output.");
        AddUnsupported(results, "Environment variables", project.EnvironmentVariables.Count,
            "Machine/user environment variable writes are not represented in current MSIX output.");
        AddUnsupported(results, "Shortcuts", project.Shortcuts.Count,
            "Arbitrary authored shortcuts are not represented in current MSIX output; MSIX exposes the app entry point from the manifest.");
        AddUnsupported(results, "Windows services", project.WindowsServices.Count,
            "Windows service installation is not represented in current MSIX output.");
        AddUnsupported(results, "Scheduled tasks", project.ScheduledTasks.Count,
            "Scheduled task installation is not represented in current MSIX output.");
        AddUnsupported(results, "Firewall rules", project.FirewallRules.Count,
            "Firewall rule installation is not represented in current MSIX output.");
        AddUnsupported(results, "File associations", project.FileAssociations.Count,
            "File association declarations are not represented in current MSIX output.");
        AddUnsupported(results, "Certificates", project.Certificates.Count,
            "Certificate installation is not represented in current MSIX output.");
        AddUnsupported(results, "COM registration", project.ComRegistrations.Count,
            "COM registration is not represented in current MSIX output.");
        AddUnsupported(results, "Drivers", project.DriverPackages.Count,
            "Driver installation is not represented in current MSIX output.");
        AddUnsupported(results, "Config transforms", project.ConfigTransforms.Count,
            "Configuration transforms are not represented in current MSIX output.");
        AddUnsupported(results, "IIS application pools", project.IisAppPools.Count,
            "IIS application pools are not represented in current MSIX output.");
        AddUnsupported(results, "IIS sites", project.IisSites.Count,
            "IIS sites are not represented in current MSIX output.");
        AddUnsupported(results, "Custom actions", project.CustomActions.Count,
            "Custom actions are not represented in current MSIX output.");

        if (project.MsixOptionalPackages.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(appInstallerFeedUrl))
            {
                results.Add(Error(
                    "MSIX optional packages",
                    "Optional related packages are emitted through the AppInstaller feed; configure AppUpdatesURL or a selected update channel FeedUrl, or remove the optional package entries."));
            }
            else
            {
                results.Add(Info(
                    "MSIX optional packages",
                    $"AppInstaller related set includes {project.MsixOptionalPackages.Count} optional package(s)."));
            }

            for (var i = 0; i < project.MsixOptionalPackages.Count; i++)
            {
                var package = project.MsixOptionalPackages[i];
                if (string.IsNullOrWhiteSpace(package.Name))
                    results.Add(Error($"MSIX optional packages[{i}].Name", "Optional package identity name is required."));
                if (string.IsNullOrWhiteSpace(package.Uri))
                    results.Add(Error($"MSIX optional packages[{i}].Uri", "Optional package URI is required."));
            }
        }

        for (var i = 0; i < project.Components.Count; i++)
        {
            var component = project.Components[i];
            var prefix = $"Components[{i}:{component.Id}]";
            if (!component.Required || !component.Selected || component.Conditions.Count > 0 || component.ConflictsWith.Count > 0)
            {
                results.Add(Error(
                    $"{prefix} selection",
                    "MSIX output has no installer component-selection UI or runtime component condition gate; make the component unconditional for MSIX or use EXE/MSI output."));
            }

            AddUnsupported(results, $"{prefix} registry", component.Registry.Count,
                "Component registry writes are not represented in current MSIX output.");
            AddUnsupported(results, $"{prefix} shortcuts", component.Shortcuts.Count,
                "Component shortcuts are not represented in current MSIX output.");
            AddUnsupported(results, $"{prefix} COM", component.ComRegistrations.Count,
                "Component COM registration is not represented in current MSIX output.");
            AddUnsupported(results, $"{prefix} GAC", component.GacAssemblies.Count,
                "GAC assembly installation is not represented in current MSIX output.");
        }

        if (string.IsNullOrWhiteSpace(appInstallerFeedUrl))
        {
            results.Add(new CheckResult
            {
                Name = "AppInstaller feed",
                Severity = Severity.Warning,
                Message = "AppUpdatesURL and selected update channel FeedUrl are empty, so the build will not emit a managed .appinstaller update feed."
            });
        }

        if (!project.HasCodeSigningCertificate)
        {
            results.Add(new CheckResult
            {
                Name = "MSIX signing",
                Severity = Severity.Warning,
                Message = "MSIX packages must be trusted/signed for enterprise deployment; configure PFX signing or a Windows certificate-store selector before release qualification."
            });
        }

        return results;
    }

    private static void AddUnsupported(List<CheckResult> results, string name, int count, string message)
    {
        if (count <= 0) return;
        results.Add(Error(name, $"{message} Authored count: {count}."));
    }

    private static CheckResult Info(string name, string message)
        => new() { Name = name, Severity = Severity.Info, Message = message };

    private static CheckResult Error(string name, string message)
        => new() { Name = name, Severity = Severity.Error, Message = message };

    private static string EffectiveAppInstallerFeedUrl(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.AppUpdateChannel))
        {
            var selected = project.UpdateChannels.FirstOrDefault(channel =>
                channel.Id.Equals(project.AppUpdateChannel, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selected?.FeedUrl))
                return selected.FeedUrl;
        }

        return project.AppUpdatesURL;
    }
}
