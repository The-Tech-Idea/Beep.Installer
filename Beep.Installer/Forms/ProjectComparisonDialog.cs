using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using System.Globalization;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Forms;

internal sealed class ProjectComparisonDialog : Form
{
    private readonly Func<InstallProject> _current;
    private readonly Label _notice = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _identity = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _error = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = InstallerTheme.Danger };
    private readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _search = new() { Name = "ComparisonSearch", Width = 280 };
    private readonly TextBox _details = new() { Name = "ComparisonDetails", Dock = DockStyle.Bottom, ReadOnly = true,
        Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _grid = new() { Name = "ProjectChanges", Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Button _choose = new() { Name = "ComparisonSelect", AutoSize = true };
    private readonly Button _refresh = new() { Name = "ComparisonRefresh", AutoSize = true };
    private readonly Button _close = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Font _body = InstallerTheme.Body;
    private ProjectComparisonSnapshot? _snapshot;
    private string? _path;
    private ComparisonProjectReadError _loadError;
    private long _generation;
    private bool _translating;
    private CancellationTokenSource? _loading;
    private IReadOnlyList<ProjectComparisonEntry> _visibleChanges = Array.Empty<ProjectComparisonEntry>();

    internal ProjectComparisonDialog(Func<InstallProject> current)
    {
        _current = current; Font = _body; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Size = new(1000, 700); MinimumSize = new(700, 480); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (var index = 0; index < 6; index++) layout.RowStyles.Add(new(index == 4 ? SizeType.Percent : SizeType.AutoSize, index == 4 ? 100 : 0));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        filters.Controls.AddRange(new Control[] { _category, _search });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        actions.Controls.AddRange(new Control[] { _choose, _refresh, _close });
        foreach (Button button in actions.Controls) button.MinimumSize = new(0, InstallerTheme.ActionMinHeight);
        foreach (var (key, weight) in new[] { ("Category", 16f), ("Kind", 12f), ("Path", 30f), ("Before", 21f), ("After", 21f) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.RowTemplate.Height = InstallerTheme.ActionMinHeight; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.VirtualMode = true;
        _grid.SelectionChanged += (_, _) => RenderSelectedChange();
        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleChanges.Count) return;
            var change = _visibleChanges[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => L(change.Category.ToString()), 1 => L(change.Kind.ToString()),
                2 => change.Path, 3 => change.Before, 4 => change.After, _ => "" };
        };
        layout.Controls.Add(_notice, 0, 0); layout.Controls.Add(_identity, 0, 1); layout.Controls.Add(_error, 0, 2);
        var gridHost = new Panel { Dock = DockStyle.Fill }; gridHost.Controls.Add(_grid); gridHost.Controls.Add(_empty);
        var review = new Panel { Dock = DockStyle.Fill }; review.Controls.Add(gridHost); review.Controls.Add(_details);
        review.ClientSizeChanged += (_, _) => _details.Height = Math.Min(review.ClientSize.Height, Math.Max(24, review.ClientSize.Height / 3));
        layout.Controls.Add(filters, 0, 3); layout.Controls.Add(review, 0, 4); layout.Controls.Add(actions, 0, 5); Controls.Add(layout);
        _choose.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = InstallerScriptSerializer.FileFilter, Title = _choose.Text };
            if (picker.ShowDialog(this) == DialogResult.OK) await LoadProjectAsync(picker.FileName);
        };
        _refresh.Click += async (_, _) => { if (_path is not null) await LoadProjectAsync(_path); };
        _category.SelectedIndexChanged += (_, _) => { if (!_translating) RenderRows(); };
        _search.TextChanged += (_, _) => RenderRows(); AcceptButton = _choose; CancelButton = _close;
        LanguageManager.LanguageChanged += OnLanguageChanged;
        BeepThemesManager.ThemeChanged += OnThemeChanged;
        SizeChanged += (_, _) => { _notice.MaximumSize = _identity.MaximumSize = _error.MaximumSize = new(Math.Max(1, ClientSize.Width - 40), 0); };
        _notice.MaximumSize = _identity.MaximumSize = _error.MaximumSize = new(Math.Max(1, ClientSize.Width - 40), 0);
        Translate(); ApplyTheme(); Engine.Accessibility.ApplyAutoNames(this);
    }

    internal async Task<bool> LoadProjectAsync(string path)
    {
        if (IsDisposed || Disposing) return false;
        var generation = ++_generation;
        _loading?.Cancel(); using var cancellation = new CancellationTokenSource(); _loading = cancellation;
        _choose.Enabled = _refresh.Enabled = false;
        try
        {
            var loaded = await Task.Run(() => ComparisonProjectReader.Read(path, cancellation.Token));
            if (IsDisposed || Disposing || generation != _generation) return false;
            _loadError = loaded.Error;
            if (loaded.Project is not null)
            {
                // Current UI bindings are read on the UI thread after asynchronous file parsing.
                var snapshot = ProjectComparison.Create(loaded.Project, _current());
                _snapshot = snapshot; _path = Path.GetFullPath(path);
            }
            Translate(); return loaded.Project is not null;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing && generation == _generation)
            { _loadError = ComparisonProjectReadError.Invalid; Translate(); }
            return false;
        }
        finally
        {
            if (generation == _generation)
            {
                _loading = null;
                if (!IsDisposed && !Disposing) { _choose.Enabled = true; _refresh.Enabled = _path is not null; }
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        Translate();
    }

    private void Translate()
    {
        Text = L("Title"); _notice.Text = L("Notice");
        _identity.Text = _snapshot is null ? L("SelectFirst") : L("Snapshot", _path ?? "", _snapshot.CapturedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), _snapshot.Changes.Count);
        _error.Text = _loadError == ComparisonProjectReadError.None ? "" : L("LoadFailed");
        _notice.AccessibleName = _notice.Text; _identity.AccessibleName = _identity.Text; _error.AccessibleName = _error.Text;
        _choose.Text = _choose.AccessibleName = L("Select"); _refresh.Text = _refresh.AccessibleName = L("Refresh"); _refresh.Enabled = _loading is null && _path is not null;
        _close.Text = _close.AccessibleName = LanguageManager.GetString("Btn_Close");
        _category.AccessibleName = L("Category"); _search.AccessibleName = L("Search"); _search.PlaceholderText = L("Search"); _grid.AccessibleName = L("Title");
        _details.AccessibleName = L("Details");
        foreach (DataGridViewColumn column in _grid.Columns) column.HeaderText = L(column.Name);
        var selected = Math.Max(0, _category.SelectedIndex); _translating = true;
        _category.Items.Clear(); _category.Items.Add(L("All"));
        foreach (var category in Enum.GetValues<ProjectChangeCategory>()) _category.Items.Add(L(category.ToString()));
        _category.SelectedIndex = selected; _translating = false;
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = RightToLeft == RightToLeft.Yes; RenderRows();
    }

    private void RenderRows()
    {
        if (_translating) return;
        var selectedIndex = _grid.CurrentCell?.RowIndex ?? -1;
        var selectedPath = selectedIndex >= 0 && selectedIndex < _visibleChanges.Count ? _visibleChanges[selectedIndex].Path : null;
        var changes = _snapshot?.Changes ?? Array.Empty<ProjectComparisonEntry>();
        var category = _category.SelectedIndex - 1; var query = _search.Text.Trim();
        _visibleChanges = changes.Where(change => (category < 0 || (int)change.Category == category)
            && (query.Length == 0 || new[] { change.Path, change.Before, change.After }.Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase)))).ToArray();
        _grid.RowCount = _visibleChanges.Count;
        var previous = _visibleChanges.Select((change, index) => (change.Path, Index: index)).FirstOrDefault(item => item.Path == selectedPath);
        if (previous.Path is not null) _grid.CurrentCell = _grid.Rows[previous.Index].Cells[2];
        _grid.Invalidate();
        RenderSelectedChange();
        _empty.Text = _snapshot is null ? L("SelectFirst") : L(_snapshot.Changes.Count == 0 ? "NoChanges" : "NoMatches");
        _empty.AccessibleName = _empty.Text;
        _empty.Visible = _grid.Rows.Count == 0;
        if (_empty.Visible) _empty.BringToFront();
    }

    private void RenderSelectedChange()
    {
        var index = _grid.CurrentCell?.RowIndex ?? -1;
        _details.Text = index < 0 || index >= _visibleChanges.Count ? "" : string.Join(Environment.NewLine,
            L("Path") + ": " + _visibleChanges[index].Path,
            L("Before") + ": " + _visibleChanges[index].Before,
            L("After") + ": " + _visibleChanges[index].After);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnThemeChanged(sender, e)); return; }
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        void Colors(Control owner)
        {
            owner.BackColor = InstallerTheme.Panel; owner.ForeColor = InstallerTheme.Text;
            foreach (Control child in owner.Controls) Colors(child);
        }
        Colors(this); _error.ForeColor = InstallerTheme.Danger;
        _grid.BackgroundColor = InstallerTheme.Panel; _grid.EnableHeadersVisualStyles = false;
        _grid.DefaultCellStyle.BackColor = _grid.ColumnHeadersDefaultCellStyle.BackColor = InstallerTheme.Panel;
        _grid.DefaultCellStyle.ForeColor = _grid.ColumnHeadersDefaultCellStyle.ForeColor = InstallerTheme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = InstallerTheme.Accent; _grid.DefaultCellStyle.SelectionForeColor = InstallerTheme.AccentText;
    }

    private static string L(string key, params object[] args) => LanguageManager.Tf("Comparison_" + key, args);
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ++_generation; _loading?.Cancel(); _loading = null; LanguageManager.LanguageChanged -= OnLanguageChanged; BeepThemesManager.ThemeChanged -= OnThemeChanged; }
        base.Dispose(disposing); if (disposing) _body.Dispose();
    }
}
