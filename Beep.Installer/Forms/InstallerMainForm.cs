using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Forms;

/// <summary>
/// Simple modal progress display used while the installer is running.
/// </summary>
public class InstallerMainForm : Form
{
    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Label _stepLabel = null!;
    private Button _cancelButton = null!;
    private TextBox _logBox = null!;

    private bool _cancelled;

    public bool Cancelled => _cancelled;

    public InstallerMainForm()
    {
        InitializeComponent();
        CancelButton = _cancelButton;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
    }

    private void InitializeComponent()
    {
        Text = LanguageManager.GetOrDefault("Wizard_Installing", "Installing\u2026");
        Size = new Size(700, 460);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        _titleLabel = new Label
        {
            Text = LanguageManager.GetOrDefault("Wizard_Installing", "Installing\u2026"),
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(24, 16),
            Size = new Size(640, 32)
        };
        _subtitleLabel = new Label
        {
            Text = "Please wait while the application is being installed.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(24, 52),
            Size = new Size(640, 20),
            ForeColor = Color.Gray
        };
        _stepLabel = new Label
        {
            Location = new Point(24, 84),
            Size = new Size(640, 20)
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(24, 112),
            Size = new Size(640, 24),
            Style = ProgressBarStyle.Continuous
        };
        _statusLabel = new Label
        {
            Location = new Point(24, 144),
            Size = new Size(640, 20)
        };
        _logBox = new TextBox
        {
            Location = new Point(24, 172),
            Size = new Size(640, 220),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(574, 400),
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancelButton.Click += (_, _) =>
        {
            _cancelButton.Enabled = false;
            _cancelled = true;
            AppendLog("Cancellation requested\u2026");
        };

        Controls.AddRange(new Control[]
        {
            _titleLabel, _subtitleLabel, _stepLabel, _progressBar,
            _statusLabel, _logBox, _cancelButton
        });
    }

    public void ShowStep(string stepName, int stepIndex, int totalSteps)
    {
        if (InvokeRequired) { Invoke(() => ShowStep(stepName, stepIndex, totalSteps)); return; }
        _stepLabel.Text = $"Step {stepIndex}/{totalSteps}: {stepName}";
    }

    public void UpdateProgress(PassedArgs args)
    {
        if (InvokeRequired) { Invoke(() => UpdateProgress(args)); return; }
        _progressBar.Value = Math.Clamp(args.ParameterInt1, 0, 100);
        if (!string.IsNullOrEmpty(args.Messege))
        {
            _statusLabel.Text = args.Messege;
            AppendLog(args.Messege);
        }
    }

    public void ShowResult(SetupReport report)
    {
        if (InvokeRequired) { Invoke(() => ShowResult(report)); return; }
        _cancelButton.Enabled = false;
        _progressBar.Value = report.Succeeded ? 100 : _progressBar.Value;
        _titleLabel.Text = report.Succeeded
            ? LanguageManager.GetOrDefault("Wizard_Complete", "Installation Complete")
            : "Installation Failed";
        var last = report.StepResults.LastOrDefault();
        _subtitleLabel.Text = report.Succeeded
            ? LanguageManager.GetOrDefault("Complete_Success", "The application has been installed successfully.")
            : $"Installation failed. {last?.Message ?? "Unknown error."}";
    }

    private void AppendLog(string line)
    {
        _logBox.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        _logBox.SelectionStart = _logBox.Text.Length;
        _logBox.ScrollToCaret();
    }
}
