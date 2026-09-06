namespace Beep.Installer.Extensibility;

public sealed class BuiltInResourceProviderRegistry
{
    private readonly Dictionary<string, IResourceProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public BuiltInResourceProviderRegistry Register(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.ResourceType))
            throw new ArgumentException("Provider ResourceType is required.", nameof(provider));

        if (_providers.ContainsKey(provider.ResourceType))
            throw new InvalidOperationException($"Resource provider '{provider.ResourceType}' is already registered.");

        _providers.Add(provider.ResourceType, provider);
        return this;
    }

    public bool TryGet(string resourceType, out IResourceProvider provider)
        => _providers.TryGetValue(resourceType, out provider!);

    public IReadOnlyCollection<IResourceProvider> Providers => _providers.Values;
}
