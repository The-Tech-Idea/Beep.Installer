using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class OfflineLayoutBuilderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"beep-layout-{Guid.NewGuid():N}");

    public OfflineLayoutBuilderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Build_CopiesLocalPackageIntoContentAddressedInventory()
    {
        var packagePath = Path.Combine(_tempDir, "vc_redist.x64.exe");
        File.WriteAllText(packagePath, "runtime payload");
        var expectedHash = Sha512(packagePath);
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "vc-redist",
            Name = "VC Runtime",
            SourcePath = "vc_redist.x64.exe",
            Sha512 = expectedHash,
            InstallArgs = "/install /quiet /norestart"
        });
        var layoutDir = Path.Combine(_tempDir, "layout");

        var result = new OfflineLayoutBuilder().Build(project, layoutDir, new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir
        });

        result.Success.Should().BeTrue();
        File.Exists(result.InventoryPath).Should().BeTrue();
        var inventory = JsonSerializer.Deserialize<OfflineLayoutInventory>(File.ReadAllText(result.InventoryPath));
        inventory.Should().NotBeNull();
        var entry = inventory!.Packages.Should().ContainSingle().Subject;
        entry.PackageId.Should().Be("vc-redist");
        entry.Architecture.Should().Be("x64");
        entry.Algorithm.Should().Be("SHA-512");
        entry.ExpectedHash.Should().Be(expectedHash);
        entry.ActualHash.Should().Be(expectedHash);
        entry.ContentAddress.Should().Be($"sha512:{expectedHash}");
        entry.AvailableOffline.Should().BeTrue();
        entry.BlobPath.Should().Contain(Path.Combine("sha512", expectedHash));
        File.Exists(Path.Combine(layoutDir, entry.BlobPath)).Should().BeTrue();
    }

    [Fact]
    public void Build_RejectsRemotePackageWithoutHashForOfflineLayout()
    {
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "vc-redist",
            DownloadUrl = "https://aka.ms/vc14/vc_redist.x64.exe",
            InstallArgs = "/install /quiet /norestart"
        });

        var result = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"));

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI2005");
        var inventory = JsonSerializer.Deserialize<OfflineLayoutInventory>(File.ReadAllText(result.InventoryPath));
        inventory!.Packages.Should().ContainSingle(p =>
            p.PackageId == "vc-redist"
            && p.SourceKind == "remote"
            && !p.AvailableOffline);
    }

    [Fact]
    public void Build_IncludesX86PackageVariantWhenPackageDeclaresX86Url()
    {
        var x64Path = Path.Combine(_tempDir, "runtime.x64.exe");
        var x86Path = Path.Combine(_tempDir, "runtime.x86.exe");
        File.WriteAllText(x64Path, "x64 payload");
        File.WriteAllText(x86Path, "x86 payload");
        var x64Hash = Sha512(x64Path);
        var x86Hash = Sha512(x86Path);
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "dotnet-desktop-runtime",
            SourcePath = "runtime.x64.exe",
            DownloadUrlX86 = new Uri(x86Path).AbsoluteUri,
            Sha512 = x64Hash,
            Sha512X86 = x86Hash,
            InstallArgs = "/install /quiet /norestart"
        });

        var result = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir
        });

        result.Success.Should().BeTrue();
        var inventory = JsonSerializer.Deserialize<OfflineLayoutInventory>(File.ReadAllText(result.InventoryPath));
        inventory!.Packages.Should().HaveCount(2);
        inventory.Packages.Should().Contain(p => p.Architecture == "x64" && p.ActualHash == x64Hash && p.AvailableOffline);
        inventory.Packages.Should().Contain(p => p.Architecture == "x86" && p.ActualHash == x86Hash && p.AvailableOffline);
    }

    [Fact]
    public void Build_CanSignAndVerifyOfflineInventory()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_tempDir, "layout.private.pem");
        var publicKeyPath = Path.Combine(_tempDir, "layout.public.pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
        var packagePath = Path.Combine(_tempDir, "runtime.exe");
        File.WriteAllText(packagePath, "signed runtime payload");
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            SourcePath = "runtime.exe",
            Sha512 = Sha512(packagePath),
            InstallArgs = "/quiet"
        });

        var build = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir,
            SigningPrivateKeyPath = privateKeyPath
        });
        var verify = new OfflineLayoutBuilder().Verify(build.LayoutDirectory, new OfflineLayoutVerificationOptions
        {
            TrustedPublicKeyPath = publicKeyPath
        });

        build.Success.Should().BeTrue();
        build.SignaturePath.Should().EndWith(OfflineLayoutBuilder.SignatureFileName);
        File.Exists(build.SignaturePath).Should().BeTrue();
        verify.Success.Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsTamperedOfflineBlob()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKeyPem();
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var packagePath = Path.Combine(_tempDir, "runtime.exe");
        File.WriteAllText(packagePath, "trusted payload");
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            SourcePath = "runtime.exe",
            Sha512 = Sha512(packagePath),
            InstallArgs = "/quiet"
        });
        var build = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir,
            SigningPrivateKey = privateKey
        });
        var inventory = JsonSerializer.Deserialize<OfflineLayoutInventory>(File.ReadAllText(build.InventoryPath))!;
        var blobPath = Path.Combine(build.LayoutDirectory, inventory.Packages.Single().BlobPath);
        File.WriteAllText(blobPath, "tampered payload");

        var verify = new OfflineLayoutBuilder().Verify(build.LayoutDirectory, new OfflineLayoutVerificationOptions
        {
            TrustedPublicKey = publicKey
        });

        verify.Success.Should().BeFalse();
        verify.Diagnostics.Should().Contain(d => d.Code == "BI2009");
        verify.Diagnostics.Should().Contain(d => d.Code == "BI2010");
    }

    [Fact]
    public void Build_ResumesRemotePackageDownloadAndAppliesSecretBackedAuthHeader()
    {
        var payload = Encoding.UTF8.GetBytes("runtime payload from authenticated remote server");
        using var server = new RangeAwareHttpServer(payload, requiredHeaderName: "X-Layout-Token", requiredHeaderValue: "resolved-token");
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fileName = "runtime.exe";
        var partialDir = Path.Combine(cacheDir, Sha256Text(server.Url));
        Directory.CreateDirectory(partialDir);
        var partialPath = Path.Combine(partialDir, fileName + ".partial");
        File.WriteAllBytes(partialPath, payload.Take(8).ToArray());
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            DownloadUrl = server.Url,
            Sha512 = Sha512(payload),
            InstallArgs = "/quiet"
        });

        var result = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            DownloadRemotePackages = true,
            CacheDirectory = cacheDir,
            HeaderName = "X-Layout-Token",
            HeaderValue = "secret://env/LAYOUT_TOKEN",
            SecretProvider = new FixedSecretProvider("env", "resolved-token")
        });

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code} {d.Path}: {d.Message}")));
        server.AuthorizationSucceeded.Should().BeTrue();
        server.RangeHeader.Should().Be("bytes=8-");
        var inventory = JsonSerializer.Deserialize<OfflineLayoutInventory>(File.ReadAllText(result.InventoryPath));
        var entry = inventory!.Packages.Should().ContainSingle().Subject;
        entry.AcquisitionKind.Should().Be("download");
        entry.ResumedDownload.Should().BeTrue();
        entry.AuthConfigured.Should().BeTrue();
        File.Exists(Path.Combine(result.LayoutDirectory, entry.BlobPath)).Should().BeTrue();
    }

    [Fact]
    public void Build_AppliesCacheRetentionBeforeLayoutGeneration()
    {
        var cacheDir = Path.Combine(_tempDir, "cache-retention");
        var oldDir = Path.Combine(cacheDir, "old");
        Directory.CreateDirectory(oldDir);
        var oldFile = Path.Combine(oldDir, "expired.partial");
        File.WriteAllText(oldFile, "expired");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
        var project = InstallerProjectFactory.CreateNew("LayoutApp", "1.0.0", "ACME", _tempDir);

        var result = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            CacheDirectory = cacheDir,
            CacheRetentionDays = 1
        });

        result.Success.Should().BeTrue();
        File.Exists(oldFile).Should().BeFalse();
        Directory.Exists(oldDir).Should().BeFalse();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string Sha512(string path)
        => Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Sha512(byte[] bytes)
        => Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

    private static string Sha256Text(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _scheme;
        private readonly string? _value;

        public FixedSecretProvider(string scheme, string? value)
        {
            _scheme = scheme;
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => _value is null
                ? SecretResolutionResult.Failed($"Secret '{reference.Name}' is not available.")
                : SecretResolutionResult.Found(_value);
    }

    private sealed class RangeAwareHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _payload;
        private readonly string _requiredHeaderName;
        private readonly string _requiredHeaderValue;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        public RangeAwareHttpServer(byte[] payload, string requiredHeaderName, string requiredHeaderValue)
        {
            _payload = payload;
            _requiredHeaderName = requiredHeaderName;
            _requiredHeaderValue = requiredHeaderValue;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/runtime.exe";
            _task = Task.Run(ServeOne);
        }

        public string Url { get; }
        public string RangeHeader { get; private set; } = "";
        public bool AuthorizationSucceeded { get; private set; }

        private async Task ServeOne()
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                var split = line.IndexOf(':');
                if (split > 0)
                    headers[line[..split].Trim()] = line[(split + 1)..].Trim();
            }

            headers.TryGetValue("Range", out var rangeHeader);
            RangeHeader = rangeHeader ?? "";
            AuthorizationSucceeded = headers.TryGetValue(_requiredHeaderName, out var headerValue)
                                     && headerValue == _requiredHeaderValue;
            if (!AuthorizationSucceeded)
            {
                await WriteResponse(stream, "401 Unauthorized", Array.Empty<byte>());
                return;
            }

            var start = 0;
            if (RangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
                && RangeHeader.EndsWith("-", StringComparison.Ordinal)
                && int.TryParse(RangeHeader["bytes=".Length..^1], out var parsed))
            {
                start = parsed;
            }

            var body = _payload.Skip(start).ToArray();
            var status = start > 0 ? "206 Partial Content" : "200 OK";
            var extra = start > 0
                ? $"Content-Range: bytes {start}-{_payload.Length - 1}/{_payload.Length}\r\n"
                : "";
            await WriteResponse(stream, status, body, extra);
        }

        private static async Task WriteResponse(Stream stream, string status, byte[] body, string extraHeaders = "")
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\n{extraHeaders}Connection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try { _task.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
        }
    }
}
