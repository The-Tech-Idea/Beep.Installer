using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Pages;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using TheTechIdea.Beep.Winform.Controls;
using TheTechIdea.Beep.Winform.Controls.Forms.ModernForm;
using TheTechIdea.Beep.Winform.Controls.Steppers.Models;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Forms;

/// <summary>
/// Modern Beep-themed installer wizard. Uses the platform's <see cref="BeepStepperBar"/>
/// (IconTimeline painter with per-step SVG icons from embedded resources) as the sidebar,
/// ModernTheme-driven colors and typography. Reuses all existing pages.
/// </summary>
public class BeepModernInstallerForm : BeepiFormPro
{
    private BeepLabel _sidebarProduct = null!;
    private BeepLabel _sidebarVersion = null!;
    private BeepStepperBar _stepper = null!;
    private Panel _content = null!;
    private BeepButton _backBtn = null!;
    private BeepButton _nextBtn = null!;
    private BeepButton _cancelBtn = null!;
    private Panel _buttonBar = null!;

    private readonly List<IInstallerPage> _pages = new();
    private int _currentPage = -1;
    private readonly InstallContext _ctx = new();
    private bool _installComplete;
    private CompletePage? _completePage;
    private ErrorPage? _errorPage;
    private readonly bool _previewMode;
    private readonly InstallProject _project;

    private const string IconNs = "Beep.Installer.Resources.Icons";
    private const int SidebarWidth = 260;

    private static readonly (string Name, string Icon)[] Steps =
    {
        ("Welcome", "home.svg"),
        ("License", "license.svg"),
        ("Prerequisites", "shield-check.svg"),
        ("Components", "puzzle.svg"),
        ("Folder", "folder-open.svg"),
        ("Start Menu", "menu.svg"),
        ("Ready", "rocket.svg"),
    };

    public BeepModernInstallerForm(InstallProject project, bool previewMode = false)
    {
        _previewMode = previewMode;
        _project = project;

        LanguageManager.Initialize();
        _ctx.Project = project;
        _ctx.InstallPath = project.DefaultDirName;
        _ctx.StartMenuFolder = project.DefaultGroupName;

        var title = string.IsNullOrEmpty(project.AppName) ? "Beep Installer" : $"{project.AppName} Setup";
        Text = !string.IsNullOrWhiteSpace(project.WindowTitle) && project.WindowTitle != "Beep Installer"
            ? project.WindowTitle : title;
        Size = new Size(980, 660);
        MinimumSize = new Size(880, 580);
        StartPosition = FormStartPosition.CenterScreen;
        ShowCaptionBar = true;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        InitializeUi();
        BuildPages();
        Engine.Accessibility.EnsureAccessibility(this);
        NavigateTo(0);
    }

    private void InitializeUi()
    {
        var theme = BeepThemesManager.CurrentTheme;
        var bg = theme?.BackColor ?? Color.White;
        var sidebarBg = Color.FromArgb(245, 246, 250);

        // ── Sidebar host ─────────────────────────────────────────────
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = SidebarWidth,
            BackColor = sidebarBg,
            Padding = Padding.Empty,
        };

