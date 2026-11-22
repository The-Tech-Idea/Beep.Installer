using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Ui;

namespace Beep.Installer.Forms;

public sealed class RecoveryDraftDialog : Form
{
    private readonly ListView _drafts = new() { Name = "RecoveryDrafts", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Label _help = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button _recover = new() { AutoSize = true, Name = "RecoverDraft" };
    private readonly Button _cancel = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly List<(ListViewItem Item, RecoveryDraftEntry Entry)> _rows = new();
    public RecoveryDraftEntry? SelectedEntry => _drafts.SelectedItems.Count == 1 ? _drafts.SelectedItems[0].Tag as RecoveryDraftEntry : null;
    public string? SelectedRecoveryPath => SelectedEntry?.RecoveryPath;

    public RecoveryDraftDialog(IEnumerable<string> paths) : this(paths.Select(path => new RecoveryDraftEntry(path))) { }

    public RecoveryDraftDialog(IEnumerable<RecoveryDraftEntry> entries)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(850, 480); MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_help, 0, 0); root.Controls.Add(_drafts, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.Add(_recover); buttons.Controls.Add(_cancel); root.Controls.Add(buttons, 0, 2); Controls.Add(root);
        _drafts.Columns.Add("", 180); _drafts.Columns.Add("", 140); _drafts.Columns.Add("", 250); _drafts.Columns.Add("", 300);
        foreach (var entry in entries)
        {
            var path = entry.RecoveryPath;
            var (project, error) = InstallerScriptSerializer.Load(path);
            error = entry.DiscoveryError ?? error;
            if (error is not null) project = null;
            var item = new ListViewItem(project?.ProjectName ?? Path.GetFileName(path)) { Tag = project is null ? null : entry, ToolTipText = error ?? path };
            item.SubItems.Add(File.Exists(path) ? File.GetLastWriteTime(path).ToString("g") : "");
            item.SubItems.Add(entry.DocumentPath ?? L("SaveState_Untitled", "Not saved yet"));
            item.SubItems.Add(error ?? path); _drafts.Items.Add(item);
            _rows.Add((item, entry));
        }
        _drafts.ShowItemToolTips = true;
        _drafts.SelectedIndexChanged += (_, _) => _recover.Enabled = SelectedRecoveryPath is not null;
        _recover.Enabled = false;
        _recover.Click += (_, _) => { if (SelectedRecoveryPath is not null) { DialogResult = DialogResult.OK; Close(); } };
        AcceptButton = _recover; CancelButton = _cancel;
        SizeChanged += (_, _) => _help.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 30), 0);
        LanguageManager.LanguageChanged += Translate;
        Translate(null, EventArgs.Empty);
    }

    private void Translate(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => Translate(sender, e)); return; }
        Text = L("Recovery_Title", "Recover unsaved project");
        _help.Text = L("Recovery_Help", "Choose a draft. Recovery opens it as unsaved; saving keeps the changes. Unreadable drafts remain on disk.");
        _help.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 30), 0);
        _drafts.AccessibleName = Text;
        _drafts.Columns[0].Text = L("Nav_Project", "Project");
        _drafts.Columns[1].Text = L("Recovery_Modified", "Draft time");
        _drafts.Columns[2].Text = L("Recovery_OriginalProject", "Original project");
        _drafts.Columns[3].Text = L("SaveState_RecoveryPath", "Recovery draft");
        foreach (var row in _rows) if (row.Entry.DocumentPath is null) row.Item.SubItems[2].Text = L("SaveState_Untitled", "Not saved yet");
        _recover.Text = _recover.AccessibleName = L("Recovery_Recover", "Recover");
        _cancel.Text = _cancel.AccessibleName = L("Common_Cancel", "Cancel");
        var rtl = RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName);
        RtlHelper.ApplyDirection(this, rtl);
    }
    protected override void Dispose(bool disposing) { if (disposing) LanguageManager.LanguageChanged -= Translate; base.Dispose(disposing); }
    private static string L(string key, string fallback) => LanguageManager.GetOrDefault(key, fallback);
}
