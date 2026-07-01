using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Pages;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Forms;

/// <summary>
/// Classic-style installer wizard — light sidebar with numbered step list, white content area.
/// This is a full-featured production alternative to <see cref="ThemedInstallerForm"/>.
/// </summary>
public class InstallerWizardForm : Form
{
    private Panel _sidebar = null!;
    private Button _backBtn = null!;
    private Button _nextBtn = null!;
    private Button _cancelBtn = null!;
    private Label _sidebarTitle = null!;
    private ListBox _stepList = null!;
    private Label _pageTitleLabel = null!;
    private Panel _pageHost = null!;
    private Panel _separator = null!;
    private PictureBox _banner = null!;

    private readonly List<IInstallerPage> _pages = new();
    private int _currentPage = -1;
    private readonly InstallContext _ctx = new();
    private bool _installComplete;
    private CompletePage? _completePage;
    private ErrorPage? _errorPage;
    private readonly bool _previewMode;
    private Panel? _installProgressPanel;
    private ProgressBar? _installProgressBar;
    private Label? _installStatusLabel;
    private bool _disposed;
    private Color _sidebarBg = Color.FromArgb(240, 240, 240);
    private Color _titleColor = Color.FromArgb(40, 40, 40);
    private Color _accentColor = Color.FromArgb(41, 98, 255);
    private TheTechIdea.Beep.Installer.InstallerBranding? _branding;
    private string _theme = "Modern";

    public InstallContext Context => _ctx;
    public InstallConfig Config => _ctx.Config;

    public InstallerWizardForm(InstallConfig config, bool previewMode = false)
        : this(config, null, previewMode) { }

