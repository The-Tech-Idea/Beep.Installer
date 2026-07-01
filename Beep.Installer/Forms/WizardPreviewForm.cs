using System;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Models;

namespace Beep.Installer.Forms;

/// <summary>
/// Preview the install wizard exactly as the end user will see it.
/// Offers both classic and themed styles side-by-side.
/// </summary>
public class WizardPreviewForm : Form
{
    private TabControl _tabs = null!;
    private readonly InstallProject _project;

    public WizardPreviewForm(InstallProject project)
    {
        _project = project;
        Text = $"Preview — {project.InstallConfig.ProductName} Setup";
        Size = new Size(880, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(760, 540);

        var hint = new Label
        {
            Text = "Preview mode — no files will be installed. Walk through the wizard to verify the end-user experience.",
            Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
            BackColor = SystemColors.Info, ForeColor = SystemColors.InfoText, Padding = new Padding(8, 0, 0, 0)
        };

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildStyleTab("Themed (modern dark sidebar)", new ThemedInstallerForm(project.InstallConfig, project.Branding, previewMode: true)));
        _tabs.TabPages.Add(BuildStyleTab("Classic (light sidebar)", new InstallerWizardForm(project.InstallConfig, project.Branding, previewMode: true)));

        Controls.Add(_tabs);
        Controls.Add(hint);

        FormClosed += (_, _) =>
        {
            foreach (TabPage tp in _tabs.TabPages)
                if (tp.Controls[0] is Form f) { try { f.Close(); f.Dispose(); } catch { } }
        };
    }

    private TabPage BuildStyleTab(string title, Form hosted)
    {
        hosted.Dock = DockStyle.Fill;
        hosted.TopLevel = false;
        hosted.FormBorderStyle = FormBorderStyle.None;
        hosted.Show();

        var page = new TabPage(title) { Padding = new Padding(0) };
        page.Controls.Add(hosted);
        return page;
    }
}
