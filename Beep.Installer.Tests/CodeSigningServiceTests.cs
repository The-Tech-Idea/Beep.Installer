using Beep.Installer.Engine;
using Beep.Installer.Engine.Msix;
using Beep.Installer.Models;
using FluentAssertions;
using System.Net;
using System.Text;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class CodeSigningServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CodeSigningServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSigning_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void SignInstaller_ResolvesEnvSecretReferenceAtSigningBoundary()
    {
        var signer = new CapturingSigner();
        var secrets = new FixedSecretProvider("env", "s3cr3t!");
        var service = new CodeSigningService(signer, secrets);

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            Path.Combine(_tempDir, "release.pfx"),
            "secret://env/SIGNING_PFX_PASSWORD",
            "https://timestamp.test",
            expectedSubject: "CN=The Tech Idea");

        result.Success.Should().BeTrue();
        result.PasswordWasSecretReference.Should().BeTrue();
        signer.LastRequest.Should().NotBeNull();
        signer.LastRequest!.CertificatePassword.Should().Be("s3cr3t!");
        signer.LastRequest.ExpectedSubject.Should().Be("CN=The Tech Idea");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void SignInstaller_FailsWhenSecretProviderCannotResolveReference()
    {
        var service = new CodeSigningService(new CapturingSigner(), new FixedSecretProvider("env", null));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            Path.Combine(_tempDir, "release.pfx"),
            "env:MISSING_SIGNING_PASSWORD",
            "https://timestamp.test");

        result.Success.Should().BeFalse();
        result.PasswordWasSecretReference.Should().BeTrue();
        result.Error.Should().Contain("not available");
    }

    [Fact]
    public void SignInstaller_UsesWindowsCertificateStoreSelectorWithoutPfxPassword()
    {
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", "unused"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            certificatePath: "",
            certificatePassword: "secret://env/SHOULD_NOT_RESOLVE_FOR_STORE",
            timestampUrl: "https://timestamp.test",
            expectedSubject: "CN=ACME Release",
            storeName: "My",
            storeLocation: "LocalMachine",
            storeThumbprint: "aa bb cc",
            storeSubject: "");

        result.Success.Should().BeTrue();
        result.PasswordWasSecretReference.Should().BeFalse();
        result.CertificateStoreName.Should().Be("My");
        result.CertificateStoreLocation.Should().Be("LocalMachine");
        result.CertificateStoreThumbprint.Should().Be("AABBCC");
        signer.LastRequest.Should().NotBeNull();
        signer.LastRequest!.CertificatePath.Should().BeEmpty();
        signer.LastRequest.CertificatePassword.Should().Be("secret://env/SHOULD_NOT_RESOLVE_FOR_STORE");
        signer.LastRequest.StoreName.Should().Be("My");
        signer.LastRequest.StoreLocation.Should().Be("LocalMachine");
        signer.LastRequest.StoreThumbprint.Should().Be("AABBCC");
    }

    [Fact]
    public void SignInstaller_PassesTimestampOutagePolicyToSigner()
    {
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            Path.Combine(_tempDir, "release.pfx"),
            "secret://env/SIGNING_PFX_PASSWORD",
            "https://timestamp.test",
            timestampOutagePolicy: "retry",
            timestampRetryCount: 4);

        result.Success.Should().BeTrue();
        result.TimestampOutagePolicy.Should().Be("retry");
        signer.LastRequest.Should().NotBeNull();
        signer.LastRequest!.TimestampOutagePolicy.Should().Be("retry");
        signer.LastRequest.TimestampRetryCount.Should().Be(4);
    }

    [Fact]
    public void SignInstaller_RejectsUnknownTimestampOutagePolicy()
    {
        var service = new CodeSigningService(new CapturingSigner(), new FixedSecretProvider("env", "unused"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            Path.Combine(_tempDir, "release.pfx"),
            null,
            "https://timestamp.test",
            timestampOutagePolicy: "ignore");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Timestamp outage policy");
    }

    [Fact]
    public void SignInstaller_RejectsMixedPfxAndStoreSelectors()
    {
        var service = new CodeSigningService(new CapturingSigner(), new FixedSecretProvider("env", "unused"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            certificatePath: Path.Combine(_tempDir, "release.pfx"),
            certificatePassword: null,
            timestampUrl: "https://timestamp.test",
            storeThumbprint: "AABBCC");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Configure exactly one signing source");
    }

    [Fact]
    public void SignInstaller_ResolvesRemoteCredentialAtSigningBoundary()
    {
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", "remote-token"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            certificatePath: "",
            certificatePassword: null,
            timestampUrl: "https://timestamp.test",
            remoteProvider: "enterprise-hsm",
            remoteEndpoint: "https://signing.example.test/api/sign",
            remoteKeyId: "release-key",
            remoteCredential: "secret://env/REMOTE_SIGNING_TOKEN");

        result.Success.Should().BeTrue();
        result.RemoteCredentialWasSecretReference.Should().BeTrue();
        result.RemoteProvider.Should().Be("enterprise-hsm");
        result.RemoteEndpoint.Should().Be("https://signing.example.test/api/sign");
        result.RemoteKeyId.Should().Be("release-key");
        signer.LastRequest.Should().NotBeNull();
        signer.LastRequest!.RemoteCredential.Should().Be("remote-token");
        signer.LastRequest.RemoteEndpoint.Should().Be("https://signing.example.test/api/sign");
    }

    [Fact]
    public void SignInstaller_RejectsMixedPfxAndRemoteSelectors()
    {
        var service = new CodeSigningService(new CapturingSigner(), new FixedSecretProvider("env", "unused"));

        var result = service.SignInstaller(
            Path.Combine(_tempDir, "Setup.exe"),
            certificatePath: Path.Combine(_tempDir, "release.pfx"),
            certificatePassword: null,
            timestampUrl: "https://timestamp.test",
            remoteEndpoint: "https://signing.example.test/api/sign");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Configure exactly one signing source");
    }

    [Fact]
    public void BuildPipeline_UsesSigningServiceAndDoesNotPersistResolvedPassword()
    {
        var source = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.exe"), "fake");
        var project = InstallerProjectFactory.CreateNew("SignedApp", "1.0.0", "ACME", source).UseTestDefaults();
        project.OutputDir = Path.Combine(_tempDir, "out");
        project.CodeSignCertificatePath = Path.Combine(_tempDir, "release.pfx");
        project.CodeSignCertificatePassword = "secret://env/SIGNING_PFX_PASSWORD";

        var signer = new CapturingSigner();
        var pipeline = TestHelpers.TestPipeline();
        pipeline.SigningService = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password"));

        var result = pipeline.Run(project);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault());
        signer.LastRequest.Should().NotBeNull();
        signer.LastRequest!.CertificatePassword.Should().Be("resolved-password");
        File.ReadAllText(Path.Combine(project.OutputDir, "script.bsetup")).Should().Contain("secret://env/SIGNING_PFX_PASSWORD");
        File.ReadAllText(Path.Combine(project.OutputDir, "script.bsetup")).Should().NotContain("resolved-password");
    }

    [Fact]
    public void BuildPipeline_Signs_Msix_And_AppInstaller_Artifacts_And_Records_Evidence()
    {
        var source = Path.Combine(_tempDir, "msix-src");
        Directory.CreateDirectory(source);
        var appPath = Path.Combine(source, "app.exe");
        File.WriteAllText(appPath, "fake");

        var project = InstallerProjectFactory.CreateNew("SignedMsix", "1.0.0", "ACME", source).UseTestDefaults();
        project.OutputDir = Path.Combine(_tempDir, "msix-out");
        project.OutputFormat = InstallerOutputFormat.Msix;
        project.MsixIdentity = "ACME.SignedMsix";
        project.MsixPublisher = "CN=ACME";
        project.MainExecutable = "app.exe";
        project.AppUpdatesURL = "https://updates.example.test/msix/";
        project.CodeSignCertificatePath = Path.Combine(_tempDir, "release.pfx");
        project.CodeSignCertificatePassword = "secret://env/SIGNING_PFX_PASSWORD";
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files = { new FileCopyOperation { SourcePath = appPath, DestinationPath = "app.exe" } }
        });

        var signer = new CapturingSigner();
        var pipeline = TestHelpers.TestPipeline();
        pipeline.SigningService = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password"));
        pipeline.MsixPackageService = new SuccessfulMsixPackageService();

        var result = pipeline.Run(project);

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors));
        signer.Requests.Select(r => Path.GetFileName(r.FilePath))
            .Should().Contain(new[] { "Setup-SignedMsix-1.0.0.exe", "ACME.SignedMsix.msix", "ACME.SignedMsix.appinstaller" });
        signer.Requests.Should().OnlyContain(r => r.CertificatePassword == "resolved-password");
        result.SigningEvidence.Select(e => e.ArtifactKind).Should().Contain(new[] { "exe", "msix", "appinstaller" });

        var reportText = File.ReadAllText(result.MsixCapabilityReportPath);
        reportText.Should().Contain("\"artifactKind\": \"msix\"");
        reportText.Should().Contain("\"artifactKind\": \"appinstaller\"");
        reportText.Should().Contain("\"signatureDigestAlgorithm\": \"SHA256\"");
        reportText.Should().Contain("\"timestamped\": true");
        reportText.Should().Contain("\"timestampOutagePolicy\": \"fail\"");
        reportText.Should().NotContain("resolved-password");
    }

    [Fact]
    public void SignToolVerificationParser_ExtractsSignerTimestampAndDigestEvidence()
    {
        var output = """
            Verifying: C:\Build\Setup.exe
            Hash of file (sha256): AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899

            Signing Certificate Chain:
                Issued to: CN=ACME Release
                Issued by: CN=ACME Root
                SHA1 hash: 11223344556677889900AABBCCDDEEFF00112233

            The signature is timestamped: Tue Sep 01 10:12:13 2026
            Timestamp Verified by:
                Issued to: CN=Trusted Timestamp Authority
                Issued by: CN=Trusted Timestamp Root
                SHA1 hash: AABBCCDDEEFF0011223344556677889900AABBCC
            """;

        var evidence = SignToolVerificationParser.Parse(output);

        evidence.SignatureDigestAlgorithm.Should().Be("SHA256");
        evidence.FileDigestSha256.Should().Be("AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899");
        evidence.CertificateSubject.Should().Be("CN=ACME Release");
        evidence.CertificateIssuer.Should().Be("CN=ACME Root");
        evidence.CertificateThumbprint.Should().Be("11223344556677889900AABBCCDDEEFF00112233");
        evidence.Timestamped.Should().BeTrue();
        evidence.TimestampDescription.Should().Be("Tue Sep 01 10:12:13 2026");
        evidence.TimestampCertificateSubject.Should().Be("CN=Trusted Timestamp Authority");
        evidence.TimestampCertificateIssuer.Should().Be("CN=Trusted Timestamp Root");
        evidence.TimestampCertificateThumbprint.Should().Be("AABBCCDDEEFF0011223344556677889900AABBCC");
    }

    [Fact]
    public void HttpRemoteAuthenticodeSigner_PostsHashWritesSignedArtifactAndExportsAuditEvents()
    {
        var artifact = Path.Combine(_tempDir, "Setup.exe");
        File.WriteAllText(artifact, "unsigned");
        var signedBytes = System.Text.Encoding.UTF8.GetBytes("signed");
        var handler = new CapturingHttpHandler("""
            {
              "success": true,
              "signedArtifactBase64": "c2lnbmVk",
              "providerVersion": "hsm-1.2.3",
              "certificateSubject": "CN=ACME Release",
              "certificateIssuer": "CN=ACME Root",
              "certificateThumbprint": "AA BB CC",
              "signatureDigestAlgorithm": "sha256",
              "timestamped": true,
              "timestampDescription": "2026-09-01T10:12:13Z",
              "verificationSummary": "signed by remote HSM"
            }
            """);
        var signer = new HttpRemoteAuthenticodeSigner(new HttpClient(handler));

        var result = signer.Sign(new AuthenticodeSigningRequest(
            artifact,
            CertificatePath: "",
            CertificatePassword: null,
            TimestampUrl: "https://timestamp.test",
            ExpectedSubject: "CN=ACME",
            RemoteProvider: "enterprise-hsm",
            RemoteEndpoint: "https://signing.example.test/api/sign",
            RemoteKeyId: "release-key",
            RemoteCredential: "remote-token"));

        result.Success.Should().BeTrue();
        File.ReadAllBytes(artifact).Should().Equal(signedBytes);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization?.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization?.Parameter.Should().Be("remote-token");
        handler.LastBody.Should().Contain("\"artifactSha256\"");
        handler.LastBody.Should().Contain("\"keyId\":\"release-key\"");
        result.ToolVersion.Should().Be("hsm-1.2.3");
        result.CertificateSubject.Should().Be("CN=ACME Release");
        result.CertificateThumbprint.Should().Be("AABBCC");
        result.AuditEvents.Select(e => e.EventType).Should().Contain(new[] { "remote-sign.requested", "remote-sign.completed" });
        result.AuditEvents.Should().OnlyContain(e => e.Provider == "enterprise-hsm" && e.KeyId == "release-key");
    }

    private sealed class CapturingSigner : IAuthenticodeSigner
    {
        public AuthenticodeSigningRequest? LastRequest { get; private set; }
        public List<AuthenticodeSigningRequest> Requests { get; } = new();

        public CodeSigningResult Sign(AuthenticodeSigningRequest request)
        {
            LastRequest = request;
            Requests.Add(request);
            return new CodeSigningResult
            {
                Success = true,
                VerificationSummary = "verified",
                ToolVersion = "10.0.26100.1",
                CertificateSubject = "CN=ACME Release",
                SignatureDigestAlgorithm = "SHA256",
                FileDigestSha256 = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                Timestamped = true,
                TimestampDescription = "Tue Sep 01 10:12:13 2026",
                TimestampCertificateSubject = "CN=Trusted Timestamp Authority",
                CertificateStoreName = request.StoreName,
                CertificateStoreLocation = request.StoreLocation,
                CertificateStoreThumbprint = request.StoreThumbprint,
                CertificateStoreSubject = request.StoreSubject,
                RemoteProvider = request.RemoteProvider,
                RemoteEndpoint = request.RemoteEndpoint,
                RemoteKeyId = request.RemoteKeyId,
                AuditEvents =
                {
                    new CodeSigningAuditEvent
                    {
                        EventType = string.IsNullOrWhiteSpace(request.RemoteEndpoint) ? "local-sign.completed" : "remote-sign.completed",
                        ArtifactPath = request.FilePath,
                        Provider = request.RemoteProvider ?? "",
                        Endpoint = request.RemoteEndpoint ?? "",
                        KeyId = request.RemoteKeyId ?? "",
                        Success = true
                    }
                }
            };
        }
    }

    private sealed class SuccessfulMsixPackageService : IMsixPackageService
    {
        public MsixResult Package(
            string payloadDir,
            string outputDir,
            string identity,
            string publisher,
            string displayName,
            string version,
            string? exeName = null,
            string description = "",
            string architecture = "x64",
            MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package,
            MsixAppInstallerOptions? appInstaller = null)
        {
            Directory.CreateDirectory(outputDir);
            var msixPath = Path.Combine(outputDir, $"{identity}{(packageKind == MsixRelatedPackageKind.Bundle ? ".msixbundle" : ".msix")}");
            File.WriteAllText(msixPath, "fake msix");

            var result = new MsixResult
            {
                Success = true,
                MsixPackagePath = msixPath,
                StagingDir = Path.Combine(outputDir, "stage"),
                ManifestPath = Path.Combine(outputDir, "stage", "AppxManifest.xml")
            };

            Directory.CreateDirectory(result.StagingDir);
            File.WriteAllText(result.ManifestPath, "<Package />");

            if (appInstaller is not null)
            {
                result.PackageUri = $"https://updates.example.test/msix/{Path.GetFileName(msixPath)}";
                result.AppInstallerUri = $"https://updates.example.test/msix/{identity}.appinstaller";
                result.AppInstallerPath = MsixPackager.GenerateAppInstaller(
                    Path.Combine(outputDir, $"{identity}.appinstaller"),
                    identity,
                    publisher,
                    version,
                    architecture,
                    result.PackageUri,
                    result.AppInstallerUri,
                    packageKind,
                    appInstaller.OptionalPackages,
                    appInstaller.HoursBetweenUpdateChecks,
                    appInstaller.ShowPrompt,
                    appInstaller.UpdateBlocksActivation,
                    appInstaller.ForceUpdateFromAnyVersion);
            }

            return result;
        }
    }

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

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly string _response;

        public CapturingHttpHandler(string response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = ReadBody(request);
            return Response();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = ReadBody(request);
            return Task.FromResult(Response());
        }

        private static string ReadBody(HttpRequestMessage request)
        {
            if (request.Content == null)
                return "";

            using var stream = request.Content.ReadAsStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private HttpResponseMessage Response()
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
    }
}
