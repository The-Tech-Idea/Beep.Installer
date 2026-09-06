using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.Updates;
using TheTechIdea.Beep.WPF.Controls;
using TheTechIdea.Beep.WPF.Controls.Styling;

namespace Beep.Installer.Extensions.WPF
{
    /// <summary>
    /// A drop-in "an update is available" window built from Beep WPF controls. Given a completed
    /// <see cref="UpdateCheckResult"/> and an <see cref="IAppUpdateService"/>, it applies the
    /// update (app + any stale modules) with live progress. Show it via <see cref="ShowFor"/> or
    /// through <see cref="BeepUpdater"/>.
    /// </summary>
    public sealed class UpdateWindow : Window
    {
        private readonly IAppUpdateService _service;
        private readonly UpdateCheckResult _check;

        private readonly BeepProgressBar _progress = new() { ControlStyle = BeepControlStyle.Material3, Height = 18, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        private readonly BeepLabel _status = new() { ControlStyle = BeepControlStyle.Material3, Visibility = Visibility.Collapsed };
        private readonly BeepButton _updateButton = new() { ControlStyle = BeepControlStyle.Material3, Content = "Update Now", Width = 130, Height = 34, Margin = new Thickness(8, 0, 0, 0) };
        private readonly BeepButton _laterButton = new() { ControlStyle = BeepControlStyle.Material3, Content = "Later", Width = 100, Height = 34 };

        /// <summary>True once an update has been applied successfully (a restart is then advisable).</summary>
        public bool Applied { get; private set; }

        public UpdateWindow(UpdateCheckResult check, IAppUpdateService service)
        {
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            BuildUi();
        }

        private void BuildUi()
        {
            Title = "Update available";
            Width = 520;
            Height = 300;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var product = _check.Feed?.Product ?? "This application";

            var header = new BeepLabel
            {
                ControlStyle = BeepControlStyle.Material3,
                Text = "A new version is available",
                FontSize = 18,
                FontWeight = FontWeights.Bold
            };
            var message = new BeepLabel
            {
                ControlStyle = BeepControlStyle.Material3,
                Text = _check.AppUpdateAvailable
                    ? $"{product} {_check.LatestVersion} is available — you have {FriendlyCurrent()}."
                    : $"{product} has module updates available."
            };
            var detail = new BeepLabel { ControlStyle = BeepControlStyle.Material3, Text = BuildDetail() };

            _updateButton.Click += OnUpdateClicked;
            _laterButton.Click += (_, _) => { DialogResult = false; Close(); };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttons.Children.Add(_laterButton);
            buttons.Children.Add(_updateButton);

            var root = new StackPanel { Margin = new Thickness(22) };
            root.Children.Add(header);
            root.Children.Add(Spacer(message, 12));
            root.Children.Add(Spacer(detail, 8));
            root.Children.Add(Spacer(_progress, 18));
            root.Children.Add(Spacer(_status, 8));
            root.Children.Add(buttons);

            Content = root;
        }

        private static FrameworkElement Spacer(FrameworkElement element, double top)
        {
            element.Margin = new Thickness(0, top, 0, 0);
            return element;
        }

        private string FriendlyCurrent()
            => string.IsNullOrWhiteSpace(_check.CurrentVersion) ? "no version recorded" : _check.CurrentVersion;

        private string BuildDetail()
        {
            var lines = new List<string>();
            if (_check.AppUpdateAvailable)
                lines.Add(_check.RequiresFullInstall
                    ? "This is a full update (your version is too old for a delta)."
                    : "This is a delta update — only the changed files are downloaded.");
            if (_check.StaleModules.Count > 0)
                lines.Add($"Modules to update: {string.Join(", ", _check.StaleModules.ConvertAll(m => m.Id))}.");
            return string.Join(Environment.NewLine, lines);
        }

        private async void OnUpdateClicked(object sender, RoutedEventArgs e)
        {
            _updateButton.IsEnabled = false;
            _laterButton.IsEnabled = false;
            _progress.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Visible;

            // Progress<T> captures the UI Dispatcher context, so these callbacks marshal back safely.
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
                    _status.Text = "Update complete. Restart the application to run the new version.";
                    _updateButton.Content = "Restart";
                    _updateButton.IsEnabled = true;
                    _updateButton.Click -= OnUpdateClicked;
                    _updateButton.Click += (_, _) => { DialogResult = true; Close(); };
                    _laterButton.Content = "Close";
                    _laterButton.IsEnabled = true;
                }
                else
                {
                    _updateButton.Content = "Retry";
                    _updateButton.IsEnabled = true;
                    _laterButton.Content = "Close";
                    _laterButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _status.Text = $"Update failed: {ex.Message}";
                _updateButton.Content = "Retry";
                _updateButton.IsEnabled = true;
                _laterButton.Content = "Close";
                _laterButton.IsEnabled = true;
            }
        }

        /// <summary>Shows the window modally for a check result. Returns true if an update was applied.</summary>
        public static bool ShowFor(Window? owner, UpdateCheckResult check, IAppUpdateService service)
        {
            var win = new UpdateWindow(check, service);
            if (owner != null) win.Owner = owner;
            win.ShowDialog();
            return win.Applied;
        }
    }
}
