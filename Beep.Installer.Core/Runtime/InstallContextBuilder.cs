using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Engine;

/// <summary>
/// The single place that builds a <see cref="SetupContext"/> for the BeepDM installer steps.
///
/// Every caller — silent install, wizard install, uninstall, self-test — goes through here so
/// the context is populated identically. Previously each call site assembled the property bag
/// by hand and none of them supplied <c>InstallConfig</c>, which made every step fail
/// validation and aborted the run.
/// </summary>
public static class InstallContextBuilder
{
    /// <summary>Builds the context for an install run.</summary>
    /// <param name="project">Authoring model.</param>
    /// <param name="installPath">Resolved absolute install directory.</param>
    /// <param name="perUser">Per-user (HKCU/LocalAppData) vs per-machine (HKLM/ProgramFiles).</param>
    /// <param name="rollback">Transactional file rollback; FileCopyStep registers copies with it.</param>
    /// <param name="payloadRoot">
    /// Known payload root, when available. PayloadPrepareStep overwrites this key once it has
    /// located/extracted the payload, so passing null here is normal for a shipped installer.
    /// </param>
    /// <param name="customValues">Values backing <c>{Custom:fieldId}</c> macros.</param>
    public static SetupContext ForInstall(
        InstallProject project,
        string installPath,
        bool perUser,
        RollbackManager? rollback = null,
        string? payloadRoot = null,
        IDictionary<string, string>? customValues = null,
        bool force = false,
        string? journalPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        perUser = InstallScopeResolver.IsPerUser(project, perUser);
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException("Install path is required.", nameof(installPath));

        // The runtime model is the effective selection consumed by policy, plans and journals.
        project.DefaultScope = perUser ? InstallationScope.User : InstallationScope.Machine;
        var context = new SetupContext();
        var config = InstallConfigProjector.ToInstallConfig(project, payloadRoot);

        context.Properties[InstallContextKeys.InstallProject] = project;
        context.Properties[InstallContextKeys.InstallConfig] = config;
        context.Properties[InstallContextKeys.InstallPath] = installPath;
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] =
            ResourceExecutionJournalStore.ResolvePath(installPath, project.AppId, journalPath);

        // Boxed value types — steps read these with TryGetValue + pattern match, because
        // TryGetProperty<T> is constrained to reference types.
        context.Properties[InstallContextKeys.PerUser] = perUser;
        context.Properties[InstallContextKeys.IsSelfContained] = project.SelfContained;

        // Allows UpgradeStep to proceed with a downgrade (CLI /FORCE). Boxed bool.
        if (force)
            context.Properties[TheTechIdea.Beep.Installer.Steps.UpgradeStep.ForceInstallKey] = true;

        if (!string.IsNullOrWhiteSpace(payloadRoot))
            context.Properties[InstallContextKeys.PayloadRoot] = payloadRoot!;

        if (rollback != null)
            context.Properties[InstallContextKeys.RollbackManager] = rollback;

        context.Properties[InstallContextKeys.CustomActions] =
            new List<CustomAction>(project.CustomActions);

        if (customValues != null && customValues.Count > 0)
        {
            context.Properties[InstallContextKeys.CustomValues] =
                new Dictionary<string, string>(customValues, StringComparer.OrdinalIgnoreCase);
            context.Properties[InstallContextKeys.RuntimeVariables] =
                new Dictionary<string, string>(customValues, StringComparer.OrdinalIgnoreCase);
        }

        // Payload location hints for our own payload steps. These previously came from a
        // global static, which made the runtime install path non-reentrant and untestable.
        context.Properties[InstallContextKeys.PayloadFolderName] =
            string.IsNullOrWhiteSpace(project.PayloadFolderName) ? "payload" : project.PayloadFolderName;
        context.Properties[InstallContextKeys.PayloadCompression] =
            project.Compression == CompressionFormat.Lzma2 ? "lzma2" : "zip";
        if (!string.IsNullOrWhiteSpace(project.SourceDirectory))
            context.Properties[InstallContextKeys.PayloadSearchBase] = project.SourceDirectory;
        if (project.PayloadSource == PayloadSourceType.Url && !string.IsNullOrWhiteSpace(project.PayloadUrl))
            context.Properties[InstallContextKeys.PayloadUrl] = project.PayloadUrl;

