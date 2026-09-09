using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Extensibility;

/// <summary>How an input should be collected from the author.</summary>
public enum ResourceInputKind
{
    Text,
    MultiLine,
    FilePath,
    FolderPath,
    Bool,
    Integer,
    Choice,
    Secret,
}

/// <summary>
/// One author-facing input of a resource provider.
/// </summary>
/// <param name="ChoiceEnum">
/// When the provider parses the value with <c>Enum.TryParse</c>, the enum type it parses into. The
/// options are then read from the type itself, so the offered list cannot drift away from the list
/// the provider will actually accept.
/// </param>
public sealed record ResourceInput(
    string Key,
    string Label,
    ResourceInputKind Kind = ResourceInputKind.Text,
    bool Required = false,
    string Help = "",
    string Default = "",
    Type? ChoiceEnum = null,
    string[]? Choices = null)
{
    /// <summary>The options to offer, whether they came from an enum or a literal list.</summary>
    public IReadOnlyList<string> Options =>
        ChoiceEnum != null ? Enum.GetNames(ChoiceEnum) : (Choices ?? Array.Empty<string>());
}

/// <summary>
/// A repeatable group of inputs stored as <c>{Prefix}.{index}.{field}</c> with a
/// <c>{CountKey}</c> holding how many there are.
/// </summary>
public sealed record ResourceInputList(
    string CountKey,
    string Prefix,
    string Label,
    IReadOnlyList<ResourceInput> Fields,
    string Help = "");

/// <summary>One resource provider, described well enough to build an editor for it.</summary>
public sealed record ResourceTypeDescriptor(
    string Type,
    string Label,
    string Summary,
    IReadOnlyList<ResourceInput> Inputs,
    IReadOnlyList<ResourceInputList>? Lists = null)
{
    public IReadOnlyList<ResourceInputList> Repeatables => Lists ?? Array.Empty<ResourceInputList>();
}

