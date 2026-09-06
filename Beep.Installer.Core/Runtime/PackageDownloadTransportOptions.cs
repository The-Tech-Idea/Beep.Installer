using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine;

/// <summary>Runtime-only credentials and proxy settings for the shared package downloader.</summary>
public sealed class PackageDownloadTransportOptions
{
    public string AuthorizationOrigin { get; init; } = "";
    [JsonIgnore] public string BearerTokenReference { get; init; } = "";
    public string ProxyUri { get; init; } = "";
    [JsonIgnore] public string ProxyUsername { get; init; } = "";
    [JsonIgnore] public string ProxyPasswordReference { get; init; } = "";
    [JsonIgnore] public ISecretProvider SecretProvider { get; init; } = new CompositeSecretProvider();

    /// <summary>Configure an unsent request and its unused handler; never returns resolved secrets.</summary>
    public void Configure(HttpClientHandler handler, HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(request);
        var destination = request.RequestUri;
        if (destination is null || !destination.IsAbsoluteUri)
            throw new ArgumentException("An absolute download destination is required.");
        if (!string.IsNullOrWhiteSpace(BearerTokenReference))
        {
            if (!SecretReference.IsReference(BearerTokenReference) || !SecretReference.TryParse(BearerTokenReference, out _, out _))
                throw new ArgumentException("Download credentials must use a secret reference, not a literal value.");
            var origin = ParseEndpoint(AuthorizationOrigin, "Authorization origin", requireHttps: true);
            handler.AllowAutoRedirect = false;
            if (destination.Scheme == origin.Scheme && destination.IdnHost == origin.IdnHost && destination.Port == origin.Port)
            {
                var token = Resolve(BearerTokenReference);
                if (token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
                    throw new ArgumentException("Resolved bearer credential has invalid characters.");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        else if (!string.IsNullOrWhiteSpace(AuthorizationOrigin))
            throw new ArgumentException("Authorization origin requires a bearer secret reference.");

        if (!string.IsNullOrWhiteSpace(ProxyUri))
        {
            var authenticated = !string.IsNullOrWhiteSpace(ProxyUsername) || !string.IsNullOrWhiteSpace(ProxyPasswordReference);
            var endpoint = ParseEndpoint(ProxyUri, "Proxy endpoint", requireHttps: authenticated);
            var proxy = new WebProxy(endpoint);
            if (authenticated)
            {
                if (string.IsNullOrWhiteSpace(ProxyUsername) || string.IsNullOrWhiteSpace(ProxyPasswordReference))
                    throw new ArgumentException("Proxy authentication requires both username and password secret reference.");
                proxy.Credentials = new NetworkCredential(ProxyUsername, Resolve(ProxyPasswordReference));
            }
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        else if (!string.IsNullOrWhiteSpace(ProxyUsername) || !string.IsNullOrWhiteSpace(ProxyPasswordReference))
            throw new ArgumentException("Proxy credentials require an explicit proxy endpoint.");
    }

    private static Uri ParseEndpoint(string value, string label, bool requireHttps)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (requireHttps ? uri.Scheme != "https" : uri.Scheme is not ("https" or "http"))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException(label + " must be an HTTP(S) origin without credentials, path, query or fragment; credentials require HTTPS.");
        return uri;
    }

    private string Resolve(string value)
    {
        if (!SecretReference.IsReference(value) || !SecretReference.TryParse(value, out var reference, out _))
            throw new ArgumentException("Download credentials must use a secret reference, not a literal value.");
        SecretResolutionResult resolved;
        try { resolved = SecretProvider.Resolve(reference); }
        catch { throw new ArgumentException("Download credential could not be resolved."); }
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.Value))
            throw new ArgumentException("Download credential could not be resolved.");
        return resolved.Value;
    }
}
