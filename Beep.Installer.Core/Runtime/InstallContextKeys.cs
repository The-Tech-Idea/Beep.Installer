namespace Beep.Installer.Engine;

/// <summary>
/// The exact <see cref="TheTechIdea.Beep.SetUp.SetupContext"/> property keys the BeepDM
/// installer steps read.
///
/// These must be string-identical to what the steps expect. The failure mode is silent:
/// <c>SetupContext.TryGetProperty&lt;T&gt;</c> is constrained to reference types and returns
/// <c>value as T</c>, so a misspelled key and a wrong-typed value both come back as
/// <c>null</c> — indistinguishable from "not supplied". Centralising the literals here is
/// what stops that class of bug returning.
///
/// Note <see cref="PerUser"/> and <see cref="IsSelfContained"/> are value types. The steps
/// read them via <c>Properties.TryGetValue</c> + pattern match, NOT via
/// <c>TryGetProperty&lt;T&gt;</c> (which cannot accept a struct), so they must be stored as
/// plain boxed booleans.
/// </summary>
public static class InstallContextKeys
{
    // ── Supplied by the host (us) ──

    /// <summary>The projected <c>InstallConfig</c>. Four steps hard-fail Validate() without it.</summary>
    public const string InstallConfig = "InstallConfig";

    /// <summary>The first-class Beep Installer authoring project used by compiled-plan resource providers.</summary>
    public const string InstallProject = "InstallProject";

    /// <summary>The deterministic compiled plan for provider-based resource execution.</summary>
    public const string CompiledInstallPlan = "CompiledInstallPlan";

    /// <summary>Optional override for the runtime resource provider registry.</summary>
    public const string ResourceProviderRegistry = "ResourceProviderRegistry";
    public const string ExtensionBundleRoot = "ExtensionBundleRoot";

    /// <summary>Optional override for condition fact evaluation during provider execution.</summary>
    public const string ResourceConditionFacts = "ResourceConditionFacts";

    /// <summary>Execution journal produced by provider-based resource execution.</summary>
    public const string ResourceExecutionJournal = "ResourceExecutionJournal";

    /// <summary>Absolute path to the durable provider execution journal JSON file.</summary>
    public const string ResourceExecutionJournalPath = "ResourceExecutionJournalPath";

    /// <summary>Optional externally supplied provider execution attempt id.</summary>
    public const string ResourceExecutionAttemptId = "ResourceExecutionAttemptId";

    /// <summary>install, repair or update. Written into provider journal metadata and entries.</summary>
    public const string ResourceExecutionMode = "ResourceExecutionMode";

    /// <summary>Optional runtime secret provider used by resource providers at execution boundaries.</summary>
    public const string ResourceSecretProvider = "ResourceSecretProvider";

    /// <summary>Effective enterprise policy used by runtime resource providers.</summary>
    public const string ResourcePolicy = "ResourcePolicy";

    /// <summary>
    /// Absolute install directory — where files are physically written; for a side-by-side
    /// install that is <c>&lt;base&gt;\app-&lt;version&gt;</c>. Nothing derives this from
    /// <c>InstallConfig.DefaultInstallPath</c>.
    /// </summary>
    public const string InstallPath = "InstallPath";

    /// <summary>
    /// The stable path shortcuts and launch targets point at: <c>&lt;base&gt;\current</c> for a
    /// side-by-side install, or the same as <see cref="InstallPath"/> for a flat install. Steps
    /// resolving a launch target read this and fall back to <see cref="InstallPath"/>.
    /// </summary>
    public const string LaunchPath = "LaunchPath";

    /// <summary>The user-chosen install directory (the parent of <c>app-&lt;version&gt;</c> and <c>current</c> for side-by-side; equal to <see cref="InstallPath"/> for flat).</summary>
    public const string InstallBaseDir = "InstallBaseDir";

    /// <summary>Boxed bool. True when the install uses the side-by-side <c>app-&lt;version&gt;</c> + <c>current</c> junction layout.</summary>
    public const string SideBySide = "SideBySide";

    /// <summary>Boxed bool. Drives registry hive selection in InstallScope; wrong value silently writes HKLM.</summary>
    public const string PerUser = "PerUser";

    /// <summary>Boxed bool. PrerequisiteCheckStep hard-fails on a machine without .NET when this is false/absent.</summary>
    public const string IsSelfContained = "IsSelfContained";

    /// <summary>Overrides ConfigManager.ResolvePayloadRoot, which hardcodes the folder name "payload".</summary>
    public const string PayloadRoot = "PayloadRoot";

    /// <summary>Transactional file rollback. FileCopyStep is its only consumer.</summary>
    public const string RollbackManager = "RollbackManager";

    /// <summary>Custom actions filtered by timing by CustomActionStep.</summary>
    public const string CustomActions = "CustomActions";

    /// <summary>
    /// The deployer's decision about executing those custom actions, as a boxed bool. Absent
    /// means nobody decided, which runs them. Mirrors
    /// <c>TheTechIdea.Beep.Installer.Steps.CustomActionStep.AllowScriptCommandsKey</c>.
    /// </summary>
    public const string AllowScriptCommands = "AllowScriptCommands";

    /// <summary>Values backing the <c>{Custom:fieldId}</c> macros.</summary>
    public const string CustomValues = "CustomValues";

    /// <summary>Runtime variables supplied by silent/enterprise command-line properties.</summary>
    public const string RuntimeVariables = "RuntimeVariables";

    /// <summary>Extra directories for DirectoryCreateStep to create.</summary>
    public const string ComponentDirs = "ComponentDirs";

    // ── Payload location, consumed by the installer's own payload steps ──
    // These carry the authoring values the payload steps used to read from a global static.

    /// <summary>Payload folder name (BeepDM's ResolvePayloadRoot hardcodes "payload").</summary>
    public const string PayloadFolderName = "PayloadFolderName";

    /// <summary>Directory to probe for a staged payload before falling back to the exe directory.</summary>
    public const string PayloadSearchBase = "PayloadSearchBase";

    /// <summary>"zip" or "lzma2" — selects which archive layout the prepare step looks for.</summary>
    public const string PayloadCompression = "PayloadCompression";

    /// <summary>Remote payload URL; empty means the download step skips itself.</summary>
    public const string PayloadUrl = "PayloadUrl";

    /// <summary>
    /// Expected SHA-256 of the archive at <see cref="PayloadUrl"/>. Absent when the author
    /// declared none, which the download step reports rather than silently trusting.
    /// </summary>
    public const string PayloadSha256 = "PayloadSha256";

    // ── Written by steps, read by later steps ──

    public const string InstalledFiles = "InstalledFiles";
    public const string CreatedDirectories = "CreatedDirectories";
    public const string RegistryEntriesWritten = "RegistryEntriesWritten";
    public const string ShortcutsCreated = "ShortcutsCreated";
    public const string SharedFiles = "SharedFiles";
    public const string GacAssembliesInstalled = "GacAssembliesInstalled";

    /// <summary>
    /// Read by VerifyInstallStep but written by nothing in BeepDM — there is no environment
    /// variable step today. Tracked as phase P2 (BeepDM EnvironmentVariableStep).
    /// </summary>
    public const string EnvVarsSet = "EnvVarsSet";
}
