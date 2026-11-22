using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;

namespace Beep.Installer.Forms;

/// <summary>A detached recipe draft. Preview and Apply use the current project; Cancel never edits it.</summary>
public sealed class ResourceRecipeDialog : Form
{
    private readonly Func<InstallProject> _project;
    private readonly Func<ResourceRecipeRequest, ResourceRecipePreview> _applyRecipe;
    private readonly ComboBox _kind = new() { Name = "RecipeKind", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> _fields = new();
    private readonly Dictionary<string, Label> _labels = new();
    private readonly Label _support = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "RecipeSupport" };
    private readonly Label _limits = new() { AutoSize = true, Dock = DockStyle.Fill, Name = "RecipeLimits" };
    private readonly TextBox _review = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Name = "RecipeReview" };
    private readonly Button _preview = new() { AutoSize = true, Name = "PreviewRecipe" };
    private readonly Button _apply = new() { AutoSize = true, Name = "ApplyRecipe", Enabled = false };
    private readonly Button _cancel = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly TableLayoutPanel _inputs = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
    private bool _initializing;
    private ResourceRecipePreview? _lastPreview;

    public ResourceRecipeDialog(Func<InstallProject> currentProject,
        Func<ResourceRecipeRequest, ResourceRecipePreview> applyRecipe, ResourceRecipeKind initialKind, string executableReference)
    {
        _project = currentProject;
        _applyRecipe = applyRecipe;
        Size = new Size(850, 780);
        MinimumSize = new Size(720, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        StartPosition = FormStartPosition.CenterParent;
        Font = InstallerTheme.Body;
        BackColor = InstallerTheme.Panel;
        _inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4 };
        body.Controls.Add(_kind, 0, 0);
        body.Controls.Add(_support, 0, 1);
        body.Controls.Add(_limits, 0, 2);
        body.Controls.Add(_inputs, 0, 3);
        scroll.Controls.Add(body);
        root.Controls.Add(scroll, 0, 0);
        root.Controls.Add(_review, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { _cancel, _apply, _preview });
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        TextField("Name"); TextField("TargetPath"); TextField("PoolName");
        AddField("Port", new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 8080 });
        TextField("Host"); TextField("RuntimeVersion");
        ChoiceField("Trigger", Enum.GetValues<ScheduledTaskTrigger>());
        TextField("StartTime");
        ChoiceField("ServiceAccount", new[] { WindowsServiceAccount.LocalService, WindowsServiceAccount.NetworkService, WindowsServiceAccount.LocalSystem });
        ChoiceField("FirewallProtocol", new[] { FirewallRuleProtocol.Tcp, FirewallRuleProtocol.Udp });
        ChoiceField("FirewallProfile", new[] { "domain,private", "domain", "private", "public" });
        AddField("UseMachineScope", new CheckBox { AutoSize = true });
        AddField("UseExeOutput", new CheckBox { AutoSize = true });
        _kind.SelectedIndexChanged += (_, _) => SetKindDefaults(executableReference);
        _preview.Click += (_, _) => PreviewDraft();
        _apply.Click += (_, _) => ApplyDraft();
        CancelButton = _cancel;
        AcceptButton = _preview;
        LanguageManager.LanguageChanged += OnLanguageChanged;
        _initializing = true;
        Translate(initialKind);
        _initializing = false;
        SetKindDefaults(executableReference);
        body.SizeChanged += (_, _) =>
        {
            var width = Math.Max(1, scroll.ClientSize.Width - 24);
            _support.MaximumSize = new Size(width, 0);
            _limits.MaximumSize = new Size(width, 0);
            foreach (var label in _labels.Values) label.MaximumSize = new Size(Math.Max(160, width * 2 / 5), 0);
        };
        Engine.Accessibility.Attach(this);
    }

    private ResourceRecipeKind Kind => _kind.SelectedItem is RecipeChoice choice ? choice.Kind : ResourceRecipeKind.WindowsService;
    private sealed record RecipeChoice(ResourceRecipeKind Kind, string Caption) { public override string ToString() => Caption; }
    private void TextField(string key) => AddField(key, new TextBox());
    private void ChoiceField<T>(string key, IEnumerable<T> options)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(options.Select(option => (object)option!).ToArray());
        combo.SelectedIndex = 0;
        AddField(key, combo);
    }
    private void AddField(string key, Control control)
    {
        control.Name = "Recipe." + key;
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 4, 4, 8);
        var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 4, 12, 8) };
        _fields.Add(key, control); _labels.Add(key, label);
        var row = _inputs.RowCount++;
        _inputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _inputs.Controls.Add(label, 0, row); _inputs.Controls.Add(control, 1, row);
        control.TextChanged += (_, _) => InvalidatePreview();
        if (control is CheckBox check) check.CheckedChanged += (_, _) => InvalidatePreview();
        if (control is NumericUpDown number) number.ValueChanged += (_, _) => InvalidatePreview();
        if (control is ComboBox combo) combo.SelectedIndexChanged += (_, _) => InvalidatePreview();
    }

    private void SetKindDefaults(string executableReference)
    {
        if (_initializing) return;
        _initializing = true;
        var project = _project();
        SetText("Name", project.AppName + (Kind == ResourceRecipeKind.WindowsService ? "Svc" : Kind == ResourceRecipeKind.IisSite ? " Site" : Kind == ResourceRecipeKind.ScheduledTask ? " Task" : " inbound"));
        SetText("TargetPath", Kind == ResourceRecipeKind.IisSite ? "{app}\\wwwroot" : executableReference);
        SetText("PoolName", project.AppName + "Pool"); SetText("Host", ""); SetText("RuntimeVersion", "v4.0"); SetText("StartTime", "09:00");
        ((CheckBox)_fields["UseMachineScope"]).Checked = false;
        ((CheckBox)_fields["UseExeOutput"]).Checked = false;
        ShowFields();
        _initializing = false;
        InvalidatePreview();
    }
    private void ShowFields()
    {
        var visible = new HashSet<string> { "Name", "TargetPath", "UseMachineScope", "UseExeOutput" };
        foreach (var field in Kind switch
        {
            ResourceRecipeKind.WindowsService => new[] { "ServiceAccount" },
            ResourceRecipeKind.IisSite => new[] { "PoolName", "Port", "Host", "RuntimeVersion" },
            ResourceRecipeKind.ScheduledTask => new[] { "Trigger", "StartTime" },
            _ => new[] { "Port", "FirewallProtocol", "FirewallProfile" }
        }) visible.Add(field);
        foreach (var field in _fields) { field.Value.Visible = visible.Contains(field.Key); _labels[field.Key].Visible = visible.Contains(field.Key); }
        _limits.Text = L("Recipe_" + Kind + "Limits", LimitsFallback(Kind));
        _labels["TargetPath"].Text = L("Recipe_Target" + Kind, Kind == ResourceRecipeKind.IisSite ? "Physical path *" : Kind == ResourceRecipeKind.FirewallRule ? "Program (optional)" : "Executable path *");
        _fields["TargetPath"].AccessibleName = _labels["TargetPath"].Text;
    }

    internal ResourceRecipeRequest CurrentRequest => new(Kind, TextValue("Name"), TextValue("TargetPath"),
        TextValue("PoolName"), (int)((NumericUpDown)_fields["Port"]).Value, TextValue("Host"), TextValue("RuntimeVersion"),
        (ScheduledTaskTrigger)((ComboBox)_fields["Trigger"]).SelectedItem!, TextValue("StartTime"),
        (WindowsServiceAccount)((ComboBox)_fields["ServiceAccount"]).SelectedItem!,
        (FirewallRuleProtocol)((ComboBox)_fields["FirewallProtocol"]).SelectedItem!, ((ComboBox)_fields["FirewallProfile"]).Text,
        ((CheckBox)_fields["UseMachineScope"]).Checked, ((CheckBox)_fields["UseExeOutput"]).Checked);

    private string TextValue(string key) => _fields[key].Text.Trim();
    private void SetText(string key, string value) => _fields[key].Text = value;
    private void InvalidatePreview()
    {
        if (_initializing) return;
        _lastPreview = null;
        _apply.Enabled = false;
        _review.Text = L("Recipe_PreviewFirst", "Preview the changes before applying this recipe.");
        _review.AccessibleDescription = _review.Text;
    }
    private void PreviewDraft()
    {
        try { ShowPreview(ResourceRecipeCatalog.Preview(_project(), CurrentRequest)); }
        catch (Exception ex) { _review.Text = ex.Message; _apply.Enabled = false; }
    }
    private void ShowPreview(ResourceRecipePreview preview)
    {
        _lastPreview = preview;
        _review.Text = string.Join(Environment.NewLine, preview.Changes.Select(change => $"+ {change.Path}: {change.Name}")) +
            Environment.NewLine + string.Join(Environment.NewLine, preview.Diagnostics.Select(issue => $"{issue.Severity} {issue.Code} {issue.Path}: {issue.Message}"));
        _apply.Enabled = preview.CanApply;
        _review.AccessibleDescription = preview.CanApply ? L("Recipe_Ready", "Ready to apply the previewed changes.") : L("Recipe_Fix", "Fix the listed errors before applying.");
    }
    private void ApplyDraft()
    {
        if (_lastPreview is null || !_lastPreview.CanApply) return;
        try
        {
            var result = _applyRecipe(CurrentRequest);
            ShowPreview(result);
            if (result.CanApply) { DialogResult = DialogResult.OK; Close(); }
        }
        catch (Exception ex) { _review.Text = ex.Message; _apply.Enabled = false; }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        _initializing = true;
        Translate(Kind);
        _initializing = false;
        if (_lastPreview is null) InvalidatePreview();
        else ShowPreview(_lastPreview);
    }
    private void Translate(ResourceRecipeKind selected)
    {
        Text = L("Recipe_Title", "Resource recipes");
        _kind.Items.Clear();
        foreach (var kind in Enum.GetValues<ResourceRecipeKind>())
            _kind.Items.Add(new RecipeChoice(kind, L(kind switch { ResourceRecipeKind.WindowsService => "Section_WindowsServices", ResourceRecipeKind.IisSite => "Section_IisSites", ResourceRecipeKind.ScheduledTask => "Section_ScheduledTasks", _ => "Section_FirewallRules" }, kind.ToString())));
        _kind.SelectedIndex = (int)selected;
        _kind.AccessibleName = Text;
        _support.Text = L("Recipe_Requirements", "Requires Windows tools, EXE/MSI output and a machine installation with administrator privileges. Use names reserved for this app. Existing machine resources are not inspected here.");
        foreach (var field in _labels)
        {
            field.Value.Text = L("Recipe_Field" + field.Key, field.Key + (field.Key is "Name" or "PoolName" ? " *" : ""));
            _fields[field.Key].AccessibleName = field.Value.Text;
        }
        ShowFields();
        _preview.Text = L("Recipe_Preview", "Preview changes"); _apply.Text = L("Recipe_Apply", "Apply recipe"); _cancel.Text = L("Common_Cancel", "Cancel");
        foreach (var button in new[] { _preview, _apply, _cancel }) { button.AccessibleName = button.Text; button.MinimumSize = new Size(0, InstallerTheme.ActionMinHeight); }
        _review.AccessibleName = L("Recipe_Preview", "Preview changes");
        RtlHelper.ApplyDirection(this, RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName));
    }
    private static string LimitsFallback(ResourceRecipeKind kind) => kind switch
    {
        ResourceRecipeKind.WindowsService => "Creates an automatic service using the selected built-in account. SCM is required. New services are removed on rollback; captured existing configuration can be restored. Credentials and advanced settings are available in the full editor.",
        ResourceRecipeKind.IisSite => "Creates a new application pool and an HTTP site. IIS/appcmd must already be installed. TLS and certificates are configured in the full editor. Rollback/removal may delete the owned site and pool; previous definitions are not restored.",
        ResourceRecipeKind.ScheduledTask => "Creates a task using the installer's executing account. Task Scheduler is required. Rollback/removal ends and deletes the task; a previous task definition is not restored. Credentials and elevation are available in the full editor.",
        _ => "Creates an inbound allow rule for the selected port, program and profiles. Windows Firewall/netsh is required. An empty program applies to all programs. Rollback/removal deletes the rule; a previous rule definition is not restored."
    };
    private static string L(string key, string fallback) => LanguageManager.GetOrDefault(key, fallback);
    protected override void Dispose(bool disposing)
    {
        if (disposing) LanguageManager.LanguageChanged -= OnLanguageChanged;
        base.Dispose(disposing);
    }
}
