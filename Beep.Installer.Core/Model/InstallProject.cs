using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;

namespace Beep.Installer.Models
{
    // ── Enums ──

    public enum PrivilegeLevel
    {
        Admin,
        Lowest,
        User
    }

    public enum InstallationScope
    {
        Machine,
        User
    }

    public enum Architecture
    {
        X64,
        X86,
        Arm64,
        AnyCPU,
        X64Compatible,
        X86Compatible
    }

    public enum ArchitectureMode
    {
        X64,
        X86,
        Arm64,
        X64Compatible,
        X86Compatible
    }

    public enum CompressionStrength
    {
        Store = 0,
        Fast = 3,
        Default = 6,
        Maximum = 9
    }

    public enum InstallerOutputFormat
    {
        Exe,
        Msix,
        MsixBundle
    }

    public enum MsixRelatedPackageKind
    {
        Package,
        Bundle
    }

    public enum PayloadSourceType
    {
        Local,
        Url
    }

    public enum CompressionFormat
    {
        Zip,
        Lzma2
    }

    public enum WizardTheme
    {
        Modern,
        Classic,
        Compact
    }

    public enum WindowsServiceStartMode
    {
        Auto,
        DelayedAuto,
        Manual,
        Disabled
    }

    public enum WindowsServiceAccount
    {
        LocalSystem,
        LocalService,
        NetworkService,
        User
    }

    public enum ScheduledTaskTrigger
    {
        OnLogon,
        OnStartup,
        Daily,
        Once
    }

    public enum FirewallRuleDirection
    {
        In,
        Out
    }

    public enum FirewallRuleAction
    {
        Allow,
        Block
    }

    public enum FirewallRuleProtocol
    {
        Any,
        Tcp,
        Udp
    }

    public enum ComServerType
    {
        InProc,
        LocalServer
    }

    public enum DriverPackageRebootBehavior
    {
        Possible,
        Required,
        Suppress
    }

    public enum DriverPackageKind
    {
        Pnp,
        Kernel,
        FileSystem
    }

    public enum DriverPackageStartMode
    {
        Boot,
        System,
        Automatic,
        Demand,
        Disabled
    }

    public enum DriverPackageErrorControl
    {
        Ignore,
        Normal,
        Severe,
        Critical
    }

    public enum ConfigTransformFormat
    {
        Json,
        Xml,
        Ini
    }

    public enum ConfigTransformOperation
    {
        Set,
        Delete
    }

    public enum IisManagedPipelineMode
    {
        Integrated,
        Classic
    }

    public enum IisBindingProtocol
    {
        Http,
        Https
    }

    public enum DeploymentSupersedenceMode
    {
        Replace,
        Update,
        BlockDowngrade
    }

    public enum PackageNodeType
    {
        Exe,
        Msi,
        Msp,
        Msu
    }

    // InstallationType and UpdateMode come from TheTechIdea.Beep.Installer — this file used
    // to shadow them with local ...Ex duplicates that existed only to be mapped back.

    // ── InstallProject ──

    public class InstallProject : INotifyPropertyChanged
    {
        public const string CurrentSchemaVersion = "1.0";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDirty;
        public bool IsDirty => _isDirty;

