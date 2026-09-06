using System;
using System.Collections.Generic;

namespace Beep.Installer.Engine;

public readonly record struct SecretReference(string Scheme, string Name)
{
    public static bool IsReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("secret://", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? value, out SecretReference reference, out string? error)
    {
        reference = default;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Secret reference is empty.";
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith("secret://", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = text["secret://".Length..];
            var slash = remainder.IndexOf('/');
            if (slash <= 0 || slash == remainder.Length - 1)
            {
                error = "Secret URI must use secret://<provider>/<name>.";
                return false;
            }

            var scheme = remainder[..slash].Trim();
            var name = Uri.UnescapeDataString(remainder[(slash + 1)..]).Trim();
            return Complete(scheme, name, out reference, out error);
        }

        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1)
        {
            error = "Secret reference must use <provider>:<name> or secret://<provider>/<name>.";
            return false;
        }

        return Complete(text[..colon].Trim(), text[(colon + 1)..].Trim(), out reference, out error);
    }

    private static bool Complete(string scheme, string name, out SecretReference reference, out string? error)
    {
        reference = default;
        error = null;

        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = "Secret provider is empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Secret name is empty.";
            return false;
        }

        reference = new SecretReference(scheme.ToLowerInvariant(), name);
        return true;
    }
}

public sealed class SecretResolutionResult
{
    public bool Success { get; init; }
    public string? Value { get; init; }
    public string? Error { get; init; }

    public static SecretResolutionResult Found(string value) => new() { Success = true, Value = value };
    public static SecretResolutionResult Failed(string error) => new() { Success = false, Error = error };
}

public interface ISecretProvider
{
    bool Supports(string scheme);
    SecretResolutionResult Resolve(SecretReference reference);
}

public sealed class EnvironmentSecretProvider : ISecretProvider
{
    public bool Supports(string scheme)
        => scheme.Equals("env", StringComparison.OrdinalIgnoreCase);

    public SecretResolutionResult Resolve(SecretReference reference)
    {
        var value = Environment.GetEnvironmentVariable(reference.Name);
        return value is null
            ? SecretResolutionResult.Failed($"Environment variable '{reference.Name}' is not set.")
            : SecretResolutionResult.Found(value);
    }
}

public sealed class CompositeSecretProvider : ISecretProvider
{
    private readonly IReadOnlyList<ISecretProvider> _providers;

    public CompositeSecretProvider(params ISecretProvider[] providers)
        => _providers = providers.Length == 0
            ? new ISecretProvider[] { new EnvironmentSecretProvider() }
            : providers;

    public bool Supports(string scheme)
        => _providers.Any(p => p.Supports(scheme));

    public SecretResolutionResult Resolve(SecretReference reference)
    {
        var provider = _providers.FirstOrDefault(p => p.Supports(reference.Scheme));
        return provider is null
            ? SecretResolutionResult.Failed($"Secret provider '{reference.Scheme}' is not registered.")
            : provider.Resolve(reference);
    }
}
