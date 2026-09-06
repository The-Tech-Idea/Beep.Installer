using System;
using System.Collections.Generic;
using System.IO;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>
/// Projects the authoring model (<see cref="InstallProject"/>, which owns build-pipeline
/// concerns such as compression, signing and MSIX identity) onto BeepDM's runtime install
/// contract (<see cref="InstallConfig"/>, a schema-versioned serialization contract).
///
/// The two models deliberately stay separate: BeepDM's installer-service scope explicitly
/// excludes packaging and code signing, so those fields must not leak into InstallConfig.
/// The collection payloads need no conversion at all — both models already use the same
/// BeepDM element types (InstallComponent, Prerequisite, ShortcutDefinition,
/// RegistryOperation, EnvironmentVariableOp).
///
/// Element instances are shared by reference on purpose: the wizard mutates
/// <c>InstallComponent.Selected</c> on the project, and the steps must observe those choices.
/// </summary>
public static class InstallConfigProjector
{
    /// <summary>
    /// Builds the runtime config for <paramref name="project"/>.
    /// </summary>
    /// <param name="project">Authoring model. Required.</param>
    /// <param name="configDirectory">
    /// Directory relative payload paths resolve against. Feeds
    /// <see cref="InstallConfig.ConfigDirectory"/>, which
    /// <c>ConfigManager.ResolvePayloadRoot</c> probes. Pass the payload root when it is
    /// already known; the explicit <c>PayloadRoot</c> context key still wins in FileCopyStep.
    /// </param>
    public static InstallConfig ToInstallConfig(InstallProject project, string? configDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new InstallConfig
        {
            SchemaVersion = InstallConfig.CurrentSchemaVersion,
            ConfigDirectory = ResolveConfigDirectory(project, configDirectory),

            // ── Identity (load-bearing: manifest, restore point, custom-action macros) ──
            ProductName = project.AppName ?? "",
            AppId = project.AppId ?? "",
            ProductVersion = project.AppVersion ?? "",
            Publisher = project.AppPublisher ?? "",

            // ── Layout ──
            // Kept as the authored template (may contain %ProgramFiles%): the resolved
            // absolute path travels as the InstallPath context key, which every step prefers.
            DefaultInstallPath = project.DefaultDirName ?? "",
            StartMenuFolder = project.DefaultGroupName ?? "",
            DefaultInstallType = project.DefaultInstallType,
            RequireAdminPrivileges = project.PrivilegesRequired == PrivilegeLevel.Admin,

            // Load-bearing: selects the registry view (WOW6432 or native) for the
            // registry, COM, shared-file and uninstall steps via InstallScope.
            Prefer64Bit = project.Prefer64Bit,

            // ── Presentation (carried so a shipped install-config.json is complete) ──
            LicenseText = project.LicenseText ?? "",
            BannerImagePath = project.WizardImageFile ?? "",
            ProductIconPath = project.SetupIconFile ?? "",
            SupportUrl = project.AppSupportURL ?? "",
            UpdateUrl = project.AppUpdatesURL ?? "",
            UpdateMode = project.AppUpdateMode,

            // ── Payload (same element types — no per-item conversion) ──
            Components = new List<InstallComponent>(project.Components),
            Prerequisites = new List<Prerequisite>(project.Prerequisites),
            Shortcuts = new List<ShortcutDefinition>(project.Shortcuts),
            RegistryEntries = new List<RegistryOperation>(project.RegistryEntries),

            // NOTE: no BeepDM step applies these yet — VerifyInstallStep reads an
            // "EnvVarsSet" key that nothing writes. Mapped now so the contract is complete;
            // the applying step lands in phase P2.
            EnvironmentVariables = new List<EnvironmentVariableOp>(project.EnvironmentVariables),
        };
    }

    private static string ResolveConfigDirectory(InstallProject project, string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return explicitDirectory!;
        if (!string.IsNullOrWhiteSpace(project.SourceDirectory) && Directory.Exists(project.SourceDirectory))
            return project.SourceDirectory;
        return AppContext.BaseDirectory;
    }
}
