using System.Net;
using System.Net.Http;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility.Providers;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class PackageDownloadTransportTests
{
    [Theory]
    [InlineData("https://updates.example.test/file", true)]
    [InlineData("https://cdn.example.test/file", false)]
    [InlineData("https://updates.example.test:8443/file", false)]
    [InlineData("http://updates.example.test/file", false)]
    public void Bearer_IsResolvedOnlyForExplicitHttpsOrigin(string destination, bool authorized)
    {
        var secrets = new TestSecrets();
        var options = new PackageDownloadTransportOptions
        {
            AuthorizationOrigin = "https://updates.example.test", BearerTokenReference = "env:TEST_TOKEN",
            SecretProvider = secrets
        };
        using var handler = new HttpClientHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, destination);
        options.Configure(handler, request);
        handler.AllowAutoRedirect.Should().BeFalse();
        secrets.Calls.Should().Be(authorized ? 1 : 0);
        request.Headers.Authorization?.Parameter.Should().Be("test-credential");
        if (!authorized) request.Headers.Authorization.Should().BeNull();
        JsonSerializer.Serialize(options).Should().NotContain("TEST_TOKEN").And.NotContain("test-credential");
    }

    [Theory]
    [InlineData("http://updates.example.test", "env:TEST_TOKEN")]
    [InlineData("https://updates.example.test/path", "env:TEST_TOKEN")]
    [InlineData("https://updates.example.test", "literal-credential")]
    [InlineData("https://updates.example.test", "env:")]
    public void InvalidAuthConfigurationFailsWithoutExposingSecrets(string origin, string reference)
    {
        using var handler = new HttpClientHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://updates.example.test/file");
        Action configure = () => new PackageDownloadTransportOptions
        {
            AuthorizationOrigin = origin, BearerTokenReference = reference, SecretProvider = new TestSecrets()
        }.Configure(handler, request);
        configure.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("literal-credential");
        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public void ProviderFailure_DoesNotExposeProviderErrorOrResolvedValue()
    {
        using var handler = new HttpClientHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://updates.example.test/file");
        Action configure = () => new PackageDownloadTransportOptions
        {
            AuthorizationOrigin = "https://updates.example.test", BearerTokenReference = "env:TOKEN",
            SecretProvider = new TestSecrets { Fail = true }
        }.Configure(handler, request);
        configure.Should().Throw<ArgumentException>().Which.ToString().Should().NotContain("sensitive-provider-detail");
    }

    [Fact]
    public void ProxyCredentials_AreScopedToExplicitSecureProxy()
    {
        using var handler = new HttpClientHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://updates.example.test/file");
        new PackageDownloadTransportOptions
        {
            ProxyUri = "https://proxy.example.test:8443", ProxyUsername = "proxy-user",
            ProxyPasswordReference = "env:PROXY_PASSWORD", SecretProvider = new TestSecrets()
        }.Configure(handler, request);
        var proxy = handler.Proxy.Should().BeOfType<WebProxy>().Subject;
        proxy.Address.Should().Be(new Uri("https://proxy.example.test:8443"));
        proxy.Credentials!.GetCredential(proxy.Address!, "Basic")!.Password.Should().Be("test-credential");
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains("Proxy-Authorization").Should().BeFalse();
    }

    [Fact]
    public void SharedDownloader_UsesConfiguredProxy()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepProxyTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "package.bin"), "proxy-package");
            using var proxy = new UpdateChannelFeedPackageServiceTests.DeltaHttpServer(root, "remote");
            var destination = Path.Combine(root, "download.bin");
            new PackageAcquisitionStore().Acquire("http://package.invalid/package.bin", destination, 0,
                CancellationToken.None, 100, false, new PackageDownloadTransportOptions { ProxyUri = proxy.Url });
            File.ReadAllText(destination).Should().Be("proxy-package");
            proxy.RequestCount.Should().Be(1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("http://proxy.example.test")]
    [InlineData("")]
    public void ProxyCredentials_RequireExplicitHttpsEndpoint(string proxy)
    {
        using var handler = new HttpClientHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://updates.example.test/file");
        var secrets = new TestSecrets();
        Action configure = () => new PackageDownloadTransportOptions
        {
            ProxyUri = proxy, ProxyUsername = "user", ProxyPasswordReference = "env:PASSWORD", SecretProvider = secrets
        }.Configure(handler, request);
        configure.Should().Throw<ArgumentException>();
        secrets.Calls.Should().Be(0);
    }

    private sealed class TestSecrets : ISecretProvider
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public bool Supports(string scheme) => true;
        public SecretResolutionResult Resolve(SecretReference reference)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("sensitive-provider-detail");
            return SecretResolutionResult.Found("test-credential");
        }
    }
}
