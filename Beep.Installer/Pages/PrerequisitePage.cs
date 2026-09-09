using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Svg;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class PrerequisitePage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _subtitle = null!;
    private BeepLabel _explanation = null!;
    private Panel _listPanel = null!;

    private List<PrerequisiteDetector.PrerequisiteResult> _results = new();
    private bool _canGoNext;
    private InstallContext _ctx = new();

    private const string IconsNs = "Beep.Installer.Resources.Icons";

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Prerequisites", "Prerequisites");
    public string Subtitle => "Checking system requirements...";
    public bool CanGoNext => _canGoNext;

    public event EventHandler<bool>? ValidityChanged;

    public PrerequisitePage()
    {
        Dock = DockStyle.Fill;
        BuildUi();
    }

    private void BuildUi()
    {
        _title = new BeepLabel
        {
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(0, 0),
            AutoSize = true,
            Text = "Prerequisites"
        };

        _subtitle = new BeepLabel
        {
            Font = new Font("Segoe UI", 9),
            Location = new Point(0, 30),
            AutoSize = true,
            ForeColor = Ui.InstallerTheme.MutedText,
            Text = "Checking system requirements..."
        };

        _listPanel = new Panel
        {
            Location = new Point(0, 60),
            Size = new Size(540, 340),
            AutoScroll = true
        };

        _explanation = new BeepLabel
        {
            Location = new Point(0, 410),
            Size = new Size(540, 40),
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.DimGray,
            Text = "Required prerequisites must be installed before continuing. " +
                   "You can install them now or continue if you have already done so manually."
        };

        Controls.AddRange(new Control[] { _title, _subtitle, _listPanel, _explanation });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        _listPanel.Controls.Clear();
        _results.Clear();
        _canGoNext = false;
        FireValidityChanged();

        _subtitle.Text = "Checking system requirements...";

        var isSelfContained = ctx.Bag.TryGetValue("IsSelfContained", out var sc) && sc is true;
        _results = PrerequisiteDetector.CheckAll(ctx.Project, isSelfContained);

        var y = 0;
        foreach (var result in _results)
        {
            var row = BuildRow(result, y);
            _listPanel.Controls.Add(row);
            y += 56;
        }

        UpdateCanGoNext();
    }

    private Panel BuildRow(PrerequisiteDetector.PrerequisiteResult result, int y)
    {
        var row = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(520, 50),
            BackColor = Color.FromArgb(248, 248, 252)
        };

        var icon = new PictureBox
        {
            Location = new Point(8, 13),
            Size = new Size(20, 20),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        icon.Image = LoadSvgIcon(IconPath(result), 20, 20);
        row.Controls.Add(icon);

        var nameLabel = new BeepLabel
        {
            Text = $"{result.Config.Name}{(result.InstalledVersion != null ? " v" + result.InstalledVersion : "")}",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(36, 4),
            AutoSize = true
        };
        row.Controls.Add(nameLabel);

        var statusLabel = new BeepLabel
        {
            Text = result.StatusText,
            ForeColor = result.Detected ? Color.FromArgb(0, 150, 0)
                : result.IsMandatory ? Color.FromArgb(200, 50, 50)
                : Color.FromArgb(200, 140, 0),
            Font = new Font("Segoe UI", 8),
            Location = new Point(36, 26),
            AutoSize = true
        };
        row.Controls.Add(statusLabel);

        if (!result.Detected)
        {
            var btnX = 36 + TextRenderer.MeasureText(nameLabel.Text, nameLabel.Font).Width + 12;

            if (!string.IsNullOrWhiteSpace(result.Config.DownloadUrl))
            {
                var downloadBtn = new BeepButton
                {
                    Text = "Download",
                    ImagePath = IconPath("download"),
                    Location = new Point(btnX, 10),
                    Size = new Size(90, 28),
                    Font = new Font("Segoe UI", 8)
                };
                var url = result.Config.DownloadUrl;
                downloadBtn.Click += (_, _) =>
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                row.Controls.Add(downloadBtn);
                btnX += 100;
            }

            if (!string.IsNullOrWhiteSpace(result.Config.DownloadUrl))
            {
                var installBtn = new BeepButton
                {
                    Text = "Install",
                    ImagePath = IconPath("file-download"),
                    Location = new Point(btnX, 10),
                    Size = new Size(80, 28),
                    Font = new Font("Segoe UI", 8)
                };
                var captured = result;
                installBtn.Click += async (_, _) =>
                {
                    await InstallPrerequisiteAsync(captured);
                    RebuildList();
                    UpdateCanGoNext();
                };
                row.Controls.Add(installBtn);
            }
        }

        return row;
    }

    private async Task InstallPrerequisiteAsync(PrerequisiteDetector.PrerequisiteResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Config.DownloadUrl)) return;

        try
        {
            result.StatusText = "Downloading...";
            result.IsChecking = true;
            RebuildList();

            var tempFile = Path.Combine(Path.GetTempPath(), $"beep_prereq_{result.Config.Id}.exe");
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(result.Config.DownloadUrl);
            await File.WriteAllBytesAsync(tempFile, data);

            result.StatusText = "Installing...";
            RebuildList();

            var args = result.Config.SilentInstallArgs ?? "/S";
            var psi = new ProcessStartInfo(tempFile, args)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                result.Detected = process.ExitCode == 0;
                result.StatusText = result.Detected ? "Detected" : "Installation failed";
            }
            else
            {
                result.StatusText = "Installation failed to start";
            }

            if (result.Detected)
            {
                var version = PrerequisiteDetector.GetDotNetRuntimeVersion();
                result.InstalledVersion = version;
            }
        }
        catch (Exception ex)
        {
            result.StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            result.IsChecking = false;
        }
    }

    private void RebuildList()
    {
        _listPanel.Controls.Clear();
        var y = 0;
        foreach (var r in _results)
        {
            _listPanel.Controls.Add(BuildRow(r, y));
            y += 56;
        }
    }

    private void UpdateCanGoNext()
    {
        _canGoNext = _results.All(r => r.Detected || !r.IsMandatory);

        if (_canGoNext)
            _subtitle.Text = "All prerequisites are met.";
        else
            _subtitle.Text = "Some prerequisites are missing. Install them or ensure they are available.";

        FireValidityChanged();
    }

    private void FireValidityChanged()
        => ValidityChanged?.Invoke(this, _canGoNext);

    public new bool Validate() => _canGoNext;

    private static string IconPath(PrerequisiteDetector.PrerequisiteResult result)
    {
        var file = result.Detected ? "circle-check" :
            result.IsMandatory ? "circle-x" : "alert-triangle";
        return IconPath(file);
    }

    private static string IconPath(string name)
        => $"{IconsNs}.{name}.svg";

    private static Image? LoadSvgIcon(string resourcePath, int width, int height)
    {
        try
        {
            var asm = typeof(PrerequisitePage).Assembly;
            using var stream = asm.GetManifestResourceStream(resourcePath);
            if (stream == null) return null;
            var svg = SvgDocument.Open<SvgDocument>(stream);
            var bitmap = new Bitmap(width, height);
            svg.Draw(bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
