using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Modal editor for component <see cref="InstallCondition"/> lists.
/// Uses WinForms data binding: DataGridView binds via BindingSource, editor fields via DataBindings.Add.
/// </summary>
public class ComponentConditionsDialog : Form
{
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
        _binding = new BindingSource();
        _binding.ListChanged += (_, _) => RefreshValidation();

        Text = L("Conditions_ComponentConditionBuilder", "Component Condition Builder");
        Size = new Size(1040, 640);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        selector.Controls.Add(new Label { Text = L("Common_Component", "Component:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _componentBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _components) _componentBox.Items.Add(c.Id + " — " + c.Name);
        _componentBox.SelectedIndexChanged += (_, _) => Rebind();
        selector.Controls.Add(_componentBox, 1, 0);
        selector.Controls.Add(new Label { Text = L("Conditions_GroupExpression", "Group expression:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, 0);
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
        selector.Controls.Add(_expressionBox, 3, 0);
        root.Controls.Add(selector, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildListPanel());
        split.Panel2.Controls.Add(BuildEditorPanel());
        root.Controls.Add(split, 0, 1);

        _validationLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.DarkGreen, Padding = new Padding(8, 0, 0, 0) };
        root.Controls.Add(_validationLabel, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = new Button { Text = L("Common_OK", "OK"), Width = 90, Height = 28 };
        _okBtn.Click += (_, _) => OnOk();
        _cancelBtn = new Button { Text = L("Common_Cancel", "Cancel"), Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
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
    }

    private Control BuildListPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
        for (var i = 0; i < 6; i++)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
        _addBtn = new Button { Text = L("Common_Add", "Add"), Dock = DockStyle.Fill };
        var x64Btn = new Button { Text = "x64 rule", Dock = DockStyle.Fill };
        var adminBtn = new Button { Text = "Admin rule", Dock = DockStyle.Fill };
        var fileBtn = new Button { Text = "File rule", Dock = DockStyle.Fill };
        _removeBtn = new Button { Text = L("Common_Remove", "Remove"), Dock = DockStyle.Fill };
        _testBtn = new Button { Text = L("Conditions_TestAll", "Test all"), Dock = DockStyle.Fill };
        _addBtn.Click += (_, _) => OnAdd();
        x64Btn.Click += (_, _) => AddCondition(ConditionType.Architecture, "x64");
        adminBtn.Click += (_, _) => AddCondition(ConditionType.IsAdmin);
        fileBtn.Click += (_, _) => AddCondition(ConditionType.FileExists, "{app}\\marker.txt");
        _removeBtn.Click += (_, _) => OnRemove();
        _testBtn.Click += (_, _) => OnTestAll();
        row.Controls.Add(_addBtn, 0, 0);
        row.Controls.Add(x64Btn, 1, 0);
        row.Controls.Add(adminBtn, 2, 0);
        row.Controls.Add(fileBtn, 3, 0);
        row.Controls.Add(_removeBtn, 4, 0);
        row.Controls.Add(_testBtn, 5, 0);
        panel.Controls.Add(_grid, 0, 0);
        panel.Controls.Add(row, 0, 1);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Common_Type", "Type:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _typeBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues(typeof(ConditionType))) _typeBox.Items.Add(t);
        _typeBox.DataBindings.Add("SelectedItem", _binding, nameof(InstallCondition.Type), true, DataSourceUpdateMode.OnPropertyChanged);
        _typeBox.SelectedValueChanged += (_, _) => RefreshEditorEnabled();
        layout.Controls.Add(_typeBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Common_Value", "Value:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _valueBox = new TextBox { Dock = DockStyle.Fill };
        _valueBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_valueBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Conditions_Operator", "Operator:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _operatorBox = new TextBox { Dock = DockStyle.Fill, Text = "==" };
        _operatorBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Operator), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_operatorBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Conditions_Value2", "Value2:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _value2Box = new TextBox { Dock = DockStyle.Fill };
        _value2Box.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value2), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_value2Box, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.Controls.Add(new Label { Text = L("Conditions_Preview", "Preview:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window
        };
        layout.Controls.Add(_previewBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var notes = new Label
        {
            Text = L("Conditions_NotesNGroupExpression", "Notes:\n • Group expression controls how all rows combine: All = every rule, Any = at least one rule, Not = invert the group.\n • Value holds the primary operand (registry path, file path, OS version, command, …).\n • Value2 is the expected value for RegistryValue and CommandReturns comparisons.\n • Operator is one of ==, =, !=, >, >=, <, <= (default '==')."),
            Dock = DockStyle.Fill,
            ForeColor = Ui.InstallerTheme.MutedText
        };
        layout.Controls.Add(notes, 0, row);
        layout.SetColumnSpan(notes, 2);
        return layout;
    }

    private InstallComponent? CurrentComponent
        => _componentBox.SelectedIndex >= 0 && _componentBox.SelectedIndex < _components.Count
            ? _components[_componentBox.SelectedIndex] : null;

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
        var needsValue2 = _typeBox.SelectedItem is ConditionType t2 && t2 == ConditionType.RegistryValue;
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
        if (CurrentComponent?.Conditions != null && _binding.Current is InstallCondition c)
            CurrentComponent.Conditions.Remove(c);
    }

    private void OnTestAll()
    {
        if (CurrentComponent == null) return;
        bool ok = ComponentSelection.IsAvailable(CurrentComponent);
        var issues = ConditionListValidator.Validate(CurrentComponent.Conditions);
        var errors = issues.Count(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
        var warnings = issues.Count - errors;
        MessageBox.Show(this,
            $"Evaluation: {(ok ? "PASS" : "FAIL")}\n" +
            $"Conditions: {CurrentComponent.Conditions.Count}\n" +
            $"Validation errors: {errors}, warnings: {warnings}",
            "Test all", MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void OnOk()
    {
        var issues = CurrentComponent == null
            ? new System.Collections.Generic.List<ConditionListValidator.Issue>()
            : ConditionListValidator.Validate(CurrentComponent.Conditions).ToList();
        var errors = issues.Where(i => i.Severity == ConditionListValidator.IssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var msg = "Cannot save — fix the following errors first:\n\n" + string.Join("\n", errors.Select(e => $" • #{e.Index + 1}: {e.Message}"));
            MessageBox.Show(this, msg, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RefreshValidation()
    {
        if (CurrentComponent == null) { _validationLabel.Text = ""; _previewBox.Text = ""; return; }
        var issues = ConditionListValidator.Validate(CurrentComponent.Conditions);
        var errors = issues.Count(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
        var warnings = issues.Count - errors;
        _validationLabel.Text = errors == 0 && warnings == 0
            ? $"Component '{CurrentComponent.Id}' — {CurrentComponent.Conditions.Count} condition(s) — OK"
            : $"Component '{CurrentComponent.Id}' — {errors} error(s), {warnings} warning(s).";
        _validationLabel.ForeColor = errors > 0 ? Color.DarkRed : (warnings > 0 ? Color.DarkOrange : Color.DarkGreen);
        _previewBox.Text = BuildConditionPreview(CurrentComponent, issues);
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
