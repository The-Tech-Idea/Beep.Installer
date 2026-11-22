namespace Beep.Installer.Ui;

/// <summary>Maps canonical diagnostic paths to editor sections without discarding row identity.</summary>
internal static class AuthoringDiagnosticNavigation
{
    internal static string SectionForPath(string path)
    {
        var root = path.Split(new[] { '[', '.' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (root == "Setup")
        {
            var property = path.StartsWith("Setup.", StringComparison.Ordinal)
                ? path[6..].Split(new[] { '[', '.' }, StringSplitOptions.None)[0] : "";
            if (property.StartsWith("Msix", StringComparison.Ordinal) || property == "OutputFormat") return "package";
            if (property == "AppUpdatesURL") return "package";
            if (property.StartsWith("CodeSign", StringComparison.Ordinal)) return "codesign";
            if (property is "SourceIncludes" or "SourceExcludes") return "includes";
            if (property.StartsWith("Source", StringComparison.Ordinal)) return "source";
            if (property is "OutputDir" or "OutputBaseFilename" or "MainExecutable") return "output";
            if (property is "DefaultDirName" or "DefaultScope" or "PrivilegesRequired" or "AllowScopeSelection") return "layout";
            if (property == "AppUpdateChannel") return "updatechannels";
            return "identity";
        }
        return root switch
        {
            "Components" or "Files" => "components",
            "Prerequisites" => "prerequisites", "PrerequisiteCatalogs" => "prereqcatalogs",
            "Packages" => "packages", "Shortcuts" => "shortcuts", "RegistryEntries" => "registry",
            "WindowsServices" => "windowsservices", "ScheduledTasks" => "scheduledtasks",
            "FirewallRules" => "firewallrules", "FileAssociations" => "fileassociations",
            "EnvironmentVariables" => "environment", "Certificates" => "certificates",
            "ComRegistrations" => "comregistrations", "DriverPackages" => "driverpackages",
            "ConfigTransforms" => "configtransforms", "IisAppPools" => "iisapppools",
            "IisSites" => "iissites", "WebDeployPackages" => "webdeploy",
            "UpdateChannels" => "updatechannels", "DeploymentSupersedence" => "supersedence",
            "Resources" => "resources", "CustomActions" => "customactions", "CustomPages" => "custompages", "WizardPages" => "wizardpages",
            _ => "build"
        };
    }
}
