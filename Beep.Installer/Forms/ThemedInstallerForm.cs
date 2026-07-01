using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Pages;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Forms;

/// <summary>
/// Themed installer wizard with dark sidebar and modern styling.
/// Used by the GENERATED Setup.exe (and for preview from the Package Builder).
/// </summary>
public class ThemedInstallerForm : Form
{
    private Panel _sidebar = null!;
    private Panel _content = null!;
    private Button _backBtn = null!;
    private Button _nextBtn = null!;
    private Button _cancelBtn = null!;
    private Label _stepIndicator = null!;
    private ProgressBar _overallProgress = null!;

    private readonly List<IInstallerPage> _pages = new();
    private int _currentPage = -1;
    private readonly InstallContext _ctx = new();
    private bool _installComplete;
    private CompletePage? _completePage;
    private ErrorPage? _errorPage;
    private readonly bool _previewMode;

    private Color SidebarBg = Color.FromArgb(30, 30, 40);
    private Color AccentColor = Color.FromArgb(41, 98, 255);
    private Color SidebarText = Color.LightGray;
    private readonly TheTechIdea.Beep.Installer.InstallerBranding _branding;
    private string _theme = "Modern";

    public ThemedInstallerForm(InstallConfig config, bool previewMode = false)
        : this(config, null, previewMode) { }

    public ThemedInstallerForm(InstallConfig config, TheTechIdea.Beep.Installer.InstallerBranding? branding, bool previewMode = false)
    {
        _previewMode = previewMode;
        _branding = branding ?? new TheTechIdea.Beep.Installer.InstallerBranding();
        _theme = string.IsNullOrEmpty(_branding.DefaultTheme) ? "Modern" : _branding.DefaultTheme;

        // Apply branding colors (overridden by theme)
        ApplyBrandingToColors();

        LanguageManager.Initialize();
        _ctx.Config = config;
        _ctx.InstallPath = config.DefaultInstallPath;
        _ctx.StartMenuFolder = config.StartMenuFolder;

        Text = string.IsNullOrEmpty(_branding.WindowTitle) || _branding.WindowTitle == "Beep Installer"
            ? (string.IsNullOrEmpty(config.ProductName) ? "Beep Installer" : $"{config.ProductName} Setup")
            : _branding.WindowTitle;
        Size = new Size(820, 580);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        InitializeUi();
        BuildPages();
        NavigateTo(0);
    }

    private void ApplyBrandingToColors()
    {
        if (_theme == "Classic")
        {
            // Classic look: light sidebar
            SidebarBg = Engine.ThemeLoader.ParseColor("#F0F0F0", Color.FromArgb(240, 240, 240));
            SidebarText = Color.FromArgb(40, 40, 40);
            AccentColor = Engine.ThemeLoader.ParseColor(_branding.AccentColor, Color.FromArgb(41, 98, 255));
        }
        else if (_theme == "Compact")
        {
            SidebarBg = Color.Black;
            SidebarText = Color.White;
            AccentColor = Engine.ThemeLoader.ParseColor(_branding.AccentColor, Color.FromArgb(0, 120, 215));
        }
        else
        {
            // Modern (default)
            SidebarBg = Engine.ThemeLoader.ParseColor(_branding.SidebarBackgroundColor, Color.FromArgb(30, 30, 40));
            SidebarText = Engine.ThemeLoader.ParseColor(_branding.SidebarTextColor, Color.LightGray);
            AccentColor = Engine.ThemeLoader.ParseColor(_branding.AccentColor, Color.FromArgb(41, 98, 255));
        }
    }

