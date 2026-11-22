using Beep.Installer.Lang;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls;
using TheTechIdea.Beep.Winform.Controls.Forms.ModernForm;
using TheTechIdea.Beep.Winform.Controls.Layouts.Helpers;
using TheTechIdea.Beep.Winform.Controls.Helpers;
using TheTechIdea.Beep.Winform.Controls.DialogsManagers.Helpers;
using TheTechIdea.Beep.Winform.Controls.DialogsManagers;

namespace Beep.Installer.Forms;

/// <summary>User-requested maintenance surface; all update decisions and writes belong to the shared engine.</summary>
public sealed class UpdateCenterForm : BeepiFormPro
{
    private readonly Dictionary<string, BeepTextBox> _fields = new();
    private readonly List<BeepButton> _browsers = new();
    private readonly List<Action> _translateControls = new();
    private Func<InstallerPolicyEvaluation> _policy = null!;
    private UpdateChannelFeedVerificationOptions _feedDefaults = new();
    private string _appName = "", _publisher = "", _appId = "";
    private BeepTextBox _status = null!;
    private BeepButton _check = null!, _apply = null!, _recover = null!, _rollback = null!, _cancel = null!;
    private CancellationTokenSource? _operation;
    private bool _eligible;
    private UpdatePreviewSnapshot? _lastPreview;
    private TextBox _preview = null!;
    private int _inputGeneration;

    private UpdateCenterForm() { }

    public static UpdateCenterForm Create(InstallProject project, Func<InstallerPolicyEvaluation> policy,
        UpdateChannelFeedVerificationOptions? feedDefaults = null)
    {
        var form = new UpdateCenterForm { _appName = project.AppName, _publisher = project.AppPublisher, _appId = project.AppId,
            _policy = policy, _feedDefaults = feedDefaults ?? new(), Text = L("Title"), AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent, KeyPreview = true };
        form.BuildRequestedDialog(project);
        return form;
    }

