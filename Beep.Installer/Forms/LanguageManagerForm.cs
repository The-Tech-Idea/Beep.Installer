using System.Globalization;
using System.ComponentModel;
using Beep.Installer.Ui;
using Beep.Installer.Engine;
using Microsoft.VisualBasic.FileIO;
using System.Xml.Linq;
using Beep.Installer.Lang;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Language management form — add/update/remove strings and languages.
/// Provides a grid-based editor for all localized strings across all supported languages.
/// </summary>
public class LanguageManagerForm : Form
{
    private ComboBox _langSelector = null!;
    private DataGridView _grid = null!;
    private ToolStripMenuItem _addLangBtn = null!;
    private ToolStripMenuItem _removeLangBtn = null!;
    private ToolStripMenuItem _addKeyBtn = null!;
    private ToolStripMenuItem _removeKeyBtn = null!;
    private ToolStripMenuItem _saveBtn = null!;
    private ToolStripMenuItem _exportBtn = null!;
    private ToolStripMenuItem _importBtn = null!;
    private Label _statusLabel = null!;
    private RichTextBox _previewBox = null!;

    private readonly Dictionary<string, Dictionary<string, string>> _allStrings = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLang = "en";
    private readonly string LangDir;
    private readonly Font _bodyFont = InstallerTheme.Body;
    private readonly Font _headingFont = InstallerTheme.SectionTitle;
    private AuthoringThemeBinding? _themeBinding;
    private string _statusKey = "LangMgr_Ready";
    private string _statusFallback = "Ready.";
    private object[] _statusArgs = Array.Empty<object>();
    private bool _refreshing;
    private bool _disposed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, MessageBoxButtons, DialogResult>? ShowMessage { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, string?>? PromptInput { get; set; }

    private DialogResult Message(string text, string title = "", MessageBoxButtons buttons = MessageBoxButtons.OK)
        => ShowMessage?.Invoke(text, title, buttons) ?? MessageBox.Show(this, text, title, buttons);
    private string? Prompt(string text, string title) => PromptInput is { } prompt ? prompt(text, title) : InputBox.Prompt(this, text, title, "");

    public LanguageManagerForm() : this(Path.Combine(AppContext.BaseDirectory, "Lang")) { }

    internal LanguageManagerForm(string languageDirectory)
    {
        LangDir = Path.GetFullPath(languageDirectory);
        Size = new Size(1100, 760); MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent; Font = _bodyFont;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
        InitializeUI(); LoadAllLanguages();
        if (_langSelector.Items.Contains("en")) _langSelector.SelectedItem = "en";
        else if (_langSelector.Items.Count > 0) _langSelector.SelectedIndex = 0;
        else ShowLanguage("en");
        _themeBinding = new AuthoringThemeBinding(this);
        LanguageManager.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged(this, EventArgs.Empty);
        Engine.Accessibility.Attach(this);
    }

    private void InitializeUI()
    {
        new LiveUiCaption("LangMgr_LanguageManagerBeepInstaller", "Language Manager — Beep Installer").Bind(this);
        var menu = new MenuStrip { Name = "Languages_Menu" };
        var file = MenuAction("Menu_File", "&File");
        var language = MenuAction("LangMgr_LanguagesMenu", "&Language");
        var keys = MenuAction("LangMgr_KeysMenu", "&Keys");
        _saveBtn = MenuAction("LangMgr_SaveAllPlain", "Save all", SaveAllLanguages); _saveBtn.ShortcutKeys = Keys.Control | Keys.S;
        _exportBtn = MenuAction("LangMgr_ExportPlain", "Export CSV…", ExportToCsv);
        _importBtn = MenuAction("LangMgr_ImportPlain", "Import CSV…", ImportFromCsv);
        file.DropDownItems.AddRange(new ToolStripItem[] { _saveBtn, _importBtn, _exportBtn });
        _addLangBtn = MenuAction("LangMgr_AddLanguage", "Add language…", AddLanguage);
        _removeLangBtn = MenuAction("LangMgr_RemoveLanguage", "Remove language…", RemoveLanguage);
        language.DropDownItems.AddRange(new ToolStripItem[] { _addLangBtn, _removeLangBtn,
            MenuAction("LangMgr_DuplicateLanguage", "Duplicate language…", DuplicateLanguage) });
        _addKeyBtn = MenuAction("LangMgr_AddKey", "Add key…", AddKey);
        _removeKeyBtn = MenuAction("LangMgr_RemoveKey", "Remove key…", RemoveKey);
        keys.DropDownItems.AddRange(new ToolStripItem[] { _addKeyBtn, _removeKeyBtn });
        menu.Items.AddRange(new ToolStripItem[] { file, language, keys }); MainMenuStrip = menu;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AuthoringThemeBinding.SetSurface(this, AuthoringSurface.Shell); AuthoringThemeBinding.SetSurface(root, AuthoringSurface.Shell);
        var title = Caption("LangMgr_Title", "Language Manager"); title.Font = _headingFont;
        var help = Caption("LangMgr_Guidance", "Edit translations without changing the application language.");
        root.Controls.Add(title, 0, 0); root.Controls.Add(help, 0, 1);
        var picker = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 8, 0, 8) };
        picker.Controls.Add(Caption("LangMgr_PickerName", "Language being edited"));
        _langSelector = new ComboBox { Name = "Languages_Picker", Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        new LiveUiCaption("LangMgr_PickerName", "Language being edited").Bind(_langSelector, text: false);
        _langSelector.SelectedIndexChanged += (_, _) => { if (!_refreshing) ShowLanguage(_langSelector.SelectedItem?.ToString() ?? "en"); };
        picker.Controls.Add(_langSelector); root.Controls.Add(picker, 0, 2);
        _grid = new DataGridView { Name = "Languages_Grid", Dock = DockStyle.Fill, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize };
        new LiveUiCaption("LangMgr_GridName", "Translations for the selected language").Bind(_grid, text: false);
        _grid.Columns.Add("Key", "Key"); _grid.Columns.Add("Value", "Value");
        _grid.Columns[0].ReadOnly = true; _grid.Columns[0].FillWeight = 40; _grid.Columns[1].FillWeight = 60;
        _grid.CellValueChanged += OnCellChanged; _grid.SelectionChanged += OnSelectionChanged;
        root.Controls.Add(_grid, 0, 3);
        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 8, 0, 0) };
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize)); preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.Controls.Add(Caption("LangMgr_PreviewTitle", "Translations for selected key"), 0, 0);
        _previewBox = new RichTextBox { Name = "Languages_Preview", Dock = DockStyle.Fill, ReadOnly = true, Font = _bodyFont };
        new LiveUiCaption("LangMgr_PreviewTitle", "Translations for selected key").Bind(_previewBox, text: false);
        preview.Controls.Add(_previewBox, 0, 1); root.Controls.Add(preview, 0, 4);
        _statusLabel = new Label { Name = "Languages_Status", Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        root.Controls.Add(_statusLabel, 0, 5);
        root.SizeChanged += (_, _) => _statusLabel.MaximumSize = new Size(Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), 0);
        Controls.Add(root); Controls.Add(menu);
    }

    private static ToolStripMenuItem MenuAction(string key, string fallback, Action? action = null)
    {
        var item = new ToolStripMenuItem { Name = key }; new LiveUiCaption(key, fallback).Bind(item);
        if (action is not null) item.Click += (_, _) => action(); return item;
    }
    private static Label Caption(string key, string fallback)
    {
        var label = new Label { AutoSize = true, Margin = new Padding(0, 4, 8, 6) };
        new LiveUiCaption(key, fallback).Bind(label); return label;
    }
    private void Status(string key, string fallback, params object[] args)
    { _statusKey = key; _statusFallback = fallback; _statusArgs = args; RefreshStatus(); }
    private void RefreshStatus() => _statusLabel.Text = _statusLabel.AccessibleName = string.Format(LanguageManager.CurrentCulture, L(_statusKey, _statusFallback), _statusArgs);
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        var focus = UiFocusBookmark.Capture(this);
        var editing = _grid.IsCurrentCellInEditMode && _grid.EditingControl is TextBox;
        var editor = _grid.EditingControl as TextBox;
        var draft = editor?.Text; var start = editor?.SelectionStart ?? 0; var length = editor?.SelectionLength ?? 0;
        _refreshing = true;
        // Native RTL recreates grid handles and would otherwise commit/end the active cell.
        // Cancel only the native edit, then restore its uncommitted text after direction changes.
        if (editing) _grid.CancelEdit();
        _grid.Columns[0].HeaderText = L("LangMgr_KeyHeader", "Key");
        _grid.Columns[1].HeaderText = string.Format(LanguageManager.CurrentCulture, L("LangMgr_ValueHeader", "Value ({0})"), _currentLang);
        RefreshStatus();
        RtlHelper.ApplyDirection(this, LanguageManager.CurrentCulture.TextInfo.IsRightToLeft);
        // Do not reload rows, dictionaries or the translation-language picker on UI language change.
        if (editing && _grid.CurrentCell is not null)
        {
            _grid.BeginEdit(false);
            if (_grid.EditingControl is TextBox replacement)
            { replacement.Text = draft; replacement.Focus(); replacement.Select(start, length); }
        }
        else focus?.Restore();
        _refreshing = false;
    }

    private void LoadAllLanguages()
    {
        _allStrings.Clear();
        _langSelector.Items.Clear();

        var langFiles = Directory.Exists(LangDir)
            ? Directory.GetFiles(LangDir, "Strings_*.resx")
            : Array.Empty<string>();

        foreach (var file in langFiles)
        {
            var langCode = Path.GetFileNameWithoutExtension(file).Replace("Strings_", "");
            _allStrings[langCode] = LoadResxFile(file);
            _langSelector.Items.Add(langCode);
        }

        Status("LangMgr_LoadedStatus", "{0:N0} languages loaded. Ready.", _allStrings.Count);
    }

    private static Dictionary<string, string> LoadResxFile(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = XDocument.Load(path);
            foreach (var data in doc.Descendants("data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (name != null && value != null) dict[name] = value;
            }
        }
        catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", ".resx import failed", ex); }
        return dict;
    }

    private void ShowLanguage(string langCode)
    {
        _grid.EndEdit();
        _currentLang = langCode;
        _refreshing = true;
        _grid.Rows.Clear();

        if (_allStrings.TryGetValue(langCode, out var strings))
        {
            foreach (var kv in strings.OrderBy(k => k.Key))
                _grid.Rows.Add(kv.Key, kv.Value);
        }

        _refreshing = false;
        _grid.Columns[1].HeaderText = string.Format(LanguageManager.CurrentCulture, L("LangMgr_ValueHeader", "Value ({0})"), langCode);
        Status("LangMgr_LanguageStatus", "Language: {0} — {1:N0} strings", langCode, strings?.Count ?? 0);
        OnSelectionChanged(this, EventArgs.Empty);
    }

    private void OnCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing || e.RowIndex < 0) return;
        var key = _grid.Rows[e.RowIndex].Cells[0].Value?.ToString();
        var value = _grid.Rows[e.RowIndex].Cells[1].Value?.ToString();
        if (key == null) return;

        if (!_allStrings.ContainsKey(_currentLang))
            _allStrings[_currentLang] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _allStrings[_currentLang][key] = value ?? "";
        Status("LangMgr_ModifiedStatus", "Modified: {0}", key);
        OnSelectionChanged(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_refreshing || _grid.CurrentRow == null) return;
        var key = _grid.CurrentRow.Cells[0].Value?.ToString();
        if (key == null) { _previewBox.Clear(); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(key);
        sb.AppendLine(new string('─', 60));
        foreach (var lang in _allStrings.OrderBy(l => l.Key))
        {
            var val = lang.Value.TryGetValue(key, out var v) ? v : "(missing)";
            sb.AppendLine($"{lang.Key,-6} │ {val}");
        }
        _previewBox.Text = sb.ToString();
    }

    private void AddLanguage()
    {
        var code = Prompt(L("LangMgr_CodePrompt", "Enter language code (e.g., it, ko, ru):"), L("LangMgr_AddLanguage", "Add language…"));
        if (string.IsNullOrWhiteSpace(code)) return;

        code = code.Trim().ToLowerInvariant();
        if (!ValidCode(code)) { Message(L("LangMgr_InvalidCode", "Use a language code containing letters, digits and hyphens.")); return; }
        if (_allStrings.ContainsKey(code))
        {
            Message(string.Format(L("LangMgr_LanguageExists", "Language '{0}' already exists."), code), L("Common_Info", "Info"));
            return;
        }

        // Copy English strings as template (values left empty for translation)
        var copyFromEn = Message(L("LangMgr_CopyTemplate", "Copy English keys as template?"), L("LangMgr_Template", "Template"), MessageBoxButtons.YesNo) == DialogResult.Yes;
        if (copyFromEn && _allStrings.TryGetValue("en", out var enStrings))
            _allStrings[code] = new Dictionary<string, string>(enStrings.ToDictionary(k => k.Key, _ => ""), StringComparer.OrdinalIgnoreCase);
        else
            _allStrings[code] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _langSelector.Items.Add(code);
        _langSelector.SelectedItem = code;
        Status("LangMgr_AddedLanguageStatus", "Added language: {0} with {1:N0} keys. Translate and Save all.", code, _allStrings[code].Count);
    }

    private void DuplicateLanguage()
    {
        _grid.EndEdit();
        var source = _langSelector.SelectedItem?.ToString();
        if (source == null) return;
        var code = Prompt(string.Format(LanguageManager.CurrentCulture, L("LangMgr_DuplicatePrompt", "Duplicate {0} as (enter new code):"), source), L("LangMgr_DuplicateLanguage", "Duplicate language…"));
        if (string.IsNullOrWhiteSpace(code)) return;

        code = code.Trim().ToLowerInvariant();
        if (!ValidCode(code)) { Message(L("LangMgr_InvalidCode", "Use a language code containing letters, digits and hyphens.")); return; }
        if (_allStrings.ContainsKey(code)) { Message(string.Format(L("LangMgr_CodeExists", "'{0}' already exists."), code)); return; }

        _allStrings[code] = new Dictionary<string, string>(_allStrings[source], StringComparer.OrdinalIgnoreCase);
        _langSelector.Items.Add(code);
        _langSelector.SelectedItem = code;
        Status("LangMgr_DuplicatedStatus", "Duplicated {0} → {1} ({2:N0} keys).", source, code, _allStrings[code].Count);
    }

    private void RemoveLanguage()
    {
        var code = _langSelector.SelectedItem?.ToString();
        if (code == null || code == "en") { Message(L("LangMgr_CannotRemoveEnglish", "Cannot remove English (base language).")); return; }
        if (Message(string.Format(L("LangMgr_ConfirmRemoveLanguage", "Remove language '{0}' and all its translations?"), code), L("Common_Confirm", "Confirm"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        var file = Path.Combine(LangDir, $"Strings_{code}.resx");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", "resx delete failed", ex); Message(string.Format(L("LangMgr_SaveError", "Error saving {0}: {1}"), code, ex.Message)); return; }
        _allStrings.Remove(code);
        _langSelector.SelectedItem = "en";
        _langSelector.Items.Remove(code);
        Status("LangMgr_RemovedLanguageStatus", "Removed language: {0}", code);
    }

    private void AddKey()
    {
        var key = Prompt(L("LangMgr_KeyPrompt", "Enter new string key:"), L("LangMgr_AddKey", "Add key…"));
        if (string.IsNullOrWhiteSpace(key)) return;

        foreach (var lang in _allStrings.Values)
            lang.TryAdd(key, "");

        ShowLanguage(_currentLang);
        Status("LangMgr_AddedKeyStatus", "Added key: {0}", key);
    }

    private void RemoveKey()
    {
        if (_refreshing || _grid.CurrentRow == null) return;
        var key = _grid.CurrentRow.Cells[0].Value?.ToString();
        if (key == null) return;
        if (Message(string.Format(L("LangMgr_ConfirmRemoveKey", "Remove key '{0}' from ALL languages?"), key), L("Common_Confirm", "Confirm"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        foreach (var lang in _allStrings.Values)
            lang.Remove(key);

        ShowLanguage(_currentLang);
        Status("LangMgr_RemovedKeyStatus", "Removed key: {0}", key);
    }

    internal void SaveAllLanguages()
    {
        _grid.EndEdit();
        int saved = 0;
        foreach (var (code, strings) in _allStrings)
        {
            try
            {
                var doc = new XDocument(
                    new XElement("root",
                        new XElement("resheader", new XAttribute("name", "resmimetype"), new XElement("value", "text/microsoft-resx")),
                        new XElement("resheader", new XAttribute("name", "version"), new XElement("value", "2.0")),
                        new XElement("resheader", new XAttribute("name", "reader"), new XElement("value", "System.Resources.ResXResourceReader, System.Windows.Forms")),
                        new XElement("resheader", new XAttribute("name", "writer"), new XElement("value", "System.Resources.ResXResourceWriter, System.Windows.Forms")),
                        strings.Select(kv =>
                            new XElement("data", new XAttribute("name", kv.Key), new XAttribute(XNamespace.Xml + "space", "preserve"),
                                new XElement("value", kv.Value)))
                    )
                );

                var path = Path.Combine(LangDir, $"Strings_{code}.resx");
                doc.Save(path);
                saved++;
            }
            catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", "resx save failed", ex); Message(string.Format(L("LangMgr_SaveError", "Error saving {0}: {1}"), code, ex.Message)); }
        }

        Status("LangMgr_FilesSaved", "{0} language files saved.", saved);
        Message(string.Format(L("LangMgr_FilesSaved", "{0} language files saved."), saved), L("Common_Done", "Done"));
    }

    private void ExportToCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "CSV Files|*.csv", FileName = "beep_translations.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try { ExportCsv(dlg.FileName); }
        catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", "CSV export failed", ex); Message(ex.Message, L("Common_Error", "Error")); }
    }

    internal void ExportCsv(string path)
    {
        _grid.EndEdit();
        var langs = _allStrings.Keys.OrderBy(k => k).ToList();
        var allKeys = _allStrings.SelectMany(kv => kv.Value.Keys).Distinct().OrderBy(k => k).ToList();

        using var writer = new StreamWriter(path);
        writer.WriteLine("Key," + string.Join(",", langs));
        foreach (var key in allKeys)
        {
            var values = langs.Select(l => _allStrings[l].TryGetValue(key, out var v) ? $"\"{v.Replace("\"", "\"\"")}\"" : "");
            writer.WriteLine($"{CsvField(key)},{string.Join(",", values)}");
        }

        Status("LangMgr_ExportedStatus", "Exported to {0}", path);
    }

    private void ImportFromCsv()
    {
        using var dlg = new OpenFileDialog { Filter = "CSV Files|*.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try { ImportCsv(dlg.FileName); }
        catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", "CSV import failed", ex); Message(string.Format(L("LangMgr_ImportError", "Import error: {0}"), ex.Message)); }
    }

    internal void ImportCsv(string path)
    {
        _grid.EndEdit();
        using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new FormatException(L("LangMgr_InvalidCsv", "CSV must have a Key column and unique language codes."));
        var langs = header.Skip(1).ToArray();
        if (header[0] != "Key" || langs.Length == 0 || langs.Any(code => !ValidCode(code)) || langs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != langs.Length)
            throw new FormatException(L("LangMgr_InvalidCsv", "CSV must have a Key column and unique language codes."));
        // Validate the entire file before replacing a single translation value.
        var imported = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in langs) imported.Add(lang, new(StringComparer.OrdinalIgnoreCase));
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            if (fields.Length != header.Length || string.IsNullOrWhiteSpace(fields[0]))
                throw new FormatException(L("LangMgr_InvalidCsv", "CSV must have a Key column and unique language codes."));
            for (var i = 0; i < langs.Length; i++) imported[langs[i]][fields[0]] = fields[i + 1];
        }
        foreach (var (lang, values) in imported)
        {
            if (!_allStrings.TryGetValue(lang, out var current)) _allStrings[lang] = current = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values) current[key] = value;
        }
        _refreshing = true; _langSelector.Items.Clear();
        foreach (var lang in _allStrings.Keys.OrderBy(code => code)) _langSelector.Items.Add(lang);
        _langSelector.SelectedItem = _currentLang; _refreshing = false;
        ShowLanguage(_currentLang);
        Status("LangMgr_ImportedStatus", "Imported from {0}", path);
    }

    private static bool ValidCode(string code) => code.Length is >= 2 and <= 35 && char.IsAsciiLetter(code[0]) && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static string CsvField(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        { _disposed = true; LanguageManager.LanguageChanged -= OnLanguageChanged; _themeBinding?.Dispose(); }
        base.Dispose(disposing);
        if (disposing) { _bodyFont.Dispose(); _headingFont.Dispose(); }
    }
}
