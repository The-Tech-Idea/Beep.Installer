using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Beep.Installer.Forms;

/// <summary>
/// Modal dialog that displays a build error in a copy-enabled text box,
/// with a Copy-to-Clipboard button and Save-As-File button. Designed so
/// the user can copy the full error + stack trace and share it.
/// </summary>
public class BuildErrorForm : Form
{
    public BuildErrorForm(string title, string summary, string detail, System.Collections.Generic.IEnumerable<string>? buildLog = null)
    {
        Text = title;
        Size = new Size(820, 600);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(600, 400);
        BackColor = Color.White;

        // ── Header ──
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(200, 50, 50),
            Padding = new Padding(20, 14, 20, 14),
        };
        var iconLabel = new Label
        {
            Text = "✕",
            Font = new Font("Segoe UI", 24, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 12),
            AutoSize = true,
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(60, 16),
            AutoSize = true,
        };
        var summaryLabel = new Label
        {
            Text = summary,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            Location = new Point(60, 38),
            AutoSize = true,
        };
        header.Controls.AddRange(new Control[] { iconLabel, titleLabel, summaryLabel });

        // ── Body: copy-enabled TextBox ──
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
        };
        var detailBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(252, 250, 245),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var sb = new StringBuilder();
        sb.AppendLine("=========================================================");
        sb.AppendLine($"  {title}");
        sb.AppendLine($"  {summary}");
        sb.AppendLine($"  Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("=========================================================");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            sb.AppendLine("ERROR DETAILS:");
            sb.AppendLine(detail);
            sb.AppendLine();
        }
        if (buildLog != null)
        {
            var hasAny = false;
            foreach (var _ in buildLog) { hasAny = true; break; }
            if (hasAny)
            {
                sb.AppendLine("BUILD LOG:");
                foreach (var line in buildLog)
                    sb.AppendLine(line);
            }
        }
        detailBox.Text = sb.ToString();
        detailBox.Select(0, 0);
        body.Controls.Add(detailBox);

        // ── Action buttons ──
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 12, 10),
        };
        var closeBtn = new Button
        {
            Text = "Close",
            Width = 100,
            Height = 36,
            DialogResult = DialogResult.Cancel,
        };
        var saveBtn = new Button
        {
            Text = "Save As…",
            Width = 110,
            Height = 36,
        };
        saveBtn.Click += (_, _) => SaveLogToFile(detailBox.Text);
        var copyBtn = new Button
        {
            Text = "Copy to Clipboard",
            Width = 140,
            Height = 36,
            BackColor = Color.FromArgb(41, 98, 255),
            ForeColor = Color.White,
        };
        var copyClicked = false;
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(detailBox.Text);
                copyClicked = true;
                copyBtn.Text = "Copied!";
                copyBtn.BackColor = Color.FromArgb(46, 160, 67);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        var selectAllBtn = new Button
        {
            Text = "Select All",
            Width = 100,
            Height = 36,
        };
        selectAllBtn.Click += (_, _) =>
        {
            detailBox.Focus();
            detailBox.SelectAll();
        };
        actions.Controls.AddRange(new Control[] { closeBtn, saveBtn, copyBtn, selectAllBtn });

        Controls.Add(actions);
        Controls.Add(body);
        Controls.Add(header);

        AcceptButton = closeBtn;
        CancelButton = closeBtn;
        Shown += (_, _) =>
        {
            // Auto-copy on first open so the user can paste immediately
            if (!copyClicked)
            {
                try { Clipboard.SetText(detailBox.Text); }
                catch (Exception ex) { Beep.Installer.Engine.Diag.Warn("BuildErrorForm", "clipboard copy failed", ex); }
            }
            detailBox.Focus();
            detailBox.SelectAll();
        };
    }

    private void SaveLogToFile(string text)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"build-error-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dlg.FileName, text); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
