using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

internal sealed record AuthoringHelpTopic(string Id, string TitleKey, string Fallback, string BodyKey, string? GuidedKey, string Aliases)
{
    internal string Title => LanguageManager.GetOrDefault(TitleKey, Fallback);
    internal string Body => LanguageManager.GetString(BodyKey);
    internal string LocalAliases => LanguageManager.GetOrDefault("HelpAliases_" + Id, "");
    internal string Availability => GuidedKey is null ? LanguageManager.GetString("Help_AdvancedOnly")
        : LanguageManager.Tf("Help_BothViews", LanguageManager.GetString(GuidedKey));
}

internal static class AuthoringHelpCatalog
{
    private static AuthoringHelpTopic T(string id, string title, string fallback, string body, string? guided, string aliases)
        => new(id, title, fallback, "HelpBody_" + body, guided, aliases);
    internal static IReadOnlyList<AuthoringHelpTopic> Topics { get; } = Array.AsReadOnly(new[]
    {
        T("identity", "Help_Identity", "Identity", "Project", "Guided_Project", "product name version publisher upgrade appid application id"),
        T("layout", "Help_Layout", "Layout", "Install", "Guided_Install", "install installation location folder destination elevation administrator per user machine scope"),
        T("eula", "Help_Eula", "EULA", "Branding", null, "license agreement legal terms consent"),
        T("source", "Help_Files", "Files", "FilesBody", "Guided_Files", "scan source payload application folder binaries executable dll"),
        T("includes", "Help_Includes", "Includes", "FilesBody", "Guided_Files", "glob wildcard include exclude ignore filter patterns"),
        T("components", "Help_Components", "Components", "FilesBody", "Guided_Files", "optional feature tree conditions selected files"),
        T("prerequisites", "Section_Prerequisites", "Prerequisites", "PrerequisitesBody", "Guided_Install", "dependency dependencies runtime dotnet net framework redistributable vc++ vcredist"),
        T("shortcuts", "Section_Shortcuts", "Shortcuts", "Install", "Guided_Install", "desktop icon start menu startup link"),
        T("registry", "Section_Registry", "Registry", "Install", "Guided_Install", "regedit hklm hkcu registry key value"),
        T("scheduledtasks", "Section_ScheduledTasks", "Scheduled Tasks", "Tasks", null, "scheduled job cron timer task scheduler trigger"),
        T("firewallrules", "Section_FirewallRules", "Firewall Rules", "Network", null, "open port inbound outbound tcp udp network exception firewall"),
        T("certificates", "Section_Certificates", "Certificates", "ResourcesBody", null, "tls ssl pfx pem trust certificate store"),
        T("comregistrations", "Section_ComRegistrations", "COM", "ResourcesBody", null, "activex regsvr32 clsid progid typelib"),
        T("driverpackages", "Section_DriverPackages", "Drivers", "ResourcesBody", null, "hardware device inf sys reboot driver"),
        T("configtransforms", "Section_ConfigTransforms", "Config Transforms", "ResourcesBody", null, "edit json xml ini configuration replace transform"),
        T("iisapppools", "Section_IisAppPools", "IIS App Pools", "Iis", null, "http web server hosting application pool identity iis"),
        T("iissites", "Section_IisSites", "IIS Sites", "Iis", null, "http web server website hosting bindings https iis"),
        T("webdeploy", "Section_WebDeploy", "Web Deploy", "Iis", null, "msdeploy web publish site hosting parameters"),
        T("environment", "Section_EnvironmentVariables", "Environment", "ResourcesBody", null, "env path variable system user environment"),
        T("fileassociations", "Section_FileAssociations", "File Associations", "ResourcesBody", null, "file extension default program open with verb"),
        T("windowsservices", "Section_WindowsServices", "Windows Services", "Service", null, "daemon background process service host start stop account"),
        T("resources", "Section_Resources", "Typed Resources", "ResourcesBody", null, "provider extension plugin custom operation resources"),
        T("branding", "Help_Branding", "Branding", "Branding", null, "logo banner theme color icon appearance"),
        T("wizardpages", "Help_WizardPages", "Wizard Pages", "Branding", null, "installer page order preview welcome ready finish"),
        T("custompages", "Section_CustomPages", "Custom Pages", "Branding", null, "custom field input password checkbox extra page"),
        T("output", "Help_Output", "Output", "Packaging", "Guided_Package", "artifact setup name output destination release folder"),
        T("payload", "Help_Payload", "Payload", "FilesBody", null, "offline download url single file self contained bundle payload"),
        T("compression", "Help_Compression", "Compression", "Packaging", null, "zip size solid compress level method"),
        T("package", "Help_Package", "Package Format", "Packaging", "Guided_Package", "exe msi msix clickonce format output track"),
        T("codesign", "Section_CodeSigning", "Code Signing", "Signing", "Guided_Signing", "digital signature authenticode timestamp sign unsigned certificate credential"),
        T("msix", "Help_Msix", "MSIX", "Packaging", null, "appx store identity publisher manifest sandbox"),
        T("msixoptional", "Section_MsixOptionalPackages", "MSIX Optional", "Packaging", null, "optional msix family package add on"),
        T("packages", "Section_Packages", "Package Nodes", "ResourcesBody", null, "nested sub package chain install dependency"),
        T("updatechannels", "Section_UpdateChannels", "Update Channels", "Updates", null, "update feed release ring rollout delta rollback upgrade"),
        T("prereqcatalogs", "Section_PrerequisiteCatalogs", "Prerequisite Catalogs", "PrerequisitesBody", null, "signed catalog runtime dependency trusted key"),
        T("supersedence", "Section_Supersedence", "Supersedence", "Updates", null, "replace previous product upgrade intune configmgr sccm"),
        T("script", "Help_Script", "Script", "BuildBody", null, "bsetup canonical edit script text export"),
        T("build", "Help_Build", "Build Workflow", "BuildBody", "Guided_Review", "validate validation diagnostics readiness blocking warning review compile build"),
        T("log", "Help_Log", "Build Log", "BuildBody", null, "error failure troubleshoot diagnostic support log"),
        T("result", "Help_Result", "Last Result", "BuildBody", "Guided_Result", "artifact result package folder copy path signing evidence"),
    });
    internal static AuthoringHelpTopic Find(string? id) => Topics.FirstOrDefault(topic => topic.Id == id) ?? Topics[0];
    internal static bool Matches(AuthoringHelpTopic topic, string query, params string[] additional)
    {
        var text = string.Join(" ", additional.Append(topic.Id).Append(topic.Title).Append(topic.Fallback).Append(topic.Aliases).Append(topic.LocalAliases).Append(topic.Body).Append(topic.Availability));
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }
}
