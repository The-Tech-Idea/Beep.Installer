using System;
using Beep.Installer.Extensibility;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Lang;

/// <summary>
/// Translations for the provider names and one-line summaries the resource wizard lists.
///
/// The catalog lives in <c>Beep.Installer.Core</c>, which has no UI and therefore no
/// <c>LanguageManager</c>, so its English text cannot be looked up where it is declared. Resolving
/// it here keeps the split intact and — more importantly — keeps every key a <b>literal</b>, so
/// <c>StringResourceCoverageTests</c> can still see it. A computed key such as
/// <c>L("ResType_" + type)</c> would be invisible to that scan, which is exactly the blind spot that
/// let 163 keys go untranslated in the first place.
///
/// <c>ResourceTypeStringsTests</c> asserts every described provider is covered here, so a new
/// provider cannot quietly arrive with an untranslatable name.
/// </summary>
internal static class ResourceTypeStrings
{
    /// <summary>The provider's display name, translated.</summary>
    internal static string Label(ResourceTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Type switch
        {
            "file.copy" => L("ResType_FileCopy_Label", descriptor.Label),
            "registry.write" => L("ResType_RegistryWrite_Label", descriptor.Label),
            "shortcut.create" => L("ResType_ShortcutCreate_Label", descriptor.Label),
            "environment.set" => L("ResType_EnvironmentSet_Label", descriptor.Label),
            "service.install" => L("ResType_ServiceInstall_Label", descriptor.Label),
            "scheduled-task.create" => L("ResType_ScheduledTask_Label", descriptor.Label),
            "firewall.rule" => L("ResType_FirewallRule_Label", descriptor.Label),
            "certificate.install" => L("ResType_CertificateInstall_Label", descriptor.Label),
            "com.register" => L("ResType_ComRegister_Label", descriptor.Label),
            "file-association.register" => L("ResType_FileAssociation_Label", descriptor.Label),
            "config.transform" => L("ResType_ConfigTransform_Label", descriptor.Label),
            "driver.package" => L("ResType_DriverPackage_Label", descriptor.Label),
            "package.install" => L("ResType_PackageInstall_Label", descriptor.Label),
            "component.select" => L("ResType_ComponentSelect_Label", descriptor.Label),
            "iis.appPool" => L("ResType_IisAppPool_Label", descriptor.Label),
            "iis.site" => L("ResType_IisSite_Label", descriptor.Label),
            "webdeploy.package" => L("ResType_WebDeploy_Label", descriptor.Label),
            _ => descriptor.Label,
        };
    }

    /// <summary>The provider's one-line summary, translated.</summary>
    internal static string Summary(ResourceTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Type switch
        {
            "file.copy" => L("ResType_FileCopy_Summary", descriptor.Summary),
            "registry.write" => L("ResType_RegistryWrite_Summary", descriptor.Summary),
            "shortcut.create" => L("ResType_ShortcutCreate_Summary", descriptor.Summary),
            "environment.set" => L("ResType_EnvironmentSet_Summary", descriptor.Summary),
            "service.install" => L("ResType_ServiceInstall_Summary", descriptor.Summary),
            "scheduled-task.create" => L("ResType_ScheduledTask_Summary", descriptor.Summary),
            "firewall.rule" => L("ResType_FirewallRule_Summary", descriptor.Summary),
            "certificate.install" => L("ResType_CertificateInstall_Summary", descriptor.Summary),
            "com.register" => L("ResType_ComRegister_Summary", descriptor.Summary),
            "file-association.register" => L("ResType_FileAssociation_Summary", descriptor.Summary),
            "config.transform" => L("ResType_ConfigTransform_Summary", descriptor.Summary),
            "driver.package" => L("ResType_DriverPackage_Summary", descriptor.Summary),
            "package.install" => L("ResType_PackageInstall_Summary", descriptor.Summary),
            "component.select" => L("ResType_ComponentSelect_Summary", descriptor.Summary),
            "iis.appPool" => L("ResType_IisAppPool_Summary", descriptor.Summary),
            "iis.site" => L("ResType_IisSite_Summary", descriptor.Summary),
            "webdeploy.package" => L("ResType_WebDeploy_Summary", descriptor.Summary),
            _ => descriptor.Summary,
        };
    }

    /// <summary>True when this provider's name and summary are translatable.</summary>
    internal static bool IsCovered(ResourceTypeDescriptor descriptor)
        => descriptor != null && Covered(descriptor.Type);

    private static bool Covered(string type) => type switch
    {
        "file.copy" or "registry.write" or "shortcut.create" or "environment.set"
            or "service.install" or "scheduled-task.create" or "firewall.rule"
            or "certificate.install" or "com.register" or "file-association.register"
            or "config.transform" or "driver.package" or "package.install"
            or "component.select" or "iis.appPool" or "iis.site" or "webdeploy.package" => true,
        _ => false,
    };
}
