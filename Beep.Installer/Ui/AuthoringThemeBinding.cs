using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Ui;

internal enum AuthoringSurface { Panel, Shell, Sidebar, Accent }
internal enum AuthoringTone { Text, Muted, Success, Warning, Danger }

/// <summary>Paint-only theme updates for authoring controls, including cached and reparented views.</summary>
internal sealed class AuthoringThemeBinding : IDisposable
{
    private sealed class Role
    {
        public Role() { }
        internal AuthoringSurface Surface;
        internal AuthoringTone Tone;
    }
    private static readonly ConditionalWeakTable<Component, Role> Roles = new();
    private readonly Form _owner;
    private readonly HashSet<Control> _controls = new();
    private bool _disposed;

    internal AuthoringThemeBinding(Form owner)
    {
        _owner = owner;
        Watch(owner);
        BeepThemesManager.ThemeChanged += OnThemeChanged;
        owner.Disposed += OnOwnerDisposed;
    }

    internal static void SetSurface(Control control, AuthoringSurface surface)
    { Roles.GetOrCreateValue(control).Surface = surface; Apply(control); }

    internal static void SetTone(Component component, AuthoringTone tone)
    {
        Roles.GetOrCreateValue(component).Tone = tone;
        if (component is Control control) Apply(control);
        else if (component is ToolStripItem item) Apply(item);
    }

    private static Color Background(AuthoringSurface surface) => surface switch
    {
        AuthoringSurface.Shell => InstallerTheme.Shell,
        AuthoringSurface.Sidebar => InstallerTheme.Sidebar,
        AuthoringSurface.Accent => InstallerTheme.Accent,
        _ => InstallerTheme.Panel
    };

    private static Color Foreground(Role role, Color background) => InstallerTheme.AccessibleForeground(role.Tone switch
    {
        AuthoringTone.Muted => InstallerTheme.MutedText,
        AuthoringTone.Success => InstallerTheme.Success,
        AuthoringTone.Warning => InstallerTheme.Warning,
        AuthoringTone.Danger => InstallerTheme.Danger,
        _ => role.Surface == AuthoringSurface.Accent ? InstallerTheme.AccentText : InstallerTheme.Text
    }, background);

    private static bool OwnsTheme(Control control) => control is LeftNavPanel or BuildWorkspaceView;
    private static bool NativeChildren(Control control) => control is PropertyGrid or DataGridView or ToolStrip
        or TextBoxBase or ComboBox or NumericUpDown or TreeView or ListView;

    internal static void Refresh(Control root)
    {
        Apply(root);
        if (OwnsTheme(root) || NativeChildren(root)) return;
        foreach (Control child in root.Controls) Refresh(child);
    }

    private void Watch(Control control)
    {
        if (_disposed || control.IsDisposed || OwnsTheme(control) || !_controls.Add(control)) return;
        control.ControlAdded += OnControlAdded;
        control.Disposed += OnControlDisposed;
        if (control is TabControl tabs)
        { tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.DrawItem += DrawTab; }
        if (control is ToolStrip strip) strip.ItemAdded += OnItemAdded;
        Apply(control);
        if (!NativeChildren(control)) foreach (Control child in control.Controls) Watch(child);
        if (control is ToolStrip toolStrip)
            foreach (ToolStripItem item in toolStrip.Items) Watch(item);
    }

    private void Watch(ToolStripItem item)
    {
        Apply(item);
        if (item is ToolStripControlHost host) Watch(host.Control);
        if (item is ToolStripDropDownItem menu && menu.HasDropDownItems) Watch(menu.DropDown);
    }

