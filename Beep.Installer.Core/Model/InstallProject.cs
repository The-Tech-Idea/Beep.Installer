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

    public enum InstallationTypeEx
    {
        Typical,
        Custom,
        Complete
    }

    public enum UpdateModeEx
    {
        Optional,
        Required
    }

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

        private UpdateModeEx _appUpdateMode = UpdateModeEx.Optional;
        public UpdateModeEx AppUpdateMode
        {
            get => _appUpdateMode;
            set => SetProperty(ref _appUpdateMode, value);
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

        private InstallationTypeEx _defaultInstallType = InstallationTypeEx.Typical;
        public InstallationTypeEx DefaultInstallType
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

        private bool _createRestorePoint = true;
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

        public static (bool ok, string? error) ValidateCustomFields(CustomWizardPage page, Dictionary<string, string> values)
        {
            foreach (var f in page.Fields)
            {
                if (!f.Required) continue;
                values.TryGetValue(f.Id, out var v);
                if (string.IsNullOrWhiteSpace(v) || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
                    return (false, $"'{f.Label}' is required.");
            }
            return (true, null);
        }

        public static Dictionary<string, string> CollectCustomFields(CustomWizardPage page, Dictionary<string, string> values)
        {
            var result = new Dictionary<string, string>();
            foreach (var f in page.Fields)
                result[f.Id] = values.TryGetValue(f.Id, out var v) ? (v ?? "") : (f.DefaultValue ?? "");
            return result;
        }

        public static string ExpandCustomMacros(string text, Dictionary<string, string>? values)
        {
            if (string.IsNullOrEmpty(text) || values == null || values.Count == 0) return text ?? "";
            foreach (var kv in values)
                text = text.Replace("{Custom:" + kv.Key + "}", kv.Value ?? "");
            return text;
        }
    }

    public class CustomActionIssue
    {
        public string Key { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsError { get; set; }
    }
}
