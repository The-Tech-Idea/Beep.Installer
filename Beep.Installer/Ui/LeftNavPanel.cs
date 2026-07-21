using System;
using System.Drawing;
using System.Windows.Forms;

namespace Beep.Installer.Ui;

/// <summary>
/// Sectioned left navigation panel. Shows a ListView with grouped items; fires
/// <see cref="SectionSelected"/> when the user clicks a leaf item.
/// </summary>
public sealed class LeftNavPanel : Panel
{
    private readonly ListView _list;
    private int _selIdx = -2;

    /// <summary>Fires the section id when a leaf is selected.</summary>
    public event EventHandler<string>? SectionSelected;

    public LeftNavPanel()
    {
        BackColor = InstallerTheme.Sidebar;
        Padding = new Padding(8, 10, 8, 10);
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            HideSelection = false,
            ShowGroups = true,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.None,
            BackColor = InstallerTheme.Sidebar,
            ForeColor = InstallerTheme.Text,
        };
        _list.Columns.Add("", 200);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndices.Count == 0) return;
            var idx = _list.SelectedIndices[0];
            if (idx == _selIdx) return;
            _selIdx = idx;
            var item = _list.Items[idx];
            if (item?.Tag is string id) SectionSelected?.Invoke(this, id);
        };
        Controls.Add(_list);
    }

    /// <summary>Add a section group. Returns the group for subsequent <see cref="AddItem"/> calls.</summary>
    public ListViewGroup AddSection(string id, string label)
    {
        var g = new ListViewGroup(id, label);
        _list.Groups.Add(g);
        return g;
    }

    /// <summary>Add a leaf item to a section group. The id is what fire on <see cref="SectionSelected"/>.</summary>
    public void AddItem(ListViewGroup group, string id, string label, string? hint = null)
    {
        var item = new ListViewItem(label, group) { Tag = id, ToolTipText = hint ?? label };
        _list.Items.Add(item);
    }

    /// <summary>Select a section programmatically.</summary>
    public void SelectSection(string id)
    {
        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i].Tag is string s && s == id)
            {
                _list.Items[i].Selected = true;
                return;
            }
        }
    }
}
