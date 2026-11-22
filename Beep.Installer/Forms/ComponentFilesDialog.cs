using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using TheTechIdea.Beep.Installer;
using Beep.Installer.Lang;
using Beep.Installer.Engine;
using Beep.Installer.Ui;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>Modal dialog for editing the file list of a single <see cref="InstallComponent"/>.
/// Uses WinForms data binding: DataGridView binds via BindingSource, editor fields bind via DataBindings.Add.</summary>
public class ComponentFilesDialog : Form
{
    private readonly Font _bodyFont = InstallerTheme.Body;
    private readonly AuthoringThemeBinding _themeBinding;
    private readonly List<Action> _captions = new();
    private readonly string _componentName;
    private bool _disposed;
    private long _totalSizeBytes;
    private int _unavailableSizes;
    private bool _ending, _sizePending, _dropBusy, _applying;
    private int _sizeGeneration, _dropGeneration;
    private string[]? _sizePaths;
    private CancellationTokenSource? _sizeCancellation, _dropCancellation;
    private string _dropStatus = "";
    private string? _dropFailure;
    private ComponentFileScan.DropResult? _dropResult;
    private Label _dropLabel = null!;
    private TextBox _dropDetails = null!;
    private TableLayoutPanel _dropPanel = null!;
    private Button _cancelScan = null!;
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
    private Ui.BindingSourceRow<FileCopyOperation> _row = null!;
    private BindingList<FileCopyOperation> _files = null!;

