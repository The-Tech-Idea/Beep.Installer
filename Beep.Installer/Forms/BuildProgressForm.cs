using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Compact modeless progress window shown during a build.
/// The form stays in front of the main window, shows live percentage, and
/// never blocks the user from interacting with the rest of the IDE.
/// </summary>
public class BuildProgressForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _bar;
    private readonly Button _cancelBtn;
    private CancellationTokenSource? _cts;
    private bool _cancelled;

    public bool WasCancelled => _cancelled;

    public BuildProgressForm(string title, string status)
    {
        Text = title;
        Size = new Size(440, 140);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = Color.White;

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(16, 14),
            AutoSize = true,
        };
        _statusLabel = new Label
        {
            Text = status,
            Location = new Point(16, 40),
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 80, 80),
        };
        _bar = new ProgressBar
        {
            Location = new Point(16, 66),
            Size = new Size(392, 24),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        _cancelBtn = new Button
        {
            Text = L("Common_Cancel", "Cancel"),
            Location = new Point(330, 100),
            Size = new Size(80, 28),
        };
        _cancelBtn.Click += (_, _) =>
        {
            _cancelled = true;
            _cts?.Cancel();
            _cancelBtn.Enabled = false;
            _cancelBtn.Text = L("BuildProgress_Cancelling", "Cancelling...");
        };

        Controls.AddRange(new Control[] { titleLabel, _statusLabel, _bar, _cancelBtn });
        Engine.Accessibility.Attach(this);
    }

    public void UpdateProgress(int percent, string message)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => UpdateProgress(percent, message))); return; }
        _bar.Value = Math.Clamp(percent, 0, 100);
        _statusLabel.Text = message;
    }

    public void SetIndeterminate(string message)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => SetIndeterminate(message))); return; }
        _bar.Style = ProgressBarStyle.Marquee;
        _statusLabel.Text = message;
    }

    public void SetCancellationSource(CancellationTokenSource cts) => _cts = cts;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_cancelled)
        {
            _cancelled = true;
            _cts?.Cancel();
        }
        base.OnFormClosing(e);
    }
}
