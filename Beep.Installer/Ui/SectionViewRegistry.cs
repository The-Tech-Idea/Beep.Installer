namespace Beep.Installer.Ui;

/// <summary>Section factories shared by the authoring shells. The editor owns cached views.</summary>
internal sealed class SectionViewRegistry
{
    private readonly Dictionary<string, Func<Panel?>> _factories = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<string> Ids => _factories.Keys.ToArray();

    internal void Register(string id, Func<Panel?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(id, factory)) throw new ArgumentException("A section with this id is already registered.", nameof(id));
    }

    internal Panel? GetView(string id) => _factories.TryGetValue(id, out var factory) ? factory() : null;
}
