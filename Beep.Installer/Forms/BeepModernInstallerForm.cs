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

    /// <summary>Accent colour from the project's branding; used for the primary action.</summary>
    private Color _accentColor = Color.FromArgb(41, 98, 255);

    /// <summary>
    /// Applies the authored setup icon to the window. Best effort — a missing or malformed
    /// icon must not stop an install.
    /// </summary>
    private void ApplyWindowIcon()
    {
        var iconPath = _project.SetupIconFile;
        if (string.IsNullOrWhiteSpace(iconPath)) return;

        // The build copies the authored icon next to the runtime script as setup.ico.
        var candidates = new[]
        {
            iconPath,
            Path.Combine(AppContext.BaseDirectory, "setup.ico"),
            Path.Combine(_project.SourceDirectory ?? "", "setup.ico"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
                Icon = new Icon(candidate);
                return;
            }
            catch (Exception ex) { Engine.Diag.Debug("Wizard", $"setup icon '{candidate}' rejected", ex); }
        }
    }

    /// <summary>Mixes <paramref name="from"/> toward <paramref name="to"/> by <paramref name="amount"/> (0..1).</summary>
    private static Color Blend(Color from, Color to, double amount)
        => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));

    /// <summary>
    /// Sidebar icon per page type. The sidebar itself is generated from the real page list
    /// (see <see cref="SyncStepperToPages"/>) rather than a parallel hardcoded array — the two
    /// had drifted apart, so every step from "Additional Tasks" onward showed the wrong label.
    /// </summary>
    private static string IconFor(IInstallerPage page) => page switch
    {
        WelcomePage => "home.svg",
        LicensePage => "license.svg",
        PrerequisitePage => "shield-check.svg",
        ComponentSelectionPage => "puzzle.svg",
        FolderPage => "folder-open.svg",
        StartMenuPage => "menu.svg",
        AdditionalTasksPage => "checklist.svg",
        ReadyPage => "rocket.svg",
        _ => "package.svg",
    };

    /// <summary>
    /// Pages that appear in the sidebar: everything except the terminal Complete and Error
    /// pages, which are outcomes rather than steps the user navigates to.
    /// </summary>
    private int NavigablePageCount
        => _pages.Count(p => p is not CompletePage && p is not ErrorPage);

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
        SyncStepperToPages();   // must follow BuildPages: the sidebar mirrors the real pages

        // Arabic/Hebrew/Persian/Urdu translations shipped, but RtlHelper had no callers, so
        // those users got a left-to-right layout. Applied after the control tree exists so the
        // recursive pass reaches every child.
        ApplyLayoutDirection();

        Engine.Accessibility.EnsureAccessibility(this);
        NavigateTo(0);
    }

    private void InitializeUi()
    {
        // Content surface comes from the shared token layer; the sidebar can still be
        // overridden per-project by the author's branding below.
        var bg = Ui.InstallerTheme.Panel;

        // Wizard pages position children in absolute pixels, so without DPI auto-scaling
        // they clip at 125% and above.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        // Branding the author configured in the builder — sidebar colours, accent, banner and
        // window icon — used to be collected, written into the .bsetup, and then ignored here:
        // the wizard hardcoded its palette and never called ThemeLoader or BannerLoader at all.
        var sidebarBg = Engine.ThemeLoader.ParseColor(
            _project.SidebarBackgroundColor, Color.FromArgb(245, 246, 250));
        var sidebarFg = Engine.ThemeLoader.ParseColor(
            _project.SidebarTextColor, Color.FromArgb(30, 30, 40));
        _accentColor = Engine.ThemeLoader.ParseColor(
            _project.AccentColor, Color.FromArgb(41, 98, 255));

        ApplyWindowIcon();

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
            ForeColor = sidebarFg,
            Dock = DockStyle.Top,
            Height = 32,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _sidebarProduct.DataBindings.Add("Text", _ctx.Project, nameof(InstallProject.AppName), true, DataSourceUpdateMode.OnPropertyChanged);

        _sidebarVersion = new BeepLabel
        {
            Font = new Font("Segoe UI", 9),
            ForeColor = Blend(sidebarFg, sidebarBg, 0.45),
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
        // Populated from the real page list once BuildPages() has run — see SyncStepperToPages.
        var models = new BindingList<StepModel>();

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
            // The authored accent colour marks the primary action.
            BackColor = _accentColor,
            ForeColor = Blend(_accentColor, Color.White, 0.85),
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
            NavigateFromStepper(e.NewStepIndex);
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

    /// <summary>
    /// Mirrors the wizard for right-to-left cultures. No-op for left-to-right languages.
    /// </summary>
    private void ApplyLayoutDirection()
    {
        try
        {
            if (Engine.RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName))
                Engine.RtlHelper.ApplyRtl(this);
        }
        catch (Exception ex)
        {
            // A layout-direction failure must never stop an install.
            Engine.Diag.Warn("Wizard", "RTL layout could not be applied", ex);
        }
    }

    /// <summary>
    /// Rebuilds the sidebar from the real page list. Keeping a separate hardcoded array meant
    /// the two could disagree — and they did: the array stopped at "Ready" and omitted
    /// Additional Tasks, so from that point on every step showed the wrong name, and custom
    /// wizard pages never appeared at all.
    /// </summary>
    private void SyncStepperToPages()
    {
        var models = new BindingList<StepModel>();
        foreach (var page in _pages)
        {
            if (page is CompletePage or ErrorPage) continue;
            models.Add(new StepModel
            {
                Text = page.PageTitle,
                ImagePath = $"{IconNs}.{IconFor(page)}",
            });
        }
        _stepper.StepModels = models;
    }

    /// <summary>
    /// Handles a click on the sidebar. Jumping backwards is free; jumping forwards must clear
    /// every page in between, because the sidebar previously navigated straight to the target
    /// and skipped their validation entirely — letting the user step over the licence,
    /// prerequisite and component pages.
    /// </summary>
    private void NavigateFromStepper(int target)
    {
        if (target < 0 || target >= _pages.Count) return;

        if (_installComplete || target <= _currentPage)
        {
            NavigateTo(target);
            return;
        }

        for (int i = _currentPage; i < target; i++)
        {
            if (_pages[i].CanGoNext && _pages[i].Validate()) { CapturePageState(); continue; }

            // Land the user on the page that still needs attention rather than silently
            // refusing the click.
            NavigateTo(i);
            return;
        }

        CapturePageState();
        NavigateTo(target);
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

        // _content.Controls.Clear() above destroys the focused control, leaving focus nowhere:
        // keyboard and screen-reader users lost their place on every page change.
        FocusFirstControl(ctrl);

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

    /// <summary>
    /// Moves focus to the first control the user can interact with on the newly shown page,
    /// falling back to Next so focus is never left on nothing.
    /// </summary>
    private void FocusFirstControl(Control pageControl)
    {
        var target = FindFocusable(pageControl);
        if (target != null) target.Focus();
        else if (_nextBtn.Enabled) _nextBtn.Focus();

        static Control? FindFocusable(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.CanSelect && child.TabStop && child.Enabled && child.Visible)
                    return child;
                var nested = FindFocusable(child);
                if (nested != null) return nested;
            }
            return null;
        }
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

    /// <summary>
    /// Builds the install progress surface: a headline, a determinate bar and a detail line.
    /// </summary>
    private (Panel host, Label headline, ProgressBar bar, Label detail) BuildProgressPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(48, 0, 48, 0),
        };
        // Spacer / headline / bar / detail — the spacer centres the group vertically.
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headline = new Label
        {
            Text = LanguageManager.GetOrDefault("Install_Preparing", "Preparing installation…"),
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 12),
        };

        var bar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
        };

        var detail = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(98, 107, 119),
            Font = new Font("Segoe UI", 9),
        };

        stack.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
        stack.Controls.Add(headline, 0, 1);
        stack.Controls.Add(bar, 0, 2);
        stack.Controls.Add(detail, 0, 3);
        host.Controls.Add(stack);

        return (host, headline, bar, detail);
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
        _backBtn.Enabled = false;
        _nextBtn.Enabled = false;

        // Cancel is disabled for the duration of a real install. It previously stayed enabled
        // but only closed the form, which left the background install running with no UI and
        // no rollback — a half-installed machine. BeepDM's SetupWizard.Run is synchronous and
        // takes no cancellation token, so a step genuinely cannot be interrupted today;
        // offering a button that cannot do what it says is worse than not offering it.
        _cancelBtn.Enabled = false;
        _cancelBtn.ToolTipText = LanguageManager.GetOrDefault(
            "Install_CancelDisabled", "Installation cannot be interrupted once it has started.");

        if (_previewMode)
        {
            for (int p = 0; p <= 100; p += 5) { await Task.Delay(40); }
            _completePage?.SetResult(true, "(Preview) Installation would proceed here.");
            ShowCompletePage();
            return;
        }

        // The install used to show one centred line of text and nothing else — no bar, no
        // percentage, no indication of which step was running.
        var (progressHost, headline, bar, detail) = BuildProgressPanel();
        _content.Controls.Add(progressHost);

        try
        {
            var customValues = new Dictionary<string, string>();
            foreach (var kv in _ctx.Bag)
                if (kv.Key.StartsWith("Custom:"))
                    customValues[kv.Key["Custom:".Length..]] = kv.Value?.ToString() ?? "";

            var rollback = new RollbackManager();

            // Same builder the silent/CLI paths use, so both produce an identical context
            // (notably the InstallConfig the BeepDM steps require).
            var context = Engine.InstallContextBuilder.ForInstall(
                _project, _ctx.InstallPath, _ctx.PerUser, rollback, payloadRoot: null, customValues);

            // Wizard-page answers the built-in steps don't model.
            context.Properties["CreateDesktopIcon"] = _ctx.CreateDesktopIcon;
            context.Properties["CreateStartMenu"] = _ctx.CreateStartMenu;
            context.Properties["AutoStart"] = _ctx.AutoStart;

            // Identical graph to the silent CLI install — see Hosting/InstallWizardGraph.
            // Keeping these in step matters: a difference between them means the wizard and
            // /S produce different installations from the same script.
            var wizard = Hosting.InstallWizardGraph.BuildInstall(
                $"beep-install-{Environment.TickCount}",
                new SetupOptions { Environment = "Production" });

            // Steps already reported a percentage in ParameterInt1; it was simply discarded.
            var stepCount = Math.Max(1, wizard.Steps?.Count ?? 1);
            var stepsDone = 0;

            var progress = new Progress<PassedArgs>(args =>
            {
                if (IsDisposed) return;
                try
                {
                    Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(args.Messege)) detail.Text = args.Messege;

                        // Each step reports 0-100 for itself; scale that into its slice of the
                        // whole run so the bar advances monotonically across the install.
                        var within = Math.Clamp(args.ParameterInt1, 0, 100);
                        var overall = (int)((stepsDone + within / 100.0) / stepCount * 100);
                        bar.Value = Math.Clamp(overall, bar.Value, 100);

                        if (within >= 100 && stepsDone < stepCount - 1) stepsDone++;

                        headline.Text = string.Format(
                            LanguageManager.GetOrDefault("Install_Progress", "Installing {0}… ({1}%)"),
                            _project.AppName, bar.Value);
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });

            await Task.Run(() => wizard.Run(context, progress));

            if (!IsDisposed) { bar.Value = 100; detail.Text = ""; }
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

        // Re-enable Cancel (disabled for the duration of the install) so a failed install is
        // not a dead end with no way to close the wizard.
        _cancelBtn.Enabled = true;
        _cancelBtn.ToolTipText = "";
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

        _cancelBtn.ToolTipText = "";
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
