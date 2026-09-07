using System.Collections;
using System.Reflection;
using System.Text.Json;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public static class ProjectCanonicalJsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] TopLevelProperties =
    {
        "SchemaVersion",
        "ProjectName",
        "AppName",
        "AppId",
        "AppVersion",
        "AppPublisher",
        "AppPublisherURL",
        "AppSupportURL",
        "AppSupportEmail",
        "AppUpdatesURL",
        "AppUpdateMode",
        "AppUpdateChannel",
        "SideBySide",
        "AppCopyright",
        "SourceDirectory",
        "SourceIncludes",
        "SourceExcludes",
        "DefaultDirName",
        "DefaultGroupName",
        "PrivilegesRequired",
        "PrivilegesRequiredOverridesAllowed",
        "DefaultInstallType",
        "Prefer64Bit",
        "AllowScopeSelection",
        "DefaultScope",
        "AllowNoIcons",
        "AlwaysShowDirOnReadyPage",
        "LicenseFile",
        "LicenseText",
        "ShowEula",
        "WindowTitle",
        "WelcomeTitle",
        "SetupIconFile",
        "WizardImageFile",
        "DefaultTheme",
        "SidebarBackgroundColor",
        "SidebarTextColor",
        "AccentColor",
        "AllowComponentSelection",
        "AllowPathChange",
        "OutputBaseFilename",
        "OutputDir",
        "OutputFormat",
        "ArchitecturesAllowed",
        "ArchitecturesInstallIn64BitMode",
        "MainExecutable",
        "PayloadFolderName",
        "PayloadSource",
        "PayloadUrl",
        "PayloadSha256",
        "CompressPayload",
        "Compression",
        "SolidCompression",
        "CompressionLevel",
        "SingleFile",
        "SelfContained",
        "CreateUninstallEntry",
        "CreateRestorePoint",
        "CodeSignCertificatePath",
        "CodeSignCertificatePassword",
        "CodeSignStoreName",
        "CodeSignStoreLocation",
        "CodeSignStoreThumbprint",
        "CodeSignStoreSubject",
        "CodeSignRemoteProvider",
        "CodeSignRemoteEndpoint",
        "CodeSignRemoteKeyId",
        "CodeSignRemoteCredential",
        "CodeSignTimestampUrl",
        "MsixIdentity",
        "MsixPublisher",
        "AppInstallerHoursBetweenUpdateChecks",
        "AppInstallerShowPrompt",
        "AppInstallerForceUpdateFromAnyVersion",
        "Components",
        "Resources",
        "Prerequisites",
        "PrerequisiteCatalogs",
        "Packages",
        "MsixOptionalPackages",
        "UpdateChannels",
        "DeploymentSupersedence",
        "Shortcuts",
        "RegistryEntries",
        "EnvironmentVariables",
        "WindowsServices",
        "ScheduledTasks",
        "FirewallRules",
        "FileAssociations",
        "Certificates",
        "ComRegistrations",
        "DriverPackages",
        "ConfigTransforms",
        "IisAppPools",
        "IisSites",
        "WebDeployPackages"
    };

    public static string ToJson(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(ToObject(project), JsonOptions) + Environment.NewLine;
    }

    public static void WriteJson(InstallProject project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson(project));
    }

    public static SortedDictionary<string, object?> ToObject(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var output = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        var properties = typeof(InstallProject)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        foreach (var name in TopLevelProperties)
        {
            if (!properties.TryGetValue(name, out var property))
                continue;

            var value = property.GetValue(project);
            output[JsonName(name)] = NormalizeValue(value, depth: 0);
        }

        return output;
    }

    private static object? NormalizeValue(object? value, int depth)
    {
        if (value == null)
            return null;

        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return value;

        if (value is DateTime dateTime)
            return dateTime.ToUniversalTime().ToString("o");

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset.ToUniversalTime().ToString("o");

        var type = value.GetType();
        if (type.IsEnum)
            return EnumToken(value);

        if (value is IDictionary dictionary)
        {
            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is null)
                    continue;
                result[Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? ""] =
                    NormalizeValue(entry.Value, depth + 1);
            }
            return result;
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable
                .Cast<object?>()
                .OrderBy(CollectionSortKey, StringComparer.OrdinalIgnoreCase)
                .Select(item => NormalizeValue(item, depth + 1))
                .ToList();
        }

        if (depth > 8 || type.Assembly == typeof(string).Assembly)
            return value.ToString();

        var output = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.GetIndexParameters().Length == 0)
                     .OrderBy(p => JsonName(p.Name), StringComparer.Ordinal))
        {
            if (!property.CanRead || property.Name is "HasErrors" or "IsDirty" or "IsModified")
                continue;
            output[JsonName(property.Name)] = NormalizeValue(property.GetValue(value), depth + 1);
        }

        return output;
    }

    private static string CollectionSortKey(object? value)
    {
        if (value == null)
            return "";

        var type = value.GetType();
        foreach (var name in new[] { "Id", "Name", "DestinationPath", "SourcePath", "Extension", "Clsid", "ProgId" })
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var propertyValue = property?.GetValue(value)?.ToString();
            if (!string.IsNullOrWhiteSpace(propertyValue))
                return propertyValue;
        }

        return value.ToString() ?? "";
    }

    private static string JsonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        if (name.Length > 1 && char.IsUpper(name[0]) && char.IsUpper(name[1]))
            return char.ToLowerInvariant(name[0]) + name[1..];

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string EnumToken(object value)
    {
        var text = value.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (text.Contains(','))
            return string.Join(",",
                text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(ToCamelToken)
                    .Order(StringComparer.OrdinalIgnoreCase));

        return ToCamelToken(text);
    }

    private static string ToCamelToken(string value)
        => string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
