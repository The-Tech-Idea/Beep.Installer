using System.ComponentModel;
using Beep.Installer.Engine;

namespace Beep.Installer.Ui;

/// <summary>Edits input values in place without replacing the owning resource or its dictionary.</summary>
internal sealed class DictionaryInputProperties : CustomTypeDescriptor
{
    private readonly IDictionary<string, string> _values;
    private readonly string? _requested;
    private readonly HashSet<string> _sensitive;

    internal DictionaryInputProperties(IDictionary<string, string> values, string? requested, IEnumerable<string>? sensitive)
    {
        _values = values; _requested = requested;
        _sensitive = new(sensitive ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public override PropertyDescriptorCollection GetProperties() => GetProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var keys = _values.Keys.Concat(_requested is null ? Array.Empty<string>() : new[] { _requested })
            .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal);
        return new(keys.Select(key => new InputProperty(_values, key,
            _sensitive.Contains(key) || AuthoringHistory.Sensitive(_values, key, _values.TryGetValue(key, out var value) ? value : "")))
            .Cast<PropertyDescriptor>().ToArray(), true);
    }
    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;

    private sealed class InputProperty : PropertyDescriptor
    {
        private readonly IDictionary<string, string> _values;
        internal InputProperty(IDictionary<string, string> values, string key, bool sensitive)
            : base(key, new Attribute[] { new PasswordPropertyTextAttribute(sensitive) }) => _values = values;
        public override Type ComponentType => typeof(DictionaryInputProperties);
        public override Type PropertyType => typeof(string);
        public override bool IsReadOnly => _values.IsReadOnly;
        public override object GetValue(object? component) => _values.TryGetValue(Name, out var value) ? value : "";
        public override void SetValue(object? component, object? value)
        {
            _values[Name] = value as string ?? "";
            OnValueChanged(component, EventArgs.Empty);
        }
        public override bool CanResetValue(object component) => false;
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
