using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

public sealed class TemplateUpdateDialog : Form
{
    private readonly Func<string, ProjectTemplateUpdatePreview> _previewFactory;
    private readonly ComboBox _templateBox;
    private readonly Label _summaryLabel;
    private readonly DataGridView _diffGrid;
    private readonly Button _applyButton;
    private ProjectTemplateUpdatePreview? _preview;

    public string SelectedTemplateId =>
        _templateBox.SelectedItem is ProjectTemplate template ? template.Id : ProjectTemplates.EmptyId;

    public ProjectTemplateUpdatePreview? Preview => _preview;

    public TemplateUpdateDialog(
        System.Collections.Generic.IEnumerable<ProjectTemplate> templates,
        Func<string, ProjectTemplateUpdatePreview> previewFactory)
    {
        _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));

        Text = L("Template_ApplyTemplateUpdate", "Apply Template Update");
        Size = new Size(980, 620);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        var selector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selector.Controls.Add(new Label { Text = L("Template_Template", "Template:"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _templateBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var template in templates.OrderBy(t => t.Category).ThenBy(t => t.Name))
            _templateBox.Items.Add(template);
        _templateBox.SelectedIndexChanged += (_, _) => RefreshPreview();
        selector.Controls.Add(_templateBox, 1, 0);
        root.Controls.Add(selector, 0, 0);

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.ControlText,
            Padding = new Padding(0, 6, 0, 6)
        };
        root.Controls.Add(_summaryLabel, 0, 1);

        _diffGrid = new DataGridView
        {
            AccessibleName = L("Template_GridName", "Differences this update would apply"),
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _diffGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L("Template_Path", "Path"), DataPropertyName = nameof(ProjectTemplateDiffEntry.Path), FillWeight = 36 });
        _diffGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L("Template_Current", "Current"), DataPropertyName = nameof(ProjectTemplateDiffEntry.CurrentValue), FillWeight = 32 });
        _diffGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L("Template_Updated", "Updated"), DataPropertyName = nameof(ProjectTemplateDiffEntry.UpdatedValue), FillWeight = 32 });
        root.Controls.Add(_diffGrid, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _applyButton = new Button { Text = L("Template_Apply", "Apply"), Width = 96, Height = 28, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = L("Common_Cancel", "Cancel"), Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 3);

        AcceptButton = _applyButton;
        CancelButton = cancelButton;

        Load += (_, _) =>
        {
            if (_templateBox.Items.Count > 0)
                _templateBox.SelectedIndex = 0;
            else
                RefreshPreview();
        };
        Engine.Accessibility.Attach(this);
    }

    private void RefreshPreview()
    {
        if (_templateBox.SelectedItem is not ProjectTemplate template)
        {
            _preview = null;
            _summaryLabel.Text = L("Template_NoTemplatesAreAvailable", "No templates are available.");
            _diffGrid.DataSource = Array.Empty<ProjectTemplateDiffEntry>();
            _applyButton.Enabled = false;
            return;
        }

        try
        {
            _preview = _previewFactory(template.Id);
            _summaryLabel.Text =
                $"Current plan hash: {DisplayHash(_preview.Current.PlanHash)}{Environment.NewLine}" +
                $"Updated plan hash: {DisplayHash(_preview.Updated.PlanHash)} · Changes: {_preview.Diff.Count} · " +
                $"Diagnostics: {_preview.Updated.Diagnostics.Count}";
            _diffGrid.DataSource = _preview.Diff;
            _applyButton.Enabled = _preview.HasChanges && !_preview.Updated.HasErrors;
        }
        catch (Exception ex)
        {
            _preview = null;
            _summaryLabel.Text = L("Template_TemplatePreviewFailed", "Template preview failed: ") + ex.Message;
            _diffGrid.DataSource = Array.Empty<ProjectTemplateDiffEntry>();
            _applyButton.Enabled = false;
        }
    }

    private static string DisplayHash(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "(not available)"
            : value.Length <= 16 ? value : value[..16] + "…";
}
