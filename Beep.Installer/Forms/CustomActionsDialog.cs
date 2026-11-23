using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>Edits a detached action list. Syntax validation never launches an authored command.</summary>
public class CustomActionsDialog : Form
{
    private readonly Font _bodyFont = InstallerTheme.Body;
    private readonly Font _headingFont = InstallerTheme.SectionTitle;
    private readonly AuthoringThemeBinding _themeBinding;
    private readonly ToolTip _tips = new();
    private readonly ErrorProvider _errors;
    private readonly BindingList<CustomAction> _actions = new();
    private readonly BindingSource _binding;
    private readonly BindingSourceRow<CustomAction> _row;
    private readonly List<(Control Control, string Key, string Fallback, bool Text)> _captions = new();
    private DataGridView _grid = null!;
    private TextBox _pathBox = null!, _argsBox = null!, _workDirBox = null!, _descriptionBox = null!;
    private ComboBox _timingBox = null!;
    private NumericUpDown _orderBox = null!, _timeoutBox = null!;
    private CheckBox _requiredCheck = null!, _failOnErrorCheck = null!;
    private Button _addBtn = null!, _removeBtn = null!, _okBtn = null!, _cancelBtn = null!;
    private Label _validationLabel = null!;
    private bool _disposed, _saveRejected;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<CustomAction> ResultActions => _actions.ToArray();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool>? ApplyDraft { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<IWin32Window, string, string, DialogResult>? ShowWarningPrompt { get; set; }

    public CustomActionsDialog(System.Collections.IList actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (CustomAction? action in actions)
        {
            if (action is null) { _actions.Add(null!); continue; }
            var copy = new CustomAction();
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(action))
                if (!property.IsReadOnly) property.SetValue(copy, property.GetValue(action));
            _actions.Add(copy);
        }
        _binding = new BindingSource { DataSource = _actions };
        _row = new BindingSourceRow<CustomAction>(_binding);
        _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        Font = _bodyFont; Size = new Size(1080, 760); MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
        Caption(this, "Actions_CustomActionsA11", "Custom actions");

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AuthoringThemeBinding.SetSurface(root, AuthoringSurface.Shell);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AuthoringThemeBinding.SetSurface(header, AuthoringSurface.Shell);
        var heading = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = _headingFont, Margin = new Padding(0, 0, 0, 6) };
        Caption(heading, "Actions_CustomActionsA11", "Custom actions");
        var guidance = new Label { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        Caption(guidance, "Actions_Guidance", "Configure commands for installation and removal.");
        AuthoringThemeBinding.SetTone(guidance, AuthoringTone.Muted);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        AuthoringThemeBinding.SetSurface(tools, AuthoringSurface.Shell);
        _addBtn = IconButton("Actions_Add", "Actions_AddAction", "Add action");
        _removeBtn = IconButton("Actions_Remove", "Actions_RemoveAction", "Remove selected action");
        _addBtn.Click += (_, _) => OnAdd(); _removeBtn.Click += (_, _) => OnRemove();
        tools.Controls.AddRange(new Control[] { _addBtn, _removeBtn });
        header.Controls.Add(heading); header.Controls.Add(guidance); header.Controls.Add(tools);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1000, 500), SplitterDistance = 320,
            Panel1MinSize = 220, Panel2MinSize = 360, SplitterWidth = 10, Margin = new Padding(0, 10, 0, 12) };
        split.Panel1.Controls.Add(BuildListPanel()); split.Panel2.Controls.Add(BuildEditorPanel());
        root.Controls.Add(header, 0, 0); root.Controls.Add(split, 0, 1); root.Controls.Add(BuildBottomBar(), 0, 2);
        Controls.Add(root);
        _binding.CurrentChanged += OnCurrentChanged;
        _actions.ListChanged += OnActionsChanged;
        Engine.Accessibility.Attach(this);
        _themeBinding = new AuthoringThemeBinding(this);
        LanguageManager.LanguageChanged += OnLanguageChanged;
        BeepThemesManager.ThemeChanged += OnThemeChanged;
        Load += (_, _) => { RefreshSelection(); RefreshValidation(); };
        RefreshSelection(); OnLanguageChanged(this, EventArgs.Empty);
    }

    private Control BuildListPanel()
    {
        _grid = new DataGridView { Name = "Actions_Grid", Dock = DockStyle.Fill, DataSource = _binding,
            AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false };
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
        _grid.DefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomAction.Path), FillWeight = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomAction.Timing), FillWeight = 40 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                var path = e.Value as string ?? ""; var normalized = path.Replace('\\', '/');
                e.Value = string.IsNullOrWhiteSpace(path) ? L("Actions_NewAction", "New action") : normalized[(normalized.LastIndexOf('/') + 1)..];
                e.FormattingApplied = true;
            }
            if (e.ColumnIndex == 1 && e.Value is CustomActionTiming timing) { e.Value = TimingCaption(timing); e.FormattingApplied = true; }
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is CustomAction action)
                e.ToolTipText = e.ColumnIndex == 0 ? action.Path : TimingCaption(action.Timing);
        };
        Caption(_grid, "Actions_GridName", "Custom actions", false);
        return _grid;
    }

    private Control BuildEditorPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = _headingFont, Margin = new Padding(0, 0, 0, 12) };
        Caption(heading, "Actions_Selected", "Selected action"); layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
        _pathBox = new TextBox { Name = "Actions_PathField", Dock = DockStyle.Fill };
        _pathBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Path), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _pathBox, "Actions_Path", "Path:");
        _argsBox = new TextBox { Name = "Actions_ArgumentsField", Dock = DockStyle.Fill };
        _argsBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Arguments), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _argsBox, "Actions_Arguments", "Arguments:");
        _workDirBox = new TextBox { Name = "Actions_WorkingDirectoryField", Dock = DockStyle.Fill };
        _workDirBox.DataBindings.Add("Text", _binding, nameof(CustomAction.WorkingDirectory), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _workDirBox, "Actions_WorkingDir", "Working dir:");
        _timingBox = new ComboBox { Name = "Actions_TimingField", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true };
        foreach (var timing in Enum.GetValues<CustomActionTiming>().Concat(_actions.Where(a => a is not null).Select(a => a.Timing)).Distinct()) _timingBox.Items.Add(timing);
        _timingBox.Format += (_, e) => { if (e.ListItem is CustomActionTiming timing) e.Value = TimingCaption(timing); };
        _timingBox.DataBindings.Add("SelectedItem", _binding, nameof(CustomAction.Timing), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _timingBox, "Actions_Timing", "Timing:");
        _orderBox = new NumericUpDown { Name = "Actions_OrderField", Dock = DockStyle.Fill, Minimum = int.MinValue, Maximum = int.MaxValue };
        _orderBox.DataBindings.Add("Value", _binding, nameof(CustomAction.Order), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _orderBox, "Common_Order", "Order:");
        // Invalid stored values stay visible for correction; validation, not the input control, owns the rule.
        _timeoutBox = new NumericUpDown { Name = "Actions_TimeoutField", Dock = DockStyle.Fill, Minimum = int.MinValue, Maximum = int.MaxValue };
        _timeoutBox.DataBindings.Add("Value", _binding, nameof(CustomAction.TimeoutMs), true, DataSourceUpdateMode.OnPropertyChanged);
        AddField(layout, _timeoutBox, "Actions_TimeoutMs", "Timeout (ms):");
        _requiredCheck = new CheckBox { Name = "Actions_RequiredField", Dock = DockStyle.Fill, AutoSize = true };
        _requiredCheck.DataBindings.Add("Checked", _binding, nameof(CustomAction.Required), true, DataSourceUpdateMode.OnPropertyChanged);
        Caption(_requiredCheck, "Actions_RequiredFailInstall", "Required (fail install)");
        _failOnErrorCheck = new CheckBox { Name = "Actions_FailOnErrorField", Dock = DockStyle.Fill, AutoSize = true };
        _failOnErrorCheck.DataBindings.Add("Checked", _binding, nameof(CustomAction.FailOnError), true, DataSourceUpdateMode.OnPropertyChanged);
        Caption(_failOnErrorCheck, "Actions_FailOnError", "Fail on error");
        foreach (var check in new[] { _requiredCheck, _failOnErrorCheck })
        { var row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(check, 1, row); check.Validated += OnEditorValidated; }
        _descriptionBox = new TextBox { Name = "Actions_DescriptionField", Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
        var descriptionBinding = _descriptionBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Description), true, DataSourceUpdateMode.OnPropertyChanged);
        descriptionBinding.DataSourceNullValue = null;
        descriptionBinding.Parse += (_, e) =>
        {
            // Committing an untouched empty control must preserve a nullable authored value.
            if (e.Value is "" && _row.Current?.Description is null) e.Value = null;
        };
        AddField(layout, _descriptionBox, "Common_Description", "Description:");
        host.Controls.Add(layout); return host;
    }

    private void AddField(TableLayoutPanel layout, Control field, string key, string fallback)
    {
        var label = new Label { Name = "Caption_" + key, AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(160, 0),
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 12, 8) };
        Caption(label, key, fallback); Caption(field, key, fallback, false);
        field.Margin = new Padding(0, 4, 0, 8); field.Validated += OnEditorValidated;
        var row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(label, 0, row); layout.Controls.Add(field, 1, row);
    }

    private Control BuildBottomBar()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AuthoringThemeBinding.SetSurface(footer, AuthoringSurface.Shell);
        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); AuthoringThemeBinding.SetSurface(summary, AuthoringSurface.Shell);
        _validationLabel = new Label { Name = "Actions_Validation", AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
        var help = new Label { Dock = DockStyle.Fill, AutoSize = true };
        Caption(help, "Actions_DraftHelp", "Changes apply when you choose OK. Commands are not run here.");
        AuthoringThemeBinding.SetTone(help, AuthoringTone.Muted);
        summary.Controls.Add(_validationLabel); summary.Controls.Add(help);
        summary.SizeChanged += (_, _) => { var width = Math.Max(1, summary.ClientSize.Width - 8); _validationLabel.MaximumSize = help.MaximumSize = new Size(width, 0); };
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AuthoringThemeBinding.SetSurface(buttons, AuthoringSurface.Shell);
        _okBtn = new Button { Name = "Actions_Ok", AutoSize = true, MinimumSize = new Size(90, 48), FlatStyle = FlatStyle.Flat };
        Caption(_okBtn, "Common_OK", "OK"); AuthoringThemeBinding.SetSurface(_okBtn, AuthoringSurface.Accent);
        _okBtn.Click += (_, _) => OnOk();
        _cancelBtn = IconButton("Actions_Cancel", "Common_Cancel", "Cancel"); _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(_okBtn, 0, 0); buttons.Controls.Add(_cancelBtn, 1, 0);
        footer.Controls.Add(summary, 0, 0); footer.Controls.Add(buttons, 1, 0);
        AcceptButton = _okBtn; CancelButton = _cancelBtn; return footer;
    }

    private Button IconButton(string name, string key, string fallback)
    {
        var button = new Button { Name = name, Size = new Size(48, 48), MinimumSize = new Size(48, 48), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0) };
        Caption(button, key, fallback, false); return button;
    }

    private void Caption(Control control, string key, string fallback, bool text = true)
    { _captions.Add((control, key, fallback, text)); new LiveUiCaption(key, fallback).Bind(control, text: text); }

    private void OnCurrentChanged(object? sender, EventArgs e) { RefreshSelection(); RefreshValidation(); }
    private void OnActionsChanged(object? sender, ListChangedEventArgs e) { RefreshSelection(); RefreshValidation(); }
    private void OnEditorValidated(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_row.HasRow) PackageBuilderForm.CommitBindings(this);
        _grid.Invalidate(); RefreshValidation();
    }

    private void RefreshSelection()
    {
        if (_pathBox is null) return;
        foreach (var field in new Control[] { _pathBox, _argsBox, _workDirBox, _timingBox, _orderBox, _timeoutBox, _requiredCheck, _failOnErrorCheck, _descriptionBox })
            field.Enabled = _row.HasRow;
        _removeBtn.Enabled = _binding.Position >= 0 && _binding.Position < _actions.Count;
    }

    private void OnAdd()
    { PackageBuilderForm.CommitBindings(this); _actions.Add(new CustomAction()); _binding.Position = _actions.Count - 1; _pathBox.Focus(); }
    private void OnRemove()
    {
        var index = _binding.Position;
        if (index < 0 || index >= _actions.Count) return;
        // The grid consumes the currency change after CurrentChanged. Keep focus on a stable
        // action while the last row and its bound editor are being removed.
        _addBtn.Focus();
        _actions.RemoveAt(index);
    }

    private void OnOk()
    {
        PackageBuilderForm.CommitBindings(this);
        var issues = InstallProject.ValidateCustomActions(_actions);
        var error = issues.FirstOrDefault(issue => issue.IsError);
        if (error is not null)
        {
            _saveRejected = true; _binding.Position = error.Index; RefreshValidation();
            var field = FieldFor(error.PropertyName); field.Focus(); return;
        }
        _saveRejected = false; RefreshValidation();
        var warnings = issues.Where(issue => !issue.IsError).ToArray();
        if (warnings.Length > 0)
        {
            var message = L("Actions_SaveWarnings", "There are warnings. Save anyway?") + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, warnings.Select(issue => "- " + issue.Key + ": " + issue.Message));
            var title = L("Common_Warnings", "Warnings");
            var answer = ShowWarningPrompt?.Invoke(this, message, title) ?? MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        if (ApplyDraft?.Invoke() == false) return;
        DialogResult = DialogResult.OK; Close();
    }

    private Control FieldFor(string name) => name switch
    { nameof(CustomAction.Path) => _pathBox, nameof(CustomAction.TimeoutMs) => _timeoutBox, nameof(CustomAction.Timing) => _timingBox, _ => _grid };

    private void RefreshValidation()
    {
        if (_validationLabel is null || _disposed) return;
        var issues = InstallProject.ValidateCustomActions(_actions);
        ValidationCounts.From(issues, issue => issue.IsError).Apply(_validationLabel,
            string.Format(LanguageManager.CurrentCulture, L("Actions_Count", "{0:N0} actions \u2014"), _actions.Count), L("Actions_Clean", "No validation issues"));
        _errors.Clear();
        foreach (var issue in issues.Where(issue => issue.IsError && issue.Index == _binding.Position))
            _errors.SetError(FieldFor(issue.PropertyName), issue.Message);
        if (_saveRejected && issues.FirstOrDefault(issue => issue.IsError) is { } error)
            _validationLabel.Text = _validationLabel.AccessibleName = L("Actions_CannotSave", "Cannot save. Fix the highlighted action:") +
                " #" + (error.Index + 1) + " \u2014 " + error.Message;
    }

    private static string TimingCaption(CustomActionTiming timing)
    {
        if (!Enum.IsDefined(timing)) return string.Format(LanguageManager.CurrentCulture, L("Actions_UnknownTiming", "Unknown timing ({0})"), (int)timing);
        return timing switch
        {
            CustomActionTiming.BeforeInstall => L("Actions_BeforeInstall", "Before install"),
            CustomActionTiming.AfterInstall => L("Actions_AfterInstall", "After install"),
            CustomActionTiming.BeforeUninstall => L("Actions_BeforeUninstall", "Before uninstall"),
            CustomActionTiming.AfterUninstall => L("Actions_AfterUninstall", "After uninstall")
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed || IsDisposed || Disposing) return;
        if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        var focus = UiFocusBookmark.Capture(this);
        _grid.Columns[0].HeaderText = L("Actions_Command", "Command");
        _grid.Columns[1].HeaderText = L("Actions_Timing", "Timing:").TrimEnd(':', '\uff1a');
        _grid.Invalidate(); _timingBox.FormatInfo = LanguageManager.CurrentCulture;
        foreach (var caption in _captions)
            if (caption.Control is Button button && !caption.Text) _tips.SetToolTip(button, L(caption.Key, caption.Fallback));
        RefreshValidation(); RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft); focus?.Restore();
        ApplyIcons();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    { if (_disposed || IsDisposed || Disposing) return; if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(ApplyIcons); return; } ApplyIcons(); }
    private void ApplyIcons()
    {
        var color = InstallerTheme.AccessibleForeground(InstallerTheme.Text, InstallerTheme.Panel);
        _addBtn.Image = BeepIcons.Render(BeepIcons.Find("action-add"), 24, color) ?? throw new InvalidOperationException("Add-action SVG is missing.");
        _removeBtn.Image = BeepIcons.Render(BeepIcons.Find("action-remove"), 24, color) ?? throw new InvalidOperationException("Remove-action SVG is missing.");
        _cancelBtn.Image = BeepIcons.Render(BeepIcons.Find("circle-x"), 24, color) ?? throw new InvalidOperationException("Cancel SVG is missing.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; LanguageManager.LanguageChanged -= OnLanguageChanged; BeepThemesManager.ThemeChanged -= OnThemeChanged;
            _actions.ListChanged -= OnActionsChanged; _binding.CurrentChanged -= OnCurrentChanged;
            _themeBinding?.Dispose(); _errors.Dispose(); _tips.Dispose(); _binding.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) { _bodyFont.Dispose(); _headingFont.Dispose(); }
    }
}
