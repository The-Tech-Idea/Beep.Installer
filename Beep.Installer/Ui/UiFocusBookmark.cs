namespace Beep.Installer.Ui;

/// <summary>Preserves the focused leaf through native RTL handle recreation.</summary>
internal sealed class UiFocusBookmark
{
    private readonly Form _owner;
    private readonly Control _control;
    private readonly int _selectionStart, _selectionLength;
    internal string ControlName { get; }

    private UiFocusBookmark(Form owner, Control control)
    {
        _owner = owner; _control = control; ControlName = control.Name;
        if (control is TextBoxBase text)
        { _selectionStart = text.SelectionStart; _selectionLength = text.SelectionLength; }
    }

    private UiFocusBookmark(UiFocusBookmark original, Control replacement)
    {
        _owner = original._owner; _control = replacement; ControlName = original.ControlName;
        _selectionStart = original._selectionStart; _selectionLength = original._selectionLength;
    }

    internal void RestoreReplacement(Control replacement)
    {
        if (string.IsNullOrEmpty(ControlName) || replacement.Name != ControlName ||
            replacement.GetType() != _control.GetType() || replacement.FindForm() != _owner) return;
        new UiFocusBookmark(this, replacement).Restore();
    }

    internal static UiFocusBookmark? Capture(Form? owner)
    {
        if (owner is not { IsDisposed: false, Disposing: false, Visible: true, IsHandleCreated: true }) return null;
        var control = owner.ContainsFocus ? FocusedLeaf(owner)
            : HasVisibleModal(owner) ? ActiveLeaf(owner) : null;
        return control is { IsDisposed: false, Disposing: false, Visible: true, Enabled: true } && control.FindForm() == owner
            ? new(owner, control) : null;
    }

    private static bool HasVisibleModal(Form owner)
        => owner.OwnedForms.Any(form => !form.IsDisposed && form.Visible && (form.Modal || HasVisibleModal(form)));

    private static Control? ActiveLeaf(ContainerControl owner)
    {
        var current = owner.ActiveControl;
        while (current is ContainerControl container && container.ActiveControl is { } child && child != current)
            current = child;
        return current;
    }

    private static Control? FocusedLeaf(Control root)
    {
        if (root.Focused) return root;
        foreach (Control child in root.Controls)
            if (child.ContainsFocus && FocusedLeaf(child) is { } focused) return focused;
        return null;
    }

    internal void Restore(bool defer = false)
    {
        if (_owner.IsDisposed || _owner.Disposing || !_owner.Visible || !_owner.IsHandleCreated ||
            _control.IsDisposed || _control.Disposing || _control.FindForm() != _owner || !_control.Visible ||
            (!_control.Enabled && !(!_owner.Enabled && HasVisibleModal(_owner)))) return;
        // A queued restoration must respect a later user navigation or focus change.
        if (defer && (Form.ActiveForm != _owner || FocusedLeaf(_owner) is { } current && current != _control)) return;
        if (defer && _control.Focused) return;
        // The native owner of ShowDialog is disabled even though its managed Enabled property
        // stays true. Retain the editing target while the modal keeps keyboard focus.
        if (HasVisibleModal(_owner))
        {
            _owner.ActiveControl = _control;
            RestoreSelection();
            return;
        }
        if (!_control.CanFocus) return;
        // RTL handle recreation can leave the form inactive with no active control.
        // Retain the intended leaf for the next activation without taking focus from another window.
        _owner.ActiveControl = _control;
        if (!_owner.ContainsFocus && Form.ActiveForm != _owner)
        {
            RestoreSelection();
            return;
        }
        _control.Focus();
        RestoreSelection();
        if (!defer) _owner.BeginInvoke(() => Restore(defer: true));
    }

    private void RestoreSelection()
    {
        if (_control is TextBoxBase text)
            text.Select(Math.Min(_selectionStart, text.TextLength), Math.Min(_selectionLength, Math.Max(0, text.TextLength - _selectionStart)));
    }
}