    public InstallerWizardForm(InstallConfig config, TheTechIdea.Beep.Installer.InstallerBranding? branding, bool previewMode = false)
    {
        _previewMode = previewMode;
        _branding = branding;
        _theme = string.IsNullOrEmpty(branding?.DefaultTheme) ? "Modern" : branding.DefaultTheme;
        ApplyBrandingToColors();

        LanguageManager.Initialize();
        _ctx.Config = config;
        _ctx.InstallPath = config.DefaultInstallPath;
        _ctx.StartMenuFolder = config.StartMenuFolder;

        Text = string.IsNullOrEmpty(config.ProductName)
            ? "Beep Installer"
            : $"{config.ProductName} Setup";
        Size = new Size(780, 540);
        MinimumSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = SystemColors.Control;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // Apply branding for "Modern" theme: swap to dark sidebar like ThemedInstallerForm
        if (_theme == "Modern" && branding != null)
        {
            _sidebarBg = Engine.ThemeLoader.ParseColor(branding.SidebarBackgroundColor, Color.FromArgb(30, 30, 40));
            _titleColor = Engine.ThemeLoader.ParseColor(branding.SidebarTextColor, Color.White);
            _accentColor = Engine.ThemeLoader.ParseColor(branding.AccentColor, Color.FromArgb(41, 98, 255));
        }

        InitializeUi();
        BuildPages();
        NavigateTo(0);

        // If the current UI language is RTL (e.g. Arabic), apply RTL layout
        if (Engine.RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName))
            Engine.RtlHelper.ApplyRtl(this);
    }

    private void ApplyBrandingToColors()
    {
        if (_branding == null) return;
        // Theme defaults: Modern = dark, Classic/Compact = light
        if (_theme == "Modern")
        {
            _sidebarBg = Engine.ThemeLoader.ParseColor(_branding.SidebarBackgroundColor, Color.FromArgb(30, 30, 40));
            _titleColor = Engine.ThemeLoader.ParseColor(_branding.SidebarTextColor, Color.White);
        }
        else
        {
            _sidebarBg = Color.FromArgb(240, 240, 240);
            _titleColor = Color.FromArgb(40, 40, 40);
        }
        _accentColor = Engine.ThemeLoader.ParseColor(_branding.AccentColor, Color.FromArgb(41, 98, 255));
    }

    // ════════════════════════════════════════════════════════════════════
    //  UI construction
    // ════════════════════════════════════════════════════════════════════

    private void InitializeUi()
    {
        // ── Sidebar ──────────────────────────────────────────────────────
        _sidebar = new Panel
        {
            Location = new Point(0, 0), Size = new Size(180, 480),
            BackColor = _sidebarBg,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        // Optional banner — real image if configured/found, otherwise a solid colored strip
        var bannerImage = TryLoadBanner();
        if (bannerImage != null)
        {
            _banner = new PictureBox
            {
                Location = new Point(0, 0), Size = new Size(180, 60),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = bannerImage,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _sidebar.Controls.Add(_banner);
        }
        else
        {
            _banner = new PictureBox
            {
                Location = new Point(0, 0), Size = new Size(180, 8),
                BackColor = _accentColor,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _sidebar.Controls.Add(_banner);
        }

        _sidebarTitle = new Label
        {
            Text = "Setup steps",
            Location = new Point(12, 12), Size = new Size(156, 20),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _stepList = new ListBox
        {
            Location = new Point(8, 40), Size = new Size(164, 432),
            Font = new Font("Segoe UI", 9),
            BorderStyle = BorderStyle.None,
            BackColor = _sidebarBg,
            ForeColor = _titleColor,
            IntegralHeight = false,
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _sidebar.Controls.AddRange(new Control[] { _sidebarTitle, _stepList });

        // ── Page area (title + content) ─────────────────────────────────
        _pageTitleLabel = new Label
        {
            Location = new Point(192, 12), Size = new Size(576, 28),
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _separator = new Panel
        {
            Location = new Point(192, 42), Size = new Size(576, 1),
            BackColor = Color.FromArgb(220, 220, 220),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _pageHost = new Panel
        {
            Location = new Point(192, 48), Size = new Size(576, 410),
            BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        // ── Buttons ──────────────────────────────────────────────────────
        _backBtn = MakeButton("< Back", 410);
        _nextBtn = MakeButton("Next >", 510);
        _cancelBtn = MakeButton("Cancel", 610);

        _backBtn.Click += (_, _) => NavigateTo(_currentPage - 1);
        _nextBtn.Click += OnNextClicked;
        _cancelBtn.Click += (_, _) => { if (ConfirmCancel()) Close(); };

        ApplyLanguage();

        Controls.AddRange(new Control[]
        {
            _sidebar, _pageTitleLabel, _separator, _pageHost,
            _backBtn, _nextBtn, _cancelBtn
        });
    }

    private Button MakeButton(string text, int x) => new()
    {
        Text = text,
        Location = new Point(x, 470),
        Size = new Size(90, 28),
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        FlatStyle = FlatStyle.Standard,
        Font = new Font("Segoe UI", 9)
    };

    private void ApplyLanguage()
    {
        _backBtn.Text = LanguageManager.GetOrDefault("Btn_Back", "< Back");
        _nextBtn.Text = _currentPage == _pages.Count - 1
            ? LanguageManager.GetOrDefault("Btn_Install", "Install")
            : LanguageManager.GetOrDefault("Btn_Next", "Next >");
        _cancelBtn.Text = LanguageManager.GetOrDefault("Btn_Cancel", "Cancel");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Page construction & navigation
    // ════════════════════════════════════════════════════════════════════

    private void BuildPages()
    {
        _pages.Add(new WelcomePage());
        _pages.Add(new LicensePage());
        _pages.Add(new ComponentSelectionPage(_ctx));
        _pages.Add(new FolderPage());
        _pages.Add(new StartMenuPage());
        _pages.Add(new AdditionalTasksPage());
        _completePage = new CompletePage();
        _pages.Add(_completePage);
        _errorPage = new ErrorPage();
        _errorPage.ValidityChanged += (_, _) =>
        {
            var readyIdx = _pages.FindIndex(p => p is ReadyPage);
            if (readyIdx >= 0)
            {
                NavigateTo(readyIdx);
                BeginInvoke(() => OnNextClicked(null, EventArgs.Empty));
            }
        };
        _pages.Add(_errorPage);

        if (_pages[1] is LicensePage lic)
            lic.ValidityChanged += (_, ok) => _nextBtn.Enabled = ok;

        _stepList.Items.Clear();
        for (int i = 0; i < _pages.Count; i++)
            _stepList.Items.Add($"  {i + 1}. {_pages[i].PageTitle}");
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _currentPage = index;
        _pageHost.Controls.Clear();

        var page = _pages[index];
        var ctrl = (UserControl)page;
        ctrl.Dock = DockStyle.Fill;
        _pageHost.Controls.Add(ctrl);

        _pageTitleLabel.Text = page.PageTitle;
        page.OnEnter(_ctx);

        _backBtn.Enabled = index > 0 && !_installComplete;
        if (page.CanGoNext) _nextBtn.Enabled = true;
        _stepList.SelectedIndex = index;
        ApplyLanguage();
    }

    private bool ConfirmCancel()
    {
        if (_installComplete || _previewMode) return true;
        var r = MessageBox.Show(this,
            "Are you sure you want to cancel the installation?",
            "Cancel Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return r == DialogResult.Yes;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Next/Install handler
    // ════════════════════════════════════════════════════════════════════

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_installComplete) { Close(); return; }

        if (_currentPage == _pages.Count - 1)
        {
            await RunInstallAsync();
        }
        else
        {
            if (_pages[_currentPage].Validate())
            {
                CapturePageState();
                NavigateTo(_currentPage + 1);
            }
        }
    }

    /// <summary>Pulls state from each page into the shared context before navigating forward.</summary>
    private void CapturePageState()
    {
        foreach (var p in _pages)
        {
            switch (p)
            {
                case LicensePage lic: _ctx.AcceptLicense = lic.Accepted; break;
                case FolderPage fld:
                    _ctx.InstallPath = fld.SelectedPath;
                    _ctx.PerUser = fld.PerUser;
                    break;
                case StartMenuPage sm:
                    _ctx.CreateStartMenu = sm.CreateStartMenu;
                    _ctx.StartMenuFolder = sm.FolderName;
                    break;
                case AdditionalTasksPage at:
                    _ctx.CreateDesktopIcon = at.CreateDesktopIcon;
                    _ctx.AutoStart = at.AutoStart;
                    _ctx.FileAssociations = at.FileAssociations;
                    break;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Install execution
    // ════════════════════════════════════════════════════════════════════

    private async Task RunInstallAsync()
    {
        CapturePageState();
        if (!_ctx.AcceptLicense)
        {
            MessageBox.Show(this, "You must accept the license to continue.",
                "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShowInstallProgressUi();
        _backBtn.Enabled = false;
        _nextBtn.Enabled = false;
        _cancelBtn.Enabled = !_previewMode;
        _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Install", "Install");

        if (_previewMode)
        {
            for (int p = 0; p <= 100; p += 5)
            {
                if (_installProgressBar is { IsDisposed: false })
                    _installProgressBar.Value = Math.Min(p, _installProgressBar.Maximum);
                if (_installStatusLabel is { IsDisposed: false } && p % 20 == 0)
                    _installStatusLabel.Text = $"Simulating\u2026 {p}%";
                await Task.Delay(40);
            }
            ShowCompletePage(true, "(Preview) Installation would proceed here.", null);
            return;
        }

        try
        {
            var context = new SetupContext();
            context.Properties["InstallConfig"] = _ctx.Config;
            context.Properties["InstallPath"] = _ctx.InstallPath;
            context.Properties["CreateDesktopIcon"] = _ctx.CreateDesktopIcon;
            context.Properties["CreateStartMenu"] = _ctx.CreateStartMenu;
            context.Properties["AutoStart"] = _ctx.AutoStart;
            context.Properties["PerUser"] = _ctx.PerUser;

            var wizard = new SetupWizardBuilder()
                .WithId($"beep-install-classic-{Environment.TickCount}")
                .WithOptions(new SetupOptions { Environment = "Production" })
                .AddStep(new PrerequisiteCheckStep())
                .AddStep(new DirectoryCreateStep("installer.prerequisites.check"))
                .AddStep(new Steps.PayloadDownloadStep("installer.directory.create"))
                .AddStep(new FileCopyStep("installer.payload.download"))
                .AddStep(new ShortcutCreateStep("installer.files.copy"))
                .AddStep(new RegistryWriteStep("installer.shortcuts.create"))
                .AddStep(new VerifyInstallStep("installer.registry.write"))
                .Build();

            var progress = new Progress<TheTechIdea.Beep.Addin.PassedArgs>(args =>
            {
                if (_disposed) return;
                try
                {
                    Invoke(() =>
                    {
                        if (_installProgressBar is { IsDisposed: false })
                            _installProgressBar.Value = Math.Min(Math.Max(args.ParameterInt1, 0), 100);
                        if (_installStatusLabel is { IsDisposed: false } && !string.IsNullOrEmpty(args.Messege))
                            _installStatusLabel.Text = args.Messege;
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });

            await Task.Run(() => wizard.Run(context, progress));
            var report = wizard.GetReport();
            var manifestPath = Path.Combine(_ctx.InstallPath, "install-manifest.json");

            if (report.Succeeded)
            {
                ShowCompletePage(report.Succeeded,
                    $"{_ctx.Config.ProductName} has been installed to {_ctx.InstallPath}.",
                    File.Exists(manifestPath) ? manifestPath : null);
            }
            else
            {
                ShowErrorPage(report);
            }
        }
        catch (Exception ex)
        {
            ShowErrorPage(null, ex.Message);
        }
    }

    private void ShowErrorPage(TheTechIdea.Beep.SetUp.SetupReport? report = null, string? detail = null)
    {
        var msg = detail ?? $"Installation failed at step: {report?.StepResults.LastOrDefault(r => !r.Succeeded)?.Message ?? "unknown"}";
        _errorPage?.SetError(msg, null);
        NavigateTo(_pages.Count - 1);
        _nextBtn.Text = "Finish";
        _nextBtn.Enabled = false;
        _backBtn.Enabled = true;
        _installComplete = false;
    }

    private void ShowInstallProgressUi()
    {
        _pageHost.Controls.Clear();
        _pageTitleLabel.Text = LanguageManager.GetOrDefault("Wizard_Installing", "Installing");

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 40, 20, 20)
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _installStatusLabel = new Label
        {
            Text = "Preparing installation\u2026",
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _installProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        var spacer = new Panel { Dock = DockStyle.Fill };

        wrapper.Controls.Add(_installStatusLabel, 0, 0);
        wrapper.Controls.Add(_installProgressBar, 0, 1);
        wrapper.Controls.Add(spacer, 0, 2);

        _pageHost.Controls.Add(wrapper);
        _installProgressPanel = wrapper;
    }

    private void ShowCompletePage(bool success, string message, string? logPath)
    {
        if (_disposed) return;
        _pageHost.Controls.Clear();
        _completePage?.SetResult(success, message, logPath);
        if (_completePage != null)
        {
            _completePage.Dock = DockStyle.Fill;
            _pageHost.Controls.Add(_completePage);
        }
        _pageTitleLabel.Text = success
            ? LanguageManager.GetOrDefault("Wizard_Complete", "Installation Complete")
            : "Installation Failed";
        _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Finish", "Finish");
        _nextBtn.Enabled = true;
        _installComplete = true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Disposal
    // ════════════════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_installComplete && !_previewMode && !ConfirmCancel()) { e.Cancel = true; return; }
        _disposed = true;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _disposed = true;
        base.Dispose(disposing);
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.B) { NavigateTo(_currentPage - 1); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.N) { OnNextClicked(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.KeyCode == Keys.Escape) { if (ConfirmCancel()) Close(); e.Handled = true; }
    }

    private Image? TryLoadBanner()
    {
        var configured = _branding?.WelcomeBannerPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var asIs = configured!;
            if (!Path.IsPathRooted(asIs)) asIs = Path.Combine(AppContext.BaseDirectory, asIs);
            if (File.Exists(asIs)) return Engine.BannerLoader.Load(asIs, 180);
        }
        foreach (var name in new[] { "banner.png", "banner.jpg", "banner.bmp", "welcome.png" })
        {
            var img = Engine.BannerLoader.LoadNextToExe(name, 180);
            if (img != null) return img;
        }
        return null;
    }
}