    private void InitializeUi()
    {
        _sidebar = new Panel
        {
            Location = new Point(0, 0), Size = new Size(200, 540),
            BackColor = SidebarBg,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        // Optional banner image at the top of the sidebar
        var banner = TryLoadBanner();
        if (banner != null)
        {
            var bannerBox = new PictureBox
            {
                Location = new Point(0, 0),
                Size = new Size(200, 60),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = banner,
                BackColor = SidebarBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _sidebar.Controls.Add(bannerBox);
        }

        var productName = _ctx.Config.ProductName;
        var logo = new Label
        {
            Text = Truncate(productName, 18),
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(16, 70),
            Size = new Size(168, 32),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var versionLabel = new Label
        {
            Text = $"v{_ctx.Config.ProductVersion}",
            Font = new Font("Segoe UI", 8), ForeColor = Color.Gray,
            Location = new Point(16, 102), Size = new Size(168, 16),
            TextAlign = ContentAlignment.MiddleCenter
        };
        _stepIndicator = new Label
        {
            Location = new Point(12, 130), Size = new Size(176, 330),
            ForeColor = SidebarText, Font = new Font("Segoe UI", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _overallProgress = new ProgressBar
        {
            Location = new Point(12, 500), Size = new Size(176, 6),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Visible = false,
            Style = ProgressBarStyle.Continuous
        };

        // Language selector
        var langLabel = new Label
        {
            Text = "Language", Location = new Point(12, 460), Size = new Size(176, 14),
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        var langCombo = new ComboBox
        {
            Location = new Point(12, 474), Size = new Size(176, 22),
            DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        var cultureNames = new (string code, string display)[]
        {
            ("en", "English"), ("ar", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629"),
            ("es", "Espa\u00f1ol"), ("fr", "Fran\u00e7ais"),
            ("de", "Deutsch"), ("zh", "\u4E2D\u6587"),
            ("ja", "\u65E5\u672C\u8A9E"), ("pt", "Portugu\u00eas")
        };
        foreach (var (code, display) in cultureNames)
            langCombo.Items.Add(new LangChoice(code, display));
        langCombo.SelectedIndex = 0;
        langCombo.SelectedIndexChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is LangChoice lc)
            {
                LanguageManager.SetLanguage(lc.Code);
                ApplyLanguageToButtons();
                UpdateSteps();
                if (Engine.RtlHelper.IsRtl(lc.Code))
                    Engine.RtlHelper.ApplyRtl(this);
                else
                {
                    RightToLeft = RightToLeft.No;
                    RightToLeftLayout = false;
                }
            }
        };

        _sidebar.Controls.AddRange(new Control[]
        {
            logo, versionLabel, _stepIndicator, _overallProgress, langLabel, langCombo
        });

        _content = new Panel
        {
            Location = new Point(212, 12), Size = new Size(580, 480),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White
        };

        _backBtn = CreateButton("< Back", 430);
        _nextBtn = CreateButton("Next >", 530);
        _cancelBtn = CreateButton("Cancel", 630);

        _backBtn.Click += (_, _) => NavigateTo(_currentPage - 1);
        _nextBtn.Click += OnNextClicked;
        _cancelBtn.Click += (_, _) => { if (ConfirmCancel()) Close(); };

        Controls.AddRange(new Control[] { _sidebar, _content, _backBtn, _nextBtn, _cancelBtn });
        ApplyLanguageToButtons();
    }

    private bool ConfirmCancel()
    {
        if (_installComplete || _previewMode) return true;
        var r = MessageBox.Show(this,
            "Are you sure you want to cancel the installation?",
            "Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return r == DialogResult.Yes;
    }

    private sealed class LangChoice
    {
        public string Code { get; }
        public string Display { get; }
        public LangChoice(string code, string display) { Code = code; Display = display; }
        public override string ToString() => Display;
    }

    private static Button CreateButton(string text, int x) => new()
    {
        Text = text,
        Location = new Point(x, 510),
        Size = new Size(90, 30),
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        Font = new Font("Segoe UI", 9)
    };

    private void BuildPages()
    {
        // Order: Welcome → License → Components → Folder → StartMenu → Tasks → Ready → [complete | error]
        _pages.Add(new WelcomePage());
        _pages.Add(new LicensePage());
        _pages.Add(new ComponentSelectionPage(_ctx));
        _pages.Add(new FolderPage());
        _pages.Add(new StartMenuPage());
        _pages.Add(new AdditionalTasksPage());
        _pages.Add(new ReadyPage());
        _completePage = new CompletePage();
        _pages.Add(_completePage);
        _errorPage = new ErrorPage();
        _errorPage.ValidityChanged += (_, _) =>
        {
            // Retry — navigate back to Ready page, then trigger Install again
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
    }

    private void ApplyLanguageToButtons()
    {
        _backBtn.Text = LanguageManager.GetOrDefault("Btn_Back", "< Back");
        _cancelBtn.Text = LanguageManager.GetOrDefault("Btn_Cancel", "Cancel");
        _nextBtn.Text = _currentPage == _pages.Count - 1
            ? LanguageManager.GetOrDefault("Btn_Install", "Install")
            : LanguageManager.GetOrDefault("Btn_Next", "Next >");
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _currentPage = index;
        _content.Controls.Clear();

        var page = _pages[index];
        var ctrl = (UserControl)page;
        ctrl.Dock = DockStyle.Fill;
        _content.Controls.Add(ctrl);
        page.OnEnter(_ctx);

        _backBtn.Enabled = index > 0 && !_installComplete;
        if (page.CanGoNext) _nextBtn.Enabled = true;
        ApplyLanguageToButtons();
        UpdateSteps();
    }

    private void UpdateSteps()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _pages.Count; i++)
        {
            var name = _pages[i].PageTitle;
            if (i < _currentPage) sb.AppendLine($"  \u2713 {name}");
            else if (i == _currentPage) sb.AppendLine($"\u25B6 {name}");
            else sb.AppendLine($"  \u25CB {name}");
        }
        _stepIndicator.Text = sb.ToString();
    }

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

    /// <summary>Pulls the current state of each page into the shared context.</summary>
    private void CapturePageState()
    {
        foreach (var p in _pages)
        {
            switch (p)
            {
                case LicensePage lic: _ctx.AcceptLicense = lic.Accepted; break;
                case FolderPage fld: _ctx.InstallPath = fld.SelectedPath; _ctx.PerUser = fld.PerUser; break;
                case StartMenuPage sm: _ctx.CreateStartMenu = sm.CreateStartMenu; _ctx.StartMenuFolder = sm.FolderName; break;
                case AdditionalTasksPage at: _ctx.CreateDesktopIcon = at.CreateDesktopIcon; _ctx.AutoStart = at.AutoStart; _ctx.FileAssociations = at.FileAssociations; break;
            }
        }
    }

    private async Task RunInstallAsync()
    {
        CapturePageState();
        if (!_ctx.AcceptLicense)
        {
            MessageBox.Show(this, "You must accept the license to continue.", "Beep Installer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _content.Controls.Clear();
        _overallProgress.Visible = true;
        _backBtn.Enabled = false; _nextBtn.Enabled = false; _cancelBtn.Enabled = !_previewMode;
        UpdateSteps();

        if (_previewMode)
        {
            // Simulate progress for preview
            for (int p = 0; p <= 100; p += 5)
            {
                _overallProgress.Value = p;
                await Task.Delay(40);
            }
            _completePage?.SetResult(true, "(Preview) Installation would proceed here.");
            ShowCompletePage();
            return;
        }

        var label = new Label
        {
            Text = "Preparing installation...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11)
        };
        _content.Controls.Add(label);

        try
        {
            // Apply component selection from the page
            // (the wizard pages set Selected on each component directly)

            var context = new SetupContext();
            context.Properties["InstallConfig"] = _ctx.Config;
            context.Properties["InstallPath"] = _ctx.InstallPath;
            context.Properties["CreateDesktopIcon"] = _ctx.CreateDesktopIcon;
            context.Properties["CreateStartMenu"] = _ctx.CreateStartMenu;
            context.Properties["AutoStart"] = _ctx.AutoStart;
            context.Properties["PerUser"] = _ctx.PerUser;

            var wizard = new SetupWizardBuilder()
                .WithId($"beep-install-{Environment.TickCount}")
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
                if (IsDisposed) return;
                try
                {
                    Invoke(() =>
                    {
                        if (_overallProgress is { IsDisposed: false })
                            _overallProgress.Value = Math.Min(args.ParameterInt1, 100);
                        if (!string.IsNullOrEmpty(args.Messege)) label.Text = args.Messege;
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
        NavigateTo(_pages.Count - 1); // Last page = error page
        _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Finish", "Finish");
        _nextBtn.Enabled = false; // User must click Retry
        _backBtn.Enabled = true;
        _installComplete = false;
    }

    private void ShowCompletePage(bool success = true, string? message = null, string? logPath = null)
    {
        _content.Controls.Clear();
        _completePage?.SetResult(success, message ?? "", logPath);
        if (_completePage != null)
        {
            _completePage.Dock = DockStyle.Fill;
            _content.Controls.Add(_completePage);
        }
        _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Finish", "Finish");
        _nextBtn.Enabled = true;
        _installComplete = true;
        _overallProgress.Value = 100;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "\u2026";

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.B) { NavigateTo(_currentPage - 1); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.N) { OnNextClicked(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.KeyCode == Keys.Escape) { if (ConfirmCancel()) Close(); e.Handled = true; }
    }

    private Image? TryLoadBanner()
    {
        // 1) Branding.WelcomeBannerPath (absolute or relative)
        var configured = _branding?.WelcomeBannerPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // Resolve relative to exe dir first, then as-is
            var asIs = configured!;
            if (!Path.IsPathRooted(asIs)) asIs = Path.Combine(AppContext.BaseDirectory, asIs);
            if (File.Exists(asIs)) return Engine.BannerLoader.Load(asIs, 200);
        }
        // 2) banner.png / banner.jpg next to the exe
        foreach (var name in new[] { "banner.png", "banner.jpg", "banner.bmp", "welcome.png" })
        {
            var img = Engine.BannerLoader.LoadNextToExe(name, 200);
            if (img != null) return img;
        }
        return null;
    }
}
