using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer.Steps;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Modal editor for <see cref="CustomAction"/> lists. Uses WinForms data binding:
/// DataGridView binds via BindingSource, editor fields bind via DataBindings.Add,
/// changes flow both ways through INotifyPropertyChanged.
/// </summary>
public class CustomActionsDialog : Form
{
    private DataGridView _grid = null!;
    private TextBox _pathBox = null!;
    private TextBox _argsBox = null!;
    private TextBox _workDirBox = null!;
    private ComboBox _timingBox = null!;
    private NumericUpDown _orderBox = null!;
    private NumericUpDown _timeoutBox = null!;
    private CheckBox _requiredCheck = null!;
    private CheckBox _failOnErrorCheck = null!;
    private TextBox _descriptionBox = null!;
    private Button _addBtn = null!;
    private Button _removeBtn = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Label _validationLabel = null!;
    private BindingSource _binding = null!;
    private BindingList<CustomAction> _actions = null!;

    public CustomActionsDialog(System.Collections.IList actions)
    {
        _actions = new BindingList<CustomAction>();
        foreach (CustomAction a in actions) _actions.Add(a);

        Text = L("Actions_CustomActionsA11", "Custom Actions (A1.1)");
        Size = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380, FixedPanel = FixedPanel.Panel1 };
        Controls.Add(split);

        split.Panel1.Controls.Add(BuildListPanel());
        split.Panel2.Controls.Add(BuildEditorPanel());
        BuildBottomBar();

        _binding.CurrentChanged += (_, _) => BindEditorToCurrent();