        return context;
    }

    /// <summary>Builds a repair context using the scope recorded by the installation.</summary>
    public static SetupContext ForRepair(
        InstallProject project,
        string installPath,
        IDictionary<string, string>? runtimeVariables = null,
        string? journalPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException("Install path is required.", nameof(installPath));
        var perUser = InstallScopeResolver.ReadInstalledScope(project, installPath, journalPath)
            ?? InstallScopeResolver.IsPerUser(project);
        project.DefaultScope = perUser ? InstallationScope.User : InstallationScope.Machine;
        var context = ForInstall(project, installPath, perUser, customValues: runtimeVariables, journalPath: journalPath);
        context.Properties[InstallContextKeys.ResourceExecutionMode] = "repair";
        if (journalPath is not null)
            context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        return context;
    }

    /// <summary>
    /// Builds the context for an uninstall run. UninstallStep reads the install manifest from
    /// disk for the file/registry/shortcut inventory; the config is still needed for scope.
    /// </summary>
    public static SetupContext ForUninstall(
        InstallProject project,
        string installPath,
        bool perUser,
        IDictionary<string, string>? runtimeVariables = null,
        string? journalPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException("Install path is required.", nameof(installPath));

        perUser = InstallScopeResolver.ReadInstalledScope(project, installPath, journalPath)
            ?? InstallScopeResolver.IsPerUser(project, perUser);
        project.DefaultScope = perUser ? InstallationScope.User : InstallationScope.Machine;
        var context = new SetupContext();
        context.Properties[InstallContextKeys.InstallProject] = project;
        context.Properties[InstallContextKeys.InstallConfig] =
            InstallConfigProjector.ToInstallConfig(project);
        context.Properties[InstallContextKeys.InstallPath] = installPath;
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] =
            ResourceExecutionJournalStore.ResolvePath(installPath, project.AppId, journalPath);
        context.Properties[InstallContextKeys.PerUser] = perUser;
        context.Properties[InstallContextKeys.CustomActions] =
            new List<CustomAction>(project.CustomActions);
        if (runtimeVariables != null && runtimeVariables.Count > 0)
            context.Properties[InstallContextKeys.RuntimeVariables] =
                new Dictionary<string, string>(runtimeVariables, StringComparer.OrdinalIgnoreCase);
        return context;
    }

    /// <summary>
    /// Verifies the context carries everything the BeepDM steps require, returning the missing
    /// or wrong-typed keys. Because the steps fail silently on a bad key, this turns an opaque
    /// "step validation failed" into a named diagnostic — and gives tests a direct assertion.
    /// </summary>
    public static IReadOnlyList<string> FindMissingRequiredKeys(SetupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problems = new List<string>();

        if (context.TryGetProperty<InstallConfig>(InstallContextKeys.InstallConfig) == null)
            problems.Add($"{InstallContextKeys.InstallConfig} (InstallConfig) — FileCopyStep, " +
                         "PrerequisiteCheckStep, ShortcutCreateStep and VerifyInstallStep all " +
                         "fail validation without it.");

        if (string.IsNullOrWhiteSpace(context.TryGetProperty<string>(InstallContextKeys.InstallPath)))
            problems.Add($"{InstallContextKeys.InstallPath} (string)");

        if (!IsBool(context, InstallContextKeys.PerUser))
            problems.Add($"{InstallContextKeys.PerUser} (boxed bool) — absence silently " +
                         "routes every registry write to HKLM.");

        if (!IsBool(context, InstallContextKeys.IsSelfContained))
            problems.Add($"{InstallContextKeys.IsSelfContained} (boxed bool) — absence makes " +
                         "PrerequisiteCheckStep fail on machines without a shared .NET runtime.");

        return problems;
    }

    private static bool IsBool(SetupContext context, string key)
        => context.Properties.TryGetValue(key, out var value) && value is bool;
}
