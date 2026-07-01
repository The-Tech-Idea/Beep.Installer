using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Beep.Installer.Forms;

/// <summary>Modal dialog for creating a new install project.</summary>
public class ProjectNewDialog : Form
{
    private readonly TextBox _nameBox = null!;
    private readonly TextBox _versionBox = null!;
    private readonly TextBox _publisherBox = null!;
    private readonly TextBox _sourceBox = null!;
    private readonly Button _browseBtn = null!;
    private readonly Button _okBtn = null!;
    private readonly Button _cancelBtn = null!;

    public new string ProductName => _nameBox.Text.Trim();
    public string Version => _versionBox.Text.Trim();
    public string Publisher => _publisherBox.Text.Trim();
    public string SourceDirectory => _sourceBox.Text.Trim();

    public ProjectNewDialog()
    {
        Text = "New Beep Installer Project";
        Size = new Size(520, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        layout.Controls.Add(MakeLabel("Product name:"), 0, r);
        _nameBox = new TextBox { Dock = DockStyle.Fill, Text = "MyApplication" };
        layout.SetColumnSpan(_nameBox, 2);
        layout.Controls.Add(_nameBox, 1, r);
        r++;

        layout.Controls.Add(MakeLabel("Version:"), 0, r);
        _versionBox = new TextBox { Dock = DockStyle.Fill, Text = "1.0.0" };
        layout.SetColumnSpan(_versionBox, 2);
        layout.Controls.Add(_versionBox, 1, r);
        r++;

        layout.Controls.Add(MakeLabel("Publisher:"), 0, r);
        _publisherBox = new TextBox { Dock = DockStyle.Fill, Text = Environment.UserName };
        layout.SetColumnSpan(_publisherBox, 2);
        layout.Controls.Add(_publisherBox, 1, r);
        r++;

        layout.Controls.Add(MakeLabel("Source directory:"), 0, r);
        _sourceBox = new TextBox { Dock = DockStyle.Fill };
        _browseBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        _browseBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog(this) == DialogResult.OK) _sourceBox.Text = d.SelectedPath;
        };
        layout.Controls.Add(_sourceBox, 1, r);
        layout.Controls.Add(_browseBtn, 2, r);
        r++;

        var hint = new Label
        {
            Text = "The source directory contains the files that will be installed (e.g. your app's bin/Release output).",
            Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Height = 40
        };
        layout.SetColumnSpan(hint, 3);
        layout.Controls.Add(hint, 0, r);
        r++;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Height = 36 };
        _okBtn = new Button { Text = "Create", Width = 90, Height = 28, DialogResult = DialogResult.OK };
        _cancelBtn = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { _okBtn, _cancelBtn });
        layout.SetColumnSpan(buttons, 3);
        layout.Controls.Add(buttons, 0, r);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        Controls.Add(layout);
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
}
