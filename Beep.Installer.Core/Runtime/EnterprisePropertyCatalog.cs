using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

public sealed class EnterprisePropertyCatalog
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; init; } = "";
    [JsonPropertyName("appName")]
    public string AppName { get; init; } = "";
    [JsonPropertyName("properties")]
    public List<EnterprisePropertyDefinition> Properties { get; init; } = new();

    public static EnterprisePropertyCatalog ForProject(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var catalog = new EnterprisePropertyCatalog
        {
            ProjectName = project.ProjectName ?? "",
            AppName = project.AppName ?? ""
        };

        catalog.Properties.AddRange(BuiltInProperties(project));
        foreach (var page in project.CustomPages.OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var field in page.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Id)))
            {
                catalog.Properties.Add(new EnterprisePropertyDefinition
                {
                    Name = field.Id,
                    Scope = "custom",
                    Page = string.IsNullOrWhiteSpace(page.Title) ? page.Id : page.Title,
                    Label = string.IsNullOrWhiteSpace(field.Label) ? field.Id : field.Label,
                    Type = FieldType(field.Type),
                    Required = field.Required,
                    DefaultValue = field.DefaultValue ?? "",
                    AllowedValues = field.Options?.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray() ?? Array.Empty<string>(),
                    ResponseFileSyntax = $"\"properties\": {{ \"{field.Id}\": \"...\" }}",
                    CommandLineSyntax = $"/PROPERTY:{field.Id}=<value>",
                    ValidationRule = CustomValidationRule(field),
                    Notes = string.IsNullOrWhiteSpace(field.DestinationMacro)
                        ? $"Available to custom actions and resources as {{Custom:{field.Id}}}."
                        : $"Available as {{Custom:{field.Id}}} and destination macro '{field.DestinationMacro}'."
                });
            }
        }

        return catalog;
    }

    public static string ToJson(EnterprisePropertyCatalog catalog)
        => JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });

    private static IEnumerable<EnterprisePropertyDefinition> BuiltInProperties(InstallProject project)
    {
        yield return new()
        {
            Name = "InstallPath",
            Scope = "built-in",
            Page = "Destination Folder",
            Label = "Destination folder",
            Type = "path",
            Required = true,
            DefaultValue = InstallScopeResolver.ResolveDefaultPath(project, InstallScopeResolver.IsPerUser(project)),
            ResponseFileSyntax = "\"installPath\": \"C:\\\\Program Files\\\\App\"",
            CommandLineSyntax = "/D=<path>",
            ValidationRule = "Must be a non-empty absolute or resolvable filesystem path.",
            Notes = "Canonical install directory. JSON 'targetDir' and CLI /DIR, /TARGETDIR, /INSTALLDIR aliases normalize to the same value."
        };
        yield return new()
        {
            Name = "PerUser",
            Scope = "built-in",
            Page = "Destination Folder",
            Label = "Install for current user only",
            Type = "boolean",
            Required = false,
            DefaultValue = InstallScopeResolver.IsPerUser(project).ToString().ToLowerInvariant(),
            ResponseFileSyntax = "\"properties\": { \"PerUser\": true }",
            CommandLineSyntax = "/PROPERTY:PerUser=true|false",
            ValidationRule = "Boolean: true/false, yes/no, 1/0.",
            Notes = "Controls per-user versus per-machine scope when the project allows the choice."
        };
        yield return new()
        {
            Name = "InstallType",
            Scope = "built-in",
            Page = "Select Components",
            Label = "Install type",
            Type = "enum",
            Required = false,
            DefaultValue = project.DefaultInstallType.ToString(),
            AllowedValues = Enum.GetNames<InstallationType>(),
            ResponseFileSyntax = "\"properties\": { \"InstallType\": \"Typical\" }",
            CommandLineSyntax = "/PROPERTY:InstallType=Typical|Complete|Custom",
            ValidationRule = "One of: Typical, Complete, Custom.",
            Notes = "Applies component defaults before explicit component selection."
        };
        yield return new()
        {
            Name = "Components",
            Scope = "built-in",
            Page = "Select Components",
            Label = "Selected component ids",
            Type = "string-list",
            Required = false,
            DefaultValue = string.Join(",", project.Components.Where(c => c.Required || c.Selected).Select(c => c.Id)),
            AllowedValues = project.Components.Select(c => c.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(),
            ResponseFileSyntax = "\"components\": [\"core\", \"docs\"]",
            CommandLineSyntax = "/COMPONENTS=<id,id>",
            ValidationRule = "Comma-separated component ids. Required components are always selected.",
            Notes = "Explicit component selection wins after install-type defaults."
        };
        yield return new()
        {
            Name = "CreateStartMenu",
            Scope = "built-in",
            Page = "Start Menu Folder",
            Label = "Create Start Menu shortcuts",
            Type = "boolean",
            Required = false,
            DefaultValue = "true",
            ResponseFileSyntax = "\"properties\": { \"CreateStartMenu\": true }",
            CommandLineSyntax = "/PROPERTY:CreateStartMenu=true|false",
            ValidationRule = "Boolean: true/false, yes/no, 1/0.",
            Notes = "When false, StartMenuFolder is ignored."
        };
        yield return new()
        {
            Name = "StartMenuFolder",
            Scope = "built-in",
            Page = "Start Menu Folder",
            Label = "Start Menu folder",
            Type = "string",
            Required = false,
            DefaultValue = project.DefaultGroupName ?? "",
            ResponseFileSyntax = "\"properties\": { \"StartMenuFolder\": \"Vendor\\\\App\" }",
            CommandLineSyntax = "/PROPERTY:StartMenuFolder=<folder>",
            ValidationRule = "Non-empty string when CreateStartMenu is true.",
            Notes = "Supports subfolders with backslashes."
        };
        yield return new()
        {
            Name = "CreateDesktopIcon",
            Scope = "built-in",
            Page = "Additional Tasks",
            Label = "Create desktop icon",
            Type = "boolean",
            Required = false,
            DefaultValue = "true",
            ResponseFileSyntax = "\"properties\": { \"CreateDesktopIcon\": true }",
            CommandLineSyntax = "/PROPERTY:CreateDesktopIcon=true|false",
            ValidationRule = "Boolean: true/false, yes/no, 1/0."
        };
        yield return new()
        {
            Name = "AutoStart",
            Scope = "built-in",
            Page = "Additional Tasks",
            Label = "Start application when Windows starts",
            Type = "boolean",
            Required = false,
            DefaultValue = "false",
            ResponseFileSyntax = "\"properties\": { \"AutoStart\": false }",
            CommandLineSyntax = "/PROPERTY:AutoStart=true|false",
            ValidationRule = "Boolean: true/false, yes/no, 1/0."
        };
        yield return new()
        {
            Name = "FileAssociations",
            Scope = "built-in",
            Page = "Additional Tasks",
            Label = "Register file associations",
            Type = "boolean",
            Required = false,
            DefaultValue = "true",
            ResponseFileSyntax = "\"properties\": { \"FileAssociations\": true }",
            CommandLineSyntax = "/PROPERTY:FileAssociations=true|false",
            ValidationRule = "Boolean: true/false, yes/no, 1/0.",
            Notes = "Applies to projects that author file association resources."
        };
        yield return new()
        {
            Name = "AcceptLicense",
            Scope = "built-in",
            Page = "License Agreement",
            Label = "Accept license",
            Type = "boolean",
            Required = !string.IsNullOrWhiteSpace(project.LicenseText),
            DefaultValue = "false",
            ResponseFileSyntax = "\"properties\": { \"AcceptLicense\": true }",
            CommandLineSyntax = "/PROPERTY:AcceptLicense=true",
            ValidationRule = "Must be true when the project has a license agreement.",
            Notes = "Silent mode treats /S as unattended consent only when enterprise policy permits it; this property records explicit consent in response files."
        };
    }

    private static string FieldType(CustomFieldType type)
        => type switch
        {
            CustomFieldType.Check => "boolean",
            CustomFieldType.Path => "path",
            CustomFieldType.Password => "secret",
            CustomFieldType.Radio => "enum",
            _ => "string"
        };

    private static string CustomValidationRule(CustomField field)
    {
        if (field.Type == CustomFieldType.Radio && field.Options.Count > 0)
            return "One of: " + string.Join(", ", field.Options) + ".";
        if (field.Type == CustomFieldType.Check)
            return field.Required ? "Must be true." : "Boolean: true/false, yes/no, 1/0.";
        return field.Required ? "Required non-empty value." : "Optional value.";
    }
}

public sealed class EnterprisePropertyDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "";
    [JsonPropertyName("page")]
    public string Page { get; init; } = "";
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";
    [JsonPropertyName("required")]
    public bool Required { get; init; }
    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; init; } = "";
    [JsonPropertyName("allowedValues")]
    public IReadOnlyList<string> AllowedValues { get; init; } = Array.Empty<string>();
    [JsonPropertyName("responseFileSyntax")]
    public string ResponseFileSyntax { get; init; } = "";
    [JsonPropertyName("commandLineSyntax")]
    public string CommandLineSyntax { get; init; } = "";
    [JsonPropertyName("validationRule")]
    public string ValidationRule { get; init; } = "";
    [JsonPropertyName("notes")]
    public string Notes { get; init; } = "";
}
