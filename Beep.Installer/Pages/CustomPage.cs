using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Pages;

/// <summary>
/// Renders a user-defined <see cref="CustomWizardPage"/> (Track A1.3): text/check/radio/path/password
/// fields. Validates required fields and stores answers into
/// <c>InstallContext.Bag["Custom:&lt;id&gt;"]</c> for use by custom actions / config macros.
/// </summary>
public class CustomPage : UserControl, IInstallerPage
{
    private readonly CustomWizardPage _page;
    private InstallContext _ctx = null!;
    private readonly FlowLayoutPanel _flow;
    private readonly Label _error;
    private readonly List<(CustomField field, Func<string> get)> _getters = new();

    public string PageTitle => string.IsNullOrWhiteSpace(_page.Title) ? _page.Id : _page.Title;
    public string Subtitle => _page.Subtitle ?? "";
    /// <summary>
    /// Whether every required field is answered.
    ///
    /// This used to be a constant <c>true</c> while <see cref="ValidityChanged"/> was declared and
    /// never raised, so a custom page with a required field left Next enabled and refused the click
    /// with a modal. It is now the <b>same</b> call <see cref="Validate"/> makes, so the button and
    /// the gate cannot disagree.
    /// </summary>
    public bool CanGoNext => _valid;

    public event EventHandler<bool>? ValidityChanged;

    private bool _valid;

    public CustomPage(CustomWizardPage page, InstallContext ctx)
    {
        _page = page;
        _ctx = ctx;
        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0)
        };
        _error = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            AutoSize = false,
            ForeColor = Color.FromArgb(168, 32, 32),
            Padding = new Padding(0, 8, 0, 0),
        };

        Controls.Add(_flow);
        Controls.Add(_error);
        _error.BringToFront();      // dock the strip before the fill panel claims the space

        BuildFields();
        RefreshValidity();
    }

    /// <summary>
    /// Re-reads every field and republishes validity.
    ///
    /// The strip keeps its height whether or not it says anything, so answering a field does not
    /// shift the row under the pointer.
    /// </summary>
    private void RefreshValidity()
    {
        var (ok, error) = InstallProject.ValidateCustomFields(_page, CurrentValues());

        _error.Text = ok ? "" : (error ?? "");

        if (ok == _valid) return;
        _valid = ok;
        ValidityChanged?.Invoke(this, ok);
    }

    private void BuildFields()
    {
        foreach (var f in _page.Fields)
        {
            var row = new Panel { Width = 520, Height = f.Type == CustomFieldType.Radio ? 120 : 56, Padding = new Padding(0, 4, 0, 4) };
            var label = new Label { Text = f.Label + (f.Required ? " *" : ""), Location = new Point(0, 3), AutoSize = true };

            switch (f.Type)
            {
                case CustomFieldType.Password:
                case CustomFieldType.Text:
                {
                    var tb = new TextBox { Location = new Point(0, 24), Width = 400, Text = f.DefaultValue ?? "", UseSystemPasswordChar = f.Type == CustomFieldType.Password };
                    tb.TextChanged += (_, _) => RefreshValidity();
                    row.Controls.Add(label); row.Controls.Add(tb);
                    _getters.Add((f, () => tb.Text));
                    break;
                }
                case CustomFieldType.Path:
                {
                    var tb = new TextBox { Location = new Point(0, 24), Width = 330, Text = f.DefaultValue ?? "" };
                    var btn = new Button { Text = "...", Location = new Point(336, 23), Width = 32 };
                    btn.Click += (_, _) =>
                    {
                        using var d = new FolderBrowserDialog { SelectedPath = tb.Text };
                        if (d.ShowDialog() == DialogResult.OK) tb.Text = d.SelectedPath;
                    };
                    tb.TextChanged += (_, _) => RefreshValidity();
                    row.Controls.Add(label); row.Controls.Add(tb); row.Controls.Add(btn);
                    _getters.Add((f, () => tb.Text));
                    break;
                }
                case CustomFieldType.Check:
                {
                    var cb = new CheckBox { Text = f.Label, Location = new Point(0, 3), Checked = string.Equals(f.DefaultValue, "true", StringComparison.OrdinalIgnoreCase) };
                    cb.CheckedChanged += (_, _) => RefreshValidity();
                    row.Height = 32;
                    row.Controls.Add(cb);
                    _getters.Add((f, () => cb.Checked ? "true" : "false"));
                    break;
                }
                case CustomFieldType.Radio:
                {
                    var radios = new List<RadioButton>();
                    int y = 24;
                    foreach (var opt in f.Options)
                    {
                        var rb = new RadioButton { Text = opt, Location = new Point(0, y), AutoSize = true, Checked = opt == (f.DefaultValue ?? "") };
                        rb.CheckedChanged += (_, _) => RefreshValidity();
                        radios.Add(rb);
                        row.Controls.Add(rb);
                        y += 24;
                    }
                    row.Controls.Add(label);
                    _getters.Add((f, () => radios.Find(r => r.Checked)?.Text ?? ""));
                    break;
                }
            }
            _flow.Controls.Add(row);
        }
    }

    public void OnEnter(InstallContext ctx) => _ctx = ctx;

    /// <summary>Current field values keyed by field Id.</summary>
    private Dictionary<string, string> CurrentValues()
    {
        var values = new Dictionary<string, string>();
        foreach (var (field, get) in _getters)
            values[field.Id] = get();
        return values;
    }

    public new bool Validate()
    {
        var values = CurrentValues();
        var (ok, error) = InstallProject.ValidateCustomFields(_page, values);
        if (!ok)
        {
            // Reachable only if the page was navigated past programmatically -- CanGoNext keeps the
            // button disabled otherwise, and the reason is already on screen.
            _error.Text = error ?? "";
            return false;
        }
        foreach (var kv in InstallProject.CollectCustomFields(_page, values))
            _ctx.Bag["Custom:" + kv.Key] = kv.Value;
        return true;
    }
}
