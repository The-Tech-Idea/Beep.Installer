using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

/// <summary>A resource caption whose control lifetime owns its language subscription.</summary>
internal readonly record struct LiveUiCaption(string? Key, string Fallback)
{
    internal string Value => Key is null ? Fallback : LanguageManager.GetOrDefault(Key, Fallback);
    public static implicit operator LiveUiCaption(string value) => new(null, value);
    public override string ToString() => Value;

    internal void Bind(ToolStripItem item, LiveUiCaption? tip = null)
    {
        var caption = this;
        void Refresh()
        {
            if (item.IsDisposed) return;
            item.Text = item.AccessibleName = caption.Value;
            if (tip is { } hint) item.ToolTipText = hint.Value;
        }
        Refresh();
        if (Key is null && tip?.Key is null) return;
        EventHandler changed = (_, _) =>
        {
            if (item.IsDisposed) return;
            var owner = item.Owner;
            if (owner is null || !owner.InvokeRequired) { Refresh(); return; }
            try { owner.BeginInvoke((Action)Refresh); }
            catch (InvalidOperationException) when (item.IsDisposed || owner.IsDisposed || !owner.IsHandleCreated) { }
        };
        LanguageManager.LanguageChanged += changed;
        item.Disposed += (_, _) => LanguageManager.LanguageChanged -= changed;
    }

    internal void Bind(Control control, bool text = true, bool trimColon = false, LiveUiCaption? prefix = null)
    {
        var caption = this;
        void Refresh()
        {
            if (control.IsDisposed || control.Disposing) return;
            var value = trimColon ? caption.Value.TrimEnd(':') : caption.Value;
            if (prefix is { } lead) value = lead.Value + ": " + value;
            if (text) control.Text = value;
            control.AccessibleName = value;
        }
        Refresh();
        if (Key is null && prefix?.Key is null) return;
        EventHandler changed = (_, _) =>
        {
            if (control.IsDisposed || control.Disposing) return;
            if (!control.InvokeRequired) { Refresh(); return; }
            try { control.BeginInvoke((Action)Refresh); }
            catch (InvalidOperationException) when (control.IsDisposed || control.Disposing || !control.IsHandleCreated) { }
        };
        LanguageManager.LanguageChanged += changed;
        control.Disposed += (_, _) => LanguageManager.LanguageChanged -= changed;
    }
}
