using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Pages;

public class ReadyPage : UserControl, IInstallerPage
{
    private Label _title = null!;
    private TextBox _summary = null!;
    private InstallContext? _context;
    private readonly RuntimePageLayout _layout;

    public string PageTitle => L("Runtime_Ready", "Ready to Install");
    public string Subtitle => L("Runtime_ReadyHint", "Review your settings and click Install to begin.");
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged
    {
        add { }
        remove { }
    }

    public ReadyPage()
    {
        _title = new Label
        {
            Text = PageTitle,
            Dock = DockStyle.Top,
            Size = new Size(500, 24),
            AutoSize = true
        };

        _summary = new TextBox
        {
            Name = "RuntimeReadySummary", AccessibleName = PageTitle,
            Dock = DockStyle.Fill,
            Size = new Size(500, 280),
            Multiline = true,
            ReadOnly = true,
            BackColor = InstallerTheme.Panel, ForeColor = InstallerTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical
        };

        Controls.AddRange(new Control[] { _summary, _title });
        _layout = new RuntimePageLayout(this, _title, _summary);
    }

    public void OnEnter(InstallContext ctx)
    {
        _context = ctx;
        ReloadStrings();
    }

    public void ReloadStrings()
    {
        _title.Text = PageTitle;
        _summary.AccessibleName = PageTitle;
        _layout?.Refresh();
        if (_context is not { } ctx) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{ctx.Project.AppName} {ctx.Project.AppVersion}");
        sb.AppendLine(ctx.Project.AppPublisher);
        sb.AppendLine($"{L("Wizard_Folder", "Destination Folder")}: {ctx.InstallPath}");
        sb.AppendLine($"{L("Runtime_InstallType", "Install type")}: {ctx.InstallType}");
        if (ctx.CreateStartMenu) sb.AppendLine($"{L("Wizard_StartMenu", "Start Menu Folder")}: {ctx.StartMenuFolder}");
        sb.AppendLine(ctx.PerUser ? L("Runtime_PerUser", "Scope: current user only") : L("Runtime_AllUsers", "Scope: all users on this computer"));

        var components = ctx.Project.Components.Where(c => c.Selected || c.Required).ToList();
        sb.AppendLine();
        sb.AppendLine($"{L("Help_Components", "Components")} ({components.Count}):");
        foreach (var c in components)
        {
            var tag = c.Required ? L("Runtime_Required", "Required") : L("Runtime_Optional", "Optional");
            sb.AppendLine($"  {tag,-11} {c.Name} — {LocaleFormatter.FormatSize(c.SizeBytes)}");
        }

        sb.AppendLine();
        sb.AppendLine(string.Format(L("Runtime_EstimatedSize", "Estimated payload size: {0}"), LocaleFormatter.FormatSize(ctx.EstimatedSizeBytes)));
        sb.AppendLine();
        sb.AppendLine(L("Runtime_ConfiguredChanges", "Configured prerequisites and system changes (conditions and detection can skip these; this is not an execution result):"));
        var p = ctx.Project;
        void Count(string key, string fallback, int count) { if (count > 0) sb.AppendLine($"  {L(key, fallback)}: {count}"); }
        Count("Section_Prerequisites", "Prerequisites", p.Prerequisites.Count);
        Count("Section_PrerequisiteCatalogs", "Prerequisite catalogs", p.PrerequisiteCatalogs.Count);
        Count("Section_Packages", "Packages", p.Packages.Count);
        Count("Section_Registry", "Registry entries", p.RegistryEntries.Count);
        Count("Section_EnvironmentVariables", "Environment variables", p.EnvironmentVariables.Count);
        Count("Section_WindowsServices", "Windows services", p.WindowsServices.Count);
        Count("Section_ScheduledTasks", "Scheduled tasks", p.ScheduledTasks.Count);
        Count("Section_FirewallRules", "Firewall rules", p.FirewallRules.Count);
        Count("Section_FileAssociations", "File associations", p.FileAssociations.Count);
        Count("Section_Certificates", "Certificates", p.Certificates.Count);
        Count("Section_ComRegistrations", "COM registrations", p.ComRegistrations.Count);
        Count("Section_DriverPackages", "Driver packages", p.DriverPackages.Count);
        Count("Section_Resources", "Provider resources", p.Resources.Count);
        Count("Section_ConfigTransforms", "Configuration transforms", p.ConfigTransforms.Count);
        Count("Section_IisAppPools", "IIS application pools", p.IisAppPools.Count);
        Count("Section_IisSites", "IIS sites", p.IisSites.Count);
        Count("Section_WebDeploy", "Web Deploy packages", p.WebDeployPackages.Count);
        Count("Section_Shortcuts", "Shortcuts", p.Shortcuts.Count);
        Count("Runtime_CustomActions", "Custom actions", p.CustomActions.Count);

        if (ctx.CreateDesktopIcon || ctx.CreateStartMenu || ctx.AutoStart)
        {
            sb.AppendLine();
            sb.AppendLine(L("Runtime_Tasks", "Selected additional tasks:"));
            if (ctx.CreateDesktopIcon) sb.AppendLine(L("Task_DesktopIcon", "Create desktop icon"));
            if (ctx.CreateStartMenu) sb.AppendLine(L("Runtime_StartMenu", "Create start menu entries"));
            if (ctx.AutoStart) sb.AppendLine(L("Task_AutoStart", "Auto-start with Windows"));
        }
        sb.AppendLine();
        sb.AppendLine(L("Install_CancelDisabled", "Installation cannot be interrupted once it has started."));
        _summary.Text = sb.ToString();
    }

    public new bool Validate() => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _layout?.Dispose();
        base.Dispose(disposing);
    }
}
