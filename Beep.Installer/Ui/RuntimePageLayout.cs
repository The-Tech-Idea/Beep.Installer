using TheTechIdea.Beep.Winform.Controls;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Ui;

/// <summary>Responsive layout and owned theme/font lifetime for runtime summaries.</summary>
internal sealed class RuntimePageLayout : IDisposable
{
    private readonly Control _owner;
    private readonly Label _title;
    private readonly Control _body;
    private readonly Control[] _actions;
    private readonly TableLayoutPanel _root;
    private readonly Font _bodyFont = InstallerTheme.Body;
    private readonly Font _titleFont = new("Segoe UI", 14, FontStyle.Bold);

    internal RuntimePageLayout(Control owner, Label title, Control body, PictureBox? icon = null,
        bool verticalActions = false, params Control[] actions)
    {
        _owner = owner; _title = title; _body = body; _actions = actions;
        owner.Controls.Clear(); owner.Font = _bodyFont; title.Font = _titleFont;
        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(8) };
        _root.ColumnStyles.Add(new(SizeType.Percent, 100));
        _root.RowStyles.Add(new(SizeType.AutoSize)); _root.RowStyles.Add(new(SizeType.Percent, 100)); _root.RowStyles.Add(new(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = icon is null ? 1 : 2, RowCount = 1, AutoSize = true };
        if (icon is not null)
        {
            header.ColumnStyles.Add(new(SizeType.Absolute, 56)); icon.Margin = new(0, 0, 8, 8); icon.TabStop = false;
            header.Controls.Add(icon, 0, 0);
        }
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        title.AutoSize = true; title.Anchor = AnchorStyles.Left | AnchorStyles.Right; title.Margin = new(0, 0, 0, 8);
        header.Controls.Add(title, icon is null ? 0 : 1, 0); _root.Controls.Add(header, 0, 0);
        // A plain panel prevents the TextBox's preferred multiline height from expanding the
        // percentage row and pushing actions beyond the page when the text is very long.
        var bodyHost = new Panel { Dock = DockStyle.Fill, Margin = new(0, 0, 0, 8) };
        body.Dock = DockStyle.Fill; bodyHost.Controls.Add(body); _root.Controls.Add(bodyHost, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = !verticalActions,
            FlowDirection = verticalActions ? FlowDirection.TopDown : FlowDirection.LeftToRight, Margin = Padding.Empty };
        foreach (var action in actions) { action.Anchor = AnchorStyles.None; action.Margin = new(0, 4, 8, 4); footer.Controls.Add(action); }
        _root.Controls.Add(footer, 0, 2); owner.Controls.Add(_root);
        _root.ClientSizeChanged += ResizeContents;
        BeepThemesManager.ThemeChanged += ThemeChanged; Refresh();
    }

    internal void Refresh()
    {
        _owner.RightToLeft = Lang.LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        _body.RightToLeft = _owner.RightToLeft;
        ApplyTheme(); ResizeContents(null, EventArgs.Empty);
    }
    private void ResizeContents(object? sender, EventArgs e)
    {
        if (_owner.IsDisposed || _owner.Disposing) return;
        var width = Math.Max(1, _root.ClientSize.Width - _root.Padding.Horizontal - 8);
        _title.MaximumSize = new(Math.Max(1, width - 56), 0);
        foreach (var action in _actions)
        {
            var measured = TextRenderer.MeasureText(action.Text, action.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
            action.Size = new(Math.Min(width, Math.Max(120, measured.Width + 36)), Math.Max(InstallerTheme.ActionMinHeight, measured.Height + 16));
            if (action is Label label) { label.AutoSize = true; label.MaximumSize = new(width, 0); }
        }
    }
    private void ApplyTheme()
    {
        _owner.BackColor = _root.BackColor = InstallerTheme.Panel; _owner.ForeColor = InstallerTheme.Text;
        foreach (var control in new[] { _body, _title }.Concat(_actions))
        { control.BackColor = InstallerTheme.Panel; control.ForeColor = InstallerTheme.Text; }
    }
    private void ThemeChanged(object? sender, EventArgs e)
    {
        if (_owner.IsDisposed || _owner.Disposing) return;
        if (_owner.InvokeRequired) { _owner.BeginInvoke(() => ThemeChanged(sender, e)); return; } ApplyTheme();
    }
    public void Dispose()
    {
        BeepThemesManager.ThemeChanged -= ThemeChanged; _root.ClientSizeChanged -= ResizeContents;
        _titleFont.Dispose(); _bodyFont.Dispose();
    }
}
