using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Installer;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Modal editor for component <see cref="InstallCondition"/> lists.
/// Uses WinForms data binding: DataGridView binds via BindingSource, editor fields via DataBindings.Add.
/// </summary>
public class ComponentConditionsDialog : Form
{
    private readonly Font _bodyFont = InstallerTheme.Body;
    private readonly AuthoringThemeBinding _themeBinding;
    private readonly System.Collections.Generic.List<Action> _captions = new();
    private bool _disposed;
    private bool _saveRejected;
    private readonly System.Collections.Generic.List<InstallComponent> _components;
    private ComboBox _componentBox = null!;
    private readonly InstallComponent? _initial;
    private ComboBox _expressionBox = null!;
    private DataGridView _grid = null!;
    private ComboBox _typeBox = null!;
    private TextBox _valueBox = null!;
    private TextBox _value2Box = null!;
    private TextBox _operatorBox = null!;
    private TextBox _previewBox = null!;
    private Button _addBtn = null!;
    private Button _removeBtn = null!;
    private Button _testBtn = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Label _validationLabel = null!;
    private BindingSource _binding = null!;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool>? ApplyDraft { get; set; }

    /// <param name="initial">
    /// The component the user already selected in the builder. The dialog opens on it instead of
    /// on the first in the list: selecting a component, opening its conditions and being asked to
    /// select it again is the flow 6.C.2 calls out. The picker stays, so switching component
    /// without closing still works.
    /// </param>
    public ComponentConditionsDialog(System.Collections.IList components, InstallComponent? initial = null)
    {
        _components = (components ?? new System.Collections.Generic.List<InstallComponent>()).Cast<InstallComponent>().ToList();
        _initial = initial;
        // The item type has to be known before anything binds to a property of it.
        //
        // The editor binds _typeBox/_valueBox/_operatorBox/_value2Box to Type, Value, Operator and
        // Value2 while DataSource is still empty. A BindingSource with no source cannot say what its
        // items look like, so the first of those bindings threw
        // ArgumentException("Cannot bind to the property or column Type on the DataSource") the
        // moment the form was shown -- every single time this dialog was opened. WinForms turned
        // that into its own modal error dialog, which is what the user was seeing.
        //
        // Handing it the type up front lets the bindings resolve with zero rows; Rebind then swaps
        // in the real list.
        _binding = new BindingSource { DataSource = typeof(InstallCondition) };
        _binding.ListChanged += (_, _) => RefreshValidation();

        Text = L("Conditions_ComponentConditionBuilder", "Component Condition Builder");
        Font = _bodyFont;
        Caption(this, "Conditions_ComponentConditionBuilder", Text);
        Size = new Size(1040, 640);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selector.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selector.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selector.Controls.Add(CaptionLabel("Common_Component", "Component:"), 0, 0);
        _componentBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _components) _componentBox.Items.Add(c.Id + " — " + c.Name);
        _componentBox.SelectedIndexChanged += (_, _) => Rebind();
        selector.Controls.Add(_componentBox, 1, 0);
        selector.Controls.Add(CaptionLabel("Conditions_GroupExpression", "Group expression:"), 0, 1);
        _expressionBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var mode in Enum.GetValues(typeof(ConditionExpressionMode)))
            _expressionBox.Items.Add(mode);
        _expressionBox.SelectedIndexChanged += (_, _) =>
        {
            if (CurrentComponent != null && _expressionBox.SelectedItem is ConditionExpressionMode mode)
            {
                CurrentComponent.ConditionExpression = mode;
                RefreshValidation();
            }
        };
        selector.Controls.Add(_expressionBox, 1, 1);
        root.Controls.Add(selector, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildListPanel());
        split.Panel2.Controls.Add(BuildEditorPanel());
        root.Controls.Add(split, 0, 1);

        _validationLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        root.SizeChanged += (_, _) => _validationLabel.MaximumSize = new Size(Math.Max(1, root.ClientSize.Width), 0);
        root.Controls.Add(_validationLabel, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = CaptionButton("Common_OK", "OK");
        _okBtn.Click += (_, _) => OnOk();
        _cancelBtn = CaptionButton("Common_Cancel", "Cancel");
        _cancelBtn.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(_okBtn);
        buttons.Controls.Add(_cancelBtn);
        root.Controls.Add(buttons, 0, 3);

        Load += (_, _) =>
        {
            if (_componentBox.Items.Count == 0) return;

            // Open on the component the builder had selected; fall back to the first only when the
            // caller had no selection (or it is not in this list).
            var index = _initial is null ? -1 : _components.FindIndex(c => ReferenceEquals(c, _initial));
            if (index < 0 && _initial is not null)
                index = _components.FindIndex(c => string.Equals(c.Id, _initial.Id, StringComparison.Ordinal));

            _componentBox.SelectedIndex = index >= 0 ? index : 0;
        };
        Engine.Accessibility.Attach(this);
        RegisterFields();
        _themeBinding = new AuthoringThemeBinding(this);
        LanguageManager.LanguageChanged += OnLanguageChanged;
        RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
    }

    private Control BuildListPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _grid = new DataGridView
        {
            Name = "ConditionGrid", DataSource = _binding,
            AccessibleName = L("Conditions_GridName", "Conditions for the selected component"),
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        _grid.DataBindingComplete += (_, _) => RefreshGridCaptions();
        _grid.CellValueChanged += (_, _) => RefreshValidation();

        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _addBtn = CaptionButton("Common_Add", "Add");
        var x64Btn = CaptionButton("Conditions_QuickX64", "x64 rule");
        var adminBtn = CaptionButton("Conditions_QuickAdmin", "Admin rule");
        var fileBtn = CaptionButton("Conditions_QuickFile", "File rule");
        _removeBtn = CaptionButton("Common_Remove", "Remove");
        _testBtn = CaptionButton("Conditions_TestAll", "Test all");
        _addBtn.Click += (_, _) => OnAdd();
        x64Btn.Click += (_, _) => AddCondition(ConditionType.Architecture, "x64");
        adminBtn.Click += (_, _) => AddCondition(ConditionType.IsAdmin);
        fileBtn.Click += (_, _) => AddCondition(ConditionType.FileExists, "{app}\\marker.txt");
        _removeBtn.Click += (_, _) => OnRemove();
        _testBtn.Click += (_, _) => OnTestAll();
        row.Controls.AddRange(new Control[] { _addBtn, x64Btn, adminBtn, fileBtn, _removeBtn, _testBtn });
        panel.Controls.Add(_grid, 0, 0);
        panel.Controls.Add(row, 0, 1);
        // A null condition is a blocking, removable row too. Use its position rather
        // than requiring a non-null object to enable the correction action.
        void RefreshRemove() => _removeBtn.Enabled = _binding.Position >= 0 && _binding.Position < _binding.Count;
        _binding.PositionChanged += (_, _) => RefreshRemove();
        _binding.ListChanged += (_, _) => RefreshRemove();
        RefreshRemove();
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(CaptionLabel("Common_Type", "Type:"), 0, row);
        _typeBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues(typeof(ConditionType))) _typeBox.Items.Add(t);
        _typeBox.DataBindings.Add("SelectedItem", _binding, nameof(InstallCondition.Type), true, DataSourceUpdateMode.OnPropertyChanged);
        _typeBox.SelectedValueChanged += (_, _) => RefreshEditorEnabled();
        layout.Controls.Add(_typeBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(CaptionLabel("Common_Value", "Value:"), 0, row);
        _valueBox = new TextBox { Dock = DockStyle.Fill };
        _valueBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_valueBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(CaptionLabel("Conditions_Operator", "Operator:"), 0, row);
        _operatorBox = new TextBox { Dock = DockStyle.Fill, Text = "==" };
        _operatorBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Operator), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_operatorBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(CaptionLabel("Conditions_Value2", "Value2:"), 0, row);
        _value2Box = new TextBox { Dock = DockStyle.Fill };
        _value2Box.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value2), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_value2Box, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.Controls.Add(CaptionLabel("Conditions_Preview", "Preview:"), 0, row);
        _previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window
        };
        layout.Controls.Add(_previewBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var notes = new Label
        {
            Text = L("Conditions_NotesNGroupExpression", "Notes:\n • Group expression controls how all rows combine: All = every rule, Any = at least one rule, Not = invert the group.\n • Value holds the primary operand (registry path, file path, OS version, command, …).\n • Value2 is the expected value for RegistryValue and CommandReturns comparisons.\n • Operator is one of ==, =, !=, >, >=, <, <= (default '==')."),
            Dock = DockStyle.Fill,
            ForeColor = Ui.InstallerTheme.MutedText
        };
        notes.AutoSize = true;
        Caption(notes, "Conditions_NotesNGroupExpression", notes.Text);
        AuthoringThemeBinding.SetTone(notes, AuthoringTone.Muted);
        layout.SizeChanged += (_, _) => notes.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        layout.Controls.Add(notes, 0, row);
        layout.SetColumnSpan(notes, 2);
        return layout;
    }

    private InstallComponent? CurrentComponent
        => _componentBox.SelectedIndex >= 0 && _componentBox.SelectedIndex < _components.Count
            ? _components[_componentBox.SelectedIndex] : null;

    private void Caption(Control control, string key, string fallback, bool text = true)
    {
        var caption = new LiveUiCaption(key, fallback);
        void Refresh()
        {
            if (text) control.Text = caption.Value;
            control.AccessibleName = caption.Value;
        }
        _captions.Add(Refresh);
        Refresh();
    }

    private Label CaptionLabel(string key, string fallback)
    {
        var label = new Label { Name = "Caption_" + key, AutoSize = true, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, MaximumSize = new Size(160, 0), MinimumSize = new Size(0, 30) };
        Caption(label, key, fallback);
        return label;
    }

    private Button CaptionButton(string key, string fallback)
    {
        var button = new Button { Name = key, AutoSize = true, MinimumSize = new Size(75, 30) };
        Caption(button, key, fallback);
        return button;
    }

    private void RegisterFields()
    {
        _componentBox.Name = "ConditionComponent"; _expressionBox.Name = "ConditionExpression";
        _typeBox.Name = "ConditionType"; _valueBox.Name = "ConditionValue";
        _operatorBox.Name = "ConditionOperator"; _value2Box.Name = "ConditionValue2";
        _previewBox.Name = "ConditionPreview"; _validationLabel.Name = "ConditionValidation";
        _addBtn.Name = "ConditionAdd"; _removeBtn.Name = "ConditionRemove";
        _testBtn.Name = "ConditionTest"; _okBtn.Name = "ConditionOk"; _cancelBtn.Name = "ConditionCancel";
        Caption(_componentBox, "Common_Component", "Component:", false);
        Caption(_expressionBox, "Conditions_GroupExpression", "Group expression:", false);
        Caption(_grid, "Conditions_GridName", "Conditions for the selected component", false);
        Caption(_typeBox, "Common_Type", "Type:", false); Caption(_valueBox, "Common_Value", "Value:", false);
        Caption(_operatorBox, "Conditions_Operator", "Operator:", false);
        Caption(_value2Box, "Conditions_Value2", "Value2:", false);
        Caption(_previewBox, "Conditions_Preview", "Preview:", false);
        foreach (var field in new Control[] { _valueBox, _operatorBox, _value2Box })
            field.TextChanged += (_, _) => { _grid.Invalidate(); RefreshValidation(); };
        _typeBox.SelectedValueChanged += (_, _) => RefreshValidation();
    }

    private void RefreshGridCaptions()
    {
        if (_grid is null) return;
        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderText = column.DataPropertyName switch
            {
                "Type" => L("Common_Type", "Type:").TrimEnd(':'),
                "Value" => L("Common_Value", "Value:").TrimEnd(':'),
                "Operator" => L("Conditions_Operator", "Operator:").TrimEnd(':'),
                "Value2" => L("Conditions_Value2", "Value2:").TrimEnd(':'),
                _ => column.HeaderText
            };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed || IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        var focus = UiFocusBookmark.Capture(this);
        foreach (var refresh in _captions) refresh();
        RefreshGridCaptions();
        RefreshValidation(); // Validation inspects rule syntax only; it never evaluates a rule.
        // Translated prose can move selected authored text within the preview. Its
        // renderer has already retained that selection; bookmark the new position.
        if (focus?.ControlName == _previewBox.Name) focus = UiFocusBookmark.Capture(this);
        RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
        focus?.Restore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            LanguageManager.LanguageChanged -= OnLanguageChanged;
            _themeBinding?.Dispose();
            _binding?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) _bodyFont.Dispose();
    }

    private void Rebind()
    {
        var c = CurrentComponent;
        if (c == null) { _binding.DataSource = null; _validationLabel.Text = ""; return; }
        c.Conditions ??= new System.Collections.Generic.List<InstallCondition>();
        _expressionBox.SelectedItem = c.ConditionExpression;
        _binding.DataSource = c.Conditions;
        RefreshValidation();
    }

    private void RefreshEditorEnabled()
    {
        var needsValue = _typeBox.SelectedItem is ConditionType t
            && t != ConditionType.AlwaysTrue && t != ConditionType.AlwaysFalse && t != ConditionType.IsAdmin;
        var needsValue2 = _typeBox.SelectedItem is ConditionType t2 && t2 is ConditionType.RegistryValue or ConditionType.CommandReturns;
        _valueBox.Enabled = needsValue;
        _value2Box.Enabled = needsValue2;
        _operatorBox.Enabled = needsValue;
    }

    private void OnAdd()
        => AddCondition(
            _typeBox.SelectedItem is ConditionType t ? t : ConditionType.AlwaysTrue,
            string.IsNullOrWhiteSpace(_valueBox.Text) ? null : _valueBox.Text.Trim(),
            string.IsNullOrWhiteSpace(_operatorBox.Text) ? "==" : _operatorBox.Text.Trim(),
            string.IsNullOrWhiteSpace(_value2Box.Text) ? null : _value2Box.Text.Trim());

    private void AddCondition(ConditionType type, string? value = null, string? op = "==", string? value2 = null)
    {
        if (CurrentComponent == null) return;
        CurrentComponent.Conditions ??= new System.Collections.Generic.List<InstallCondition>();
        var cond = new InstallCondition
        {
            Type = type,
            Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
            Value2 = string.IsNullOrWhiteSpace(value2) ? null : value2.Trim(),
            Operator = string.IsNullOrWhiteSpace(op) ? "==" : op.Trim()
        };
        CurrentComponent.Conditions.Add(cond);
        _binding.ResetBindings(false);
        _binding.Position = CurrentComponent.Conditions.Count - 1;
        RefreshValidation();
    }

    private void OnRemove()
    {
        // Never read BindingSource.Current on an empty list. Removing by position
        // also supports a null rule identified by syntax validation.
        var index = _binding.Position;
        if (CurrentComponent?.Conditions is { } conditions && index >= 0 && index < conditions.Count)
        {
            conditions.RemoveAt(index);
            _binding.ResetBindings(false);
            RefreshValidation();
        }
    }

    private void OnTestAll()
    {
        if (CurrentComponent == null) return;
        bool ok = ComponentSelection.IsAvailable(CurrentComponent);
        var issues = ConditionListValidator.Validate(CurrentComponent.Conditions);
        var errors = issues.Count(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
        var warnings = issues.Count - errors;
        MessageBox.Show(this,
            $"{L("Conditions_Evaluation", "Evaluation")}: {(ok ? L("Conditions_Pass", "PASS") : L("Conditions_Fail", "FAIL"))}\n" +
            $"{L("Conditions_CountLabel", "Conditions")}: {CurrentComponent.Conditions.Count}\n" +
            $"{L("Conditions_ErrorsLabel", "Validation errors")}: {errors}, {L("Conditions_WarningsLabel", "warnings")}: {warnings}",
            L("Conditions_TestAll", "Test all"), MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OnOk()
    {
        PackageBuilderForm.CommitBindings(this);
        for (var componentIndex = 0; componentIndex < _components.Count; componentIndex++)
        {
            var component = _components[componentIndex];
            var error = ConditionListValidator.Validate(component.Conditions)
                .FirstOrDefault(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
            if (error is null) continue;
            _saveRejected = true;
            _componentBox.SelectedIndex = componentIndex;
            _binding.Position = error.Index;
            if (error.Index >= 0 && error.Index < _grid.Rows.Count)
                _grid.CurrentCell = _grid.Rows[error.Index].Cells[0];
            RefreshValidation();
            // Current blocking rules are either a missing Value or a null row. Keep the
            // latter selected for removal; do not infer a field from translated prose.
            if (component.Conditions[error.Index] is not null) _valueBox.Focus();
            else _grid.Focus();
            return;
        }
        if (ApplyDraft?.Invoke() == false) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RefreshValidation()
    {
        if (_disposed || _validationLabel is null || _previewBox is null) return;
        if (CurrentComponent == null) { _validationLabel.Text = _validationLabel.AccessibleName = ""; _previewBox.Text = ""; return; }
        var issues = ConditionListValidator.Validate(CurrentComponent.Conditions);
        Ui.ValidationCounts
            .From(issues, i => i.Severity == ConditionListValidator.IssueSeverity.Error)
            .Apply(_validationLabel, $"{L("Common_Component", "Component:")} {CurrentComponent.Id} —",
                string.Format(LanguageManager.CurrentCulture, "{0}: {1:N0} — {2}",
                    L("Conditions_CountLabel", "Conditions"), CurrentComponent.Conditions.Count, L("Common_OK", "OK")));
        if (_saveRejected)
        {
            var error = issues.FirstOrDefault(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
            if (error is not null)
                _validationLabel.Text = _validationLabel.AccessibleName =
                    L("Conditions_CannotSave", "Cannot save — fix the following errors first:") +
                    $" {CurrentComponent.Id} — #{error.Index + 1}: {error.Message}";
        }
        AuthoringReportPresentation.UpdateText(_previewBox, ConditionPreviewPresentation.Render(CurrentComponent, issues));
    }

    public static string BuildConditionPreview(InstallComponent component, System.Collections.Generic.IReadOnlyList<ConditionListValidator.Issue>? issues = null)
    {
        if (component.Conditions == null || component.Conditions.Count == 0)
            return "No component gates. This component is available whenever its install type/profile allows it.";

        var lines = new System.Collections.Generic.List<string>
        {
            $"Expression: {component.ConditionExpression}",
            component.ConditionExpression switch
            {
                ConditionExpressionMode.Any => "Meaning: install/offer this component when at least one rule passes.",
                ConditionExpressionMode.Not => "Meaning: install/offer this component when the complete rule group does not pass.",
                _ => "Meaning: install/offer this component only when every rule passes."
            },
            ""
        };

        for (var i = 0; i < component.Conditions.Count; i++)
            lines.Add($"{i + 1}. {Describe(component.Conditions[i])}");

        if (issues?.Count > 0)
        {
            lines.Add("");
            lines.Add("Issues:");
            foreach (var issue in issues)
                lines.Add($"- {issue.Severity} #{issue.Index + 1}: {issue.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Describe(InstallCondition condition)
    {
        var op = string.IsNullOrWhiteSpace(condition.Operator) ? "==" : condition.Operator.Trim();
        return condition.Type switch
        {
            ConditionType.AlwaysTrue => "Always true",
            ConditionType.AlwaysFalse => "Always false",
            ConditionType.IsAdmin => "Current installer session is elevated/admin",
            ConditionType.Architecture => $"Machine architecture {op} {condition.Value}",
            ConditionType.OsVersion => $"Windows version {op} {condition.Value}",
            ConditionType.FileExists => $"File exists: {condition.Value}",
            ConditionType.DirectoryExists => $"Directory exists: {condition.Value}",
            ConditionType.RegistryExists => $"Registry key exists: {condition.Value}",
            ConditionType.RegistryValue => $"Registry value {condition.Value} {op} {condition.Value2}",
            ConditionType.CommandReturns => $"Command `{condition.Value}` output/exit {op} {condition.Value2}",
            _ => $"{condition.Type} {op} {condition.Value}"
        };
    }
}
