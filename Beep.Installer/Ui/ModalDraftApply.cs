using System.Text.Json;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Ui;

/// <summary>Checks a modal's captured target before committing, retaining conflicts for inspection.</summary>
internal sealed class ModalDraftApply : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IgnoreReadOnlyProperties = true };
    private readonly Form _dialog;
    private readonly Func<bool> _isCurrent;
    private readonly Action _apply;
    private readonly Label _conflict;
    private bool _applied;
    private bool _disposed;

    internal ModalDraftApply(Form dialog, Func<bool> isCurrent, Action apply)
    {
        _dialog = dialog; _isCurrent = isCurrent; _apply = apply;
        _conflict = new Label { Name = "ModalDraftConflict", AutoSize = true, Dock = DockStyle.Bottom,
            Padding = new Padding(12, 8, 12, 8), Visible = false };
        AuthoringThemeBinding.SetTone(_conflict, AuthoringTone.Warning);
        dialog.Controls.Add(_conflict);
        _conflict.BringToFront();
        dialog.SizeChanged += OnSizeChanged;
        dialog.Disposed += OnDisposed;
        LanguageManager.LanguageChanged += OnLanguageChanged;
        BeepThemesManager.ThemeChanged += OnLanguageChanged;
        UpdateCaption();
    }

    internal static string Capture(object target) => JsonSerializer.Serialize(target, target.GetType(), SnapshotOptions);

    internal bool TryApply()
    {
        if (_disposed) return false;
        if (_applied) return true;
        PackageBuilderForm.CommitBindings(_dialog);
        if (!_isCurrent())
        {
            UpdateCaption();
            _conflict.Visible = true;
            return false;
        }
        _apply();
        _applied = true;
        return true;
    }

    private void UpdateCaption()
    {
        if (_disposed || _dialog.IsDisposed) return;
        _conflict.Text = _conflict.AccessibleName = LanguageManager.GetString("Editor_DraftChanged");
        _conflict.MaximumSize = new Size(Math.Max(1, _dialog.ClientSize.Width), 0);
        _conflict.RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        AuthoringThemeBinding.SetTone(_conflict, AuthoringTone.Warning);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed || _dialog.IsDisposed) return;
        if (_dialog.InvokeRequired)
        {
            if (!_dialog.IsHandleCreated) return;
            try { _dialog.BeginInvoke(UpdateCaption); }
            catch (InvalidOperationException) { /* Closing/disposed between the event and dispatch. */ }
        }
        else UpdateCaption();
    }
    private void OnSizeChanged(object? sender, EventArgs e) => UpdateCaption();
    private void OnDisposed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LanguageManager.LanguageChanged -= OnLanguageChanged;
        BeepThemesManager.ThemeChanged -= OnLanguageChanged;
        _dialog.SizeChanged -= OnSizeChanged;
        _dialog.Disposed -= OnDisposed;
    }
}
