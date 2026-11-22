using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>Detached wizard preview. Layout scaling simulates dimensions, not OS DPI.</summary>
public class WizardPreviewForm : Form
{
    private readonly string _script;
    private readonly string _productName;
    private readonly Font _body = InstallerTheme.Body;
    private readonly ComboBox _page = new() { Name = "PreviewPage", DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox _scale = new() { Name = "PreviewScale", DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Button _reset = new() { Name = "PreviewReset", AutoSize = true };
    private readonly Label _hint = new() { AutoSize = true };
    private readonly Panel _viewport = new() { Name = "PreviewViewport", Dock = DockStyle.Fill, AutoScroll = true };
    private BeepModernInstallerForm _wizard = null!;
    private bool _updating;
    private float _factor = 1;
    private static readonly float[] Factors = { 1, 1.25f, 1.5f, 2 };
    internal BeepModernInstallerForm HostedWizard => _wizard;

    public WizardPreviewForm(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _script = InstallerScriptSerializer.Write(project); _productName = project.AppName;
        Font = _body;
        Size = new(1120, 850); MinimumSize = new(820, 620); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(8) };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(_hint, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange(new Control[] { _page, _scale, _reset }); root.Controls.Add(toolbar, 0, 1); root.Controls.Add(_viewport, 0, 2); Controls.Add(root);
        foreach (var factor in Factors) _scale.Items.Add($"{factor * 100:0}%"); _scale.SelectedIndex = 0;
        _page.SelectedIndexChanged += (_, _) => { if (!_updating) _wizard.SelectPreviewPage(_page.SelectedIndex); };
        _scale.SelectedIndexChanged += (_, _) => { if (!_updating) ApplyScale(Factors[_scale.SelectedIndex]); };
        _reset.Click += (_, _) => ResetPreview();
        root.ClientSizeChanged += (_, _) => _hint.MaximumSize = new(Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), 0);
        ResetPreview(); LanguageManager.LanguageChanged += LanguageChanged; BeepThemesManager.ThemeChanged += ThemeChanged;
        Translate(); ApplyTheme(); Engine.Accessibility.Attach(this);
    }
    private void ResetPreview()
    {
        _updating = true;
        try
        {
            if (_wizard is not null) { _viewport.Controls.Remove(_wizard); _wizard.Dispose(); }
            var snapshot = InstallerScriptSerializer.Read(_script, _productName).project
                ?? throw new InvalidOperationException("The project could not be captured for preview.");
            _wizard = new BeepModernInstallerForm(snapshot, previewMode: true)
                { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.None, Location = Point.Empty, AutoScaleMode = AutoScaleMode.None };
            _viewport.Controls.Add(_wizard); _wizard.Show(); _factor = 1;
            _wizard.FormClosed += (_, _) => { if (!_updating && !IsDisposed && !Disposing) Close(); };
            UpdatePageChoices(0); ApplyScale(Factors[_scale.SelectedIndex]);
        }
        finally { _updating = false; }
    }
    private void ApplyScale(float factor)
    {
        _wizard.Scale(new SizeF(factor / _factor, factor / _factor)); _factor = factor;
        _wizard.MinimumSize = new((int)(880 * factor), (int)(580 * factor));
        _wizard.Size = new((int)(980 * factor), (int)(660 * factor));
        _viewport.AutoScrollMinSize = _wizard.Size; _viewport.PerformLayout();
    }
    private void UpdatePageChoices(int selected)
    {
        _page.Items.Clear(); foreach (var title in _wizard.PreviewPageTitles) _page.Items.Add(title);
        if (_page.Items.Count > 0) _page.SelectedIndex = Math.Clamp(selected, 0, _page.Items.Count - 1);
    }
    private void Translate()
    {
        Text = string.Format(L("Preview_Title", "Wizard preview — {0}"), _productName);
        _hint.Text = L("Preview_LayoutHint", "Preview only. Select a configured page, change language in the wizard, or simulate layout scaling. Scroll to inspect larger layouts. No installation runs; this does not qualify actual OS DPI.");
        _page.AccessibleName = L("Preview_Page", "Preview page"); _scale.AccessibleName = L("Preview_Scale", "Layout scale");
        _reset.Text = _reset.AccessibleName = L("Preview_Reset", "Reset preview");
        _updating = true; try { UpdatePageChoices(_wizard.PreviewCurrentPage); } finally { _updating = false; }
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = RightToLeft == RightToLeft.Yes;
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => LanguageChanged(sender, e)); return; }
        if (IsHandleCreated) BeginInvoke(() => { if (!IsDisposed && !Disposing) Translate(); });
        else Translate();
    }
    private void ApplyTheme()
    {
        BackColor = InstallerTheme.Panel; ForeColor = InstallerTheme.Text;
        foreach (var control in new Control[] { _page, _scale, _reset, _hint, _viewport })
        { control.BackColor = InstallerTheme.Panel; control.ForeColor = InstallerTheme.Text; }
        _reset.UseVisualStyleBackColor = false;
    }
    private void ThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => ThemeChanged(sender, e)); return; }
        ApplyTheme();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LanguageManager.LanguageChanged -= LanguageChanged;
            BeepThemesManager.ThemeChanged -= ThemeChanged;
        }
        base.Dispose(disposing);
        if (disposing) _body.Dispose();
    }
}
