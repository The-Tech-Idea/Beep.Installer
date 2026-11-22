using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

internal sealed class TemplateGalleryDialog : Form
{
    private readonly List<ProjectTemplate> _templates;
    private readonly List<TemplateGalleryEntry> _entries;
    private readonly TemplateFavoritesStore _store;
    private TemplateFavoritesResult _favorites;
    private readonly TextBox _search = new() { Name = "TemplateSearch", Dock = DockStyle.Top };
    private readonly ComboBox _category = new() { Name = "TemplateCategory", DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = true, Width = 160 };
    private readonly CheckBox _onlyFavorites = new() { Name = "TemplateFavoritesOnly", AutoSize = true };
    private readonly ListView _list = new() { Name = "TemplateGallery", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false };
    private readonly TextBox _details = new() { Name = "TemplateSample", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _favorite = new() { Name = "TemplateFavoriteToggle", AutoSize = true };
    private readonly Button _select = new() { Name = "TemplateSelect", AutoSize = true };
    private readonly Button _cancel = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Button _import = new() { Name = "TemplateImport", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Font _font = InstallerTheme.Body;
    private string _selectedId;
    private bool _rebuilding;
    private bool _importing;
    private int _generation;
    private TemplateImportError? _importError;
    private bool _discovering;
    private bool _libraryWarning;
    internal TemplateLibraryStore LibraryStore;
    internal Task<TemplateLibraryDiscovery>? PendingDiscovery;
    internal Func<IWin32Window, (string package, string publicKey)?> ChooseImportPaths = PickImportPaths;
    internal Task<TemplateImportResult>? PendingImport;

    internal string SelectedTemplateId => _selectedId;
    internal ProjectTemplate? SelectedTemplate => _templates.FirstOrDefault(template => template.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase));
    internal TemplateGalleryDialog(string selectedId = ProjectTemplates.EmptyId, IEnumerable<ProjectTemplate>? templates = null, TemplateFavoritesStore? store = null, TemplateLibraryStore? library = null)
    {
        _templates = (templates ?? ProjectTemplates.Builtins).ToList();
        _entries = TemplateGallery.Describe(_templates).ToList(); _selectedId = selectedId;
        _store = store ?? new TemplateFavoritesStore(); _favorites = _store.Read(); Font = _font;
        LibraryStore = library ?? new TemplateLibraryStore();
        Size = new(1000, 700); MinimumSize = new(740, 500); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new(12) };
        root.ColumnStyles.Add(new(SizeType.Percent, 44)); root.ColumnStyles.Add(new(SizeType.Percent, 56));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(_search, 0, 0); root.SetColumnSpan(_search, 2);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        filters.Controls.AddRange(new Control[] { _category, _onlyFavorites }); root.Controls.Add(filters, 0, 1); root.SetColumnSpan(filters, 2);
        _list.Columns.Add("", 180); _list.Columns.Add("", 100); _list.Columns.Add("", 75);
        root.Controls.Add(_list, 0, 2);
        var detailHost = new Panel { Dock = DockStyle.Fill }; detailHost.Controls.Add(_details); root.Controls.Add(detailHost, 1, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        foreach (var button in new[] { _import, _favorite, _select, _cancel }) { button.MinimumSize = new(0, InstallerTheme.ActionMinHeight); actions.Controls.Add(button); }
        root.Controls.Add(actions, 0, 3); root.SetColumnSpan(actions, 2); root.Controls.Add(_status, 0, 4); root.SetColumnSpan(_status, 2); Controls.Add(root);
        AcceptButton = _select; CancelButton = _cancel;
        _search.TextChanged += (_, _) => Rebuild(); _category.SelectedIndexChanged += (_, _) => { if (!_rebuilding) Rebuild(); };
        _onlyFavorites.CheckedChanged += (_, _) => Rebuild();
        _list.SelectedIndexChanged += (_, _) => { if (_rebuilding || _list.SelectedItems.Count == 0) return; _selectedId = (string)_list.SelectedItems[0].Tag!; ShowDetails(); };
        _list.DoubleClick += (_, _) => SelectTemplate(); _select.Click += (_, _) => SelectTemplate();
        _import.Click += (_, _) => { var paths = ChooseImportPaths(this); if (paths is not null) PendingImport = ImportFromAsync(paths.Value.package, paths.Value.publicKey); };
        _favorite.Click += (_, _) =>
        {
            if (!_favorites.Available || _list.SelectedItems.Count == 0) return;
            _favorites = _store.SetFavorite(_selectedId, !_favorites.Ids.Contains(_selectedId, StringComparer.OrdinalIgnoreCase)); Rebuild();
        };
        _list.ClientSizeChanged += (_, _) =>
        {
            var width = Math.Max(1, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            _list.Columns[2].Width = Math.Min(width / 4, 90); _list.Columns[1].Width = Math.Min(width / 3, 130);
            _list.Columns[0].Width = Math.Max(1, width - _list.Columns[1].Width - _list.Columns[2].Width);
        };
        root.ClientSizeChanged += (_, _) => _status.MaximumSize = new(Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), 0);
        LanguageManager.LanguageChanged += LanguageChanged; BeepThemesManager.ThemeChanged += ThemeChanged;
        Load += (_, _) => PendingDiscovery = DiscoverSavedAsync();
        Translate(); Shown += (_, _) => _search.Focus();
    }
    private static string DisplayName(TemplateGalleryEntry entry) => L("Gallery_Name_" + entry.Id, entry.Name);
    private static string Description(TemplateGalleryEntry entry) => L("Gallery_Description_" + entry.Id, entry.Description);
    private static string Category(string category) => L("Gallery_Category_" + category, category);
    private static string Shortcut(string sample)
    {
        var separator = sample.IndexOf(':');
        return separator < 0 ? sample : L("Gallery_Location_" + sample[..separator], sample[..separator]) + sample[separator..];
    }
    private void Rebuild()
    {
        if (_rebuilding) return; _rebuilding = true; _list.BeginUpdate();
        try
        {
            _list.Items.Clear(); var category = _category.SelectedItem as CategoryChoice;
            var tokens = _search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in _entries)
            {
                var favorite = _favorites.Ids.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
                var searchable = string.Join(" ", entry.Id, DisplayName(entry), Description(entry), Category(entry.Category), entry.Name, entry.Description, entry.Category, string.Join(" ", entry.SampleShortcuts.Select(Shortcut)));
                if (category?.Id is { Length: > 0 } id && !entry.Category.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                if (_onlyFavorites.Checked && !favorite) continue;
                if (tokens.Any(token => !searchable.Contains(token, StringComparison.CurrentCultureIgnoreCase))) continue;
                var item = new ListViewItem(new[] { DisplayName(entry), Category(entry.Category), favorite ? "★" : "" }) { Tag = entry.Id };
                _list.Items.Add(item); if (entry.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
            }
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) { _list.Items[0].Selected = true; _selectedId = (string)_list.Items[0].Tag!; }
        }
        finally { _list.EndUpdate(); _rebuilding = false; } ShowDetails();
    }
    private void ShowDetails()
    {
        var entry = _list.SelectedItems.Count == 0 ? null : _entries.FirstOrDefault(value => value.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase));
        _select.Enabled = !_importing && entry?.HasSample == true; _favorite.Enabled = !_importing && entry?.HasSample == true && _favorites.Available;
        _import.Enabled = !_importing && !_discovering;
        _favorite.Text = _favorite.AccessibleName = _favorites.Ids.Contains(_selectedId, StringComparer.OrdinalIgnoreCase)
            ? L("Gallery_RemoveFavorite", "Remove favorite") : L("Gallery_AddFavorite", "Add favorite");
        _details.Text = entry is null ? L("Gallery_NoMatches", "No templates match these filters.")
            : string.Join(Environment.NewLine + Environment.NewLine, DisplayName(entry), Category(entry.Category), Description(entry),
                string.Format(L("Gallery_Sample", "Sample components: {0}. Shortcut targets:"), entry.ComponentCount), string.Join(Environment.NewLine, entry.SampleShortcuts.Select(Shortcut)),
                SelectedTemplate?.Blueprint is null
                    ? L("Gallery_Limits", "Sample filenames are placeholders. Review executable targets, resources, scope and signing before building.")
                    : L("Gallery_SignedLimits", "Captured package content. Verification does not validate payload files or approve installation. Creation replaces product identity and source fields. Review resources and paths before building."));
        if (entry is not null && SelectedTemplate?.Blueprint is { } blueprint)
            _details.Text += Environment.NewLine + Environment.NewLine + string.Format(L("Gallery_Verified", "Verified signature. Issuer: {0}\nTrusted key SHA-256: {1}\nProject SHA-256: {2}"), blueprint.Issuer, blueprint.TrustedKeySha256, blueprint.ProjectSha256)
                + Environment.NewLine + L("Gallery_LibraryHint", "Saved discovery remembers source references and reviewed hashes. Sources and keys are verified again in later sessions. Changed sources require reimport.")
                + Environment.NewLine + Environment.NewLine + ProjectComparison.ProtectedCanonicalJson(blueprint.CreateProject());
        _status.Text = string.Join(Environment.NewLine, new[] {
            _favorites.Available ? "" : L("Gallery_FavoritesUnavailable", "Favorites could not be read or saved. Existing data has been kept; template selection is still available."),
            _libraryWarning ? L("Gallery_LibraryWarning", "Some saved templates could not be loaded or saved. Existing library data was kept. Reimport changed or unavailable sources to review and verify them again.") : "",
            _importing || _discovering ? L("Gallery_Verifying", "Verifying signed template…") : _importError is { } error ? L("Gallery_ImportError_" + error, "The signed template could not be imported. Existing selection has been kept.") : ""
        }.Where(text => text.Length > 0));
    }
    private void SelectTemplate() { if (!_select.Enabled) return; DialogResult = DialogResult.OK; Close(); }
    private sealed record CategoryChoice(string Id) { public override string ToString() => Id.Length == 0 ? L("Gallery_AllCategories", "All categories") : Category(Id); }
    private void Translate()
    {
        Text = L("Gallery_Title", "Template gallery"); _search.AccessibleName = L("Gallery_Search", "Search templates"); _search.PlaceholderText = _search.AccessibleName;
        _category.AccessibleName = L("Gallery_Category", "Category"); _onlyFavorites.Text = _onlyFavorites.AccessibleName = L("Gallery_FavoritesOnly", "Favorites only");
        _list.AccessibleName = Text; _details.AccessibleName = L("Gallery_SampleDetails", "Template sample details");
        _select.Text = _select.AccessibleName = L("Gallery_Select", "Use selected template"); _cancel.Text = _cancel.AccessibleName = L("Common_Cancel", "Cancel");
        _import.Text = _import.AccessibleName = L("Gallery_Import", "Import signed template…");
        _list.Columns[0].Text = L("NewProject_Template", "Template:"); _list.Columns[1].Text = _category.AccessibleName; _list.Columns[2].Text = L("Gallery_Favorite", "Favorite");
        var selected = (_category.SelectedItem as CategoryChoice)?.Id ?? ""; _rebuilding = true;
        _category.Items.Clear(); _category.Items.Add(new CategoryChoice(""));
        foreach (var category in _entries.Select(entry => entry.Category).Distinct(StringComparer.OrdinalIgnoreCase)) _category.Items.Add(new CategoryChoice(category));
        _category.SelectedItem = _category.Items.Cast<CategoryChoice>().FirstOrDefault(choice => choice.Id == selected) ?? _category.Items[0]; _rebuilding = false;
        RightToLeftLayout = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft; RightToLeft = RightToLeftLayout ? RightToLeft.Yes : RightToLeft.No;
        _details.RightToLeft = _search.RightToLeft = _list.RightToLeft = RightToLeft; _list.RightToLeftLayout = RightToLeftLayout; ApplyTheme(); Rebuild();
    }
    private void ApplyTheme() { BackColor = InstallerTheme.Panel; ForeColor = InstallerTheme.Text; foreach (var control in new Control[] { _search, _category, _list, _details, _onlyFavorites, _status, _favorite, _select, _cancel, _import }) { control.BackColor = InstallerTheme.Panel; control.ForeColor = InstallerTheme.Text; } }
    private static (string package, string publicKey)? PickImportPaths(IWin32Window owner)
    {
        using var package = new FolderBrowserDialog { Description = L("Gallery_PackageFolder", "Select the signed template package folder") };
        if (package.ShowDialog(owner) != DialogResult.OK) return null;
        using var key = new OpenFileDialog { Title = L("Gallery_PublicKey", "Select a public key you trust for this template"), Filter = L("Update_KeyFilter", "Public keys (*.pem)|*.pem|All files (*.*)|*.*"), CheckFileExists = true };
        return key.ShowDialog(owner) == DialogResult.OK ? (package.SelectedPath, key.FileName) : null;
    }
    internal async Task<TemplateImportResult> ImportFromAsync(string package, string publicKey)
    {
        if (_importing || IsDisposed || Disposing) return new(null, TemplateImportError.Unavailable);
        if (PendingDiscovery is { IsCompleted: false }) await PendingDiscovery;
        if (_importing || IsDisposed || Disposing) return new(null, TemplateImportError.Unavailable);
        var generation = ++_generation; _importing = true; _importError = null; ShowDetails();
        var result = await Task.Run(() => VerifiedTemplateImporter.Import(package, publicKey));
        if (IsDisposed || Disposing || generation != _generation) return result;
        if (result.Blueprint is { } blueprint)
        {
            var library = LibraryStore;
            var remembered = await Task.Run(() => library.Remember(package, publicKey, blueprint));
            if (IsDisposed || Disposing || generation != _generation) return result;
            _importing = false;
            _libraryWarning |= remembered != TemplateLibraryError.None;
            var descriptor = new ProjectTemplate { Id = blueprint.Id, Name = blueprint.Name, Description = blueprint.Description, Category = blueprint.Category, Blueprint = blueprint };
            _templates.RemoveAll(template => template.Id.Equals(descriptor.Id, StringComparison.OrdinalIgnoreCase)); _templates.Add(descriptor);
            _entries.RemoveAll(entry => entry.Id.Equals(descriptor.Id, StringComparison.OrdinalIgnoreCase)); _entries.AddRange(TemplateGallery.Describe(new[] { descriptor }));
            _rebuilding = true; _search.Clear(); _onlyFavorites.Checked = false; _category.SelectedIndex = 0; _rebuilding = false;
            _selectedId = descriptor.Id; Translate(); Rebuild();
        }
        else { _importing = false; _importError = result.Error; ShowDetails(); }
        return result;
    }
    internal async Task<TemplateLibraryDiscovery> DiscoverSavedAsync()
    {
        if (_discovering || _importing || IsDisposed || Disposing) return new(Array.Empty<ProjectTemplate>(), 0, TemplateLibraryError.Unavailable);
        var generation = _generation; _discovering = true; ShowDetails(); var library = LibraryStore;
        var result = await Task.Run(() => library.Discover());
        if (IsDisposed || Disposing || generation != _generation) return result;
        _discovering = false; _libraryWarning |= result.Error != TemplateLibraryError.None || result.RejectedEntries > 0;
        foreach (var template in result.Templates)
        {
            if (_templates.Any(existing => existing.Id.Equals(template.Id, StringComparison.OrdinalIgnoreCase))) continue;
            _templates.Add(template); _entries.AddRange(TemplateGallery.Describe(new[] { template }));
        }
        Translate(); return result;
    }
    private void LanguageChanged(object? sender, EventArgs e) { if (IsDisposed || Disposing) return; if (InvokeRequired) { BeginInvoke(() => LanguageChanged(sender, e)); return; } Translate(); }
    private void ThemeChanged(object? sender, EventArgs e) { if (IsDisposed || Disposing) return; if (InvokeRequired) { BeginInvoke(() => ThemeChanged(sender, e)); return; } ApplyTheme(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ++_generation; LanguageManager.LanguageChanged -= LanguageChanged; BeepThemesManager.ThemeChanged -= ThemeChanged; }
        base.Dispose(disposing); if (disposing) _font.Dispose();
    }
}
