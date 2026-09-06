using Beep.Installer.Lang;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using Beep.Installer.Policy;
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
    private Func<InstallerPolicyEvaluation> _policy = null!;
    private UpdateChannelFeedVerificationOptions _feedDefaults = new();
    private string _appName = "", _publisher = "", _appId = "";
    private BeepTextBox _status = null!;
    private BeepButton _check = null!, _apply = null!, _recover = null!, _rollback = null!, _cancel = null!;
    private CancellationTokenSource? _operation;
    private bool _eligible;

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
        var fields = new TableLayoutPanel { AutoSize = true, Width = width, ColumnCount = 3 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (BeepLayoutMetrics.LabelColumnWidth * 2).ScaleValue(this)));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BeepLayoutMetrics.ButtonSmall.Width.ScaleValue(this)));
        void Field(string id, string label, string value = "")
        {
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new BeepLabel { Text = label, UseThemeColors = true, Dock = DockStyle.Fill,
                Height = height, TabStop = false }, 0, row);
            var input = new BeepTextBox { Text = value, UseThemeColors = true, Dock = DockStyle.Fill,
                Height = height, AccessibleName = label, AccessibleRole = AccessibleRole.Text, TabIndex = row * 2 };
            input.TextChanged += (_, _) => { _eligible = false; if (_apply is not null) _apply.Enabled = false; };
            fields.Controls.Add(input, 1, row);
            _fields.Add(id, input);
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
                            ? dialogs.SelectFolder(label, Directory.Exists(input.Text) ? input.Text : null, allowCreate: false)
                            : dialogs.OpenFile(id is "feedKey" or "deltaKey" ? L("KeyFilter")
                                : L("JsonFilter"), title: label);
                        if (selected is not null) input.Text = selected;
                    }
                    catch (Exception ex) { _status.Text = L("PathError", ex.Message); }
                };
                fields.Controls.Add(browse, 2, row);
                _browsers.Add(browse);
            }
        }
        Field("feed", L("Feed"));
        Field("feedKey", L("FeedKey"));
        Field("deltaKey", L("DeltaKey"));
        Field("install", L("Install"));
        Field("version", L("Version"));
        Field("current", L("Current"), project.AppUpdateChannel);
        Field("target", L("Target"), project.AppUpdateChannel);
        Field("cohort", L("Cohort"));
        Field("delta", L("Delta"));
        Field("journal", L("Journal"));
        content.Controls.Add(fields);
        _status = new BeepTextBox { ReadOnly = true, Multiline = true, WordWrap = true, UseThemeColors = true,
            Width = width, Height = height * 3, AccessibleName = L("Status"), TabIndex = 1,
            Text = L("Instructions") };
        content.Controls.Add(_status);
        var actions = new FlowLayoutPanel { AutoSize = true, Width = width, TabIndex = 2 };
        BeepButton Action(string label, Func<Task> action)
        {
            var button = new BeepButton { Text = label, UseThemeColors = true,
                Width = BeepLayoutMetrics.ButtonLarge.Width.ScaleValue(this), Height = height,
                AccessibleName = label, AccessibleRole = AccessibleRole.PushButton, TabIndex = actions.Controls.Count };
            button.Click += async (_, _) => await action();
            actions.Controls.Add(button);
            return button;
        }
        _check = Action(L("Check"), () => RunAsync("check"));
        _apply = Action(L("Apply"), () => RunAsync("apply"));
        _recover = Action(L("Recover"), () => RunAsync("recover"));
        _rollback = Action(L("Rollback"), () => RunAsync("rollback"));
        _cancel = Action(LanguageManager.T("Btn_Cancel"), () => { _operation?.Cancel(); return Task.CompletedTask; });
        var close = Action(L("Close"), () => { Close(); return Task.CompletedTask; });
        _apply.Enabled = _cancel.Enabled = false;
        AcceptButton = _check;
        CancelButton = close;
        content.Controls.Add(actions);
        Controls.Add(content);
        FormClosing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true;
            _operation.Cancel();
            _status.Text = L("Cancelling");
        };
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = RightToLeft == RightToLeft.Yes;
        MinimumSize = new Size(width, height * 4);
        ResumeLayout(true);
        DialogHelpers.FitFormToContent(this);
    }

    private async Task RunAsync(string action)
    {
        if (_operation is not null || (action == "apply" && !_eligible)) return;
        var values = _fields.ToDictionary(p => p.Key, p => p.Value.Text.Trim());
        if (action is "apply" or "rollback" && MessageBox.Show(this,
            L("ConfirmChanges", L(action == "apply" ? "Apply" : "Rollback")),
            L("ConfirmTitle"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        SetBusy(true);
        _cancel.Enabled = action is "check" or "apply";
        _status.Text = action == "check" ? L("Checking") : L("Running");
        try
        {
            var policy = _policy();
            var progress = new Progress<string>(message =>
            {
                if (ReferenceEquals(_operation, cancellation)) _status.Text = message;
            });
            var feed = _feedDefaults with { FeedPath = values["feed"],
                TrustedPublicKeyPath = values["feedKey"], ExpectedAppName = _appName, ExpectedAppId = _appId,
                ExpectedAppPublisher = _publisher };
            if (action == "check")
            {
                var result = await Task.Run(() => UpdateChannelFeedPackageService.Check(feed, values["version"],
                    values["current"], values["target"], values["cohort"], policyEvaluation: policy,
                    cancellationToken: cancellation.Token));
                _eligible = result.Success && result.Decision?.Allowed == true;
                _status.Text = Describe(result);
            }
            else if (action == "apply")
            {
                if (!Directory.Exists(values["install"])) throw new ArgumentException(L("MissingInstall"));
                var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(values["install"]));
                var key = File.ReadAllText(values["deltaKey"]);
                var result = await Task.Run(() => UpdateChannelFeedPackageService.ApplyDelta(feed, new()
                {
                    CurrentInstallDirectory = install, StageDirectory = install + ".update-stage-" + Guid.NewGuid().ToString("N"),
                    DeltaDirectory = values["delta"], CurrentVersion = values["version"], JournalPath = values["journal"],
                    TrustedPublicKeys = new() { key }, RequireSignature = true,
                    CancellationToken = cancellation.Token, Progress = progress
                }, values["current"], values["target"], values["cohort"], policyEvaluation: policy));
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
                    if (!preview.Success) { _status.Text = preview.Error; return; }
                    _status.Text = L("Preview", preview.RecoveryState, preview.InstallDirectory, preview.InstalledVersion, preview.JournalPath);
                    if (cancellation.IsCancellationRequested || MessageBox.Show(this, _status.Text + Environment.NewLine
                        + L("ConfirmRecovery"),
                        L("Recover"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
                var result = await Task.Run(() => action == "recover"
                    ? new DeltaUpdatePackageService().RecoverAtomicApply(options)
                    : new DeltaUpdatePackageService().RollbackAtomicApply(options));
                _eligible = false;
                if (result.Success)
                {
                    RefreshInstalledState(result);
                    _fields["current"].Text = ""; // Journals do not establish the restored channel.
                }
                _status.Text = result.Success ? L("Completed", L(action == "recover" ? "Recover" : "Rollback"), result.RecoveryState) : result.Error;
            }
        }
        catch (OperationCanceledException) { _eligible = false; _status.Text = L("Cancelled"); }
        catch (Exception ex) { _eligible = false; _status.Text = L("Failed", ex.Message); }
        finally { _operation = null; SetBusy(false); }
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
