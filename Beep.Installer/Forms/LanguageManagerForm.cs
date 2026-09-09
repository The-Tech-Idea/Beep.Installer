using System.Globalization;
using System.Resources;
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
    private Button _addLangBtn = null!;
    private Button _removeLangBtn = null!;
    private Button _addKeyBtn = null!;
    private Button _removeKeyBtn = null!;
    private Button _saveBtn = null!;
    private Button _exportBtn = null!;
    private Button _importBtn = null!;
    private Label _statusLabel = null!;
    private RichTextBox _previewBox = null!;

    private readonly Dictionary<string, Dictionary<string, string>> _allStrings = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLang = "en";
    private static readonly string LangDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lang");

    public LanguageManagerForm()
    {
        Text = L("LangMgr_LanguageManagerBeepInstaller", "Language Manager — Beep Installer");
        Size = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        // Absolute pixel sizes below require DPI auto-scaling, or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        InitializeUI();
        LoadAllLanguages();
        ShowLanguage("en");
        Engine.Accessibility.Attach(this);
    }

    private void InitializeUI()
    {
        // ── Top toolbar ──────────────────────────────────────
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(240, 240, 240) };

        _langSelector = new ComboBox { Location = new Point(8, 8), Size = new Size(150, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        _langSelector.SelectedIndexChanged += (_, _) => ShowLanguage(_langSelector.SelectedItem?.ToString() ?? "en");

        _addLangBtn = new Button { Text = L("LangMgr_Language", "+ Language"), Location = new Point(165, 7), Size = new Size(95, 26) };
        _addLangBtn.Click += (_, _) => AddLanguage();

        _removeLangBtn = new Button { Text = L("LangMgr_Language", "- Language"), Location = new Point(265, 7), Size = new Size(95, 26) };
        _removeLangBtn.Click += (_, _) => RemoveLanguage();

        var dupBtn = new Button { Text = L("LangMgr_Dup", "📋 Dup"), Location = new Point(365, 7), Size = new Size(55, 26) };
        dupBtn.Click += (_, _) => DuplicateLanguage();

        _addKeyBtn = new Button { Text = L("LangMgr_Key", "+ Key"), Location = new Point(425, 7), Size = new Size(70, 26) };
        _addKeyBtn.Click += (_, _) => AddKey();

        _removeKeyBtn = new Button { Text = L("LangMgr_Key", "- Key"), Location = new Point(500, 7), Size = new Size(70, 26) };
        _removeKeyBtn.Click += (_, _) => RemoveKey();

        _saveBtn = new Button { Text = L("LangMgr_SaveAll", "💾 Save All"), Location = new Point(580, 7), Size = new Size(85, 26), BackColor = Color.FromArgb(41, 98, 255), ForeColor = Color.White };
        _saveBtn.Click += (_, _) => SaveAllLanguages();

        _exportBtn = new Button { Text = L("LangMgr_Export", "📤 Export"), Location = new Point(675, 7), Size = new Size(75, 26) };
        _exportBtn.Click += (_, _) => ExportToCsv();

        _importBtn = new Button { Text = L("LangMgr_Import", "📥 Import"), Location = new Point(755, 7), Size = new Size(75, 26) };
        _importBtn.Click += (_, _) => ImportFromCsv();

        toolbar.Controls.AddRange(new Control[] { _langSelector, _addLangBtn, _removeLangBtn, _addKeyBtn, _removeKeyBtn, _saveBtn, _exportBtn, _importBtn });

        // ── Main grid ────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };

        // ── Preview panel ────────────────────────────────────
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 160 };
        _previewBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Segoe UI", 10),
            BackColor = Color.White
        };

        var previewLabel = new Label
        {
            Text = L("LangMgr_LivePreviewSelectA", "Live Preview (select a key to see all translations):"),
            Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8, FontStyle.Bold),
            BackColor = Color.FromArgb(250, 250, 250)
        };
        bottomPanel.Controls.Add(_previewBox);
        bottomPanel.Controls.Add(previewLabel);

        // ── Status bar ───────────────────────────────────────
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom, Height = 22,
            Text = L("LangMgr_Ready", "Ready."),
            BackColor = Color.FromArgb(230, 230, 230), TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(_grid);
        Controls.Add(bottomPanel);
        Controls.Add(toolbar);
        Controls.Add(_statusLabel);
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

        _statusLabel.Text = $"{_allStrings.Count} languages loaded from {LangDir}. Ready.";
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
        _currentLang = langCode;
        // Unsubscribe before clearing to prevent handler leak (subscribed on every language switch)
        _grid.CellValueChanged -= OnCellChanged;
        _grid.SelectionChanged -= OnSelectionChanged;

        _grid.Columns.Clear();
        _grid.Columns.Add("Key", "Key");
        _grid.Columns.Add("Value", $"Value ({langCode})");
        _grid.Columns[0].Width = 200;
        _grid.Columns[1].Width = 500;
        _grid.Rows.Clear();

        if (_allStrings.TryGetValue(langCode, out var strings))
        {
            foreach (var kv in strings.OrderBy(k => k.Key))
                _grid.Rows.Add(kv.Key, kv.Value);
        }

        _grid.CellValueChanged += OnCellChanged;
        _grid.SelectionChanged += OnSelectionChanged;
        _statusLabel.Text = $"Language: {langCode} — {strings?.Count ?? 0} strings";
    }

    private void OnCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var key = _grid.Rows[e.RowIndex].Cells[0].Value?.ToString();
        var value = _grid.Rows[e.RowIndex].Cells[1].Value?.ToString();
        if (key == null) return;

        if (!_allStrings.ContainsKey(_currentLang))
            _allStrings[_currentLang] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _allStrings[_currentLang][key] = value ?? "";
        _statusLabel.Text = $"Modified: {key}";
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow == null) return;
        var key = _grid.CurrentRow.Cells[0].Value?.ToString();
        if (key == null) { _previewBox.Clear(); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Key: {key}");
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
        var code = InputBox.Prompt(this, "Enter language code (e.g., it, ko, ru):", "Add Language", "");
        if (string.IsNullOrWhiteSpace(code)) return;

        code = code.Trim().ToLower();
        if (_allStrings.ContainsKey(code))
        {
            MessageBox.Show(string.Format(L("LangMgr_LanguageExists", "Language '{0}' already exists."), code), L("Common_Info", "Info"));
            return;
        }

        // Copy English strings as template (values left empty for translation)
        var copyFromEn = MessageBox.Show(L("LangMgr_CopyTemplate", "Copy English keys as template?"), L("LangMgr_Template", "Template"), MessageBoxButtons.YesNo) == DialogResult.Yes;
        if (copyFromEn && _allStrings.TryGetValue("en", out var enStrings))
            _allStrings[code] = new Dictionary<string, string>(enStrings.ToDictionary(k => k.Key, _ => ""), StringComparer.OrdinalIgnoreCase);
        else
            _allStrings[code] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _langSelector.Items.Add(code);
        _langSelector.SelectedItem = code;
        _statusLabel.Text = $"Added language: {code} with {_allStrings[code].Count} keys. Translate and Save All.";
    }

    private void DuplicateLanguage()
    {
        var source = _langSelector.SelectedItem?.ToString();
        if (source == null) return;
        var code = InputBox.Prompt(this, $"Duplicate '{source}' as (enter new code):", "Duplicate Language", "");
        if (string.IsNullOrWhiteSpace(code)) return;

        code = code.Trim().ToLower();
        if (_allStrings.ContainsKey(code)) { MessageBox.Show(string.Format(L("LangMgr_CodeExists", "'{0}' already exists."), code)); return; }

        _allStrings[code] = new Dictionary<string, string>(_allStrings[source], StringComparer.OrdinalIgnoreCase);
        _langSelector.Items.Add(code);
        _langSelector.SelectedItem = code;
        _statusLabel.Text = $"Duplicated {source} → {code} ({_allStrings[code].Count} keys).";
    }

    private void RemoveLanguage()
    {
        var code = _langSelector.SelectedItem?.ToString();
        if (code == null || code == "en") { MessageBox.Show(L("LangMgr_CannotRemoveEnglish", "Cannot remove English (base language).")); return; }
        if (MessageBox.Show(string.Format(L("LangMgr_ConfirmRemoveLanguage", "Remove language '{0}' and all its translations?"), code), L("Common_Confirm", "Confirm"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        _allStrings.Remove(code);
        _langSelector.Items.Remove(code);
        _langSelector.SelectedItem = "en";
        var file = Path.Combine(LangDir, $"Strings_{code}.resx");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Engine.Diag.Debug("LanguageManagerForm", "resx delete failed", ex); }
        _statusLabel.Text = $"Removed language: {code}";
    }

    private void AddKey()
    {
        var key = InputBox.Prompt(this, "Enter new string key:", "Add Key", "");
        if (string.IsNullOrWhiteSpace(key)) return;

        foreach (var lang in _allStrings.Values)
            lang.TryAdd(key, "");

        ShowLanguage(_currentLang);
        _statusLabel.Text = $"Added key: {key}";
    }

    private void RemoveKey()
    {
        if (_grid.CurrentRow == null) return;
        var key = _grid.CurrentRow.Cells[0].Value?.ToString();
        if (key == null) return;
        if (MessageBox.Show(string.Format(L("LangMgr_ConfirmRemoveKey", "Remove key '{0}' from ALL languages?"), key), L("Common_Confirm", "Confirm"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        foreach (var lang in _allStrings.Values)
            lang.Remove(key);

        ShowLanguage(_currentLang);
        _statusLabel.Text = $"Removed key: {key}";
    }

    private void SaveAllLanguages()
    {
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
            catch (Exception ex) { MessageBox.Show(string.Format(L("LangMgr_SaveError", "Error saving {0}: {1}"), code, ex.Message)); }
        }

        _statusLabel.Text = $"Saved {saved} language files.";
        MessageBox.Show(string.Format(L("LangMgr_FilesSaved", "{0} language files saved."), saved), L("Common_Done", "Done"));
    }

    private void ExportToCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "CSV Files|*.csv", FileName = "beep_translations.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var langs = _allStrings.Keys.OrderBy(k => k).ToList();
        var allKeys = _allStrings.SelectMany(kv => kv.Value.Keys).Distinct().OrderBy(k => k).ToList();

        using var writer = new StreamWriter(dlg.FileName);
        writer.WriteLine("Key," + string.Join(",", langs));
        foreach (var key in allKeys)
        {
            var values = langs.Select(l => _allStrings[l].TryGetValue(key, out var v) ? $"\"{v.Replace("\"", "\"\"")}\"" : "");
            writer.WriteLine($"{key},{string.Join(",", values)}");
        }

        _statusLabel.Text = $"Exported to {dlg.FileName}";
    }

    private void ImportFromCsv()
    {
        using var dlg = new OpenFileDialog { Filter = "CSV Files|*.csv" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var lines = File.ReadAllLines(dlg.FileName);
            var langs = lines[0].Split(',').Skip(1).Select(l => l.Trim()).ToList();

            foreach (var lang in langs)
                if (!_allStrings.ContainsKey(lang)) _allStrings[lang] = new();

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = ParseCsvLine(lines[i]);
                var key = parts[0];
                for (int j = 0; j < langs.Count && j + 1 < parts.Length; j++)
                    _allStrings[langs[j]][key] = parts[j + 1];
            }

            _langSelector.Items.Clear();
            foreach (var l in _allStrings.Keys.OrderBy(k => k)) _langSelector.Items.Add(l);
            _statusLabel.Text = $"Imported from {dlg.FileName}";
            ShowLanguage(_currentLang);
        }
        catch (Exception ex) { MessageBox.Show(string.Format(L("LangMgr_ImportError", "Import error: {0}"), ex.Message)); }
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