/// <summary>
/// What each typed resource provider actually reads, in a form a UI can render.
///
/// A <c>CompiledInstallOperation</c> carries its arguments in a <c>SortedDictionary&lt;string,
/// string&gt;</c>. That is the right runtime shape and an impossible authoring one: the builder
/// showed the dictionary raw, so composing an operation meant knowing both the provider's type
/// string and every key it reads — none of which appears anywhere in the UI. Eighteen providers were
/// reachable in principle and unauthorable in practice.
///
/// This is the missing half: the keys, with labels, help, requiredness and the option lists the
/// providers will accept. <c>ResourceCatalogCoverageTests</c> holds it to the providers themselves,
/// so a key added to a provider and not described here fails the build rather than quietly
/// reappearing as an undocumented dictionary entry.
/// </summary>
public static class ResourceInputCatalog
{
    /// <summary>
    /// Keys a provider reads that are <b>not</b> author input, and why.
    ///
    /// Without this the coverage test would demand descriptors for values the author must never
    /// supply — rollback snapshots the provider writes itself, install-context variables, and the
    /// counts that back a repeatable group.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NonAuthoredKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayloadRoot"] = "install-context variable, not an operation input",
        ["Architecture"] = "install-context variable set by the host, not an operation input",
        ["OfflineLayoutDirectory"] = "install-context variable, not an operation input",
        ["bindingCount"] = "managed by the bindings list",
        ["parameterCount"] = "managed by the parameters list",
    };

    /// <summary>A key prefix that is provider-written rollback state rather than author input.</summary>
    public const string RollbackStatePrefix = "previous.";

    private static readonly ResourceInput[] None = Array.Empty<ResourceInput>();

    private static readonly ResourceTypeDescriptor[] Descriptors =
    {
        new("file.copy", "Copy a file", "Copies a file from the payload into the install location.",
        new ResourceInput[]
        {
            new("source", "Source", ResourceInputKind.FilePath, Required: true,
                Help: "Path inside the payload. Relative paths are resolved against the payload root."),
            new("destination", "Destination", ResourceInputKind.FilePath, Required: true,
                Help: "Where the file lands. Use {app} for the install folder — the destination must stay "
                      + "under it, and a path outside is rejected."),
            new("overwrite", "Overwrite if present", ResourceInputKind.Bool, Default: "true"),
            new("skipIfNewer", "Skip if the existing file is newer", ResourceInputKind.Bool),
            new("required", "Fail the install if this file is missing", ResourceInputKind.Bool, Default: "true"),
        }),

        new("registry.write", "Write a registry value", "Creates or updates one registry value, and restores the previous one on rollback.",
        new ResourceInput[]
        {
            new("keyPath", "Key path", Required: true,
                Help: @"Hive-relative, for example Software\Contoso. The hive is chosen by the install "
                      + @"scope, so naming one here (HKLM\...) is rejected."),
            new("valueName", "Value name", Help: "Leave empty to write the key's default value."),
            new("value", "Value"),
            new("valueKind", "Value kind", ResourceInputKind.Choice, ChoiceEnum: typeof(RegistryValueKind),
                Default: "String"),
        }),

        new("shortcut.create", "Create a shortcut", "Adds a shortcut, and removes it again on uninstall.",
        new ResourceInput[]
        {
            new("name", "Name", Required: true, Help: "The caption the user sees."),
            new("targetPath", "Target", ResourceInputKind.FilePath, Required: true),
            new("location", "Location", ResourceInputKind.Choice, ChoiceEnum: typeof(ShortcutLocation),
                Default: "StartMenu"),
            new("startMenuSubfolder", "Start menu subfolder",
                Help: "Only used for Start menu shortcuts."),
            new("arguments", "Arguments"),
            new("workingDirectory", "Working directory", ResourceInputKind.FolderPath),
            new("iconPath", "Icon", ResourceInputKind.FilePath),
        }),

        new("environment.set", "Set an environment variable", "Writes a variable at install and removes it at uninstall.",
        new ResourceInput[]
        {
            new("name", "Name", Required: true),
            new("value", "Value"),
            new("scope", "Scope", ResourceInputKind.Choice, ChoiceEnum: typeof(EnvironmentVariableTarget),
                Default: "Machine",
                Help: "Machine scope needs administrator rights."),
        }),

        new("service.install", "Install a Windows service", "Registers a service, optionally starting it after install.",
        new ResourceInput[]
        {
            new("name", "Service name", Required: true, Help: "The short name sc.exe uses."),
            new("displayName", "Display name"),
            new("description", "Description", ResourceInputKind.MultiLine),
            new("executablePath", "Executable", ResourceInputKind.FilePath, Required: true),
            new("arguments", "Arguments"),
            new("startMode", "Start mode", ResourceInputKind.Choice, Default: "auto",
                Choices: new[] { "auto", "demand", "disabled" }),
            new("account", "Account", ResourceInputKind.Choice, Default: "localsystem",
                Choices: new[] { "localsystem", "localservice", "networkservice", "user" }),
            new("username", "User name", Help: "Only when the account is 'user'."),
            new("password", "Password", ResourceInputKind.Secret),
            new("dependsOn", "Depends on", Help: "Comma-separated service names."),
            new("startAfterInstall", "Start after install", ResourceInputKind.Bool, Default: "true"),
            new("stopOnUninstall", "Stop on uninstall", ResourceInputKind.Bool, Default: "true"),
            new("failureRestartDelaySeconds", "Restart delay after failure (seconds)", ResourceInputKind.Integer),
        }),

        new("scheduled-task.create", "Create a scheduled task", "Registers a Windows scheduled task.",
        new ResourceInput[]
        {
            new("name", "Task name", Required: true),
            new("executablePath", "Executable", ResourceInputKind.FilePath, Required: true),
            new("arguments", "Arguments"),
            new("workingDirectory", "Working directory", ResourceInputKind.FolderPath),
            new("trigger", "Trigger", ResourceInputKind.Choice, Default: "once",
                Choices: new[] { "once", "daily", "onstartup", "onlogon" }),
            new("startTime", "Start time", Help: "HH:mm, for triggers that need one."),
            new("runElevated", "Run with highest privileges", ResourceInputKind.Bool),
            new("username", "Run as user"),
            new("password", "Password", ResourceInputKind.Secret),
            new("enabled", "Enabled", ResourceInputKind.Bool, Default: "true"),
            new("stopOnUninstall", "Remove on uninstall", ResourceInputKind.Bool, Default: "true"),
        }),

        new("firewall.rule", "Add a firewall rule", "Creates a Windows Firewall rule through netsh advfirewall.",
        new ResourceInput[]
        {
            new("name", "Rule name", Required: true),
            new("description", "Description"),
            new("direction", "Direction", ResourceInputKind.Choice, Default: "in",
                Choices: new[] { "in", "out" }),
            new("action", "Action", ResourceInputKind.Choice, Default: "allow",
                Choices: new[] { "allow", "block" }),
            new("protocol", "Protocol", ResourceInputKind.Choice, Default: "tcp",
                Choices: new[] { "tcp", "udp", "any" },
                Help: "Protocol 'any' cannot be combined with explicit ports."),
            new("localPort", "Local port"),
            new("remotePort", "Remote port"),
            new("program", "Program", ResourceInputKind.FilePath),
            new("service", "Service"),
            new("profile", "Profile", Default: "any",
                Help: "any, domain, private, public, or a comma-separated combination."),
            new("enabled", "Enabled", ResourceInputKind.Bool, Default: "true"),
        }),

        new("certificate.install", "Install a certificate", "Imports a certificate into a Windows certificate store.",
        new ResourceInput[]
        {
            new("sourcePath", "Certificate file", ResourceInputKind.FilePath, Required: true),
            new("password", "Password", ResourceInputKind.Secret, Help: "Only for .pfx files."),
            new("storeName", "Store name", ResourceInputKind.Choice, Default: "My",
                Choices: new[] { "My", "Root", "CA", "TrustedPublisher", "TrustedPeople" }),
            new("storeLocation", "Store location", ResourceInputKind.Choice, Default: "LocalMachine",
                Choices: new[] { "CurrentUser", "LocalMachine" }),
            new("thumbprint", "Expected thumbprint",
                Help: "Checked after import; leave empty to skip the check."),
            new("friendlyName", "Friendly name"),
        }),

        new("com.register", "Register a COM server", "Registers a COM server and its ProgIDs.",
        new ResourceInput[]
        {
            new("serverPath", "Server", ResourceInputKind.FilePath, Required: true),
            new("clsid", "CLSID", Help: "Registry-form GUID, including braces."),
            new("progId", "ProgID"),
            new("versionIndependentProgId", "Version-independent ProgID"),
            new("description", "Description"),
            new("threadingModel", "Threading model", ResourceInputKind.Choice, Default: "Apartment",
                Choices: new[] { "Apartment", "Both", "Free", "Neutral" }),
            new("serverType", "Server type", ResourceInputKind.Choice, Default: "InprocServer32",
                Choices: new[] { "InprocServer32", "LocalServer32" }),
            new("typeLibId", "Type library id"),
            new("version", "Version"),
            new("arguments", "Arguments"),
        }),

        new("file-association.register", "Register a file association", "Associates an extension with this application.",
        new ResourceInput[]
        {
            new("extension", "Extension", Required: true, Help: "Including the dot, for example .contoso."),
            new("progId", "ProgID", Required: true),
            new("executablePath", "Opens with", ResourceInputKind.FilePath, Required: true),
            new("arguments", "Arguments", Default: "\"%1\""),
            new("description", "Description"),
            new("iconPath", "Icon", ResourceInputKind.FilePath),
            new("verb", "Verb", Default: "open"),
            new("verbDisplayName", "Verb caption"),
            new("contentType", "MIME content type"),
            new("perceivedType", "Perceived type",
                Help: "For example text, image, audio, video, compressed."),
        }),

        new("config.transform", "Edit a configuration file", "Sets or removes one value in an installed JSON, XML or INI file.",
        new ResourceInput[]
        {
            new("targetPath", "File", ResourceInputKind.FilePath, Required: true),
            new("format", "Format", ResourceInputKind.Choice, Default: "json",
                Choices: new[] { "json", "xml", "ini" }),
            new("operation", "Operation", ResourceInputKind.Choice, Default: "set",
                Choices: new[] { "set", "remove" }),
            new("keyPath", "Key path", Required: true,
                Help: "Dotted path for JSON, XPath for XML, key name for INI."),
            new("section", "Section", Help: "INI only."),
            new("value", "Value"),
            new("backupOnInstall", "Back up before editing", ResourceInputKind.Bool, Default: "true"),
            new("restoreOnRollback", "Restore the backup on rollback", ResourceInputKind.Bool, Default: "true"),
        }),

        new("driver.package", "Install a driver package", "Stages and installs a driver package with pnputil.",
        new ResourceInput[]
        {
            new("infPath", "INF file", ResourceInputKind.FilePath, Required: true),
            new("className", "Device class"),
            new("kind", "Kind", ResourceInputKind.Choice, Default: "pnp",
                Choices: new[] { "pnp", "legacy" }),
            new("installDevices", "Install matching devices now", ResourceInputKind.Bool),
            new("publishedName", "Published name",
                Help: "Filled in by Windows after staging; set it only to force removal of a known driver."),
        }),

        new("package.install", "Chain another installer", "Runs a nested MSI/EXE package, with detection and retry.",
        new ResourceInput[]
        {
            new("id", "Package id", Required: true),
            new("packageType", "Package type", ResourceInputKind.Choice, Default: "exe",
                Choices: new[] { "exe", "msi", "msp", "msu" }),
            new("sourcePath", "Local package", ResourceInputKind.FilePath,
                Help: "Used when the package ships inside the payload."),
            new("downloadUrl", "Download URL", Help: "Used when the package is fetched at install time."),
            new("downloadUrlX86", "Download URL (x86)",
                Help: "Used instead of Download URL when installing on x86."),
            new("sha256", "Expected SHA-256"),
            new("sha512", "Expected SHA-512"),
            new("sha512X86", "Expected SHA-512 (x86)",
                Help: "Checked instead of SHA-512 when installing on x86."),
            new("installArgs", "Install arguments"),
            new("uninstallArgs", "Uninstall arguments"),
            new("uninstallCommand", "Uninstall command"),
            new("detectionCommand", "Detection command",
                Help: "Run before installing; a match means the package is already present."),
            new("detectionPattern", "Detection pattern"),
            new("detectionCommandX86", "Detection command (x86)"),
            new("detectionPatternX86", "Detection pattern (x86)"),
            new("mandatory", "Fail the install if this package fails", ResourceInputKind.Bool, Default: "true"),
            new("retryCount", "Retries", ResourceInputKind.Integer, Default: "0"),
            new("timeoutSeconds", "Timeout (seconds)", ResourceInputKind.Integer),
        }),

        new("component.select", "Preselect a component", "Sets whether a component starts selected, or is mandatory.",
        new ResourceInput[]
        {
            new("id", "Component id", Required: true),
            new("selected", "Selected by default", ResourceInputKind.Bool, Default: "true"),
            new("required", "Cannot be deselected", ResourceInputKind.Bool),
        }),

        new("iis.appPool", "Create an IIS application pool", "Adds an application pool and its identity.",
        new ResourceInput[]
        {
            new("name", "Pool name", Required: true),
            new("runtimeVersion", "CLR version", ResourceInputKind.Choice, Default: "v4.0",
                Choices: new[] { "v4.0", "v2.0", "" },
                Help: "Empty means No Managed Code."),
            new("pipelineMode", "Pipeline mode", ResourceInputKind.Choice, Default: "Integrated",
                Choices: new[] { "Integrated", "Classic" }),
            new("identity", "Identity", ResourceInputKind.Choice, Default: "ApplicationPoolIdentity",
                Choices: new[] { "ApplicationPoolIdentity", "LocalSystem", "LocalService", "NetworkService", "SpecificUser" }),
            new("username", "User name", Help: "Only when the identity is SpecificUser."),
            new("password", "Password", ResourceInputKind.Secret),
            new("startAfterInstall", "Start after install", ResourceInputKind.Bool, Default: "true"),
        }),

        new("iis.site", "Create an IIS site", "Adds a site, its physical path and its bindings.",
        new ResourceInput[]
        {
            new("name", "Site name", Required: true),
            new("physicalPath", "Physical path", ResourceInputKind.FolderPath, Required: true),
            new("applicationPool", "Application pool"),
            new("startAfterInstall", "Start after install", ResourceInputKind.Bool, Default: "true"),
            new("removeOnUninstall", "Remove on uninstall", ResourceInputKind.Bool, Default: "true"),
        },
        new ResourceInputList[]
        {
            new("bindingCount", "binding", "Bindings",
            new ResourceInput[]
            {
                new("protocol", "Protocol", ResourceInputKind.Choice, Default: "http",
                    Choices: new[] { "http", "https" }),
                new("ipAddress", "IP address", Default: "*"),
                new("port", "Port", ResourceInputKind.Integer, Default: "80"),
                new("host", "Host name"),
                new("certificateThumbprint", "Certificate thumbprint", Help: "https bindings only."),
                new("certificateStoreName", "Certificate store", Default: "My"),
                new("sslFlags", "SSL flags", ResourceInputKind.Integer),
            },
            Help: "A site with no binding cannot be reached."),
        }),

        new("webdeploy.package", "Deploy a Web Deploy package", "Runs msdeploy against a .zip package.",
        new ResourceInput[]
        {
            new("name", "Name", Required: true),
            new("packagePath", "Package", ResourceInputKind.FilePath, Required: true),
            new("siteName", "Target site"),
            new("destination", "Destination", Help: "msdeploy destination, when it is not a site name."),
            new("removeOnUninstall", "Remove on uninstall", ResourceInputKind.Bool),
        },
        new ResourceInputList[]
        {
            new("parameterCount", "parameter", "Package parameters",
            new ResourceInput[]
            {
                new("name", "Name"),
                new("value", "Value"),
            },
            Help: "setParam values passed to msdeploy."),
        }),
    };

    /// <summary>Every described provider type, in the order the wizard offers them.</summary>
    public static IReadOnlyList<ResourceTypeDescriptor> All => Descriptors;

    public static bool TryGet(string type, out ResourceTypeDescriptor descriptor)
    {
        descriptor = Descriptors.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase))!;
        return descriptor != null;
    }

    /// <summary>True when the key is provider-managed rather than something the author supplies.</summary>
    public static bool IsNonAuthored(string key)
        => NonAuthoredKeys.ContainsKey(key) || key.StartsWith(RollbackStatePrefix, StringComparison.Ordinal);
}