        // Docked header panel (Top): the two labels stack from the top regardless
        // of font size, theme, or DPI change because Dock=Top re-anchors every
        // PerformLayout pass. Replaces the previous Location-based anchoring which
        // drifted when BeepiFormPro re-applied themes/fonts.
        var sidebarHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = sidebarBg,
            Padding = new Padding(16, 24, 12, 8),
        };

        _sidebarProduct = new BeepLabel
        {
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 40),
            Dock = DockStyle.Top,
            Height = 32,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _sidebarProduct.DataBindings.Add("Text", _ctx.Project, nameof(InstallProject.AppName), true, DataSourceUpdateMode.OnPropertyChanged);

        _sidebarVersion = new BeepLabel
        {
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(120, 120, 135),
            Dock = DockStyle.Top,
            Height = 20,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _sidebarVersion.DataBindings.Add("Text", _ctx.Project, nameof(InstallProject.AppVersion), true, DataSourceUpdateMode.OnPropertyChanged);

        // Add children bottom-up so visual order is product-name then version
        // (Dock=Top stacks up from the bottom of its parent).
        sidebarHeader.Controls.Add(_sidebarVersion);
        sidebarHeader.Controls.Add(_sidebarProduct);

        // Build the strongly-typed step models. StepModel.ImagePath flows
        // through to the painter; ListItems is left empty to avoid the
        // stepper's ListChanged handler rebuilding from the wrong source.
        var models = new BindingList<StepModel>();
        for (int i = 0; i < Steps.Length; i++)
        {
            models.Add(new StepModel
            {
                Text = Steps[i].Name,
                ImagePath = $"{IconNs}.{Steps[i].Icon}",
            });
        }

        _stepper = new BeepStepperBar
        {
            Orientation = Orientation.Vertical,
            Dock = DockStyle.Fill,
            PainterName = "IconTimeline",
            DisplayMode = StepDisplayMode.SvgIcon,
            AllowStepNavigation = true,
            AutoProgressSteps = false,
            // Flicker suppression: stop the 60 FPS animation timer,
            // the async tooltip popups, and the inner Next/Prev buttons.
            HighlightActiveStep = false,
            ShowNextPrevButtons = false,
            ShowConnectorLines = true,
            AutoGenerateTooltips = false,
            EnableTooltip = false,
            // Silence the per-click flicker: the click ripple + pop-up notification
            // each start the 60 FPS animation timer on every click.
            EnableClickRipple = false,
            EnableClickNotifications = false,
            StepLabelVisibility = TabLabelVisibility.Always,
            ButtonSize = new Size(40, 40),
            BackColor = sidebarBg,
            Font = new Font("Segoe UI", 10),
            // Padding=Empty so the painter's outer 6px hit-area padding aligns
            // exactly with the painted circles -- no dead zone between
            // visible icon and clickable area.
            Padding = Padding.Empty,
        };
        // StepModels must be assigned before InitializeStyles triggers because the
        // setter calls SyncStepModelsWithSteps() and InitializeSteps() -- which
        // pull labels/states from the model collection.
        _stepper.StepModels = models;

        // Apply the modern theme once after construction. Setting Theme triggers
        // ApplyTheme() which resolves the painter, font, and palette -- we never
        // touch Theme again so we don't re-enter InitializePainter.
        _stepper.Theme = "ModernTheme";

        var stepperHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = sidebarBg,
            Padding = new Padding(0, 12, 0, 0),
        };
        stepperHost.Controls.Add(_stepper);

        // Order matters: add the Fill child first so it claims the remaining
        // height, then the Top child stacks above it.
        sidebar.Controls.Add(stepperHost);
        sidebar.Controls.Add(sidebarHeader);
        Controls.Add(sidebar);

        // ── Content panel + button bar (right pane) ─────────────────
        _buttonBar = new Panel
        {
            Height = 72,
            BackColor = bg,
            Padding = new Padding(24, 14, 24, 14),
        };

        _cancelBtn = new BeepButton
        {
            Text = "Cancel",
            ImagePath = $"{IconNs}.circle-x.svg",
            Size = new Size(120, 42),
            Font = new Font("Segoe UI", 9),
        };
        _cancelBtn.Click += (_, _) => { if (ConfirmCancel()) Close(); };

        _nextBtn = new BeepButton
        {
            Text = "Next >",
            ImagePath = $"{IconNs}.arrow-right.svg",
            Size = new Size(140, 42),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
        };
        _nextBtn.Click += OnNextClicked;

        _backBtn = new BeepButton
        {
            Text = "< Back",
            ImagePath = $"{IconNs}.arrow-left.svg",
            Size = new Size(120, 42),
            Font = new Font("Segoe UI", 9),
        };
        _backBtn.Click += (_, _) => NavigateTo(_currentPage - 1);

        // Bottom button row: TableLayoutPanel with two columns -- a 100% spacer
        // on the left and an AutoSize cluster on the right that holds the three
        // buttons left-to-right. Replaces the earlier flowHost/flow churn.
        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = bg,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var buttonCluster = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = bg,
            Margin = new Padding(0),
        };
        buttonCluster.Controls.AddRange(new Control[] { _backBtn, _nextBtn, _cancelBtn });

        var spacer = new Panel { BackColor = bg, Dock = DockStyle.Fill };
        buttonRow.Controls.Add(spacer, 0, 0);
        buttonRow.Controls.Add(buttonCluster, 1, 0);

        _buttonBar.Controls.Add(buttonRow);

        _content = new Panel
        {
            BackColor = bg,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
        };

        var rightPane = new Panel { Dock = DockStyle.Fill };
        rightPane.Controls.Add(_content);
        rightPane.Controls.Add(_buttonBar);
        _content.BringToFront();
        _buttonBar.BringToFront();
        Controls.Add(rightPane);

        // Wire the stepper AFTER it has been added to a parent so Theme
        // propagation hits the live control tree.
        _stepper.StepChanged += (_, e) =>
        {
            if (e.NewStepIndex == _currentPage) return;
            NavigateTo(e.NewStepIndex);
        };

        PerformLayout();
    }

    private bool ConfirmCancel()
    {
        if (_installComplete || _previewMode) return true;
        var r = MessageBox.Show(this,
            "Are you sure you want to cancel the installation?",
            "Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return r == DialogResult.Yes;
    }

    private void BuildPages()
    {
        _pages.Add(new WelcomePage());
        _pages.Add(new LicensePage());
        _pages.Add(new PrerequisitePage());
        _pages.Add(new ComponentSelectionPage(_ctx));
        _pages.Add(new FolderPage());
        _pages.Add(new StartMenuPage());
        _pages.Add(new AdditionalTasksPage());
        _pages.Add(new ReadyPage());
        foreach (var cp in ((IEnumerable<CustomWizardPage>)_project.CustomPages).OrderBy(p => p.Order))
            _pages.Add(new CustomPage(cp, _ctx));
        _completePage = new CompletePage();
        _pages.Add(_completePage);
        _errorPage = new ErrorPage();
        _errorPage.ValidityChanged += (_, _) =>
        {
            var readyIdx = _pages.FindIndex(p => p is ReadyPage);
            if (readyIdx >= 0)
            {
                NavigateTo(readyIdx);
                BeginInvoke(() => OnNextClicked(null!, EventArgs.Empty));
            }
        };
        _pages.Add(_errorPage);

        if (_pages.Count > 1 && _pages[1] is LicensePage lic)
            lic.ValidityChanged += (_, ok) => _nextBtn.Enabled = ok;
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
        _nextBtn.Enabled = page.CanGoNext;
        ApplyButtonText();

        // Mark the stepper state via SetStepState only -- never assign
        // CurrentStep (its setter starts the 60 FPS animation timer; we
        // disabled animations anyway, but staying on SetStepState avoids
        // ripple/render churn from the painter's animation context).
        for (int i = 0; i < index && i < _stepper.StepCount; i++)
            _stepper.SetStepState(i, StepState.Completed);
        if (index < _stepper.StepCount)
            _stepper.SetStepState(index, StepState.Active);
        for (int i = index + 1; i < _stepper.StepCount; i++)
            _stepper.SetStepState(i, StepState.Pending);
    }

    private void ApplyButtonText()
    {
        _backBtn.Text = LanguageManager.GetOrDefault("Btn_Back", "< Back");
        _cancelBtn.Text = LanguageManager.GetOrDefault("Btn_Cancel", "Cancel");

        if (_pages.Count == 0 || _currentPage < 0 || _currentPage >= _pages.Count)
            _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Next", "Next >");
        else if (_pages[_currentPage] is ReadyPage)
            _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Install", "Install");
        else if (_currentPage == _pages.Count - 1)
            _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Finish", "Finish");
        else
            _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Next", "Next >");
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_installComplete) { Close(); return; }

        if (_pages[_currentPage] is ReadyPage)
        {
            await RunInstallAsync();
        }
        else if (_currentPage == _pages.Count - 1)
        {
            Close();
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
        _backBtn.Enabled = false; _nextBtn.Enabled = false; _cancelBtn.Enabled = !_previewMode;

        if (_previewMode)
        {
            for (int p = 0; p <= 100; p += 5) { await Task.Delay(40); }
            _completePage?.SetResult(true, "(Preview) Installation would proceed here.");
            ShowCompletePage();
            return;
        }

        var label = new Label
        {
            Text = "Preparing installation...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12),
        };
        _content.Controls.Add(label);

        try
        {
            var context = new SetupContext();
            context.Properties["InstallProject"] = _ctx.Project;
            context.Properties["InstallPath"] = _ctx.InstallPath;
            context.Properties["CreateDesktopIcon"] = _ctx.CreateDesktopIcon;
            context.Properties["CreateStartMenu"] = _ctx.CreateStartMenu;
            context.Properties["AutoStart"] = _ctx.AutoStart;
            context.Properties["PerUser"] = _ctx.PerUser;
            context.Properties["IsSelfContained"] = true;
            context.Properties["InstallProject"] = _project;
            context.Properties["CustomActions"] = _project.CustomActions.Any() ? _project.CustomActions.ToList() : new List<CustomAction>();

            var customValues = new Dictionary<string, string>();
            foreach (var kv in _ctx.Bag)
                if (kv.Key.StartsWith("Custom:"))
                    customValues[kv.Key["Custom:".Length..]] = kv.Value?.ToString() ?? "";
            context.Properties["CustomValues"] = customValues;

            var rollback = new RollbackManager();
            context.Properties["RollbackManager"] = rollback;

            var wizard = new SetupWizardBuilder()
                .WithId($"beep-install-{Environment.TickCount}")
                .WithOptions(new SetupOptions { Environment = "Production" })
                .AddStep(new PrerequisiteCheckStep())
                .AddStep(new DirectoryCreateStep("installer.prerequisites.check"))
                .AddStep(new CustomActionStep(CustomActionTiming.BeforeInstall, "installer.directory.create"))
                .AddStep(new Steps.PayloadDownloadStep("installer.custom.beforeinstall"))
                .AddStep(new Steps.PayloadPrepareStep("installer.payload.download"))
                .AddStep(new FileCopyStep("installer.payload.prepare"))
                .AddStep(new SharedFileCountStep("installer.files.copy"))
                .AddStep(new ComServerRegistrationStep("installer.files.copy"))
                .AddStep(new GacInstallStep("installer.files.copy"))
                .AddStep(new ShortcutCreateStep("installer.files.copy"))
                .AddStep(new RegistryWriteStep("installer.shortcuts.create"))
                .AddStep(new CustomActionStep(CustomActionTiming.AfterInstall, "installer.registry.write"))
                .AddStep(new VerifyInstallStep("installer.custom.afterinstall"))
                .Build();

            var progress = new Progress<PassedArgs>(args =>
            {
                if (IsDisposed) return;
                try
                {
                    Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(args.Messege)) label.Text = args.Messege;
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });

            await Task.Run(() => wizard.Run(context, progress));
            var report = wizard.GetReport();
            if (report.Succeeded) rollback.Commit(); else rollback.Rollback();
            var manifestPath = Path.Combine(_ctx.InstallPath, "install-manifest.json");

            if (report.Succeeded)
            {
                ShowCompletePage(report.Succeeded,
                    $"{_ctx.Project.AppName} has been installed to {_ctx.InstallPath}.",
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

    private void ShowErrorPage(SetupReport? report = null, string? detail = null)
    {
        var msg = detail ?? $"Installation failed at step: {report?.StepResults.LastOrDefault(r => !r.Succeeded)?.Message ?? "unknown"}";
        _errorPage?.SetError(msg, null);
        NavigateTo(_pages.Count - 1);
        _nextBtn.Text = LanguageManager.GetOrDefault("Btn_Finish", "Finish");
        _nextBtn.Enabled = false;
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
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.B) { NavigateTo(_currentPage - 1); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.N) { OnNextClicked(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.KeyCode == Keys.Escape) { if (ConfirmCancel()) Close(); e.Handled = true; }
    }
}
