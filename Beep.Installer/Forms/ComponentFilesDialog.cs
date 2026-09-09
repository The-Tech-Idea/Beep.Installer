using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using TheTechIdea.Beep.Installer;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>Modal dialog for editing the file list of a single <see cref="InstallComponent"/>.
/// Uses WinForms data binding: DataGridView binds via BindingSource, editor fields bind via DataBindings.Add.</summary>
public class ComponentFilesDialog : Form
{
    private DataGridView _grid = null!;
    private TextBox _sourceBox = null!;
    private TextBox _destBox = null!;
    private TextBox _descriptionBox = null!;
    private CheckBox _overwriteCheck = null!;
    private CheckBox _skipIfNewerCheck = null!;
    private CheckBox _requiredCheck = null!;
    private Button _addBtn = null!;
    private Button _removeBtn = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Label _sizeLabel = null!;
    private BindingSource _binding = null!;
    private BindingList<FileCopyOperation> _files = null!;

    public System.Collections.Generic.IList<FileCopyOperation> ResultFiles => _files;
    private readonly string? _defaultSourceDir;

    public ComponentFilesDialog(InstallComponent component, string? defaultSourceDir)
    {
        _defaultSourceDir = defaultSourceDir;
        Text = $"Edit files — {component.Name}";
        Size = new Size(720, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(600, 400);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);

        _files = new BindingList<FileCopyOperation>(component.Files.ToList());
        _files.ListChanged += (_, _) => UpdateSize();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };

        _binding = new BindingSource { DataSource = _files };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _binding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };

        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _removeBtn = new Button { Text = L("Files_RemoveSelected", "Remove selected") };
        _removeBtn.Click += (_, _) =>
        {
            if (_binding.Current is FileCopyOperation f) _files.Remove(f);
        };
        var dropHint = new Label
        {
            Text = L("Files_TipDragFilesOr", "Tip: drag files or folders from Explorer onto the list above"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(4, 6, 0, 0)
        };
        topButtons.Controls.Add(_removeBtn);
        topButtons.Controls.Add(dropHint);
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Controls.Add(topButtons);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        editor.Controls.Add(new Label { Text = L("Files_SourcePath", "Source path:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _sourceBox = new TextBox { Dock = DockStyle.Fill };
        _sourceBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.SourcePath), true, DataSourceUpdateMode.OnPropertyChanged);
        var browseBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        browseBtn.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { InitialDirectory = defaultSourceDir };
            if (d.ShowDialog(this) == DialogResult.OK) _sourceBox.Text = d.FileName;
        };
        editor.Controls.Add(_sourceBox, 1, r);
        editor.Controls.Add(browseBtn, 2, r);
        r++;

        editor.Controls.Add(new Label { Text = L("Files_Destination", "Destination:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _destBox = new TextBox { Dock = DockStyle.Fill };
        _destBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.DestinationPath), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_destBox, 2);
        editor.Controls.Add(_destBox, 1, r);
        r++;

        editor.Controls.Add(new Label { Text = L("Common_Description", "Description:"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _descriptionBox = new TextBox { Dock = DockStyle.Fill };
        _descriptionBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.Description), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_descriptionBox, 2);
        editor.Controls.Add(_descriptionBox, 1, r);
        r++;

        _overwriteCheck = new CheckBox { Text = L("Files_OverwriteExisting", "Overwrite existing"), Dock = DockStyle.Fill, Checked = true };
        _overwriteCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.Overwrite), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_overwriteCheck, 2); editor.Controls.Add(_overwriteCheck, 1, r); r++;
        _skipIfNewerCheck = new CheckBox { Text = L("Files_SkipIfNewer", "Skip if newer"), Dock = DockStyle.Fill };
        _skipIfNewerCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.SkipIfNewer), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_skipIfNewerCheck, 2); editor.Controls.Add(_skipIfNewerCheck, 1, r); r++;
        _requiredCheck = new CheckBox { Text = L("Files_RequiredFile", "Required file"), Dock = DockStyle.Fill, Checked = true };
        _requiredCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.IsRequired), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_requiredCheck, 2); editor.Controls.Add(_requiredCheck, 1, r); r++;

        _addBtn = new Button { Text = L("Common_Add", "Add") };
        _addBtn.Click += (_, _) => AddFromEditor();
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 32 };
        btnRow.Controls.Add(_addBtn);
        editor.SetColumnSpan(btnRow, 3);
        editor.Controls.Add(btnRow, 0, r); r++;

        _sizeLabel = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };
        editor.SetColumnSpan(_sizeLabel, 3);
        editor.Controls.Add(_sizeLabel, 0, r);

        split.Panel2.Controls.Add(editor);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = new Button { Text = L("Common_OK", "OK"), Width = 90, Height = 28, DialogResult = DialogResult.OK };
        _cancelBtn = new Button { Text = L("Common_Cancel", "Cancel"), Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        bottom.Controls.AddRange(new Control[] { _okBtn, _cancelBtn });
        _okBtn.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(split);
        Controls.Add(bottom);
        UpdateSize();
        Engine.Accessibility.Attach(this);
    }

    private void AddFromEditor()
    {
        if (string.IsNullOrWhiteSpace(_sourceBox.Text)) { MessageBox.Show(this, L("Files_SourceRequired", "Source path required.")); return; }
        var f = new FileCopyOperation
        {
            SourcePath = _sourceBox.Text.Trim(),
            DestinationPath = string.IsNullOrWhiteSpace(_destBox.Text)
                ? Path.GetFileName(_sourceBox.Text)
                : _destBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            Overwrite = _overwriteCheck.Checked,
            SkipIfNewer = _skipIfNewerCheck.Checked,
            IsRequired = _requiredCheck.Checked
        };
        _files.Add(f);
    }

    private void UpdateSize()
    {
        long total = 0;
        foreach (var f in _files)
            if (File.Exists(f.SourcePath)) total += new FileInfo(f.SourcePath).Length;
        _sizeLabel.Text = $"Total: {total / 1024.0 / 1024.0:F2} MB across {_files.Count} file(s).";
    }
}