    private void BuildRequestedDialog(InstallProject project)
    {
        SuspendLayout();
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = BeepLayoutMetrics.DialogPadding.ScalePadding(this) };
        var width = BeepLayoutMetrics.DialogLarge.Width.ScaleValue(this);
        var height = BeepLayoutMetrics.MinTouchTarget.ScaleValue(this);
        var identity = new BeepLabel { Text = $"{_appName} — {_publisher}", UseThemeColors = true,
            Width = width, Height = height, AccessibleName = L("Identity") };
        content.Controls.Add(identity);
        _translateControls.Add(() => identity.AccessibleName = L("Identity"));
        var fields = new TableLayoutPanel { AutoSize = true, Width = width, ColumnCount = 3 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (BeepLayoutMetrics.LabelColumnWidth * 2).ScaleValue(this)));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BeepLayoutMetrics.ButtonSmall.Width.ScaleValue(this)));
        void Field(string id, string key, string value = "")
        {
            var label = L(key);
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new BeepLabel { Text = label, UseThemeColors = true, Dock = DockStyle.Fill,
                Height = height, TabStop = false };
            fields.Controls.Add(caption, 0, row);
            var input = new BeepTextBox { Name = "UpdateInput_" + id, Text = value, UseThemeColors = true, Dock = DockStyle.Fill,
                Height = height, AccessibleName = label, AccessibleRole = AccessibleRole.Text, TabIndex = row * 2 };
            input.TextChanged += (_, _) =>
            {
                ++_inputGeneration; _eligible = false; _lastPreview = null;
                if (_apply is not null) _apply.Enabled = false;
                if (_preview is not null) _preview.Text = LanguageManager.T("UpdatePreview_CheckRequired");
            };
            fields.Controls.Add(input, 1, row);
            _fields.Add(id, input);
            _translateControls.Add(() => { caption.Text = L(key); input.AccessibleName = L(key); });
            if (id is "feed" or "feedKey" or "deltaKey" or "install" or "delta" or "journal")
            {
                var browse = new BeepButton { Text = LanguageManager.T("Btn_Browse"), UseThemeColors = true, Dock = DockStyle.Fill,
                    Height = height, AccessibleName = LanguageManager.T("Btn_Browse") + " " + label, AccessibleRole = AccessibleRole.PushButton,
                    TabIndex = row * 2 + 1 };
                browse.Click += (_, _) =>
                {
                    if (_operation is not null) return;
                    try
                    {
                        var dialogs = new BeepDialogManager(this);
                        var selected = id is "install" or "delta"
                            ? dialogs.SelectFolder(L(key), Directory.Exists(input.Text) ? input.Text : null, allowCreate: false)
                            : dialogs.OpenFile(id is "feedKey" or "deltaKey" ? L("KeyFilter")
                                : L("JsonFilter"), title: L(key));
                        if (selected is not null) input.Text = selected;
                    }
                    catch (Exception ex) { _status.Text = L("PathError", ex.Message); }
                };
                fields.Controls.Add(browse, 2, row);
                _browsers.Add(browse);
                _translateControls.Add(() => { browse.Text = LanguageManager.T("Btn_Browse"); browse.AccessibleName = browse.Text + " " + L(key); });
            }
        }
        Field("feed", "Feed");
        Field("feedKey", "FeedKey");
        Field("deltaKey", "DeltaKey");
        Field("install", "Install");
        Field("version", "Version");
        Field("current", "Current", project.AppUpdateChannel);
        Field("target", "Target", project.AppUpdateChannel);
        Field("cohort", "Cohort");
        Field("delta", "Delta");
        Field("journal", "Journal");
        content.Controls.Add(fields);
        _preview = new TextBox { Name = "UpdatePreview", ReadOnly = true, Multiline = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, Width = width, Height = height * 6,
            AccessibleName = LanguageManager.T("UpdatePreview_Title"),
            Text = LanguageManager.T("UpdatePreview_CheckRequired") };
        content.Controls.Add(_preview);
        _status = new BeepTextBox { ReadOnly = true, Multiline = true, WordWrap = true, UseThemeColors = true,
            Width = width, Height = height * 3, AccessibleName = L("Status"), TabIndex = 1,
            Text = L("Instructions") };
        content.Controls.Add(_status);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, WrapContents = true,
            Padding = BeepLayoutMetrics.DialogPadding.ScalePadding(this), TabIndex = 2 };
        BeepButton Action(string key, Func<Task> action, bool global = false)
        {
            string Caption() => global ? LanguageManager.T(key) : L(key);
            var label = Caption();
            var button = new BeepButton { Text = label, UseThemeColors = true,
                Width = BeepLayoutMetrics.ButtonLarge.Width.ScaleValue(this), Height = height,
                AccessibleName = label, AccessibleRole = AccessibleRole.PushButton, TabIndex = actions.Controls.Count };
            button.Click += async (_, _) => await action();
            actions.Controls.Add(button);
            _translateControls.Add(() => { button.Text = Caption(); button.AccessibleName = button.Text; });
            return button;
        }
        _check = Action("Check", () => RunAsync("check"));
        _apply = Action("Apply", () => RunAsync("apply"));
        _recover = Action("Recover", () => RunAsync("recover"));
        _rollback = Action("Rollback", () => RunAsync("rollback"));
        _cancel = Action("Btn_Cancel", () => { _operation?.Cancel(); return Task.CompletedTask; }, global: true);
        var close = Action("Close", () => { Close(); return Task.CompletedTask; });
        _apply.Enabled = _cancel.Enabled = false;
        AcceptButton = _check;
        CancelButton = close;
        Controls.Add(content);
        Controls.Add(actions);
        content.ClientSizeChanged += (_, _) =>
        {
            var available = Math.Max(1, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (Control child in content.Controls) child.Width = available;
        };
        FormClosing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true;
            _operation.Cancel();
            _status.Text = L("Cancelling");
        };
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = RightToLeft == RightToLeft.Yes;
        MinimumSize = new Size(width + content.Padding.Horizontal, height * 10);
        ClientSize = new Size(MinimumSize.Width, height * 16);
        ResumeLayout(true);
        LanguageManager.LanguageChanged += OnLanguageChanged;
    }

    private async Task RunAsync(string action)
    {
        if (_operation is not null || (action == "apply" && !_eligible)) return;
        var values = _fields.ToDictionary(p => p.Key, p => p.Value.Text.Trim());
        var generation = _inputGeneration;
        var reviewedFeed = _lastPreview?.FeedSha256 ?? "";
        if (action == "apply" && _lastPreview?.CanApply != true) return;
        if (action is "apply" or "rollback" && MessageBox.Show(this,
            L("ConfirmChanges", L(action == "apply" ? "Apply" : "Rollback")),
            L("ConfirmTitle"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        if (generation != _inputGeneration) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        SetBusy(true);
        _cancel.Enabled = action is "check" or "apply";
        _status.Text = action == "check" ? L("Checking") : L("Running");
        if (action == "check")
        { _eligible = false; _lastPreview = null; _preview.Text = LanguageManager.T("UpdatePreview_CheckRequired"); }
        try
        {
            var policy = _policy();
            var progress = new Progress<string>(message =>
            {
                if (!IsDisposed && !Disposing && ReferenceEquals(_operation, cancellation)) _status.Text = message;
            });
            var feed = _feedDefaults with { FeedPath = values["feed"],
                TrustedPublicKeyPath = values["feedKey"], ExpectedAppName = _appName, ExpectedAppId = _appId,
                ExpectedAppPublisher = _publisher };
            if (action == "check")
            {
                var checkedUpdate = await Task.Run(() =>
                {
                    var result = UpdateChannelFeedPackageService.Check(feed, values["version"],
                        values["current"], values["target"], values["cohort"], policyEvaluation: policy,
                        cancellationToken: cancellation.Token);
                    var keys = ReadDeltaKeys(values["deltaKey"]);
                    return (Result: result, Preview: UpdatePreview.Create(result, values["version"], values["delta"], keys, cancellation.Token));
                });
                if (IsDisposed || Disposing) return;
                if (generation != _inputGeneration) { _status.Text = LanguageManager.T("UpdatePreview_CheckRequired"); return; }
                _lastPreview = checkedUpdate.Preview;
                _eligible = _lastPreview.CanApply && Directory.Exists(values["install"]);
                _preview.Text = UpdatePreviewPresentation.Describe(_lastPreview)
                    + (Directory.Exists(values["install"]) ? "" : Environment.NewLine + L("MissingInstall"));
                _status.Text = Describe(checkedUpdate.Result);
            }
            else if (action == "apply")
            {
                if (!Directory.Exists(values["install"])) throw new ArgumentException(L("MissingInstall"));
                var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(values["install"]));
                var result = await Task.Run(() => UpdateChannelFeedPackageService.ApplyDelta(feed, new()
                {
                    CurrentInstallDirectory = install, StageDirectory = install + ".update-stage-" + Guid.NewGuid().ToString("N"),
                    DeltaDirectory = values["delta"], CurrentVersion = values["version"], JournalPath = values["journal"],
                    TrustedPublicKeys = ReadDeltaKeys(values["deltaKey"]).ToList(), RequireSignature = true,
                    CancellationToken = cancellation.Token, Progress = progress
                }, values["current"], values["target"], values["cohort"], policyEvaluation: policy,
                    expectedFeedSha256: reviewedFeed));
                if (IsDisposed || Disposing) return;
                _eligible = false;
                if (!string.IsNullOrEmpty(result.Apply?.JournalPath)) _fields["journal"].Text = result.Apply.JournalPath;
                if (result.Success)
                {
                    RefreshInstalledState(result.Apply!);
                    _fields["current"].Text = result.Check.Decision!.TargetChannelId;
                }
                _status.Text = result.Success ? L("Applied", result.Apply!.JournalPath)
                    : Describe(result.Check) + Environment.NewLine + result.Error + Environment.NewLine + result.Apply?.Error;
            }
            else
            {
                if (policy.HasErrors) throw new InvalidOperationException(L("PolicyError"));
                var options = new DeltaUpdateRollbackOptions { JournalPath = values["journal"] };
                if (action == "recover")
                {
                    var preview = await Task.Run(() => new DeltaUpdatePackageService().RecoverAtomicApply(options, dryRun: true));
                    if (IsDisposed || Disposing) return;
                    if (!preview.Success) { _status.Text = preview.Error; return; }
                    _status.Text = L("Preview", preview.RecoveryState, preview.InstallDirectory, preview.InstalledVersion, preview.JournalPath);
                    if (cancellation.IsCancellationRequested || MessageBox.Show(this, _status.Text + Environment.NewLine
                        + L("ConfirmRecovery"),
                        L("Recover"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
                var result = await Task.Run(() => action == "recover"
                    ? new DeltaUpdatePackageService().RecoverAtomicApply(options)
                    : new DeltaUpdatePackageService().RollbackAtomicApply(options));
                if (IsDisposed || Disposing) return;
                _eligible = false;
                if (result.Success)
                {
                    RefreshInstalledState(result);
                    _fields["current"].Text = ""; // Journals do not establish the restored channel.
                }
                _status.Text = result.Success ? L("Completed", L(action == "recover" ? "Recover" : "Rollback"), result.RecoveryState) : result.Error;
            }
        }
        catch (OperationCanceledException) { _eligible = false; if (!IsDisposed && !Disposing) _status.Text = L("Cancelled"); }
        catch (Exception ex) { _eligible = false; if (!IsDisposed && !Disposing) _status.Text = L("Failed", ex.Message); }
        finally { _operation = null; if (!IsDisposed && !Disposing) SetBusy(false); }
    }

    private static IReadOnlyList<string> ReadDeltaKeys(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 65536) return Array.Empty<string>();
            using var reader = new StreamReader(stream);
            return new[] { reader.ReadToEnd() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return Array.Empty<string>(); }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        Text = L("Title");
        foreach (var translate in _translateControls) translate();
        _status.AccessibleName = L("Status");
        _preview.AccessibleName = LanguageManager.T("UpdatePreview_Title");
        _preview.Text = _lastPreview is null ? LanguageManager.T("UpdatePreview_CheckRequired")
            : UpdatePreviewPresentation.Describe(_lastPreview)
                + (Directory.Exists(_fields["install"].Text.Trim()) ? "" : Environment.NewLine + L("MissingInstall"));
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = RightToLeft == RightToLeft.Yes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { LanguageManager.LanguageChanged -= OnLanguageChanged; _operation?.Cancel(); }
        base.Dispose(disposing);
    }

    private void SetBusy(bool busy)
    {
        foreach (var field in _fields.Values) field.Enabled = !busy;
        foreach (var browse in _browsers) browse.Enabled = !busy;
        _check.Enabled = _recover.Enabled = _rollback.Enabled = !busy;
        _apply.Enabled = !busy && _eligible;
        _cancel.Enabled = busy;
    }

    private void RefreshInstalledState(DeltaUpdatePackageResult result)
    {
        _fields["install"].Text = result.InstallDirectory;
        _fields["version"].Text = result.InstalledVersion;
        _fields["journal"].Text = result.JournalPath;
    }

    private static string L(string key, params object[] args) => LanguageManager.Tf("Update_" + key, args);

    private static string Describe(UpdateChannelCheckResult result)
        => string.Join(Environment.NewLine,
            new[] { result.Verification.SignatureTrusted ? L("Trusted") : L("Untrusted"),
                result.Decision is null ? L("NoDecision") : L("Decision", result.Decision.Action) }
            .Concat(result.Decision?.Reasons ?? new())
            .Concat(result.Verification.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))
            .Concat(result.PolicyEvaluation?.Diagnostics.Select(d => $"{d.Code}: {d.Message}") ?? Enumerable.Empty<string>()));
}
