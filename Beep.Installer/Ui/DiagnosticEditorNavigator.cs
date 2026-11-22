using System.Collections;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Beep.Installer.Engine;

namespace Beep.Installer.Ui;

/// <summary>Resolves canonical rows and nested objects before focusing a property editor.</summary>
internal static class DiagnosticEditorNavigator
{
    private static readonly Regex Segment = new(@"^(?<name>\w+)(?:\[(?<index>\d+)\])?$", RegexOptions.CultureInvariant);

    internal static bool Focus(Control section, string path)
    {
        var controls = Descendants(section).ToArray();
        section.AccessibleDescription = path;
        var segments = path.Split('.');
        var first = Segment.Match(segments[0]);
        if (!first.Success || !int.TryParse(first.Groups["index"].Value, out var index))
        {
            var fieldName = segments.Last();
            var fieldSegment = Segment.Match(fieldName);
            if (first.Groups["name"].Value == "Setup" && fieldSegment.Success && fieldSegment.Groups["index"].Success)
            {
                var patternList = controls.OfType<ListBox>().FirstOrDefault(c => c.Name == fieldSegment.Groups["name"].Value);
                if (patternList is null || !int.TryParse(fieldSegment.Groups["index"].Value, out var fieldIndex) ||
                    fieldIndex < 0 || fieldIndex >= patternList.Items.Count) return false;
                patternList.SelectedIndex = fieldIndex;
                return patternList.Focus();
            }
            var field = controls.FirstOrDefault(c => c.Name == fieldName);
            return field?.Focus() == true;
        }

        var grid = controls.OfType<DataGridView>().FirstOrDefault();
        if (grid?.DataSource is not BindingSource binding || binding.DataSource is not IList list ||
            index < 0 || index >= list.Count) return false;
        var item = list[index];
        var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.DataBoundItem, item));
        if (row is null || item is null) return false;

        var propertyName = segments.Last();
        var column = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible && c.DataPropertyName == propertyName)
            ?? grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
        if (column is null) return false;
        grid.ClearSelection();
        grid.CurrentCell = row.Cells[column.Index];
        row.Selected = true;

        var properties = controls.OfType<PropertyGrid>().FirstOrDefault();
        if (properties is null)
        {
            var field = controls.FirstOrDefault(c => c.Name == propertyName || c.DataBindings.Cast<Binding>()
                .Any(b => b.BindingMemberInfo.BindingField == propertyName));
            return field?.Focus() == true || grid.Focus();
        }
        if (!TryResolve(item, segments.Skip(1).ToArray(), out var owner, out var property))
        { properties.SelectedObject = item; return grid.Focus(); }
        // A nested model is edited directly. Selection is one-way; it must never replace the
        // owning resource through a SelectedObject data binding.
        properties.SelectedObject = owner;
        properties.AccessibleDescription = path;
        if (property is not null && FindProperty(properties, property) is { } target)
            properties.SelectedGridItem = target;
        return properties.Focus();
    }

    internal static bool TryResolve(object item, string[] segments, out object owner, out string? property)
    {
        owner = item;
        property = null;
        IEnumerable<string>? sensitive = null;
        for (var position = 0; position < segments.Length; position++)
        {
            if (owner is IDictionary<string, string> values)
            {
                var key = string.Join('.', segments.Skip(position));
                if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl)) return false;
                owner = new DictionaryInputProperties(values, key, sensitive); property = key;
                return true;
            }
            var match = Segment.Match(segments[position]);
            if (!match.Success) return false;
            var name = match.Groups["name"].Value;
            var descriptor = TypeDescriptor.GetProperties(owner)[name];
            if (descriptor is null) return false;
            if (position == segments.Length - 1 && !match.Groups["index"].Success)
            {
                if (descriptor.GetValue(owner) is IDictionary<string, string> dictionary)
                {
                    owner = new DictionaryInputProperties(dictionary, null,
                        owner is CompiledInstallOperation operation && name == "Inputs" ? operation.SensitiveInputs : null);
                    return true;
                }
                property = name;
                return true;
            }
            var value = descriptor.GetValue(owner);
            sensitive = owner is CompiledInstallOperation resource && name == "Inputs" ? resource.SensitiveInputs : null;
            if (match.Groups["index"].Success)
            {
                if (!int.TryParse(match.Groups["index"].Value, out var index) || value is not IList list ||
                    index < 0 || index >= list.Count) return false;
                value = list[index];
            }
            if (value is null) return false;
            owner = value;
        }
        return true;
    }

    private static GridItem? FindProperty(PropertyGrid properties, string name)
    {
        var root = properties.SelectedGridItem;
        if (root is null) return null;
        while (root.Parent is not null) root = root.Parent;
        GridItem? Search(GridItem item)
        {
            if (item.PropertyDescriptor?.Name == name) return item;
            foreach (GridItem child in item.GridItems)
                if (Search(child) is { } found) return found;
            return null;
        }
        return Search(root);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
