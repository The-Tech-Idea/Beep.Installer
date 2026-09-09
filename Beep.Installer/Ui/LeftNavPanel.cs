using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Beep.Installer.Ui;

/// <summary>
/// Sectioned left navigation panel. Shows a ListView with grouped items; fires
/// <see cref="SectionSelected"/> when the user clicks a leaf item.
/// </summary>
public sealed class LeftNavPanel : Panel
{
    private sealed record NavSection(string Id, string Label);
    private sealed record NavItem(string SectionId, string Id, string Label, string Hint);

    private readonly ListView _list;
    private readonly List<NavSection> _sections = new();
    private readonly List<NavItem> _items = new();
    private string _filterText = "";
    private string _selectedSectionId = "";
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
            if (item?.Tag is string id)
            {
                _selectedSectionId = id;
                SectionSelected?.Invoke(this, id);
            }
        };
        Controls.Add(_list);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string FilterText
    {
        get => _filterText;
        set
        {
            var normalized = (value ?? "").Trim();
            if (string.Equals(_filterText, normalized, StringComparison.OrdinalIgnoreCase))
                return;
            _filterText = normalized;
            RebuildList();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> VisibleSectionIds
        => _list.Items.Cast<ListViewItem>()
            .Select(i => i.Tag as string)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedSectionId
        => _selectedSectionId;

    /// <summary>Add a section group. Returns the group for subsequent <see cref="AddItem"/> calls.</summary>
    public ListViewGroup AddSection(string id, string label)
    {
        _sections.Add(new NavSection(id, label));
        return AddListGroup(id, label);
    }

    /// <summary>Add a leaf item to a section group. The id is what fire on <see cref="SectionSelected"/>.</summary>
    public void AddItem(ListViewGroup group, string id, string label, string? hint = null)
    {
        var sectionId = string.IsNullOrWhiteSpace(group.Name) ? group.Header : group.Name;
        _items.Add(new NavItem(sectionId, id, label, hint ?? label));
        AddListItem(group, id, label, hint ?? label);
    }

    /// <summary>Select a section programmatically.</summary>
    public void SelectSection(string id)
    {
        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i].Tag is string s && s == id)
            {
                _selIdx = i;
                _selectedSectionId = id;

                // A collapsed group hides its rows, so EnsureVisible would scroll to something the
                // user cannot see. Open the owning group first.
                ExpandGroupFor(id);

                _list.Items[i].Selected = true;
                _list.Items[i].EnsureVisible();
                SectionSelected?.Invoke(this, id);
                return;
            }
        }
    }

    public bool SelectFirstMatch()
    {
        if (_list.Items.Count == 0)
            return false;

        _list.Items[0].Selected = true;
        _list.Select();
        return true;
    }

    public bool SelectAdjacentSection(int delta)
    {
        if (_list.Items.Count == 0)
            return false;

        var current = _list.SelectedIndices.Count == 0 ? _selIdx : _list.SelectedIndices[0];
        if (current < 0 && !string.IsNullOrWhiteSpace(_selectedSectionId))
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                if (_list.Items[i].Tag is string selectedId && string.Equals(selectedId, _selectedSectionId, StringComparison.Ordinal))
                {
                    current = i;
                    break;
                }
            }
        }
        var next = current < 0
            ? 0
            : (current + delta + _list.Items.Count) % _list.Items.Count;

        if (_list.Items[next].Tag is not string id)
            return false;

        SelectSection(id);
        _list.Select();
        return true;
    }

    private void RebuildList()
    {
        var selectedId = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null;
        _selIdx = -2;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _list.Groups.Clear();

            foreach (var section in _sections)
            {
                var sectionItems = _items
                    .Where(i => string.Equals(i.SectionId, section.Id, StringComparison.Ordinal))
                    .Where(i => Matches(section, i))
                    .ToList();
                if (sectionItems.Count == 0)
                    continue;

                var group = AddListGroup(section.Id, section.Label);
                foreach (var item in sectionItems)
                    AddListItem(group, item.Id, item.Label, item.Hint);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
            SelectSection(selectedId);
    }

    private bool Matches(NavSection section, NavItem item)
    {
        if (string.IsNullOrWhiteSpace(_filterText))
            return true;

        return Contains(section.Label, _filterText)
            || Contains(item.Id, _filterText)
            || Contains(item.Label, _filterText)
            || Contains(item.Hint, _filterText);
    }

    private static bool Contains(string value, string pattern)
        => value?.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Adds a collapsible group header.
    ///
    /// The nav grew to forty items across eight groups, and a flat Details list showed all of them
    /// at once -- roughly fifty rows in a narrow column, so finding anything meant scrolling past
    /// everything. Groups are collapsible, and everything except the group holding the current
    /// section starts collapsed, so the menu opens as eight headers rather than one long list.
    ///
    /// While a filter is active the groups stay expanded: the point of typing is to see the matches.
    /// </summary>
    private ListViewGroup AddListGroup(string id, string label)
    {
        var group = new ListViewGroup(id, label)
        {
            CollapsedState = ExpandByDefault(id)
                ? ListViewGroupCollapsedState.Expanded
                : ListViewGroupCollapsedState.Collapsed,
        };

        _list.Groups.Add(group);
        return group;
    }

    /// <summary>Which group opens expanded: the one being searched in, or the one in use.</summary>
    private bool ExpandByDefault(string sectionId)
    {
        if (!string.IsNullOrWhiteSpace(_filterText))
            return true;

        // The group holding the selected section, so the current place stays visible.
        var owner = _items.FirstOrDefault(i => string.Equals(i.Id, _selectedSectionId, StringComparison.Ordinal));
        if (owner != null)
            return string.Equals(owner.SectionId, sectionId, StringComparison.Ordinal);

        // Nothing selected yet: open the first group so the panel is not a wall of headers.
        return _sections.Count > 0 && string.Equals(_sections[0].Id, sectionId, StringComparison.Ordinal);
    }

    /// <summary>Expands the group that owns a section, so selecting it can actually show it.</summary>
    private void ExpandGroupFor(string sectionId)
    {
        var owner = _items.FirstOrDefault(i => string.Equals(i.Id, sectionId, StringComparison.Ordinal));
        if (owner == null) return;

        foreach (ListViewGroup group in _list.Groups)
        {
            if (string.Equals(group.Name, owner.SectionId, StringComparison.Ordinal))
                group.CollapsedState = ListViewGroupCollapsedState.Expanded;
        }
    }

    private void AddListItem(ListViewGroup group, string id, string label, string hint)
    {
        var item = new ListViewItem(label, group) { Tag = id, ToolTipText = hint };
        _list.Items.Add(item);
    }
}
