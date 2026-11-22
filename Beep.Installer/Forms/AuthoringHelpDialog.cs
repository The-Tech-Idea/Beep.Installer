using Beep.Installer.Lang;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Forms;

/// <summary>Packaged, offline authoring guidance. No project graph or credential values are read.</summary>
internal sealed class AuthoringHelpDialog : Form
{
    private readonly TextBox _search = new() { Name = "HelpSearch", Dock = DockStyle.Top };
    private readonly ListView _topics = new() { Name = "HelpTopics", Dock = DockStyle.Fill, View = View.Details,
        FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
    private readonly TextBox _body = new() { Name = "HelpBody", ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true };
    private readonly Button _close = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Font _font = InstallerTheme.Body;
    private string _selectedId;
    private bool _rebuilding;

    internal AuthoringHelpDialog(string? context, bool focusSearch = false)
    {
        _selectedId = AuthoringHelpCatalog.Find(context).Id; Font = _font;
        Size = new(1000, 650); MinimumSize = new(700, 440); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 38)); layout.ColumnStyles.Add(new(SizeType.Percent, 62));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        _topics.Columns.Add("", 150); _topics.Columns.Add("", 120);
        layout.Controls.Add(_search, 0, 0); layout.SetColumnSpan(_search, 2);
        layout.Controls.Add(_topics, 0, 1); layout.Controls.Add(_body, 1, 1);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _close.MinimumSize = new(0, InstallerTheme.ActionMinHeight); actions.Controls.Add(_close);
        layout.Controls.Add(actions, 0, 2); layout.SetColumnSpan(actions, 2); Controls.Add(layout); CancelButton = _close;
        _search.TextChanged += (_, _) => RebuildTopics();
        _topics.SelectedIndexChanged += (_, _) =>
        {
            if (_rebuilding || _topics.SelectedItems.Count == 0) return;
            _selectedId = (string)_topics.SelectedItems[0].Tag!; ShowTopic();
        };
        _topics.ClientSizeChanged += (_, _) =>
        {
            var width = Math.Max(1, _topics.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            _topics.Columns[1].Width = Math.Min(120, width / 2); _topics.Columns[0].Width = Math.Max(1, width - _topics.Columns[1].Width);
        };
        LanguageManager.LanguageChanged += OnLanguageChanged; BeepThemesManager.ThemeChanged += OnThemeChanged;
        Shown += (_, _) => { RebuildTopics(); if (focusSearch) _search.Focus(); else _body.Focus(); };
        Translate(); ApplyTheme(); Engine.Accessibility.ApplyAutoNames(this);
    }

    private void Translate()
    {
        Text = LanguageManager.GetString("Help_Title"); _search.PlaceholderText = _search.AccessibleName = LanguageManager.GetString("Help_Search");
        _topics.AccessibleName = LanguageManager.GetString("Help_Topics"); _body.AccessibleName = Text;
        _topics.Columns[0].Text = LanguageManager.GetString("Help_Topics"); _topics.Columns[1].Text = LanguageManager.GetString("Help_Views");
        _close.Text = _close.AccessibleName = LanguageManager.GetString("Btn_Close");
        RightToLeft = LanguageManager.CurrentCulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No; RightToLeftLayout = RightToLeft == RightToLeft.Yes;
        _topics.RightToLeftLayout = RightToLeft == RightToLeft.Yes; RebuildTopics();
    }

    private void RebuildTopics()
    {
        _rebuilding = true; _topics.BeginUpdate();
        try
        {
            _topics.Items.Clear();
            foreach (var topic in AuthoringHelpCatalog.Topics.Where(topic => AuthoringHelpCatalog.Matches(topic, _search.Text)))
            {
                var item = new ListViewItem(topic.Title) { Tag = topic.Id, ToolTipText = topic.Title + Environment.NewLine + topic.Availability };
                item.SubItems.Add(LanguageManager.GetString(topic.GuidedKey is null ? "Help_Advanced" : "Help_GuidedAdvanced")); _topics.Items.Add(item);
                if (topic.Id == _selectedId) item.Selected = true;
            }
            if (_topics.SelectedItems.Count == 0 && _topics.Items.Count > 0)
            { _topics.Items[0].Selected = true; _selectedId = (string)_topics.Items[0].Tag!; }
        }
        finally { _topics.EndUpdate(); _rebuilding = false; }
        ShowTopic();
    }

    private void ShowTopic()
    {
        if (_topics.Items.Count == 0) { _body.Text = LanguageManager.GetString("Help_NoMatches"); return; }
        var topic = AuthoringHelpCatalog.Find(_selectedId);
        _body.Text = string.Join(Environment.NewLine + Environment.NewLine, topic.Title, topic.Availability, topic.Body, LanguageManager.GetString("Help_Keyboard"));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; } Translate();
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnThemeChanged(sender, e)); return; } ApplyTheme();
    }
    private void ApplyTheme()
    {
        BackColor = InstallerTheme.Panel; ForeColor = InstallerTheme.Text;
        foreach (Control control in new Control[] { _search, _topics, _body, _close })
        { control.BackColor = InstallerTheme.Panel; control.ForeColor = InstallerTheme.Text; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { LanguageManager.LanguageChanged -= OnLanguageChanged; BeepThemesManager.ThemeChanged -= OnThemeChanged; }
        base.Dispose(disposing); if (disposing) _font.Dispose();
    }
}