    private void OnControlAdded(object? sender, ControlEventArgs e)
    { if (sender is Control parent && !NativeChildren(parent) && e.Control is not null) Watch(e.Control); }
    private void OnItemAdded(object? sender, ToolStripItemEventArgs e) => Watch(e.Item!);
    private void OnControlDisposed(object? sender, EventArgs e)
    { if (sender is Control control) Unwatch(control); }
    private void OnOwnerDisposed(object? sender, EventArgs e) => Dispose();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed || _owner.IsDisposed || _owner.Disposing) return;
        if (_owner.InvokeRequired)
        {
            try { _owner.BeginInvoke(() => OnThemeChanged(sender, e)); }
            catch (InvalidOperationException) when (_disposed || _owner.IsDisposed || !_owner.IsHandleCreated) { }
            return;
        }
        foreach (var control in _controls.ToArray())
        {
            Apply(control);
            if (control is ToolStrip strip)
                foreach (ToolStripItem item in strip.Items) Apply(item);
        }
    }

    private static void Apply(Control control)
    {
        if (control.IsDisposed || control.Disposing || OwnsTheme(control)) return;
        var role = Roles.GetOrCreateValue(control);
        var background = Background(control is Form ? AuthoringSurface.Shell : role.Surface);
        if (control is Label && control.Parent is { } parent)
            background = Background(parent is Form ? AuthoringSurface.Shell : Roles.GetOrCreateValue(parent).Surface);
        control.BackColor = background;
        control.ForeColor = Foreground(role, background);
        switch (control)
        {
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatAppearance.BorderColor = InstallerTheme.Border;
                break;
            case TabPage page: page.UseVisualStyleBackColor = false; break;
            case DataGridView grid:
                grid.BackgroundColor = background; grid.GridColor = InstallerTheme.Border; grid.EnableHeadersVisualStyles = false;
                grid.DefaultCellStyle.BackColor = grid.AlternatingRowsDefaultCellStyle.BackColor = background;
                grid.DefaultCellStyle.ForeColor = grid.AlternatingRowsDefaultCellStyle.ForeColor = control.ForeColor;
                grid.DefaultCellStyle.SelectionBackColor = InstallerTheme.Accent;
                grid.DefaultCellStyle.SelectionForeColor = InstallerTheme.AccentText;
                grid.ColumnHeadersDefaultCellStyle.BackColor = grid.RowHeadersDefaultCellStyle.BackColor = InstallerTheme.Sidebar;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = grid.RowHeadersDefaultCellStyle.ForeColor = InstallerTheme.AccessibleForeground(InstallerTheme.Text, InstallerTheme.Sidebar);
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.RowHeadersDefaultCellStyle.SelectionBackColor = InstallerTheme.Accent;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = grid.RowHeadersDefaultCellStyle.SelectionForeColor = InstallerTheme.AccentText;
                break;
            case PropertyGrid properties:
                properties.ViewBackColor = properties.CommandsBackColor = background;
                properties.ViewForeColor = properties.CommandsForeColor = control.ForeColor;
                properties.HelpBackColor = InstallerTheme.Sidebar;
                properties.HelpForeColor = InstallerTheme.AccessibleForeground(InstallerTheme.Text, InstallerTheme.Sidebar);
                properties.CategoryForeColor = control.ForeColor;
                properties.LineColor = properties.CategorySplitterColor = InstallerTheme.Border;
                properties.SelectedItemWithFocusBackColor = InstallerTheme.Accent;
                properties.SelectedItemWithFocusForeColor = InstallerTheme.AccentText;
                break;
            case LinkLabel link:
                link.LinkColor = link.ActiveLinkColor = InstallerTheme.AccessibleForeground(InstallerTheme.Accent, background);
                link.VisitedLinkColor = InstallerTheme.AccessibleForeground(InstallerTheme.MutedText, background);
                break;
            case ToolStrip strip:
                strip.Renderer = new Renderer();
                break;
        }
        if (control.IsHandleCreated) control.Invalidate();
    }

    private static void Apply(ToolStripItem item)
    {
        if (item.IsDisposed) return;
        var role = Roles.GetOrCreateValue(item);
        var background = Background(role.Surface);
        item.BackColor = background; item.ForeColor = Foreground(role, background);
        if (item is ToolStripStatusLabel status && status.IsLink)
            status.LinkColor = status.ActiveLinkColor = status.VisitedLinkColor = item.ForeColor;
    }

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount) return;
        var selected = tabs.SelectedIndex == e.Index;
        var background = selected ? InstallerTheme.Accent : InstallerTheme.Sidebar;
        var foreground = selected ? InstallerTheme.AccentText : InstallerTheme.AccessibleForeground(InstallerTheme.Text, background);
        using var brush = new SolidBrush(background); e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
            (tabs.RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft : 0));
        if (selected && tabs.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -3), foreground, background);
    }

    private void Unwatch(Control control)
    {
        if (!_controls.Remove(control)) return;
        control.ControlAdded -= OnControlAdded; control.Disposed -= OnControlDisposed;
        if (control is TabControl tabs) tabs.DrawItem -= DrawTab;
        if (control is ToolStrip strip) strip.ItemAdded -= OnItemAdded;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; BeepThemesManager.ThemeChanged -= OnThemeChanged; _owner.Disposed -= OnOwnerDisposed;
        foreach (var control in _controls.ToArray()) Unwatch(control);
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        internal Renderer() : base(new Colors()) => RoundedEdges = false;
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var role = Roles.GetOrCreateValue(e.Item);
            var background = e.Item.Selected || e.Item.Pressed ? InstallerTheme.Sidebar : Background(role.Surface);
            e.TextColor = Foreground(role, background);
            base.OnRenderItemText(e);
        }
    }

    private sealed class Colors : ProfessionalColorTable
    {
        internal Colors() => UseSystemColors = false;
        public override Color ToolStripGradientBegin => InstallerTheme.Panel;
        public override Color ToolStripGradientMiddle => InstallerTheme.Panel;
        public override Color ToolStripGradientEnd => InstallerTheme.Panel;
        public override Color StatusStripGradientBegin => InstallerTheme.Panel;
        public override Color StatusStripGradientEnd => InstallerTheme.Panel;
        public override Color ToolStripDropDownBackground => InstallerTheme.Panel;
        public override Color ImageMarginGradientBegin => InstallerTheme.Panel;
        public override Color ImageMarginGradientMiddle => InstallerTheme.Panel;
        public override Color ImageMarginGradientEnd => InstallerTheme.Panel;
        public override Color MenuItemSelected => InstallerTheme.Sidebar;
        public override Color MenuItemSelectedGradientBegin => InstallerTheme.Sidebar;
        public override Color MenuItemSelectedGradientEnd => InstallerTheme.Sidebar;
        public override Color MenuItemBorder => InstallerTheme.Border;
        public override Color ToolStripBorder => InstallerTheme.Border;
        public override Color SeparatorDark => InstallerTheme.Border;
        public override Color SeparatorLight => InstallerTheme.Border;
        public override Color ButtonSelectedGradientBegin => InstallerTheme.Sidebar;
        public override Color ButtonSelectedGradientMiddle => InstallerTheme.Sidebar;
        public override Color ButtonSelectedGradientEnd => InstallerTheme.Sidebar;
        public override Color ButtonPressedGradientBegin => InstallerTheme.Sidebar;
        public override Color ButtonPressedGradientMiddle => InstallerTheme.Sidebar;
        public override Color ButtonPressedGradientEnd => InstallerTheme.Sidebar;
        public override Color MenuItemPressedGradientBegin => InstallerTheme.Sidebar;
        public override Color MenuItemPressedGradientMiddle => InstallerTheme.Sidebar;
        public override Color MenuItemPressedGradientEnd => InstallerTheme.Sidebar;
    }
}