    public System.Collections.Generic.IList<FileCopyOperation> ResultFiles => _files;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool>? ApplyDraft { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<OpenFileDialog, IWin32Window, DialogResult>? ShowFilePicker { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string[], string?, CancellationToken, ComponentFileScan.SizeResult> ReadSizes { get; set; } = ComponentFileScan.ReadSizes;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string[], string?, FileCopyOperation[], CancellationToken, ComponentFileScan.DropResult> ReadDrop { get; set; } = ComponentFileScan.ReadDrop;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Task PendingSize { get; private set; } = Task.CompletedTask;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Task PendingDrop { get; private set; } = Task.CompletedTask;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Task PendingApply { get; private set; } = Task.CompletedTask;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal long? ReadySizeBytes => !_sizePending && _sizePaths is not null &&
        _sizePaths.SequenceEqual(_files.Select(file => file.SourcePath), StringComparer.Ordinal) ? _totalSizeBytes : null;
    private readonly string? _defaultSourceDir;
    private readonly string _scanSourceDir;

    public ComponentFilesDialog(InstallComponent component, string? defaultSourceDir)
    {
        _defaultSourceDir = defaultSourceDir;
        _scanSourceDir = string.IsNullOrWhiteSpace(defaultSourceDir) ? Environment.CurrentDirectory :
            Path.IsPathRooted(defaultSourceDir) ? defaultSourceDir : Path.Combine(Environment.CurrentDirectory, defaultSourceDir);
        _componentName = component.Name;
        Font = _bodyFont;
        RefreshTitle();
        Size = new Size(840, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(700, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);

        _files = new BindingList<FileCopyOperation>(component.Files.Select(file =>
            System.Text.Json.JsonSerializer.Deserialize<FileCopyOperation>(System.Text.Json.JsonSerializer.Serialize(file))!).ToList());
        _files.ListChanged += OnFilesChanged;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };

        _binding = new BindingSource { DataSource = _files };
        _row = new Ui.BindingSourceRow<FileCopyOperation>(_binding);
        _grid = new DataGridView
        {
            AccessibleName = L("Files_GridName", "Files in this component"),
            Dock = DockStyle.Fill,
            DataSource = _binding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        _grid.AllowDrop = true;
        _grid.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
            (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        _grid.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) BeginDrop(paths);
        };
        _grid.CellValueChanged += (_, _) => UpdateSize();

        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
        _removeBtn = CaptionButton("Files_RemoveSelected", "Remove selected");
        _removeBtn.Click += (_, _) =>
        {
            // _row.Current safely handles an empty BindingSource.
            if (_row.Current is { } f) _files.Remove(f);
        };
        _row.WireRowButtons(_removeBtn);
        var dropHint = new Label
        {
            Text = L("Files_TipDragFilesOr", "Tip: drag files or folders from Explorer onto the list above"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(4, 6, 0, 0)
        };
        Caption(dropHint, "Files_TipDragFilesOr", dropHint.Text);
        AuthoringThemeBinding.SetTone(dropHint, AuthoringTone.Muted);
        topButtons.SizeChanged += (_, _) => dropHint.MaximumSize = new Size(Math.Max(1, topButtons.ClientSize.Width - 8), 0);
        topButtons.Controls.Add(_removeBtn);
        topButtons.Controls.Add(dropHint);
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Controls.Add(topButtons);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8), AutoScroll = true };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int r = 0;
        editor.Controls.Add(CaptionLabel("Files_SourcePath", "Source path:"), 0, r);
        _sourceBox = new TextBox { Dock = DockStyle.Fill };
        _sourceBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.SourcePath), true, DataSourceUpdateMode.OnPropertyChanged);
        var browseBtn = new Button { Name = "FileSourceBrowse", Text = "…", Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(40, 30) };
        browseBtn.Click += (_, _) =>
        {
            using var d = AuthoringFileDialogs.File(L("Files_SourcePath", "Source path:"), "*.*");
            d.InitialDirectory = Directory.Exists(_defaultSourceDir) ? _defaultSourceDir! : "";
            if ((ShowFilePicker?.Invoke(d, this) ?? d.ShowDialog(this)) == DialogResult.OK) _sourceBox.Text = d.FileName;
        };
        editor.Controls.Add(_sourceBox, 1, r);
        editor.Controls.Add(browseBtn, 2, r);
        r++;
        editor.Controls.Add(CaptionLabel("Files_Destination", "Destination:"), 0, r);
        _destBox = new TextBox { Dock = DockStyle.Fill };
        _destBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.DestinationPath), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_destBox, 2); editor.Controls.Add(_destBox, 1, r); r++;
        editor.Controls.Add(CaptionLabel("Common_Description", "Description:"), 0, r);
        _descriptionBox = new TextBox { Dock = DockStyle.Fill };
        _descriptionBox.DataBindings.Add("Text", _binding, nameof(FileCopyOperation.Description), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_descriptionBox, 2); editor.Controls.Add(_descriptionBox, 1, r); r++;
        _overwriteCheck = new CheckBox { Text = L("Files_OverwriteExisting", "Overwrite existing"), Dock = DockStyle.Fill, AutoSize = true, Checked = true };
        _overwriteCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.Overwrite), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_overwriteCheck, 2); editor.Controls.Add(_overwriteCheck, 1, r); r++;
        _skipIfNewerCheck = new CheckBox { Text = L("Files_SkipIfNewer", "Skip if newer"), Dock = DockStyle.Fill, AutoSize = true };
        _skipIfNewerCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.SkipIfNewer), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_skipIfNewerCheck, 2); editor.Controls.Add(_skipIfNewerCheck, 1, r); r++;
        _requiredCheck = new CheckBox { Text = L("Files_RequiredFile", "Required file"), Dock = DockStyle.Fill, AutoSize = true, Checked = true };
        _requiredCheck.DataBindings.Add("Checked", _binding, nameof(FileCopyOperation.IsRequired), true, DataSourceUpdateMode.OnPropertyChanged);
        editor.SetColumnSpan(_requiredCheck, 2); editor.Controls.Add(_requiredCheck, 1, r); r++;
        _addBtn = CaptionButton("Common_Add", "Add");
        _addBtn.Click += (_, _) => AddFromEditor();
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        btnRow.Controls.Add(_addBtn); editor.SetColumnSpan(btnRow, 3); editor.Controls.Add(btnRow, 0, r); r++;
        _sizeLabel = new Label { Name = "FileSizeSummary", Text = "", Dock = DockStyle.Fill, AutoSize = true };
        editor.SetColumnSpan(_sizeLabel, 3); editor.Controls.Add(_sizeLabel, 0, r);
        for (var editorRow = 0; editorRow <= r; editorRow++) editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.SizeChanged += (_, _) => _sizeLabel.MaximumSize = new Size(Math.Max(1, editor.ClientSize.Width - editor.Padding.Horizontal), 0);
        AuthoringThemeBinding.SetTone(_sizeLabel, AuthoringTone.Muted);
        split.Panel2.Controls.Add(editor);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.RightToLeft };
        _okBtn = CaptionButton("Common_OK", "OK");
        _cancelBtn = CaptionButton("Common_Cancel", "Cancel");
        _cancelBtn.DialogResult = DialogResult.Cancel;
        bottom.Controls.AddRange(new Control[] { _okBtn, _cancelBtn });
        _okBtn.Click += (_, _) =>
        {
            if (_dropBusy || _applying) return;
            PendingApply = ApplyAsync();
        };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var dropStatus = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _dropLabel = new Label { Name = "ComponentFileDropStatus", AutoSize = true, Visible = false, UseMnemonic = false };
        _dropDetails = new TextBox { Name = "ComponentFileDropDetails", Dock = DockStyle.Fill, Multiline = true,
            ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false };
        Caption(_dropDetails, "Files_DropIssues", "Scan details:", false);
        _cancelScan = CaptionButton("Files_CancelScan", "Cancel scan"); _cancelScan.Visible = false;
        _cancelScan.Click += (_, _) => CancelDrop();
        dropStatus.SizeChanged += (_, _) => _dropLabel.MaximumSize = new Size(Math.Max(1, dropStatus.ClientSize.Width - 8), 0);
        dropStatus.Controls.Add(_dropLabel); dropStatus.Controls.Add(_cancelScan);
        _dropPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        _dropPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); _dropPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _dropPanel.Controls.Add(dropStatus, 0, 0); _dropPanel.Controls.Add(_dropDetails, 0, 1);
        root.Controls.Add(split, 0, 0); root.Controls.Add(_dropPanel, 0, 1); root.Controls.Add(bottom, 0, 2); Controls.Add(root);
        AcceptButton = _okBtn; CancelButton = _cancelBtn;
        UpdateSize();
        Engine.Accessibility.Attach(this);
        RegisterCaptions(browseBtn);
        _grid.DataBindingComplete += (_, _) => RefreshGridCaptions();
        RefreshGridCaptions();
        _themeBinding = new AuthoringThemeBinding(this);
        _sourceBox.TextChanged += (_, _) =>
        {
            foreach (Binding binding in _sourceBox.DataBindings) binding.WriteValue();
            UpdateSize();
        };
        Load += (_, _) => UpdateSize();
        FormClosed += (_, _) => { _ending = true; StopScans(); };
        LanguageManager.LanguageChanged += OnLanguageChanged;
        RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
    }

    private void AddFromEditor()
    {
        if (string.IsNullOrWhiteSpace(_sourceBox.Text)) { MessageBox.Show(this, L("Files_SourceRequired", "Source path required.")); return; }
        var f = new FileCopyOperation
        {
            SourcePath = _sourceBox.Text.Trim(),
            DestinationPath = string.IsNullOrWhiteSpace(_destBox.Text) ? Path.GetFileName(_sourceBox.Text) : _destBox.Text.Trim(),
            Description = _descriptionBox.Text.Trim(),
            Overwrite = _overwriteCheck.Checked,
            SkipIfNewer = _skipIfNewerCheck.Checked,
            IsRequired = _requiredCheck.Checked
        };
        _files.Add(f);
    }

    private void UpdateSize()
    {
        if (_disposed || _ending || _sizeLabel is null) return;
        if (!IsHandleCreated) { _sizePending = true; RefreshSizeCaption(); return; }
        var paths = _files.Select(file => file.SourcePath).ToArray();
        if (_sizePaths is not null && paths.SequenceEqual(_sizePaths, StringComparer.Ordinal)) return;
        _sizePaths = paths;
        _sizeCancellation?.Cancel(); _sizeCancellation?.Dispose();
        _sizeCancellation = new CancellationTokenSource();
        var generation = ++_sizeGeneration; var token = _sizeCancellation.Token;
        _sizePending = true; RefreshSizeCaption();
        var reader = ReadSizes;
        PendingSize = ReadSizeAsync(paths, generation, token, reader);
    }

    private async Task ReadSizeAsync(string[] paths, int generation, CancellationToken token,
        Func<string[], string?, CancellationToken, ComponentFileScan.SizeResult> reader)
    {
        try
        {
            var result = await Task.Run(() => reader(paths, _scanSourceDir, token), token);
            if (_disposed || _ending || token.IsCancellationRequested || generation != _sizeGeneration) return;
            if (!paths.SequenceEqual(_files.Select(file => file.SourcePath), StringComparer.Ordinal)) { _sizePaths = null; UpdateSize(); return; }
            _totalSizeBytes = result.Bytes; _unavailableSizes = result.Unavailable;
            _sizePending = false; RefreshSizeCaption();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (_disposed || _ending || generation != _sizeGeneration) { }
        catch (Exception)
        {
            _totalSizeBytes = 0; _unavailableSizes = paths.Length;
            _sizePending = false; RefreshSizeCaption();
        }
    }

    private void OnFilesChanged(object? sender, ListChangedEventArgs e) => UpdateSize();

    private async Task ApplyAsync()
    {
        _applying = true; RefreshDropCaption();
        try
        {
            while (!_disposed && !_ending)
            {
                PackageBuilderForm.CommitBindings(this); UpdateSize();
                if (!_sizePending) break;
                await PendingSize;
            }
            if (_disposed || _ending) return;
            if (ApplyDraft?.Invoke() == false) return;
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex)
        {
            _dropFailure = ex.Message; _dropStatus = "Files_ApplyFailed"; _dropResult = null;
        }
        finally
        {
            _applying = false;
            if (!_disposed && !_ending) RefreshDropCaption();
        }
    }

    private void RefreshTitle()
    {
        Text = AccessibleName = string.Format(LanguageManager.CurrentCulture,
            L("Files_EditTitle", "Edit files — {0}"), _componentName);
    }

    private void RefreshSizeCaption()
    {
        _sizeLabel.Text = _sizeLabel.AccessibleName = _sizePending ? L("Files_SizeChecking", "Checking file sizes…") : string.Format(LanguageManager.CurrentCulture,
            L("Files_TotalSize", "Known size: {0:N2} MB across {1:N0} files ({2:N0} sizes unavailable)."),
            _totalSizeBytes / 1024.0 / 1024.0, _files.Count, _unavailableSizes);
    }

    internal void BeginDrop(string[] paths)
    {
        if (_disposed || _ending || _applying) return;
        PackageBuilderForm.CommitBindings(this);
        _dropCancellation?.Cancel(); _dropCancellation?.Dispose();
        _dropCancellation = new CancellationTokenSource();
        var token = _dropCancellation.Token; var generation = ++_dropGeneration;
        var baseline = ModalDraftApply.Capture(_files);
        var existing = System.Text.Json.JsonSerializer.Deserialize<FileCopyOperation[]>(baseline)!;
        _dropBusy = true; _dropResult = null; _dropFailure = null; _dropStatus = "Files_DropScanning";
        RefreshDropCaption();
        var reader = ReadDrop;
        PendingDrop = ReadDropAsync(paths.ToArray(), existing, baseline, generation, token, reader);
    }

    private async Task ReadDropAsync(string[] paths, FileCopyOperation[] existing, string baseline, int generation,
        CancellationToken token, Func<string[], string?, FileCopyOperation[], CancellationToken, ComponentFileScan.DropResult> reader)
    {
        try
        {
            var result = await Task.Run(() => reader(paths, _scanSourceDir, existing, token), token);
            if (_disposed || _ending || token.IsCancellationRequested || generation != _dropGeneration) return;
            PackageBuilderForm.CommitBindings(this);
            if (baseline != ModalDraftApply.Capture(_files)) _dropStatus = "Files_DropStale";
            else
            {
                _files.RaiseListChangedEvents = false;
                try { foreach (var file in result.Files) _files.Add(file); }
                finally { _files.RaiseListChangedEvents = true; _files.ResetBindings(); }
                _dropResult = result; _dropStatus = "Files_DropSummary";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (_disposed || _ending || generation != _dropGeneration) { }
        catch (Exception ex) { _dropFailure = ex.Message; _dropStatus = "Files_DropFailed"; }
        finally
        {
            if (!_disposed && !_ending && generation == _dropGeneration)
            { _dropBusy = false; RefreshDropCaption(); }
        }
    }

    private void CancelDrop()
    {
        _dropCancellation?.Cancel(); ++_dropGeneration;
        _dropBusy = false; _dropStatus = "Files_DropCancelled"; _dropResult = null;
        RefreshDropCaption();
    }

    private void RefreshDropCaption()
    {
        _cancelScan.Visible = _dropBusy; _okBtn.Enabled = !_dropBusy && !_applying;
        _dropLabel.Visible = _dropStatus.Length > 0;
        var text = _dropStatus switch
        {
            "Files_DropScanning" => L("Files_DropScanning", "Reading dropped files… The draft has not changed."),
            "Files_DropCancelled" => L("Files_DropCancelled", "Scan cancelled. The draft has been kept."),
            "Files_DropStale" => L("Files_DropStale", "The draft changed during the scan. Drop the files again to use the current draft."),
            "Files_DropFailed" => L("Files_DropFailed", "The dropped files could not be read. The draft has been kept.") + " " + _dropFailure,
            "Files_ApplyFailed" => L("Files_ApplyFailed", "The file list could not be applied. The draft is still available; review the current project before retrying.") + " " + _dropFailure,
            "Files_DropSummary" when _dropResult is { } result => string.Format(LanguageManager.CurrentCulture,
                L("Files_DropSummary", "Added: {0:N0}, duplicates: {1:N0}, destination conflicts: {2:N0}, unavailable paths: {3:N0}."),
                result.Files.Count, result.Duplicates, result.Conflicts, result.Unavailable),
            _ => ""
        };
        _dropDetails.Visible = _dropResult?.Issues.Count > 0;
        _dropPanel.RowStyles[1].Height = _dropDetails.Visible ? Math.Max(60, _dropDetails.Font.Height * 4) : 0;
        AuthoringReportPresentation.UpdateText(_dropDetails, _dropResult is null ? "" : string.Join(Environment.NewLine, _dropResult.Issues));
        _dropLabel.Text = _dropLabel.AccessibleName = text;
        AuthoringThemeBinding.SetTone(_dropLabel, _dropStatus is "Files_DropFailed" or "Files_ApplyFailed" ? AuthoringTone.Danger :
            _dropStatus is "Files_DropStale" || _dropResult is { Conflicts: > 0 } or { Unavailable: > 0 } ? AuthoringTone.Warning : AuthoringTone.Muted);
    }

    private void StopScans()
    {
        ++_sizeGeneration; ++_dropGeneration;
        _sizeCancellation?.Cancel(); _dropCancellation?.Cancel();
    }

    private void Caption(Control control, string key, string fallback, bool text = true)
    {
        var caption = new LiveUiCaption(key, fallback);
        void Refresh() { if (text) control.Text = caption.Value; control.AccessibleName = caption.Value; }
        _captions.Add(Refresh); Refresh();
    }

    private Label CaptionLabel(string key, string fallback)
    {
        var label = new Label { Name = "Caption_" + key, AutoSize = true, Dock = DockStyle.Fill,
            MaximumSize = new Size(180, 0), MinimumSize = new Size(0, 30), TextAlign = ContentAlignment.MiddleLeft };
        Caption(label, key, fallback); return label;
    }

    private Button CaptionButton(string key, string fallback)
    {
        var button = new Button { Name = key, AutoSize = true, MinimumSize = new Size(75, 30) };
        Caption(button, key, fallback); return button;
    }

    private void RegisterCaptions(Button browse)
    {
        _grid.Name = "ComponentFileGrid"; _sourceBox.Name = "ComponentFileSource";
        _destBox.Name = "ComponentFileDestination"; _descriptionBox.Name = "ComponentFileDescription";
        _okBtn.Name = "ComponentFilesOk"; _cancelBtn.Name = "ComponentFilesCancel";
        Caption(_grid, "Files_GridName", "Files in this component", false);
        Caption(_sourceBox, "Files_SourcePath", "Source path:", false);
        Caption(_destBox, "Files_Destination", "Destination:", false);
        Caption(_descriptionBox, "Common_Description", "Description:", false);
        Caption(_overwriteCheck, "Files_OverwriteExisting", "Overwrite existing");
        Caption(_skipIfNewerCheck, "Files_SkipIfNewer", "Skip if newer");
        Caption(_requiredCheck, "Files_RequiredFile", "Required file");
        void RefreshBrowse() => browse.AccessibleName = AuthoringFileDialogs.BrowseCaption(L("Files_SourcePath", "Source path:"));
        _captions.Add(RefreshBrowse); RefreshBrowse();
    }

    private void RefreshGridCaptions()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderText = column.DataPropertyName switch
            {
                "SourcePath" => L("Files_SourcePath", "Source path:"),
                "DestinationPath" => L("Files_Destination", "Destination:"),
                "Description" => L("Common_Description", "Description:"),
                "Overwrite" => L("Files_OverwriteExisting", "Overwrite existing"),
                "SkipIfNewer" => L("Files_SkipIfNewer", "Skip if newer"),
                "IsRequired" => L("Files_RequiredFile", "Required file"),
                "SharedCount" => L("Files_SharedCount", "Shared-file reference counting"),
                _ => column.HeaderText
            };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke(() => OnLanguageChanged(sender, e)); }
            catch (InvalidOperationException) { }
            return;
        }
        var focus = UiFocusBookmark.Capture(this);
        RefreshTitle(); foreach (var refresh in _captions) refresh();
        RefreshGridCaptions(); RefreshSizeCaption(); // Uses captured totals; no file I/O on culture changes.
        RefreshDropCaption();
        RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
        focus?.Restore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; LanguageManager.LanguageChanged -= OnLanguageChanged;
            StopScans(); _sizeCancellation?.Dispose(); _dropCancellation?.Dispose();
            _files.ListChanged -= OnFilesChanged;
            _themeBinding?.Dispose(); _binding?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) _bodyFont.Dispose();
    }
}
