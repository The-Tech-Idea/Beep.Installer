using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>Reviews and exports a captured support artifact without re-running diagnostics.</summary>
internal sealed class RuntimeSupportBundleDialog : Form
{
    private readonly string _sourcePath;
    private readonly Label _title = new();
    private readonly TextBox _preview = new() { Name = "SupportBundlePreview", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _notice = new();
    private readonly Label _status = new();
    private readonly Button _export = new() { Name = "SupportBundleExport", Enabled = false };
    private readonly Button _close = new() { DialogResult = DialogResult.Cancel };
    private readonly RuntimePageLayout _layout;
    private RuntimeSupportBundlePreview? _snapshot;
    private int _generation;
    private bool _loaded;
    private string? _exportedPath;
    private bool _exportFailed;
    private bool _exporting;
    internal Func<IWin32Window, string?> ChooseExportPath = ChoosePath;
    internal Func<string, RuntimeSupportBundlePreview> ReadPreview = RuntimeSupportBundleExport.ReadPreview;
    internal Task? PendingLoad { get; private set; }
    internal Task<bool>? PendingExport;

    internal RuntimeSupportBundleDialog(string sourcePath)
    {
        _sourcePath = sourcePath; Size = new(950, 700); MinimumSize = new(700, 500);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        _layout = new RuntimePageLayout(this, _title, _preview, null, true, _notice, _export, _close, _status);
        CancelButton = _close;
        _export.Click += async (_, _) =>
        {
            if (_exporting) return;
            var path = ChooseExportPath(this);
            if (path is not null) { PendingExport = ExportToAsync(path); await PendingExport; }
        };
        Shown += async (_, _) => await LoadAsync();
        LanguageManager.LanguageChanged += LanguageChanged;
        Translate();
    }

    internal Task LoadAsync()
    {
        // One open review owns one captured artifact. Shown, RTL handle recreation and
        // repeated callers must neither race another read nor replace reviewed evidence.
        if (PendingLoad is { } pending) return pending;
        if (IsDisposed || Disposing) return Task.CompletedTask;
        return PendingLoad = ReadAndPublishAsync();
    }

    private async Task ReadAndPublishAsync()
    {
        var generation = ++_generation;
        var snapshot = await Task.Run(() => ReadPreview(_sourcePath));
        if (IsDisposed || Disposing || generation != _generation) return;
        _snapshot = snapshot; _loaded = true; _preview.Text = snapshot.Json; _export.Enabled = snapshot.CanExport && !_exporting;
        Translate();
    }
    internal async Task<bool> ExportToAsync(string path)
    {
        if (IsDisposed || Disposing || _exporting || _snapshot?.CanExport != true) return false;
        var snapshot = _snapshot; var generation = _generation; _exporting = true; _export.Enabled = false;
        var succeeded = await Task.Run(() =>
        {
            try { RuntimeSupportBundleExport.Export(snapshot, path); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            { Diag.Warn("SupportExport", "Could not export the reviewed support artifact.", eventId: "BI2436"); return false; }
        });
        if (IsDisposed || Disposing) return succeeded;
        _exporting = false;
        if (generation == _generation)
        {
            _exportFailed = !succeeded;
            if (succeeded) _exportedPath = Path.GetFullPath(path);
            Translate();
        }
        _export.Enabled = _snapshot?.CanExport == true; return succeeded;
    }
    private static string? ChoosePath(IWin32Window owner)
    {
        using var dialog = new SaveFileDialog { Title = L("Support_Export", "Export support bundle"), Filter = "JSON (*.json)|*.json",
            DefaultExt = "json", AddExtension = true, OverwritePrompt = true, FileName = "support-bundle.json" };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
    private void Translate()
    {
        Text = _title.Text = L("Support_Review", "Review support bundle");
        _notice.Text = L("Support_ReviewNotice", "Review the redacted diagnostics before saving or sharing. Redaction may not remove every sensitive detail. The captured result and privacy metadata are preserved.");
        _export.Text = _export.AccessibleName = L("Support_Export", "Export support bundle");
        _close.Text = _close.AccessibleName = L("Btn_Cancel", "Cancel");
        _preview.AccessibleName = Text;
        _status.Text = _exportFailed ? L("Support_ExportFailed", "The support bundle could not be exported. Choose another destination or retry.")
            : _exportedPath is not null ? string.Format(L("Support_Exported", "Saved support bundle to {0}."), _exportedPath)
            : !_loaded ? L("Support_Loading", "Loading captured diagnostics…")
            : _snapshot?.CanExport == true ? ""
            : L("Support_Unavailable", "The captured bundle is unavailable, invalid or too large to review. Export is disabled.");
        RightToLeftLayout = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft; _layout.Refresh();
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => LanguageChanged(sender, e)); return; } Translate();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ++_generation; LanguageManager.LanguageChanged -= LanguageChanged; _layout.Dispose(); }
        base.Dispose(disposing);
    }
}
