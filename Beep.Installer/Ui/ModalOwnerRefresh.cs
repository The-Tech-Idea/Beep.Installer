using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Beep.Installer.Ui;

/// <summary>Keeps a ShowDialog owner disabled when a refresh recreates its native handle.</summary>
internal sealed class ModalOwnerRefresh : IDisposable
{
    private static readonly ConditionalWeakTable<Form, ReturnState> Pending = new();
    private readonly Form _owner;
    private readonly bool _enabled;

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    internal static IDisposable? Begin(Form owner)
    {
        var modal = owner.OwnedForms.FirstOrDefault(form => form.Visible && form.Modal);
        return modal is null ? null : new ModalOwnerRefresh(owner, modal);
    }

    private ModalOwnerRefresh(Form owner, Form modal)
    {
        _owner = owner; _enabled = owner.Enabled;
        Pending.GetValue(owner, _ => new ReturnState(owner, modal));
        owner.Enabled = false;
    }

    public void Dispose()
    {
        if (_owner.IsDisposed || _owner.Disposing) return;
        _owner.Enabled = _enabled;
        // ShowDialog disabled the original HWND. A replacement HWND must remain disabled too.
        if (_owner.IsHandleCreated && _owner.OwnedForms.Any(form => form.Visible && form.Modal))
            EnableWindow(_owner.Handle, false);
    }

    private sealed class ReturnState
    {
        private readonly Form _owner, _modal;
        internal ReturnState(Form owner, Form modal)
        {
            _owner = owner; _modal = modal;
            modal.FormClosed += Closed; owner.Disposed += Disposed;
        }
        private void Closed(object? sender, FormClosedEventArgs e)
        {
            Detach();
            if (!_owner.IsDisposed && !_owner.Disposing && _owner.IsHandleCreated &&
                !_owner.OwnedForms.Any(form => form != _modal && form.Visible && form.Modal))
                EnableWindow(_owner.Handle, _owner.Enabled);
        }
        private void Disposed(object? sender, EventArgs e) => Detach();
        private void Detach()
        {
            _modal.FormClosed -= Closed; _owner.Disposed -= Disposed; Pending.Remove(_owner);
        }
    }
}
