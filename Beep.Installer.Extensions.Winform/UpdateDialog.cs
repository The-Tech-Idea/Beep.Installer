using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.Updates;
using TheTechIdea.Beep.Winform.Controls;
using TheTechIdea.Beep.Winform.Controls.ProgressBars;

namespace Beep.Installer.Extensions.Winform
{
    /// <summary>
    /// A drop-in "an update is available" dialog built from Beep WinForms controls. Given a
    /// completed <see cref="UpdateCheckResult"/> and an <see cref="IAppUpdateService"/>, it lets
    /// the user apply the update with live progress — the app-level update plus any stale modules.
    /// Show it with <see cref="ShowFor"/> or via <see cref="BeepUpdater"/>.
    /// </summary>
    public sealed class UpdateDialog : Form
    {
        private readonly IAppUpdateService _service;
        private readonly UpdateCheckResult _check;

        private readonly BeepLabel _header = new();
        private readonly BeepLabel _message = new();
        private readonly BeepLabel _detail = new();
        private readonly BeepProgressBar _progress = new();
        private readonly BeepLabel _status = new();
        private readonly BeepButton _updateButton = new();
        private readonly BeepButton _laterButton = new();

        /// <summary>True once an update has been applied successfully (a restart is then advisable).</summary>
        public bool Applied { get; private set; }

        public UpdateDialog(UpdateCheckResult check, IAppUpdateService service)
        {
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Update available";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(480, 268);

            var product = _check.Feed?.Product ?? "This application";

            _header.Text = "A new version is available";
            _header.Location = new Point(20, 18);
            _header.Size = new Size(440, 28);
            _header.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

            _message.Text = _check.AppUpdateAvailable
                ? $"{product} {_check.LatestVersion} is available — you have {FriendlyCurrent()}."
                : $"{product} has module updates available.";
            _message.Location = new Point(20, 54);
            _message.Size = new Size(440, 24);

            _detail.Text = BuildDetail();
            _detail.Location = new Point(20, 82);
            _detail.Size = new Size(440, 44);

            _progress.Location = new Point(20, 134);
            _progress.Size = new Size(440, 18);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Value = 0;
            _progress.Visible = false;

            _status.Location = new Point(20, 158);
            _status.Size = new Size(440, 24);
            _status.Visible = false;

            _updateButton.Text = "Update Now";
            _updateButton.Size = new Size(130, 36);
            _updateButton.Location = new Point(330, 214);
            _updateButton.Click += OnUpdateClicked;

            _laterButton.Text = "Later";
            _laterButton.Size = new Size(100, 36);
            _laterButton.Location = new Point(216, 214);
            _laterButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { _header, _message, _detail, _progress, _status, _updateButton, _laterButton });
        }

        private string FriendlyCurrent()
            => string.IsNullOrWhiteSpace(_check.CurrentVersion) ? "no version recorded" : _check.CurrentVersion;

        private string BuildDetail()
        {
            var lines = new System.Collections.Generic.List<string>();
            if (_check.AppUpdateAvailable)
                lines.Add(_check.RequiresFullInstall
                    ? "This is a full update (your version is too old for a delta)."
                    : "This is a delta update — only the changed files are downloaded.");
            if (_check.StaleModules.Count > 0)
                lines.Add($"Modules to update: {string.Join(", ", _check.StaleModules.ConvertAll(m => m.Id))}.");
            return string.Join(Environment.NewLine, lines);
        }

        private async void OnUpdateClicked(object? sender, EventArgs e)
        {
            _updateButton.Enabled = false;
            _laterButton.Enabled = false;
            _progress.Visible = true;
            _status.Visible = true;

            // Progress<T> captures this UI thread's SynchronizationContext, so the callbacks below
            // marshal back safely — no Invoke needed.
            var progress = new Progress<PassedArgs>(a =>
            {
                if (a.ParameterInt1 is > 0 and <= 100) _progress.Value = a.ParameterInt1;
                if (!string.IsNullOrEmpty(a.Messege)) _status.Text = a.Messege;
            });

            try
            {
                var ok = true;

                if (_check.AppUpdateAvailable)
                {
                    _status.Text = "Downloading and applying update…";
                    var result = await _service.ApplyAppUpdateAsync(_check, progress);
                    ok = result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
                    _status.Text = result.Message;
                }

                if (ok && _check.StaleModules.Count > 0)
                {
                    _status.Text = "Updating modules…";
                    var modResult = await _service.ApplyModuleUpdatesAsync(_check, progress);
                    ok = modResult.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok;
                    _status.Text = modResult.Message;
                }

                if (ok)
                {
                    _progress.Value = 100;
                    Applied = true;
                    _updateButton.Text = "Restart";
                    _updateButton.Enabled = true;
                    _updateButton.Click -= OnUpdateClicked;
                    _updateButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
                    _laterButton.Text = "Close";
                    _laterButton.Enabled = true;
                    _status.Text = "Update complete. Restart the application to run the new version.";
                }
                else
                {
                    _laterButton.Text = "Close";
                    _laterButton.Enabled = true;
                    _updateButton.Text = "Retry";
                    _updateButton.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _status.Text = $"Update failed: {ex.Message}";
                _laterButton.Text = "Close";
                _laterButton.Enabled = true;
                _updateButton.Text = "Retry";
                _updateButton.Enabled = true;
            }
        }

        /// <summary>Shows the dialog modally for a check result. Returns true if an update was applied.</summary>
        public static bool ShowFor(IWin32Window? owner, UpdateCheckResult check, IAppUpdateService service)
        {
            using var dlg = new UpdateDialog(check, service);
            if (owner != null) dlg.ShowDialog(owner); else dlg.ShowDialog();
            return dlg.Applied;
        }
    }
}
