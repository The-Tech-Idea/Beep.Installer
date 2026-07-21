using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Engine;

namespace Beep.Installer.Forms;

/// <summary>
/// Final result dialog shown after a build completes. Gives the user a clear
/// path to the produced EXE: select/copy path, open containing folder, run the installer.
/// </summary>
public class BuildResultForm : Form
{
    public BuildResultForm(BuildPipeline.BuildResult result, string projectName)
    {
        Text = result.Success ? "Build Complete" : "Build Failed";
        Size = new Size(720, 460);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        var headerColor = result.Success ? Color.FromArgb(46, 160, 67) : Color.FromArgb(200, 50, 50);

        // ── Header banner ──
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = headerColor,
            Padding = new Padding(20, 16, 20, 16),
        };
        var statusIcon = new Label
        {
            Text = result.Success ? "✓" : "✕",
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 14),
        };
        var statusTitle = new Label
        {
            Text = result.Success ? "Build succeeded" : "Build failed",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(70, 18),
        };
        var statusSub = new Label
        {
            Text = $"{projectName}  •  {result.Elapsed.TotalSeconds:F1}s",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(240, 240, 240),
            AutoSize = true,
            Location = new Point(70, 42),
        };
        header.Controls.AddRange(new Control[] { statusIcon, statusTitle, statusSub });

        // ── Output file row ──
        var fileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            ColumnCount = 3,
            Padding = new Padding(20, 12, 20, 4),
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var fileLabel = new Label
        {
            Text = "Setup EXE:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var pathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.WhiteSmoke,
        };
        var copyBtn = new Button
        {
            Text = "Copy",
            Width = 70,
            Height = 28,
        };
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(result.OutputFile);
                copyBtn.Text = "Copied!";
            }
            catch (Exception ex)
            {
                // The clipboard can be locked by another process. Saying "Copied!" regardless
                // told the user their paste would work when it would not.
                copyBtn.Text = "Copy failed";
                Beep.Installer.Engine.Diag.Warn("BuildResultForm", "clipboard copy failed", ex);
            }
        };
        if (!string.IsNullOrEmpty(result.OutputFile) && File.Exists(result.OutputFile))
        {
            pathBox.Text = result.OutputFile;
        }
        else
        {
            pathBox.Text = result.Success ? "(no output file)" : "(build failed — see log)";
            pathBox.ForeColor = Color.Gray;
        }
        fileRow.Controls.Add(fileLabel, 0, 0);
        fileRow.Controls.Add(pathBox, 1, 0);
        fileRow.Controls.Add(copyBtn, 2, 0);

        var setupRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 2,
            Padding = new Padding(20, 4, 20, 4),
        };
        setupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        setupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setupRow.Controls.Add(new Label
        {
            Text = "Setup script:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        setupRow.Controls.Add(new TextBox
        {
            Text = !string.IsNullOrWhiteSpace(result.SetupScriptPath) && File.Exists(result.SetupScriptPath)
                ? result.SetupScriptPath
                : "(not produced)",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.WhiteSmoke,
        }, 1, 0);

        // ── Size + Msix row ──
        var statsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            ColumnCount = 2,
            Padding = new Padding(20, 4, 20, 4),
        };
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var sizeLabel = new Label
        {
            Text = result.OutputSizeBytes > 0
                ? $"Size: {FormatSize(result.OutputSizeBytes)}"
                : "Size: —",
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(60, 60, 60),
        };
        var msixLabel = new Label
        {
            Text = !string.IsNullOrEmpty(result.MsixPackagePath) && File.Exists(result.MsixPackagePath)
                ? $"MSIX: {Path.GetFileName(result.MsixPackagePath)}"
                : "MSIX: —",
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(60, 60, 60),
        };
        statsRow.Controls.Add(sizeLabel, 0, 0);
        statsRow.Controls.Add(msixLabel, 1, 0);

        // ── Warnings/errors row ──
        var issuesRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            ColumnCount = 2,
            Padding = new Padding(20, 4, 20, 4),
        };
        issuesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        issuesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var warnLabel = new Label
        {
            Text = $"Warnings: {result.Warnings?.Count ?? 0}",
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = result.Warnings?.Count > 0 ? Color.DarkOrange : Color.FromArgb(60, 60, 60),
        };
        var errLabel = new Label
        {
            Text = $"Errors: {result.Errors?.Count ?? 0}",
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = result.Errors?.Count > 0 ? Color.DarkRed : Color.FromArgb(60, 60, 60),
        };
        issuesRow.Controls.Add(warnLabel, 0, 0);
        issuesRow.Controls.Add(errLabel, 1, 0);

        // ── Action buttons ──
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 14, 12, 14),
        };
        var closeBtn = new Button
        {
            Text = "Close",
            Width = 100,
            Height = 36,
            DialogResult = DialogResult.Cancel,
        };
        var runBtn = new Button
        {
            Text = "Run Installer",
            Width = 130,
            Height = 36,
            Enabled = result.Success && File.Exists(result.OutputFile),
        };
        runBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(result.OutputFile) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var openFolderBtn = new Button
        {
            Text = "Open Folder",
            Width = 120,
            Height = 36,
            Enabled = !string.IsNullOrEmpty(result.OutputFile) && File.Exists(result.OutputFile),
        };
        openFolderBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.OutputFile}\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        actions.Controls.AddRange(new Control[] { closeBtn, runBtn, openFolderBtn });

        Controls.Add(actions);
        Controls.Add(issuesRow);
        Controls.Add(statsRow);
        Controls.Add(setupRow);
        Controls.Add(fileRow);
        Controls.Add(header);

        AcceptButton = closeBtn;
        CancelButton = closeBtn;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes > 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        if (bytes > 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }
}
