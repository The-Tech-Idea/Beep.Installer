using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Pages;

public class ReadyPage : UserControl, IInstallerPage
{
    private Label _title = null!;
    private TextBox _summary = null!;

    public string PageTitle => "Ready to Install";
    public string Subtitle => "Review your settings and click Install to begin.";
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
            Text = "Setup is ready to begin installation.",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(500, 24),
            AutoSize = true
        };

        _summary = new TextBox
        {
            Location = new Point(0, 35),
            Size = new Size(500, 280),
            Multiline = true,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { _title, _summary });
    }

    public void OnEnter(InstallContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Product       : {ctx.Config.ProductName} {ctx.Config.ProductVersion}");
        sb.AppendLine($"Publisher     : {ctx.Config.Publisher}");
        sb.AppendLine($"Install type  : {ctx.InstallType}");
        sb.AppendLine($"Install path  : {ctx.InstallPath}");
        sb.AppendLine($"Start menu    : {ctx.Config.StartMenuFolder}");

        if (ctx.PerUser)
            sb.AppendLine("Scope         : Current user only");

        var components = ctx.Config.Components.Where(c => c.Selected || c.Required).ToList();
        sb.AppendLine();
        sb.AppendLine($"Components ({components.Count}):");
        foreach (var c in components)
        {
            var tag = c.Required ? "[Required]" : "[Optional]";
            sb.AppendLine($"  {tag,-11} {c.Name} — {LocaleFormatter.FormatSize(c.SizeBytes)}");
        }

        sb.AppendLine();
        sb.AppendLine($"Total estimated size: {LocaleFormatter.FormatSize(ctx.EstimatedSizeBytes)}");

        if (ctx.CreateDesktopIcon || ctx.CreateStartMenu || ctx.AutoStart)
        {
            sb.AppendLine();
            sb.AppendLine("Additional tasks:");
            if (ctx.CreateDesktopIcon) sb.AppendLine("  • Create desktop icon");
            if (ctx.CreateStartMenu) sb.AppendLine("  • Create start menu entries");
            if (ctx.AutoStart) sb.AppendLine("  • Auto-start with Windows");
        }

        _summary.Text = sb.ToString();
    }

    public new bool Validate() => true;
}