        public void MarkClean() => _isDirty = false;
    public void MarkDirty() { _isDirty = true; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty))); }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            _isDirty = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        // ── Script metadata ──

        private string _schemaVersion = CurrentSchemaVersion;
        public string SchemaVersion
        {
            get => _schemaVersion;
            set => SetProperty(ref _schemaVersion, value);
        }

        private string _projectName = "NewProject";
        public string ProjectName
        {
            get => _projectName;
            set => SetProperty(ref _projectName, value);
        }

        private string _createdAt = DateTime.UtcNow.ToString("o");
        public string CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        private string _modifiedAt = DateTime.UtcNow.ToString("o");
        public string ModifiedAt
        {
            get => _modifiedAt;
            set => SetProperty(ref _modifiedAt, value);
        }

        // ── Product identity ──

        private string _appId = "";
        public string AppId
        {
            get => _appId;
            set => SetProperty(ref _appId, value);
        }

        private string _appName = "Beep Application";
        public string AppName
        {
            get => _appName;
            set => SetProperty(ref _appName, value);
        }

        private string _appVersion = "1.0.0";
        public string AppVersion
        {
            get => _appVersion;
            set => SetProperty(ref _appVersion, value);
        }

        private string _appPublisher = "The Tech Idea";
        public string AppPublisher
        {
            get => _appPublisher;
            set => SetProperty(ref _appPublisher, value);
        }

        private string _appPublisherURL = "";
        public string AppPublisherURL
        {
            get => _appPublisherURL;
            set => SetProperty(ref _appPublisherURL, value);
        }

        private string _appSupportURL = "";
        public string AppSupportURL
        {
            get => _appSupportURL;
            set => SetProperty(ref _appSupportURL, value);
        }

        private string _appSupportEmail = "";
        public string AppSupportEmail
        {
            get => _appSupportEmail;
            set => SetProperty(ref _appSupportEmail, value);
        }

        private string _appUpdatesURL = "";
        public string AppUpdatesURL
        {
            get => _appUpdatesURL;
            set => SetProperty(ref _appUpdatesURL, value);
        }

        private UpdateMode _appUpdateMode = UpdateMode.Optional;
        public UpdateMode AppUpdateMode
        {
            get => _appUpdateMode;
            set => SetProperty(ref _appUpdateMode, value);
        }

        private string _appUpdateChannel = "";
        public string AppUpdateChannel
        {
            get => _appUpdateChannel;
            set => SetProperty(ref _appUpdateChannel, value);
        }

        private int _appInstallerHoursBetweenUpdateChecks = 24;
        public int AppInstallerHoursBetweenUpdateChecks
        {
            get => _appInstallerHoursBetweenUpdateChecks;
            set => SetProperty(ref _appInstallerHoursBetweenUpdateChecks, Math.Max(0, value));
        }

        private bool _appInstallerShowPrompt = true;
        public bool AppInstallerShowPrompt
        {
            get => _appInstallerShowPrompt;
            set => SetProperty(ref _appInstallerShowPrompt, value);
        }

        private bool _appInstallerForceUpdateFromAnyVersion;
        public bool AppInstallerForceUpdateFromAnyVersion
        {
            get => _appInstallerForceUpdateFromAnyVersion;
            set => SetProperty(ref _appInstallerForceUpdateFromAnyVersion, value);
        }

        private bool _sideBySide;
        /// <summary>
        /// When true the app installs into <c>&lt;dir&gt;\app-&lt;version&gt;\</c> with a
        /// <c>&lt;dir&gt;\current</c> junction that shortcuts point at — so a later delta update
        /// materializes the new version beside the old and flips the junction, never overwriting
        /// the running files. Off by default (a plain flat install). Turn on for apps that
        /// self-update via <c>TheTechIdea.Beep.Updates</c>.
        /// </summary>
        public bool SideBySide
        {
            get => _sideBySide;
            set => SetProperty(ref _sideBySide, value);
        }

        private string _appCopyright = "";
        public string AppCopyright
        {
            get => _appCopyright;
            set => SetProperty(ref _appCopyright, value);
        }

        // ── Install layout ──

        private string _defaultDirName = "";
        public string DefaultDirName
        {
            get => _defaultDirName;
            set => SetProperty(ref _defaultDirName, value);
        }

        private string _defaultGroupName = "";
        public string DefaultGroupName
        {
            get => _defaultGroupName;
            set => SetProperty(ref _defaultGroupName, value);
        }

        private PrivilegeLevel _privilegesRequired = PrivilegeLevel.Admin;
        public PrivilegeLevel PrivilegesRequired
        {
            get => _privilegesRequired;
            set => SetProperty(ref _privilegesRequired, value);
        }

        private bool _privilegesRequiredOverridesAllowed;
        public bool PrivilegesRequiredOverridesAllowed
        {
            get => _privilegesRequiredOverridesAllowed;
            set => SetProperty(ref _privilegesRequiredOverridesAllowed, value);
        }

        private InstallationType _defaultInstallType = InstallationType.Typical;
        public InstallationType DefaultInstallType
        {
            get => _defaultInstallType;
            set => SetProperty(ref _defaultInstallType, value);
        }

        private bool _prefer64Bit = true;
        public bool Prefer64Bit
        {
            get => _prefer64Bit;
            set => SetProperty(ref _prefer64Bit, value);
        }

        private bool _allowScopeSelection = true;
        public bool AllowScopeSelection
        {
            get => _allowScopeSelection;
            set => SetProperty(ref _allowScopeSelection, value);
        }

        private InstallationScope _defaultScope = InstallationScope.Machine;
        public InstallationScope DefaultScope
        {
            get => _defaultScope;
            set => SetProperty(ref _defaultScope, value);
        }

        private bool _allowNoIcons;
        public bool AllowNoIcons
        {
            get => _allowNoIcons;
            set => SetProperty(ref _allowNoIcons, value);
        }

        private bool _alwaysShowDirOnReadyPage = true;
        public bool AlwaysShowDirOnReadyPage
        {
            get => _alwaysShowDirOnReadyPage;
            set => SetProperty(ref _alwaysShowDirOnReadyPage, value);
        }

        // ── Source ──

        private string _sourceDirectory = "";
        public string SourceDirectory
        {
            get => _sourceDirectory;
            set => SetProperty(ref _sourceDirectory, value);
        }

        private ObservableCollection<string> _sourceIncludes = new() { "**/*" };
        public ObservableCollection<string> SourceIncludes
        {
            get => _sourceIncludes;
            set => SetProperty(ref _sourceIncludes, value);
        }

        private ObservableCollection<string> _sourceExcludes = new() { "**/*.pdb", "**/*.log", "**/appsettings.Development.json" };
        public ObservableCollection<string> SourceExcludes
        {
            get => _sourceExcludes;
            set => SetProperty(ref _sourceExcludes, value);
        }

        // ── EULA ──

        private string _licenseFile = "";
        public string LicenseFile
        {
            get => _licenseFile;
            set => SetProperty(ref _licenseFile, value);
        }

        private string _licenseText = "";
        public string LicenseText
        {
            get => _licenseText;
            set => SetProperty(ref _licenseText, value);
        }

        private bool _showEula = true;
        public bool ShowEula
        {
            get => _showEula;
            set => SetProperty(ref _showEula, value);
        }

        // ── Wizard window ──

        private string _windowTitle = "";
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        private string _welcomeTitle = "";
        public string WelcomeTitle
        {
            get => _welcomeTitle;
            set => SetProperty(ref _welcomeTitle, value);
        }

        // ── Branding assets ──

        private string _setupIconFile = "";
        public string SetupIconFile
        {
            get => _setupIconFile;
            set => SetProperty(ref _setupIconFile, value);
        }

        private string _wizardImageFile = "";
        public string WizardImageFile
        {
            get => _wizardImageFile;
            set => SetProperty(ref _wizardImageFile, value);
        }

        // ── Theme / colors ──

        private WizardTheme _defaultTheme = WizardTheme.Modern;
        public WizardTheme DefaultTheme
        {
            get => _defaultTheme;
            set => SetProperty(ref _defaultTheme, value);
        }

        private string _sidebarBackgroundColor = "#1E1E28";
        public string SidebarBackgroundColor
        {
            get => _sidebarBackgroundColor;
            set => SetProperty(ref _sidebarBackgroundColor, value);
        }

        private string _sidebarTextColor = "#FFFFFF";
        public string SidebarTextColor
        {
            get => _sidebarTextColor;
            set => SetProperty(ref _sidebarTextColor, value);
        }

        private string _accentColor = "#2962FF";
        public string AccentColor
        {
            get => _accentColor;
            set => SetProperty(ref _accentColor, value);
        }

        private bool _allowComponentSelection = true;
        public bool AllowComponentSelection
        {
            get => _allowComponentSelection;
            set => SetProperty(ref _allowComponentSelection, value);
        }

        private bool _allowPathChange = true;
        public bool AllowPathChange
        {
            get => _allowPathChange;
            set => SetProperty(ref _allowPathChange, value);
        }

        // ── Build pipeline ──

        private string _outputBaseFilename = "Setup";
        public string OutputBaseFilename
        {
            get => _outputBaseFilename;
            set => SetProperty(ref _outputBaseFilename, value);
        }

        private string _outputDir = "";
        public string OutputDir
        {
            get => _outputDir;
            set => SetProperty(ref _outputDir, value);
        }

        private InstallerOutputFormat _outputFormat = InstallerOutputFormat.Exe;
        public InstallerOutputFormat OutputFormat
        {
            get => _outputFormat;
            set => SetProperty(ref _outputFormat, value);
        }

        private Architecture _architecturesAllowed = Architecture.X64Compatible;
        public Architecture ArchitecturesAllowed
        {
            get => _architecturesAllowed;
            set => SetProperty(ref _architecturesAllowed, value);
        }

        private ArchitectureMode _architecturesInstallIn64BitMode = ArchitectureMode.X64Compatible;
        public ArchitectureMode ArchitecturesInstallIn64BitMode
        {
            get => _architecturesInstallIn64BitMode;
            set => SetProperty(ref _architecturesInstallIn64BitMode, value);
        }

        private string _mainExecutable = "";
        public string MainExecutable
        {
            get => _mainExecutable;
            set => SetProperty(ref _mainExecutable, value);
        }

        private string _payloadFolderName = "payload";
        public string PayloadFolderName
        {
            get => _payloadFolderName;
            set => SetProperty(ref _payloadFolderName, value);
        }

        private PayloadSourceType _payloadSource = PayloadSourceType.Local;
        public PayloadSourceType PayloadSource
        {
            get => _payloadSource;
            set => SetProperty(ref _payloadSource, value);
        }

        private string _payloadUrl = "";
        public string PayloadUrl
        {
            get => _payloadUrl;
            set => SetProperty(ref _payloadUrl, value);
        }

        private string _payloadSha256 = "";
        /// <summary>
        /// Expected SHA-256 of the archive at <see cref="PayloadUrl"/>, as 64 hex characters.
        /// Declaring it is what makes a remote payload trustworthy: without it the installer
        /// extracts and runs whatever the URL returns, so a poisoned mirror, a hijacked CDN edge or
        /// a plain-HTTP hop is enough to install arbitrary files. Verified before extraction.
        /// </summary>
        public string PayloadSha256
        {
            get => _payloadSha256;
            set => SetProperty(ref _payloadSha256, (value ?? "").Trim());
        }

        private bool _compressPayload = true;
        public bool CompressPayload
        {
            get => _compressPayload;
            set => SetProperty(ref _compressPayload, value);
        }

        private CompressionFormat _compression = CompressionFormat.Zip;
        public CompressionFormat Compression
        {
            get => _compression;
            set => SetProperty(ref _compression, value);
        }

        private bool _solidCompression = true;
        public bool SolidCompression
        {
            get => _solidCompression;
            set => SetProperty(ref _solidCompression, value);
        }

        private CompressionStrength _compressionLevel = CompressionStrength.Default;
        public CompressionStrength CompressionLevel
        {
            get => _compressionLevel;
            set => SetProperty(ref _compressionLevel, value);
        }

        private bool _singleFile = true;
        public bool SingleFile
        {
            get => _singleFile;
            set => SetProperty(ref _singleFile, value);
        }

        private bool _selfContained = true;
        public bool SelfContained
        {
            get => _selfContained;
            set => SetProperty(ref _selfContained, value);
        }

        private bool _createUninstallEntry = true;
        public bool CreateUninstallEntry
        {
            get => _createUninstallEntry;
            set => SetProperty(ref _createUninstallEntry, value);
        }

        private bool _createRestorePoint;
        /// <summary>
        /// Take a Windows System Restore point before installing. Off by default: the step is now
        /// wired into the install graph, and it never was before, so no project has ever taken one
        /// however this was authored. Defaulting it on would silently add a restore point to every
        /// install — SRSetRestorePoint is slow, needs elevation, and needs System Protection on.
        /// </summary>
        public bool CreateRestorePoint
        {
            get => _createRestorePoint;
            set => SetProperty(ref _createRestorePoint, value);
        }

        private string _codeSignCertificatePath = "";
        public string CodeSignCertificatePath
        {
            get => _codeSignCertificatePath;
            set => SetProperty(ref _codeSignCertificatePath, value);
        }

        private string _codeSignCertificatePassword = "";
        public string CodeSignCertificatePassword
        {
            get => _codeSignCertificatePassword;
            set => SetProperty(ref _codeSignCertificatePassword, value);
        }

        private string _codeSignStoreName = "";
        public string CodeSignStoreName
        {
            get => _codeSignStoreName;
            set => SetProperty(ref _codeSignStoreName, value);
        }

        private string _codeSignStoreLocation = "";
        public string CodeSignStoreLocation
        {
            get => _codeSignStoreLocation;
            set => SetProperty(ref _codeSignStoreLocation, value);
        }

        private string _codeSignStoreThumbprint = "";
        public string CodeSignStoreThumbprint
        {
            get => _codeSignStoreThumbprint;
            set => SetProperty(ref _codeSignStoreThumbprint, value);
        }

        private string _codeSignStoreSubject = "";
        public string CodeSignStoreSubject
        {
            get => _codeSignStoreSubject;
            set => SetProperty(ref _codeSignStoreSubject, value);
        }

        private string _codeSignRemoteProvider = "";
        public string CodeSignRemoteProvider
        {
            get => _codeSignRemoteProvider;
            set => SetProperty(ref _codeSignRemoteProvider, value);
        }

        private string _codeSignRemoteEndpoint = "";
        public string CodeSignRemoteEndpoint
        {
            get => _codeSignRemoteEndpoint;
            set => SetProperty(ref _codeSignRemoteEndpoint, value);
        }

        private string _codeSignRemoteKeyId = "";
        public string CodeSignRemoteKeyId
        {
            get => _codeSignRemoteKeyId;
            set => SetProperty(ref _codeSignRemoteKeyId, value);
        }

        private string _codeSignRemoteCredential = "";
        public string CodeSignRemoteCredential
        {
            get => _codeSignRemoteCredential;
            set => SetProperty(ref _codeSignRemoteCredential, value);
        }

        public bool HasCodeSigningCertificate =>
            !string.IsNullOrWhiteSpace(CodeSignCertificatePath)
            || !string.IsNullOrWhiteSpace(CodeSignStoreThumbprint)
            || !string.IsNullOrWhiteSpace(CodeSignStoreSubject)
            || !string.IsNullOrWhiteSpace(CodeSignRemoteEndpoint);

        private string _codeSignTimestampUrl = "http://timestamp.digicert.com";
        public string CodeSignTimestampUrl
        {
            get => _codeSignTimestampUrl;
            set => SetProperty(ref _codeSignTimestampUrl, value);
        }

        private string _msixIdentity = "";
        public string MsixIdentity
        {
            get => _msixIdentity;
            set => SetProperty(ref _msixIdentity, value);
        }

        private string _msixPublisher = "";
        public string MsixPublisher
        {
            get => _msixPublisher;
            set => SetProperty(ref _msixPublisher, value);
        }

        private ObservableCollection<MsixOptionalPackageDefinition> _msixOptionalPackages = new();
        public ObservableCollection<MsixOptionalPackageDefinition> MsixOptionalPackages
        {
            get => _msixOptionalPackages;
            set => SetProperty(ref _msixOptionalPackages, value);
        }

        private ObservableCollection<UpdateChannelDefinition> _updateChannels = new();
        public ObservableCollection<UpdateChannelDefinition> UpdateChannels
        {
            get => _updateChannels;
            set => SetProperty(ref _updateChannels, value);
        }

        // ── Collections (ObservableCollection with INotifyPropertyChanged) ──

        private ObservableCollection<InstallComponent> _components = new();
        public ObservableCollection<InstallComponent> Components
        {
            get => _components;
            set => SetProperty(ref _components, value);
        }

        private ObservableCollection<Prerequisite> _prerequisites = new();
        public ObservableCollection<Prerequisite> Prerequisites
        {
            get => _prerequisites;
            set => SetProperty(ref _prerequisites, value);
        }

        private ObservableCollection<PrerequisiteCatalogReference> _prerequisiteCatalogs = new();
        public ObservableCollection<PrerequisiteCatalogReference> PrerequisiteCatalogs
        {
            get => _prerequisiteCatalogs;
            set => SetProperty(ref _prerequisiteCatalogs, value);
        }

        private ObservableCollection<PackageNodeDefinition> _packages = new();
        public ObservableCollection<PackageNodeDefinition> Packages
        {
            get => _packages;
            set => SetProperty(ref _packages, value);
        }

        private ObservableCollection<DeploymentSupersedenceRule> _deploymentSupersedence = new();
        public ObservableCollection<DeploymentSupersedenceRule> DeploymentSupersedence
        {
            get => _deploymentSupersedence;
            set => SetProperty(ref _deploymentSupersedence, value);
        }

        private ObservableCollection<ShortcutDefinition> _shortcuts = new();
        public ObservableCollection<ShortcutDefinition> Shortcuts
        {
            get => _shortcuts;
            set => SetProperty(ref _shortcuts, value);
        }

        private ObservableCollection<RegistryOperation> _registryEntries = new();
        public ObservableCollection<RegistryOperation> RegistryEntries
        {
            get => _registryEntries;
            set => SetProperty(ref _registryEntries, value);
        }

        private ObservableCollection<EnvironmentVariableOp> _environmentVariables = new();
        public ObservableCollection<EnvironmentVariableOp> EnvironmentVariables
        {
            get => _environmentVariables;
            set => SetProperty(ref _environmentVariables, value);
        }

        private ObservableCollection<WindowsServiceDefinition> _windowsServices = new();
        public ObservableCollection<WindowsServiceDefinition> WindowsServices
        {
            get => _windowsServices;
            set => SetProperty(ref _windowsServices, value);
        }

        private ObservableCollection<ScheduledTaskDefinition> _scheduledTasks = new();
        public ObservableCollection<ScheduledTaskDefinition> ScheduledTasks
        {
            get => _scheduledTasks;
            set => SetProperty(ref _scheduledTasks, value);
        }

        private ObservableCollection<FirewallRuleDefinition> _firewallRules = new();
        public ObservableCollection<FirewallRuleDefinition> FirewallRules
        {
            get => _firewallRules;
            set => SetProperty(ref _firewallRules, value);
        }

        private ObservableCollection<FileAssociationDefinition> _fileAssociations = new();
        public ObservableCollection<FileAssociationDefinition> FileAssociations
        {
            get => _fileAssociations;
            set => SetProperty(ref _fileAssociations, value);
        }

        private ObservableCollection<CertificateDefinition> _certificates = new();
        public ObservableCollection<CertificateDefinition> Certificates
        {
            get => _certificates;
            set => SetProperty(ref _certificates, value);
        }

        private ObservableCollection<ComRegistrationDefinition> _comRegistrations = new();
        public ObservableCollection<ComRegistrationDefinition> ComRegistrations
        {
            get => _comRegistrations;
            set => SetProperty(ref _comRegistrations, value);
        }

        private ObservableCollection<DriverPackageDefinition> _driverPackages = new();
        public ObservableCollection<DriverPackageDefinition> DriverPackages
        {
            get => _driverPackages;
            set => SetProperty(ref _driverPackages, value);
        }

        private ObservableCollection<Beep.Installer.Engine.CompiledInstallOperation> _resources = new();
        public ObservableCollection<Beep.Installer.Engine.CompiledInstallOperation> Resources
        {
            get => _resources;
            set => SetProperty(ref _resources, value);
        }

        private ObservableCollection<ConfigTransformDefinition> _configTransforms = new();
        public ObservableCollection<ConfigTransformDefinition> ConfigTransforms
        {
            get => _configTransforms;
            set => SetProperty(ref _configTransforms, value);
        }

        private ObservableCollection<IisAppPoolDefinition> _iisAppPools = new();
        public ObservableCollection<IisAppPoolDefinition> IisAppPools
        {
            get => _iisAppPools;
            set => SetProperty(ref _iisAppPools, value);
        }

        private ObservableCollection<IisSiteDefinition> _iisSites = new();
        public ObservableCollection<IisSiteDefinition> IisSites
        {
            get => _iisSites;
            set => SetProperty(ref _iisSites, value);
        }

        private ObservableCollection<WebDeployPackageDefinition> _webDeployPackages = new();
        public ObservableCollection<WebDeployPackageDefinition> WebDeployPackages
        {
            get => _webDeployPackages;
            set => SetProperty(ref _webDeployPackages, value);
        }

        private ObservableCollection<CustomAction> _customActions = new();
        public ObservableCollection<CustomAction> CustomActions
        {
            get => _customActions;
            set => SetProperty(ref _customActions, value);
        }

        private ObservableCollection<CustomWizardPage> _customPages = new();
        public ObservableCollection<CustomWizardPage> CustomPages
        {
            get => _customPages;
            set => SetProperty(ref _customPages, value);
        }

        private ObservableCollection<string> _enabledWizardPages = new();
        public ObservableCollection<string> EnabledWizardPages
        {
            get => _enabledWizardPages;
            set => SetProperty(ref _enabledWizardPages, value);
        }

        // ── Validation ──

        public IReadOnlyList<CustomActionIssue> ValidateCustomActions() => ValidateCustomActions(CustomActions);

        public static IReadOnlyList<CustomActionIssue> ValidateCustomActions(IList<CustomAction> actions)
        {
            var issues = new List<CustomActionIssue>();
            if (actions == null) return issues;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var key = "#" + (i + 1);
                if (!string.IsNullOrWhiteSpace(a.Path)) key = a.Path + "@" + a.Timing;

                if (string.IsNullOrWhiteSpace(a.Path))
                    issues.Add(new CustomActionIssue { Key = key, Message = a.Required ? "Path is required for required actions." : "Path is empty.", IsError = a.Required });
                else if (!seen.Add(key))
                    issues.Add(new CustomActionIssue { Key = key, Message = "Duplicate action targeting '" + a.Path + "' at " + a.Timing + ".", IsError = false });
                if (a.TimeoutMs < 0)
                    issues.Add(new CustomActionIssue { Key = key, Message = "TimeoutMs must be >= 0.", IsError = true });
            }
            return issues;
        }

        // Field-level behaviour lives in Engine.CustomPageManager; these page-level wrappers
        // delegate so there is exactly one implementation.

        public static (bool ok, string? error) ValidateCustomFields(CustomWizardPage page, Dictionary<string, string> values)
            => Engine.CustomPageManager.Validate(page.Fields, values);

        public static (bool ok, string? error) ValidateCustomFields(IEnumerable<CustomField> fields, Dictionary<string, string> values)
            => Engine.CustomPageManager.Validate(fields, values);

        public static Dictionary<string, string> CollectCustomFields(CustomWizardPage page, Dictionary<string, string> values)
            => Engine.CustomPageManager.Collect(page.Fields, values);

        public static string ExpandCustomMacros(string text, Dictionary<string, string>? values)
            => Engine.CustomPageManager.ExpandMacros(text, values);
    }

    public class CustomActionIssue
    {
        public string Key { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsError { get; set; }
    }

    public class WindowsServiceDefinition
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public WindowsServiceStartMode StartMode { get; set; } = WindowsServiceStartMode.Auto;
        public bool StartAfterInstall { get; set; } = true;
        public bool StopOnUninstall { get; set; } = true;
        public WindowsServiceAccount Account { get; set; } = WindowsServiceAccount.LocalSystem;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public List<string> DependsOn { get; set; } = new();
        public int FailureRestartDelaySeconds { get; set; } = 60;
    }

    public class ScheduledTaskDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public ScheduledTaskTrigger Trigger { get; set; } = ScheduledTaskTrigger.OnLogon;
        public string StartTime { get; set; } = "09:00";
        public bool Enabled { get; set; } = true;
        public bool RunElevated { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool StopOnUninstall { get; set; } = true;
    }

    public class FirewallRuleDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public FirewallRuleDirection Direction { get; set; } = FirewallRuleDirection.In;
        public FirewallRuleAction Action { get; set; } = FirewallRuleAction.Allow;
        public FirewallRuleProtocol Protocol { get; set; } = FirewallRuleProtocol.Tcp;
        public string LocalPort { get; set; } = "";
        public string RemotePort { get; set; } = "";
        public string Program { get; set; } = "";
        public string Service { get; set; } = "";
        public string Profile { get; set; } = "any";
        public bool Enabled { get; set; } = true;
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class FileAssociationDefinition
    {
        public string Extension { get; set; } = "";
        public string ProgId { get; set; } = "";
        public string Description { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string Arguments { get; set; } = "\"%1\"";
        public string IconPath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string PerceivedType { get; set; } = "";
        public string Verb { get; set; } = "open";
        public string VerbDisplayName { get; set; } = "";
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class CertificateDefinition
    {
        public string SourcePath { get; set; } = "";
        public string Password { get; set; } = "";
        public string StoreName { get; set; } = "My";
        public InstallationScope StoreLocation { get; set; } = InstallationScope.Machine;
        public string Thumbprint { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class ComRegistrationDefinition
    {
        public string Clsid { get; set; } = "";
        public string ProgId { get; set; } = "";
        public string VersionIndependentProgId { get; set; } = "";
        public string Description { get; set; } = "";
        public string ServerPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public ComServerType ServerType { get; set; } = ComServerType.InProc;
        public string ThreadingModel { get; set; } = "Both";
        public string TypeLibId { get; set; } = "";
        public string Version { get; set; } = "";
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class DriverPackageDefinition
    {
        public string Name { get; set; } = "";
        public DriverPackageKind Kind { get; set; } = DriverPackageKind.Pnp;
        public string InfPath { get; set; } = "";
        public string DriverBinaryPath { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DriverPackageStartMode StartMode { get; set; } = DriverPackageStartMode.Demand;
        public DriverPackageErrorControl ErrorControl { get; set; } = DriverPackageErrorControl.Normal;
        public string LoadOrderGroup { get; set; } = "";
        public List<string> DependsOn { get; set; } = new();
        public string PublishedName { get; set; } = "";
        public string HardwareId { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool InstallDevices { get; set; }
        public bool RequireSigned { get; set; } = true;
        public bool RemoveOnUninstall { get; set; } = true;
        public DriverPackageRebootBehavior RebootBehavior { get; set; } = DriverPackageRebootBehavior.Possible;
    }

    public class ConfigTransformDefinition
    {
        public string Name { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public ConfigTransformFormat Format { get; set; } = ConfigTransformFormat.Json;
        public ConfigTransformOperation Operation { get; set; } = ConfigTransformOperation.Set;
        public string KeyPath { get; set; } = "";
        public string Value { get; set; } = "";
        public string Section { get; set; } = "";
        public bool BackupOnInstall { get; set; } = true;
        public bool RestoreOnRollback { get; set; } = true;
    }

    public class IisAppPoolDefinition
    {
        public string Name { get; set; } = "";
        public string RuntimeVersion { get; set; } = "v4.0";
        public IisManagedPipelineMode PipelineMode { get; set; } = IisManagedPipelineMode.Integrated;
        public bool Enable32Bit { get; set; }
        public string Identity { get; set; } = "ApplicationPoolIdentity";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool AutoStart { get; set; } = true;
        public bool StartAfterInstall { get; set; } = true;
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class IisSiteDefinition
    {
        public string Name { get; set; } = "";
        public string PhysicalPath { get; set; } = "";
        public string ApplicationPool { get; set; } = "";
        public bool StartAfterInstall { get; set; } = true;
        public bool RemoveOnUninstall { get; set; } = true;
        public List<IisBindingDefinition> Bindings { get; set; } = new();
    }

    public class IisBindingDefinition
    {
        public IisBindingProtocol Protocol { get; set; } = IisBindingProtocol.Http;
        public string IpAddress { get; set; } = "*";
        public int Port { get; set; } = 80;
        public string Host { get; set; } = "";
        public string CertificateThumbprint { get; set; } = "";
        public string CertificateStoreName { get; set; } = "My";
        public int SslFlags { get; set; }
    }

    public class WebDeployPackageDefinition
    {
        public string Name { get; set; } = "";
        public string PackagePath { get; set; } = "";
        public string SiteName { get; set; } = "";
        public string Destination { get; set; } = "auto";
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RemoveOnUninstall { get; set; } = true;
    }

    public class DeploymentSupersedenceRule
    {
        public string PackageId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string MinimumVersion { get; set; } = "";
        public string MaximumVersion { get; set; } = "";
        public DeploymentSupersedenceMode Mode { get; set; } = DeploymentSupersedenceMode.Replace;
        public bool UninstallPrevious { get; set; } = true;
        public string DetectionKey { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    public class PackageNodeDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public PackageNodeType PackageType { get; set; } = PackageNodeType.Exe;
        public string SourcePath { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string DownloadUrlX86 { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Sha512 { get; set; } = "";
        public string Sha512X86 { get; set; } = "";
        public string DetectionCommand { get; set; } = "";
        public string DetectionCommandX86 { get; set; } = "";
        public string DetectionPattern { get; set; } = "";
        public string DetectionPatternX86 { get; set; } = "";
        public string InstallArgs { get; set; } = "";
        public string RepairArgs { get; set; } = "";
        public string UninstallCommand { get; set; } = "";
        public string UninstallArgs { get; set; } = "";
        public List<string> DependsOn { get; set; } = new();
        public bool IsMandatory { get; set; } = true;
        public bool RemoveOnUninstall { get; set; } = false;
        public string SuccessExitCodes { get; set; } = "0,3010,1641";
        public string RebootExitCodes { get; set; } = "3010,1641";
        public int TimeoutSeconds { get; set; } = 1800;
        public int RetryCount { get; set; } = 3;
        public string HelpUrl { get; set; } = "";
    }

    public class UpdateChannelDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ring { get; set; } = "";
        public string FeedUrl { get; set; } = "";
        public int RolloutPercentage { get; set; } = 100;
        public string MinimumVersion { get; set; } = "";
        public DateTimeOffset? DeadlineUtc { get; set; }
        public bool Critical { get; set; }
        public string MaintenanceWindow { get; set; } = "";
        public string RollbackVersion { get; set; } = "";
        public bool Revoked { get; set; }
    }

    public class PrerequisiteCatalogReference
    {
        public string Path { get; set; } = "";
        public string Signature { get; set; } = "";
        public string TrustedPublicKeyPath { get; set; } = "";
        public string TrustedPublicKey { get; set; } = "";
        public bool Required { get; set; } = true;
    }

    public class MsixOptionalPackageDefinition
    {
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Version { get; set; } = "";
        public Architecture Architecture { get; set; } = Architecture.X64;
        public string Uri { get; set; } = "";
        public MsixRelatedPackageKind Kind { get; set; } = MsixRelatedPackageKind.Package;
    }
}
