using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Forms;

/// <summary>
/// Modal editor for component <see cref="InstallCondition"/> lists.
/// Uses WinForms data binding: DataGridView binds via BindingSource, editor fields via DataBindings.Add.
/// </summary>
public class ComponentConditionsDialog : Form
{
    private readonly System.Collections.Generic.List<InstallComponent> _components;
    private ComboBox _componentBox = null!;
    private DataGridView _grid = null!;
    private ComboBox _typeBox = null!;
    private TextBox _valueBox = null!;
    private TextBox _value2Box = null!;
    private TextBox _operatorBox = null!;
    private Button _addBtn = null!;
    private Button _removeBtn = null!;
    private Button _testBtn = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Label _validationLabel = null!;
    private BindingSource _binding = null!;

    public ComponentConditionsDialog(System.Collections.IList components)
    {
        _components = (components ?? new System.Collections.Generic.List<InstallComponent>()).Cast<InstallComponent>().ToList();
        Text = "Component Conditions (A3.4)";
        Size = new Size(900, 540);
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

        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selector.Controls.Add(new Label { Text = "Component:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _componentBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in _components) _componentBox.Items.Add(c.Id + " — " + c.Name);
        _componentBox.SelectedIndexChanged += (_, _) => Rebind();
        selector.Controls.Add(_componentBox, 1, 0);
        root.Controls.Add(selector, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildListPanel());
        split.Panel2.Controls.Add(BuildEditorPanel());
        root.Controls.Add(split, 0, 1);

        _validationLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.DarkGreen, Padding = new Padding(8, 0, 0, 0) };
        root.Controls.Add(_validationLabel, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = new Button { Text = "OK", Width = 90, Height = 28 };
        _okBtn.Click += (_, _) => OnOk();
        _cancelBtn = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_okBtn);
        buttons.Controls.Add(_cancelBtn);
        root.Controls.Add(buttons, 0, 3);

        Load += (_, _) => { if (_componentBox.Items.Count > 0) _componentBox.SelectedIndex = 0; };
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

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        _addBtn = new Button { Text = "Add", Dock = DockStyle.Fill };
        _removeBtn = new Button { Text = "Remove", Dock = DockStyle.Fill };
        _testBtn = new Button { Text = "Test all", Dock = DockStyle.Fill };
        _addBtn.Click += (_, _) => OnAdd();
        _removeBtn.Click += (_, _) => OnRemove();
        _testBtn.Click += (_, _) => OnTestAll();
        row.Controls.Add(_addBtn, 0, 0);
        row.Controls.Add(_removeBtn, 1, 0);
        row.Controls.Add(_testBtn, 2, 0);
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
        layout.Controls.Add(new Label { Text = "Type:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _typeBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues(typeof(ConditionType))) _typeBox.Items.Add(t);
        _typeBox.DataBindings.Add("SelectedItem", _binding, nameof(InstallCondition.Type), true, DataSourceUpdateMode.OnPropertyChanged);
        _typeBox.SelectedValueChanged += (_, _) => RefreshEditorEnabled();
        layout.Controls.Add(_typeBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = "Value:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _valueBox = new TextBox { Dock = DockStyle.Fill };
        _valueBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_valueBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = "Operator:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _operatorBox = new TextBox { Dock = DockStyle.Fill, Text = "==" };
        _operatorBox.DataBindings.Add("Text", _binding, nameof(InstallCondition.Operator), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_operatorBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = "Value2:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _value2Box = new TextBox { Dock = DockStyle.Fill };
        _value2Box.DataBindings.Add("Text", _binding, nameof(InstallCondition.Value2), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_value2Box, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var notes = new Label
        {
            Text = "Notes:\n • Value holds the primary operand (registry path, file path, OS version, …).\n • Value2 is the expected value for RegistryValue comparisons.\n • Operator is one of ==, =, !=, >, >=, <, <= (default '==').",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray
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
        _binding.DataSource = c.Conditions;
        _binding.ListChanged += (_, _) => RefreshValidation();
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
    {
        if (CurrentComponent == null) return;
        CurrentComponent.Conditions ??= new System.Collections.Generic.List<InstallCondition>();
        var cond = new InstallCondition
        {
            Type = _typeBox.SelectedItem is ConditionType t ? t : ConditionType.AlwaysTrue,
            Value = string.IsNullOrWhiteSpace(_valueBox.Text) ? null : _valueBox.Text.Trim(),
            Value2 = string.IsNullOrWhiteSpace(_value2Box.Text) ? null : _value2Box.Text.Trim(),
            Operator = string.IsNullOrWhiteSpace(_operatorBox.Text) ? "==" : _operatorBox.Text.Trim()
        };
        CurrentComponent.Conditions.Add(cond);
    }

    private void OnRemove()
    {
        if (CurrentComponent?.Conditions != null && _binding.Current is InstallCondition c)
            CurrentComponent.Conditions.Remove(c);
    }

    private void OnTestAll()
    {
        if (CurrentComponent == null) return;
        bool ok = InstallConditionEvaluator.EvaluateAll(CurrentComponent.Conditions);
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
        if (CurrentComponent == null) { _validationLabel.Text = ""; return; }
        var issues = ConditionListValidator.Validate(CurrentComponent.Conditions);
        var errors = issues.Count(i => i.Severity == ConditionListValidator.IssueSeverity.Error);
        var warnings = issues.Count - errors;
        _validationLabel.Text = errors == 0 && warnings == 0
            ? $"Component '{CurrentComponent.Id}' — {CurrentComponent.Conditions.Count} condition(s) — OK"
            : $"Component '{CurrentComponent.Id}' — {errors} error(s), {warnings} warning(s).";
        _validationLabel.ForeColor = errors > 0 ? Color.DarkRed : (warnings > 0 ? Color.DarkOrange : Color.DarkGreen);
    }
}