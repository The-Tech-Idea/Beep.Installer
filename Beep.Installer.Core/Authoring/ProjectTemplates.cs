using System.Collections.Generic;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>
/// Installer script templates gallery. Built-in starting points (Empty/Console/WinForms/WPF/Service)
/// that produce a configured, buildable <see cref="InstallProject"/> in one step.
/// </summary>
public class ProjectTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";

    public override string ToString() => $"{Name} — {Description}";
}

public static class ProjectTemplates
{
    public const string EmptyId = "empty";
    public const string ConsoleId = "console";
    public const string WinFormsId = "winforms";
    public const string WpfId = "wpf";
    public const string ServiceId = "service";

    /// <summary>The built-in templates shown in the gallery.</summary>
    public static IReadOnlyList<ProjectTemplate> Builtins => new[]
    {
        new ProjectTemplate { Id = EmptyId,    Name = "Empty",        Description = "Blank project — configure everything yourself.", Category = "General" },
        new ProjectTemplate { Id = ConsoleId,  Name = "Console App",  Description = "Command-line tool: core component + Start Menu shortcut.", Category = "Desktop" },
        new ProjectTemplate { Id = WinFormsId, Name = "WinForms App", Description = "Desktop app: core component + Desktop & Start Menu shortcuts.", Category = "Desktop" },
        new ProjectTemplate { Id = WpfId,      Name = "WPF App",      Description = "Desktop app: core component + Desktop & Start Menu shortcuts.", Category = "Desktop" },
        new ProjectTemplate { Id = ServiceId,  Name = "Windows Service", Description = "Background service: core component + Start Menu shortcut.", Category = "Service" },
    };

    /// <summary>Creates a fresh installer model from the named template. Unknown ids fall back to Empty.</summary>
    public static InstallProject Create(string templateId, string productName, string version, string publisher, string sourceDirectory)
    {
        var p = InstallerProjectFactory.CreateNew(productName, version, publisher, sourceDirectory);
        var product = string.IsNullOrWhiteSpace(productName) ? "MyApplication" : productName.Trim();

        switch ((templateId ?? EmptyId).ToLowerInvariant())
        {
            case ConsoleId:
                AddCoreComponent(p, "Console Application");
                p.Shortcuts.Add(Shortcut(product, "consoleapp.exe", ShortcutLocation.StartMenu, product));
                break;
            case WinFormsId:
            case WpfId:
                AddCoreComponent(p, "Main Application");
                var exe = templateId == WpfId ? "app.dll" : "app.exe";
                p.Shortcuts.Add(Shortcut(product, exe, ShortcutLocation.Desktop, ""));
                p.Shortcuts.Add(Shortcut(product, exe, ShortcutLocation.StartMenu, product));
                break;
            case ServiceId:
                AddCoreComponent(p, "Service Host");
                p.Shortcuts.Add(Shortcut(product, "servicehost.exe", ShortcutLocation.Startup, ""));
                break;
        }
        return p;
    }

    private static void AddCoreComponent(InstallProject p, string description)
    {
        p.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Description = description,
            Required = true,
            Selected = true,
            IncludedIn = InstallationType.Typical
        });
    }

    private static ShortcutDefinition Shortcut(string name, string target, ShortcutLocation location, string subfolder)
        => new()
        {
            Name = name,
            TargetPath = target,
            Location = location,
            StartMenuSubfolder = subfolder ?? ""
        };
}