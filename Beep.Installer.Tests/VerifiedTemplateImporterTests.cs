using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class VerifiedTemplateImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepTemplateImportTests-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _rsa = RSA.Create(2048);
    private string PublicKey => Path.Combine(_root, "public.pem");
    public VerifiedTemplateImporterTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(PublicKey, _rsa.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(_root, "private.pem"), _rsa.ExportPkcs8PrivateKeyPem());
    }
    public void Dispose()
    {
        _rsa.Dispose();
        foreach (var dir in Directory.EnumerateDirectories(_root)) { foreach (var file in Directory.EnumerateFiles(dir)) File.Delete(file); Directory.Delete(dir); }
        foreach (var file in Directory.EnumerateFiles(_root)) File.Delete(file); Directory.Delete(_root);
    }
    private string Export(string template = ProjectTemplates.ConsoleId)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        ProjectTemplatePackageService.Export(new() { TemplateId = template, ProductName = "Actual signed sample", Publisher = "Signed publisher",
            OutputDirectory = path, SigningPrivateKeyPath = Path.Combine(_root, "private.pem") }).Success.Should().BeTrue();
        return path;
    }
    private void Resign(string path, Action<JsonObject>? changeProject = null, Action<JsonObject>? changeManifest = null)
    {
        var projectPath = Path.Combine(path, ProjectTemplatePackageService.ProjectFileName);
        if (changeProject is not null)
        { var project = JsonNode.Parse(File.ReadAllText(projectPath))!.AsObject(); changeProject(project); File.WriteAllText(projectPath, project.ToJsonString()); }
        var manifestPath = Path.Combine(path, ProjectTemplatePackageService.ManifestFileName);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["ProjectSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(projectPath))).ToLowerInvariant();
        changeManifest?.Invoke(manifest);
        var text = manifest.ToJsonString(); File.WriteAllText(manifestPath, text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, ProjectTemplatePackageService.SignatureFileName), "rsa-sha256:" +
            Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }
    [Theory]
    [InlineData(ProjectTemplates.EmptyId)] [InlineData(ProjectTemplates.ConsoleId)] [InlineData(ProjectTemplates.WinFormsId)]
    [InlineData(ProjectTemplates.WpfId)] [InlineData(ProjectTemplates.ServiceId)]
    public void ExportedSignedTemplatesReconstructTheirActualDetachedContent(string id)
    {
        var path = Export(id); var result = VerifiedTemplateImporter.Import(path, PublicKey);
        var canonical = File.ReadAllText(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var reconstructed = JsonSerializer.Deserialize<Beep.Installer.Models.InstallProject>(canonical, options)!;
        var reconstructedJson = ProjectCanonicalJsonExporter.ToJson(reconstructed);
        ProjectAuthoringWorkspace.DiffCanonicalJson(canonical, reconstructedJson).Should().BeEmpty("canonical reconstruction must retain the fixture graph");
        var persisted = InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(reconstructed), reconstructed.ProjectName).project!;
        ProjectAuthoringWorkspace.DiffCanonicalJson(canonical, ProjectCanonicalJsonExporter.ToJson(persisted)).Should().BeEmpty("the script must retain the fixture graph");
        result.Error.Should().Be(TemplateImportError.None); result.Blueprint.Should().NotBeNull();
        var blueprint = result.Blueprint!; var project = blueprint.CreateProject();
        JsonElement.DeepEquals(JsonDocument.Parse(ProjectCanonicalJsonExporter.ToJson(project)).RootElement,
            JsonDocument.Parse(File.ReadAllText(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName))).RootElement).Should().BeTrue();
        project.AppName.Should().Be("Actual signed sample"); project.AppPublisher.Should().Be("Signed publisher");
        project.AppName = "edited"; blueprint.CreateProject().AppName.Should().Be("Actual signed sample");
        File.WriteAllText(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName), "changed after review");
        blueprint.CreateProject().AppName.Should().Be("Actual signed sample"); blueprint.TrustedKeySha256.Should().HaveLength(64);
    }
    [Fact]
    public void ValidSignatureDoesNotAuthorizeReplacingContentWithTheNamedBuiltin()
    {
        var path = Export(); Resign(path, project => { project["appName"] = "Custom packaged content"; project["appPublisher"] = "Custom issuer";
            project["components"]![0]!["name"] = "Signed component title";
            project["components"]![0]!["description"] = "Actual packaged component"; });
        var imported = VerifiedTemplateImporter.Import(path, PublicKey);
        imported.Error.Should().Be(TemplateImportError.None);
        imported.Blueprint!.CreateProject().AppName.Should().Be("Custom packaged content");
        imported.Blueprint.CreateProject().Components.Single().Name.Should().Be("Signed component title");
        imported.Blueprint.CreateProject().Components.Single().Description.Should().Be("Actual packaged component");
    }
    [Fact]
    public void TamperedOrWrongKeyPackagesCannotProduceBlueprints()
    {
        var path = Export(); File.AppendAllText(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName), " ");
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Untrusted));
        path = Export(); using var other = RSA.Create(2048); File.WriteAllText(PublicKey, other.ExportSubjectPublicKeyInfoPem());
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Untrusted));
    }
    [Theory]
    [InlineData("../outside.json")] [InlineData("C:\\outside.json")] [InlineData("folder/file.json")]
    public void SignedManifestCannotReadOutsideThePackage(string projectFile)
    {
        var path = Export(); Resign(path, changeManifest: manifest => manifest["ProjectFile"] = projectFile);
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Invalid));
    }
    [Fact]
    public void UnsupportedSignedFieldsAreRejectedWithoutDataLoss()
    {
        var path = Export(); Resign(path, project => project["futureFeature"] = new JsonObject());
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.UnsupportedContent));
    }
    [Fact]
    public void OversizeAndMissingInputsReturnTypedErrors()
    {
        var path = Export(); using (var file = File.OpenWrite(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName))) file.SetLength(VerifiedTemplateImporter.MaximumProjectBytes + 1L);
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.TooLarge));
        VerifiedTemplateImporter.Import(Path.Combine(_root, "missing"), PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Unavailable));
    }

    [Fact]
    public void ExplicitEmptyComponentsAndDisplayNamesSurviveSaveWithoutChangingLegacyDefaults()
    {
        var empty = ProjectTemplates.Create(ProjectTemplates.EmptyId, "Empty", "1.0.0", "Publisher", "");
        InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(empty)).project!.Components.Should().BeEmpty();
        InstallerScriptSerializer.Read("[Setup]\nAppName=Legacy\n").project!.Components.Should().ContainSingle();
        var project = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Custom", "1.0.0", "Publisher", "");
        project.Components.Single().Name = "Distinct display name";
        project.OutputBaseFilename = "Setup-Custom-1.2.3";
        InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project)).project!.Components.Single().Name.Should().Be("Distinct display name");
        InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(project)).project!.OutputBaseFilename.Should().Be("Setup-Custom-1.2.3");
    }

    [Fact]
    public void DuplicateOrFutureManifestCannotProduceBlueprint()
    {
        var path = Export(); var manifestPath = Path.Combine(path, ProjectTemplatePackageService.ManifestFileName);
        var manifest = File.ReadAllText(manifestPath); File.WriteAllText(manifestPath, manifest.Insert(manifest.IndexOf('{') + 1, "\"Name\":\"duplicate\","));
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Invalid));
        path = Export(); Resign(path, changeManifest: node => node["SchemaVersion"] = "2.0");
        VerifiedTemplateImporter.Import(path, PublicKey).Should().Be(new TemplateImportResult(null, TemplateImportError.Invalid));
    }

    private TemplateLibraryStore Library() => new(Path.Combine(_root, "library.json"));

    [Fact]
    public void FreshLibraryReverifiesPinnedSourcesWithoutSavingProjectOrKeyContents()
    {
        var path = Export(); var blueprint = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
        Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.None);
        var json = File.ReadAllText(Path.Combine(_root, "library.json")); json.Should().NotContain("BEGIN").And.NotContain("components").And.NotContain("private.pem");
        var discovered = Library().Discover(); discovered.Error.Should().Be(TemplateLibraryError.None); discovered.RejectedEntries.Should().Be(0);
        discovered.Templates.Should().ContainSingle().Which.Blueprint!.CreateProject().AppName.Should().Be("Actual signed sample");
        discovered.Templates.Single().Blueprint!.ManifestSha256.Should().Be(blueprint.ManifestSha256);
    }

    [Theory]
    [InlineData("project")] [InlineData("manifest")] [InlineData("key")] [InlineData("missing")]
    public void ChangedEvenValidlySignedSourcesRequireExplicitReimport(string change)
    {
        var path = Export(); var blueprint = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
        Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.None);
        var before = File.ReadAllText(Path.Combine(_root, "library.json"));
        if (change == "project") Resign(path, project => project["appName"] = "New reviewed content");
        if (change == "manifest") Resign(path, changeManifest: manifest => manifest["Name"] = "New signed label");
        if (change == "key") { using var other = RSA.Create(2048); File.WriteAllText(PublicKey, other.ExportSubjectPublicKeyInfoPem()); }
        if (change == "missing") File.Delete(Path.Combine(path, ProjectTemplatePackageService.ProjectFileName));
        var discovered = Library().Discover(); discovered.RejectedEntries.Should().Be(1); discovered.Templates.Should().BeEmpty();
        File.ReadAllText(Path.Combine(_root, "library.json")).Should().Be(before);
        if (change is "project" or "manifest")
        {
            var reviewed = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
            Library().Remember(path, PublicKey, reviewed).Should().Be(TemplateLibraryError.None);
            Library().Discover().Templates.Should().ContainSingle(); Library().Read().Entries.Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("{\"schemaVersion\":2,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[],\"schemaVersion\":1}")]
    public void InvalidLibraryCannotBeSilentlyOverwritten(string json)
    {
        File.WriteAllText(Path.Combine(_root, "library.json"), json); var path = Export(); var blueprint = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
        Library().Read().Error.Should().Be(TemplateLibraryError.Invalid); Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.Invalid);
        File.ReadAllText(Path.Combine(_root, "library.json")).Should().Be(json); Library().Discover().Templates.Should().BeEmpty();
    }

    [Fact]
    public void FailedLibraryReplacementPreservesDataAndCleansOwnedTemporaryFile()
    {
        var path = Export(); var blueprint = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
        Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.None);
        var catalogPath = Path.Combine(_root, "library.json"); var original = File.ReadAllText(catalogPath);
        using (var held = new FileStream(catalogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.Unavailable);
        File.ReadAllText(catalogPath).Should().Be(original); Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void OversizedSavedLibraryIsPreservedAndCannotBeOverwritten()
    {
        var catalog = Path.Combine(_root, "library.json"); using (var file = File.Create(catalog)) file.SetLength(256 * 1024 + 1);
        var path = Export(); var blueprint = VerifiedTemplateImporter.Import(path, PublicKey).Blueprint!;
        Library().Read().Error.Should().Be(TemplateLibraryError.Invalid); Library().Remember(path, PublicKey, blueprint).Should().Be(TemplateLibraryError.Invalid);
        new FileInfo(catalog).Length.Should().Be(256 * 1024 + 1); Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();
    }
}
