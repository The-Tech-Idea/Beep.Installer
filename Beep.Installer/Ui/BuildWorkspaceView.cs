using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

/// <summary>The same live job and retained result in either authoring shell.</summary>
internal sealed class BuildWorkspaceView : UserControl
{
    private readonly AuthoringBuildWorkspace _workspace;
    private readonly Action<ProcessStartInfo> _launch;
    private readonly Label _identity = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "BuildIdentity" };
    private readonly Label _stage = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "BuildStage" };
    private readonly Label _notice = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "BuildRevisionNotice" };
    private readonly Label _message = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "BuildMessage" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Name = "BuildProgress" };
    private readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Both, WordWrap = false, Name = "WorkspaceBuildLog" };
    private readonly Button _cancel = new() { AutoSize = true, Name = "CancelWorkspaceBuild" };
    private readonly Button _open = new() { AutoSize = true, Name = "OpenWorkspaceArtifact" };
    private readonly Button _folder = new() { AutoSize = true, Name = "OpenWorkspaceFolder" };
    private readonly Button _save = new() { AutoSize = true, Name = "SaveWorkspaceLog" };
    private readonly TextBox _path = new() { Dock = DockStyle.Fill, ReadOnly = true, Name = "WorkspaceArtifactPath" };

    internal BuildWorkspaceView(AuthoringBuildWorkspace workspace, Action<ProcessStartInfo> launch)
    {
        _workspace = workspace;
        _launch = launch;
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        BackColor = InstallerTheme.Panel;
        Font = InstallerTheme.Body;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _identity.Font = InstallerTheme.SectionTitle;
        _notice.ForeColor = InstallerTheme.Warning;
        layout.Controls.Add(_identity, 0, 0);
        layout.Controls.Add(_stage, 0, 1);
        layout.Controls.Add(_notice, 0, 2);
        layout.Controls.Add(_message, 0, 3);
        layout.Controls.Add(_progress, 0, 4);
        layout.Controls.Add(_path, 0, 5);
        layout.Controls.Add(_log, 0, 6);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        actions.Controls.AddRange(new Control[] { _cancel, _open, _folder, _save });
        layout.Controls.Add(actions, 0, 7);
        Controls.Add(layout);
        _cancel.Click += (_, _) => _workspace.RequestCancellation();
        _open.Click += (_, _) => OpenArtifact(false);
        _folder.Click += (_, _) => OpenArtifact(true);
        _save.Click += (_, _) => SaveLog();
        _workspace.Changed += OnChanged;
        LanguageManager.LanguageChanged += OnChanged;
        Engine.Accessibility.ApplyAutoNames(this);
        RefreshState();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshStateCore(applyDirection: false); }
    private void OnChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(RefreshState); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            return;
        }
        RefreshState();
    }

    private void RefreshState() => RefreshStateCore(applyDirection: true);

    private void RefreshStateCore(bool applyDirection)
    {
        if (IsDisposed || Disposing) return;
        var state = _workspace.Snapshot;
        _identity.Text = state.Id == Guid.Empty ? L("Workspace_Title", "Build workspace") : $"{state.ProjectName} · {state.Version} · {state.Format}";
        _stage.Text = StageCaption(state.Stage);
        _stage.AccessibleName = _stage.Text;
        _notice.Text = L("Workspace_OlderRevision", "This build uses an earlier project revision. Its result is not attached to the current project.");
        _notice.Visible = !state.IsCurrentProject;
        _message.Text = state.IsRunning && !state.CanCancel && state.Stage != AuthoringBuildStage.Cancelling
            ? L("Guided_NoPackageCancel", "This packaging service cannot be interrupted while its current operation is running.") : state.Message;
        _progress.Value = Math.Clamp(state.Percent, 0, 100);
        _progress.AccessibleName = _stage.Text;
        _progress.AccessibleDescription = $"{state.Percent}%";
        _path.Text = state.Result?.OutputFile ?? "";
        _path.Visible = !string.IsNullOrEmpty(_path.Text);
        _path.AccessibleName = L("BuildResult_Artifact", "Output artifact:");
        if (_log.Text != state.Log)
        {
            var start = _log.SelectionStart;
            var length = _log.SelectionLength;
            var follow = start + length >= _log.TextLength;
            if (state.Log.StartsWith(_log.Text, StringComparison.Ordinal)) _log.AppendText(state.Log[_log.TextLength..]);
            else _log.Text = state.Log;
            if (follow) { _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); }
            else _log.Select(Math.Min(start, _log.TextLength), Math.Min(length, Math.Max(0, _log.TextLength - start)));
        }
        _log.AccessibleName = L("Workspace_Log", "Build log");
        _cancel.Text = state.Stage == AuthoringBuildStage.Cancelling ? L("BuildProgress_Cancelling", "Cancelling...") : L("Common_Cancel", "Cancel");
        _cancel.Enabled = state.IsRunning && state.CanCancel;
        _cancel.Visible = state.IsRunning;
        _open.Text = L("Workspace_OpenArtifact", "Open artifact");
        _folder.Text = L("Builder_OpenFolder", "Open Folder");
        _save.Text = L("Workspace_SaveLog", "Save log");
        _open.Enabled = state.Result is { Success: true, Canceled: false } && File.Exists(_path.Text);
        _folder.Enabled = !string.IsNullOrEmpty(_path.Text) && Directory.Exists(Path.GetDirectoryName(_path.Text));
        _save.Enabled = !string.IsNullOrEmpty(state.Log);
        foreach (var button in new[] { _cancel, _open, _folder, _save })
        {
            button.AccessibleName = button.Text;
            button.MinimumSize = new Size(0, InstallerTheme.ActionMinHeight);
        }
        // Direction changes recreate native handles. Never start that transition from
        // OnHandleCreated while an ancestor's handle recreation is still in progress.
        if (applyDirection)
            RightToLeft = RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName) ? RightToLeft.Yes : RightToLeft.No;
    }

    internal static string StageCaption(AuthoringBuildStage stage) => stage switch
    {
        AuthoringBuildStage.Idle => L("Workspace_Idle", "Ready to build"),
        AuthoringBuildStage.Preparing => L("Workspace_Preparing", "Preparing"),
        AuthoringBuildStage.Building => L("Workspace_Building", "Building"),
        AuthoringBuildStage.Signing => L("Workspace_Signing", "Signing"),
        AuthoringBuildStage.Cancelling => L("BuildProgress_Cancelling", "Cancelling..."),
        AuthoringBuildStage.Completed => L("Workspace_Completed", "Build completed"),
        AuthoringBuildStage.Failed => L("Workspace_Failed", "Build failed"),
        _ => L("Workspace_Cancelled", "Build cancelled")
    };

    private void OpenArtifact(bool folder)
    {
        var result = _workspace.Snapshot.Result;
        if (!folder && result is not { Success: true, Canceled: false }) return;
        var path = result?.OutputFile;
        if (string.IsNullOrEmpty(path)) return;
        var target = folder ? Path.GetDirectoryName(path) : path;
        if (folder ? !Directory.Exists(target) : !File.Exists(target)) return;
        try { _launch(new ProcessStartInfo(target!) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, _open.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog { Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt", FileName = "build.log" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, _workspace.Snapshot.Log); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, _save.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string L(string key, string fallback) => LanguageManager.GetOrDefault(key, fallback);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _workspace.Changed -= OnChanged; LanguageManager.LanguageChanged -= OnChanged; }
        base.Dispose(disposing);
    }
}
