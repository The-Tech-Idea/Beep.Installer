using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Extensibility;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Authoring a typed resource without knowing the provider's dictionary keys.
///
/// The rules live in <see cref="ResourceDraftBuilder"/> rather than in the dialog, so they are
/// exercised directly here — no form, no message loop, and nothing reaching past <c>private</c>.
///
/// The test that matters most is <see cref="AnOperationBuiltFromADraftIsAcceptedByItsOwnProvider"/>:
/// it hands what the wizard produces to the real provider's own validation. Everything else could be
/// self-consistent and still author operations the executor rejects.
/// </summary>
public class ResourceDraftTests
{
    private static ResourceDraft Shortcut()
    {
        var draft = ResourceDraftBuilder.CreateDefault("shortcut.create");
        draft.Values["name"] = "Contoso Suite";
        draft.Values["targetPath"] = @"{app}\Contoso.exe";
        return draft;
    }

    [Fact]
    public void ADefaultDraftCarriesTheCatalogDefaults()
    {
        var draft = ResourceDraftBuilder.CreateDefault("file.copy");

        draft.Type.Should().Be("file.copy");
        draft.Value("overwrite").Should().Be("true", "the catalog defaults it on");
        draft.Value("skipIfNewer").Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownTypeIsRefusedRatherThanProducingAnEmptyDraft()
    {
        var act = () => ResourceDraftBuilder.CreateDefault("not.a.provider");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AMissingRequiredInputIsReportedAgainstItsOwnField()
    {
        var draft = ResourceDraftBuilder.CreateDefault("shortcut.create");

        var problems = ResourceDraftBuilder.Validate(draft);

        problems.Should().Contain(p => p.Key == "name");
        problems.Should().Contain(p => p.Key == "targetPath");
    }

    [Fact]
    public void ACompleteDraftHasNothingToReport()
        => ResourceDraftBuilder.Validate(Shortcut()).Should().BeEmpty();

    [Fact]
    public void AnIntegerFieldWillNotTakeText()
    {
        var draft = ResourceDraftBuilder.CreateDefault("package.install");
        draft.Values["id"] = "vcredist";
        draft.Values["retryCount"] = "twice";

        ResourceDraftBuilder.Validate(draft).Should().Contain(p => p.Key == "retryCount");
    }

    [Fact]
    public void AChoiceFieldWillNotTakeSomethingTheProviderWouldReject()
    {
        var draft = Shortcut();
        draft.Values["location"] = "Dock";        // not a ShortcutLocation

        var problems = ResourceDraftBuilder.Validate(draft);

        problems.Should().Contain(p => p.Key == "location");
        problems.Single(p => p.Key == "location").Message.Should().Contain("StartMenu",
            "the message should say what is allowed");
    }

    [Fact]
    public void BuildingDropsEmptyValuesRatherThanWritingBlanks()
    {
        // Providers distinguish "not supplied" from "supplied as empty", and several treat the
        // latter as an explicit override.
        var operation = ResourceDraftBuilder.Build(Shortcut(), "shortcut.create:1");

        operation.Inputs.Should().ContainKey("name");
        operation.Inputs.Should().NotContainKey("arguments", "nothing was typed there");
    }

    [Fact]
    public void SecretsAreRecordedAsSensitive()
    {
        // SensitiveInputs is what keeps a password out of logs and evidence files. Nothing populated
        // it before, so every secret authored through the raw dictionary grid was logged in clear.
        var draft = ResourceDraftBuilder.CreateDefault("service.install");
        draft.Values["name"] = "ContosoSvc";
        draft.Values["executablePath"] = @"{app}\svc.exe";
        draft.Values["password"] = "hunter2";

        var operation = ResourceDraftBuilder.Build(draft, "service.install:1");

        operation.SensitiveInputs.Should().Contain("password");
        operation.SensitiveInputs.Should().NotContain("name");
    }

    [Fact]
    public void ARepeatableGroupIsWrittenWithItsCount()
    {
        var draft = ResourceDraftBuilder.CreateDefault("iis.site");
        draft.Values["name"] = "Contoso";
        draft.Values["physicalPath"] = @"{app}\www";

        ResourceInputCatalog.TryGet("iis.site", out var descriptor).Should().BeTrue();
        var bindings = descriptor.Repeatables.Single();

        var row = ResourceDraftBuilder.CreateRow(bindings);
        row["protocol"] = "https";
        row["port"] = "443";
        draft.Rows("binding").Add(row);

        var operation = ResourceDraftBuilder.Build(draft, "iis.site:1");

        operation.Inputs["bindingCount"].Should().Be("1");
        operation.Inputs["binding.0.protocol"].Should().Be("https");
        operation.Inputs["binding.0.port"].Should().Be("443");
    }

    [Fact]
    public void ARoundTripThroughAnOperationKeepsEveryAnswer()
    {
        // The wizard edits existing operations, not only new ones.
        var draft = ResourceDraftBuilder.CreateDefault("iis.site");
        draft.Values["name"] = "Contoso";
        draft.Values["physicalPath"] = @"{app}\www";

        ResourceInputCatalog.TryGet("iis.site", out var descriptor).Should().BeTrue();
        var row = ResourceDraftBuilder.CreateRow(descriptor.Repeatables.Single());
        row["protocol"] = "https";
        row["host"] = "contoso.example";
        draft.Rows("binding").Add(row);

        var operation = ResourceDraftBuilder.Build(draft, "iis.site:1");
        var reloaded = ResourceDraftBuilder.FromOperation(operation);

        reloaded.Value("name").Should().Be("Contoso");
        reloaded.Value("physicalPath").Should().Be(@"{app}\www");
        reloaded.Rows("binding").Should().HaveCount(1);
        reloaded.Rows("binding")[0]["host"].Should().Be("contoso.example");
    }

    [Fact]
    public void IdsDoNotCollideWithWhatIsAlreadyThere()
    {
        var existing = new[]
        {
            ResourceDraftBuilder.Build(Shortcut(), "shortcut.create:1"),
            ResourceDraftBuilder.Build(Shortcut(), "shortcut.create:2"),
        };

        ResourceDraftBuilder.NextId("shortcut.create", existing).Should().Be("shortcut.create:3");
    }

    [Theory]
    [InlineData("file.copy")]
    [InlineData("shortcut.create")]
    [InlineData("registry.write")]
    [InlineData("environment.set")]
    [InlineData("component.select")]
    public void AnOperationBuiltFromADraftIsAcceptedByItsOwnProvider(string type)
    {
        // The end-to-end claim: what the wizard produces, the provider accepts. A catalog that named
        // the wrong key, or marked the wrong field required, would pass every other test here and
        // fail this one.
        // Real paths on disk: several providers check that a source actually exists, which is correct
        // behaviour and would otherwise be mistaken here for a catalog fault.
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BeepRes_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var file = System.IO.Path.Combine(root, "Contoso.exe");
        System.IO.File.WriteAllText(file, "app");

        try
        {

        var draft = ResourceDraftBuilder.CreateDefault(type);
        FillRequired(draft, type, file, root);

        ResourceDraftBuilder.Validate(draft).Should().BeEmpty("the draft should be complete");

        var operation = ResourceDraftBuilder.Build(draft, $"{type}:1");

        var provider = BuiltInInstallerResourceProviders.CreateDefaultRegistry()
            .Providers.Single(p => p.ResourceType == type);

        var result = provider.Validate(operation, new ResourceProviderContext
        {
            DryRun = true,
            InstallRoot = root,               // a context requirement, not an authored input
            ProductName = "Contoso",
            ProductVersion = "1.0.0",
        });

        result.Code.Should().NotBe(ResourceProviderResultCode.Failed,
            $"the provider rejected what the wizard built: {result.Message}");

        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>Plausible values for whatever the catalog marks required.</summary>
    private static void FillRequired(ResourceDraft draft, string type, string existingFile, string existingFolder)
    {
        ResourceInputCatalog.TryGet(type, out var descriptor).Should().BeTrue();

        foreach (var input in descriptor.Inputs.Where(i => i.Required))
        {
            draft.Values[input.Key] = input.Kind switch
            {
                ResourceInputKind.FilePath => existingFile,
                ResourceInputKind.FolderPath => existingFolder,
                ResourceInputKind.Integer => "1",
                ResourceInputKind.Bool => "true",
                ResourceInputKind.Choice => input.Options.First(o => o.Length > 0),
                _ => Plausible(type, input.Key),
            };
        }
    }

    private static string Plausible(string type, string key) => (type, key) switch
    {
        ("registry.write", "keyPath") => @"Software\Contoso",   // hive-relative: the scope picks the hive
        ("file-association.register", "extension") => ".contoso",
        _ => "Contoso",
    };
}
