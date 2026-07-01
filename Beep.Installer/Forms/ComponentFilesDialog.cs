using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Forms;

/// <summary>Modal dialog for editing the file list of a single <see cref="InstallComponent"/>.</summary>
public class ComponentFilesDialog : Form
{
    private readonly ListView _list = null!;
    private readonly TextBox _sourceBox = null!;
    private readonly TextBox _destBox = null!;
    private readonly CheckBox _overwriteCheck = null!;
    private readonly CheckBox _skipIfNewerCheck = null!;
    private readonly CheckBox _requiredCheck = null!;
    private readonly TextBox _descriptionBox = null!;
    private readonly Button _browseBtn = null!;
    private readonly Button _addBtn = null!;
    private readonly Button _updateBtn = null!;
    private readonly Button _removeBtn = null!;
    private readonly Button _okBtn = null!;
    private readonly Button _cancelBtn = null!;
    private readonly Label _sizeLabel = null!;

    public List<FileCopyOperation> ResultFiles { get; private set; } = new();
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
        AutoScaleDimensions = new SizeF(96F, 96F);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };

        // Top: file list
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            AllowDrop = true
        };
        _list.Columns.Add("Source", 200);
        _list.Columns.Add("Destination", 140);
        _list.Columns.Add("Description", 120);
        _list.Columns.Add("Overwrite", 70);
        _list.Columns.Add("Newer-only", 70);
        _list.Columns.Add("Size", 80);
        _list.SelectedIndexChanged += (_, _) => BindFromSelection();
        _list.DragEnter += (_, e) => { e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None; };
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            int added = 0, skipped = 0;
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories))
                    {
                        if (TryAddFromPath(f, defaultSourceDir)) added++;
                        else skipped++;
                    }
                }
                else if (File.Exists(p))
                {
                    if (TryAddFromPath(p, defaultSourceDir)) added++;
                    else skipped++;
                }
            }
            if (added > 0) UpdateSize();
            if (skipped > 0) MessageBox.Show(this, $"{skipped} file(s) could not be added (outside source directory).", "Drag-drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        foreach (var f in component.Files)
            AddToList(f);

        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _removeBtn = new Button { Text = "Remove selected" };
        _removeBtn.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count == 0) return;
            var idx = _list.SelectedIndices[0];
            _list.Items.RemoveAt(idx);
            UpdateSize();
        };
        var dropHint = new Label
        {
            Text = "Tip: drag files or folders from Explorer onto the list above",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(4, 6, 0, 0)
        };
        topButtons.Controls.Add(_removeBtn);
        topButtons.Controls.Add(dropHint);
        split.Panel1.Controls.Add(_list);
        split.Panel1.Controls.Add(topButtons);

        // Bottom: editor
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        editor.Controls.Add(new Label { Text = "Source path:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _sourceBox = new TextBox { Dock = DockStyle.Fill };
        _browseBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        _browseBtn.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { InitialDirectory = defaultSourceDir };
            if (d.ShowDialog(this) == DialogResult.OK) _sourceBox.Text = d.FileName;
        };
        editor.Controls.Add(_sourceBox, 1, r);
        editor.Controls.Add(_browseBtn, 2, r);
        r++;

        editor.Controls.Add(new Label { Text = "Destination:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _destBox = new TextBox { Dock = DockStyle.Fill };
        editor.SetColumnSpan(_destBox, 2);
        editor.Controls.Add(_destBox, 1, r);
        r++;

        editor.Controls.Add(new Label { Text = "Description:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _descriptionBox = new TextBox { Dock = DockStyle.Fill };
        editor.SetColumnSpan(_descriptionBox, 2);
        editor.Controls.Add(_descriptionBox, 1, r);
        r++;

        _overwriteCheck = new CheckBox { Text = "Overwrite existing", Checked = true, Dock = DockStyle.Fill };
        _skipIfNewerCheck = new CheckBox { Text = "Skip if newer", Dock = DockStyle.Fill };
        _requiredCheck = new CheckBox { Text = "Required file", Checked = true, Dock = DockStyle.Fill };
        editor.SetColumnSpan(_overwriteCheck, 2); editor.Controls.Add(_overwriteCheck, 1, r); r++;
        editor.SetColumnSpan(_skipIfNewerCheck, 2); editor.Controls.Add(_skipIfNewerCheck, 1, r); r++;
        editor.SetColumnSpan(_requiredCheck, 2); editor.Controls.Add(_requiredCheck, 1, r); r++;

        _addBtn = new Button { Text = "Add" };
        _updateBtn = new Button { Text = "Update selected" };
        _addBtn.Click += (_, _) => AddFromEditor();
        _updateBtn.Click += (_, _) => UpdateFromEditor();
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 32 };
        btnRow.Controls.AddRange(new Control[] { _addBtn, _updateBtn });
        editor.SetColumnSpan(btnRow, 3);
        editor.Controls.Add(btnRow, 0, r); r++;

        _sizeLabel = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };
        editor.SetColumnSpan(_sizeLabel, 3);
        editor.Controls.Add(_sizeLabel, 0, r);

        split.Panel2.Controls.Add(editor);

        // Bottom OK / Cancel
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = new Button { Text = "OK", Width = 90, Height = 28, DialogResult = DialogResult.OK };
        _cancelBtn = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        bottom.Controls.AddRange(new Control[] { _okBtn, _cancelBtn });

        _okBtn.Click += (_, _) => { ResultFiles = CollectFromList(); DialogResult = DialogResult.OK; Close(); };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(split);
        Controls.Add(bottom);
        UpdateSize();
    }

    private void AddToList(FileCopyOperation f)
    {
        var lvi = new ListViewItem(f.SourcePath);
        lvi.SubItems.Add(f.DestinationPath);
        lvi.SubItems.Add(f.Description);
        lvi.SubItems.Add(f.Overwrite ? "yes" : "no");
        lvi.SubItems.Add(f.SkipIfNewer ? "yes" : "no");
        lvi.SubItems.Add(FormatSize(f.SourcePath));
        lvi.Tag = f;
        _list.Items.Add(lvi);
    }

    /// <summary>Add a file dropped onto the list, computing a destination relative to the project source dir.</summary>
    private bool TryAddFromPath(string filePath, string? defaultSourceDir)
    {
        try
        {
            string dest;
            if (!string.IsNullOrEmpty(defaultSourceDir) &&
                filePath.StartsWith(defaultSourceDir, StringComparison.OrdinalIgnoreCase))
            {
                dest = Path.GetRelativePath(defaultSourceDir, filePath);
            }
            else
            {
                // Outside the project source dir — fall back to flat layout
                dest = Path.GetFileName(filePath);
            }
            var f = new FileCopyOperation
            {
                SourcePath = filePath,
                DestinationPath = dest,
                Description = Path.GetFileName(filePath),
                Overwrite = true,
                IsRequired = false
            };
            AddToList(f);
            return true;
        }
        catch { return false; }
    }

    private static string FormatSize(string path)
    {
        try { return File.Exists(path) ? $"{new FileInfo(path).Length / 1024.0:F0} KB" : "—"; }
        catch { return "—"; }
    }

    private void UpdateSize()
    {
        long total = 0;
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is FileCopyOperation f && File.Exists(f.SourcePath))
                total += new FileInfo(f.SourcePath).Length;
        }
        _sizeLabel.Text = $"Total: {total / 1024.0 / 1024.0:F2} MB across {_list.Items.Count} file(s).";
    }

    private void BindFromSelection()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not FileCopyOperation f) return;
        _sourceBox.Text = f.SourcePath;
        _destBox.Text = f.DestinationPath;
        _descriptionBox.Text = f.Description;
        _overwriteCheck.Checked = f.Overwrite;
        _skipIfNewerCheck.Checked = f.SkipIfNewer;
        _requiredCheck.Checked = f.IsRequired;
    }

    private void AddFromEditor()
    {
        if (string.IsNullOrWhiteSpace(_sourceBox.Text)) { MessageBox.Show(this, "Source path required."); return; }
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
        AddToList(f);
        UpdateSize();
    }

    private void UpdateFromEditor()
    {
        if (_list.SelectedItems.Count == 0) { AddFromEditor(); return; }
        if (_list.SelectedItems[0].Tag is not FileCopyOperation f) return;
        f.SourcePath = _sourceBox.Text.Trim();
        f.DestinationPath = _destBox.Text.Trim();
        f.Description = _descriptionBox.Text.Trim();
        f.Overwrite = _overwriteCheck.Checked;
        f.SkipIfNewer = _skipIfNewerCheck.Checked;
        f.IsRequired = _requiredCheck.Checked;
        var idx = _list.SelectedIndices[0];
        _list.Items.RemoveAt(idx);
        AddToListAt(f, idx);
        _list.Items[idx].Selected = true;
        UpdateSize();
    }

    private void AddToListAt(FileCopyOperation f, int index)
    {
        var lvi = new ListViewItem(f.SourcePath);
        lvi.SubItems.Add(f.DestinationPath);
        lvi.SubItems.Add(f.Description);
        lvi.SubItems.Add(f.Overwrite ? "yes" : "no");
        lvi.SubItems.Add(f.SkipIfNewer ? "yes" : "no");
        lvi.SubItems.Add(FormatSize(f.SourcePath));
        lvi.Tag = f;
        _list.Items.Insert(index, lvi);
    }

    private List<FileCopyOperation> CollectFromList()
    {
        var list = new List<FileCopyOperation>();
        foreach (ListViewItem item in _list.Items)
            if (item.Tag is FileCopyOperation f) list.Add(f);
        return list;
    }
}