        Load += (_, _) => ValidateAll();
        Engine.Accessibility.Attach(this);
    }

    private Control BuildListPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _binding = new BindingSource { DataSource = _actions };

        _grid = new DataGridView
        {
            AccessibleName = L("Actions_GridName", "Custom actions"),
            Dock = DockStyle.Fill,
            DataSource = _binding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        // After auto-generation, hide the INotifyPropertyChanged-bound list column
        if (_grid.Columns["SizeBytes"] is { } sizeColumn) sizeColumn.Visible = false;

        var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        _addBtn = new Button { Text = L("Common_Add", "Add"), Dock = DockStyle.Fill };
        _removeBtn = new Button { Text = L("Common_Remove", "Remove"), Dock = DockStyle.Fill };
        _okBtn = new Button { Text = L("Common_OK", "OK"), Dock = DockStyle.Fill, DialogResult = DialogResult.None };
        _addBtn.Click += (_, _) => OnAdd();
        _removeBtn.Click += (_, _) => OnRemove();
        _okBtn.Click += (_, _) => OnOk();
        buttonRow.Controls.Add(_addBtn, 0, 0);
        buttonRow.Controls.Add(_removeBtn, 1, 0);
        buttonRow.Controls.Add(_okBtn, 2, 0);

        panel.Controls.Add(_grid, 0, 0);
        panel.Controls.Add(buttonRow, 0, 1);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Actions_Path", "Path:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _pathBox = new TextBox { Dock = DockStyle.Fill };
        _pathBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Path), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_pathBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Actions_Arguments", "Arguments:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _argsBox = new TextBox { Dock = DockStyle.Fill };
        _argsBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Arguments), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_argsBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Actions_WorkingDir", "Working dir:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _workDirBox = new TextBox { Dock = DockStyle.Fill };
        _workDirBox.DataBindings.Add("Text", _binding, nameof(CustomAction.WorkingDirectory), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_workDirBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Actions_Timing", "Timing:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _timingBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _timingBox.Items.AddRange(new object[] {
            CustomActionTiming.BeforeInstall, CustomActionTiming.AfterInstall,
            CustomActionTiming.BeforeUninstall, CustomActionTiming.AfterUninstall });
        _timingBox.DataBindings.Add("SelectedItem", _binding, nameof(CustomAction.Timing), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_timingBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Common_Order", "Order:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _orderBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = int.MinValue, Maximum = int.MaxValue };
        _orderBox.DataBindings.Add("Value", _binding, nameof(CustomAction.Order), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_orderBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = L("Actions_TimeoutMs", "Timeout (ms):"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _timeoutBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
        _timeoutBox.DataBindings.Add("Value", _binding, nameof(CustomAction.TimeoutMs), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_timeoutBox, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var flagsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _requiredCheck = new CheckBox { Text = L("Actions_RequiredFailInstall", "Required (fail install)"), AutoSize = true };
        _requiredCheck.DataBindings.Add("Checked", _binding, nameof(CustomAction.Required), true, DataSourceUpdateMode.OnPropertyChanged);
        _failOnErrorCheck = new CheckBox { Text = L("Actions_FailOnError", "Fail on error"), AutoSize = true, Checked = true, Margin = new Padding(12, 3, 0, 0) };
        _failOnErrorCheck.DataBindings.Add("Checked", _binding, nameof(CustomAction.FailOnError), true, DataSourceUpdateMode.OnPropertyChanged);
        flagsRow.Controls.Add(_requiredCheck);
        flagsRow.Controls.Add(_failOnErrorCheck);
        layout.Controls.Add(flagsRow, 1, row++);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(new Label { Text = L("Common_Description", "Description:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Top }, 0, row);
        _descriptionBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
        _descriptionBox.DataBindings.Add("Text", _binding, nameof(CustomAction.Description), true, DataSourceUpdateMode.OnPropertyChanged);
        layout.Controls.Add(_descriptionBox, 1, row++);

        return layout;
    }

    private void BuildBottomBar()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, ColumnCount = 1 };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _validationLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.DarkRed, Padding = new Padding(8, 0, 0, 0) };
        bar.Controls.Add(_validationLabel, 0, 0);

        _actions.ListChanged += (_, _) => ValidateAll();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 200, FlowDirection = FlowDirection.RightToLeft };
        _cancelBtn = new Button { Text = L("Common_Cancel", "Cancel"), Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var closeBtn = new Button { Text = L("Common_OK", "OK"), Width = 90, Height = 28 };
        closeBtn.Click += (_, _) => OnOk();
        buttons.Controls.Add(closeBtn);
        buttons.Controls.Add(_cancelBtn);
        bar.Controls.Add(buttons, 0, 0);
        AcceptButton = closeBtn;
        CancelButton = _cancelBtn;
        var host = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        host.Controls.Add(bar);
        Controls.Add(host);
    }

    private void BindEditorToCurrent()
    {
        // The DataBindings on each editor control handle population and writeback.
        // Enable/disable editors based on whether a row is selected.
        bool hasCurrent = _binding.Current != null;
        _pathBox.Enabled = hasCurrent;
        _argsBox.Enabled = hasCurrent;
        _workDirBox.Enabled = hasCurrent;
        _timingBox.Enabled = hasCurrent;
        _orderBox.Enabled = hasCurrent;
        _timeoutBox.Enabled = hasCurrent;
        _requiredCheck.Enabled = hasCurrent;
        _failOnErrorCheck.Enabled = hasCurrent;
        _descriptionBox.Enabled = hasCurrent;
    }

    private void OnAdd()
    {
        var a = new CustomAction { Path = "command.exe", Required = true, Timing = CustomActionTiming.AfterInstall };
        _actions.Add(a);
        _binding.Position = _actions.Count - 1;
    }

    private void OnRemove()
    {
        if (_binding.Current is CustomAction a) _actions.Remove(a);
    }

    private void OnOk()
    {
        var issues = InstallProject.ValidateCustomActions(_actions).ToList();
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            var msg = "Cannot save - fix the following errors first:\n\n" + string.Join("\n", errors.Select(e => " - " + e.Key + ": " + e.Message));
            MessageBox.Show(this, msg, L("Common_Validation", "Validation"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var warnings = issues.Where(i => !i.IsError).ToList();
        if (warnings.Count > 0)
        {
            var msg = "There are warnings. Save anyway?\n\n" + string.Join("\n", warnings.Select(w => " - " + w.Key + ": " + w.Message));
            if (MessageBox.Show(this, msg, L("Common_Warnings", "Warnings"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ValidateAll()
    {
        var issues = InstallProject.ValidateCustomActions(_actions);
        var errors = issues.Count(i => i.IsError);
        var warnings = issues.Count - errors;
        _validationLabel.Text = errors == 0 && warnings == 0
            ? $"{_actions.Count} action(s) - OK"
            : $"{_actions.Count} action(s) - {errors} error(s), {warnings} warning(s).";
        _validationLabel.ForeColor = errors > 0 ? Color.DarkRed : (warnings > 0 ? Color.DarkOrange : Color.DarkGreen);
    }
}
