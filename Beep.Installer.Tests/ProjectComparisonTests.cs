using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ProjectComparisonTests
{
    private static InstallProject Project() => InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src");

    [Fact]
    public void ReviewHighlightsVersionDestinationsResourcesAndPackagingWithoutChangingEitherModel()
    {
        var before = Project(); var after = Project(); after.AppId = before.AppId;
        after.AppVersion = "2.0"; after.DefaultDirName = "{localappdata}\\App"; after.OutputFormat = InstallerOutputFormat.Msi;
        after.WindowsServices.Add(new() { Name = "Service", ExecutablePath = "{app}\\service.exe" });
        var beforeJson = JsonSerializer.Serialize(before); var afterJson = JsonSerializer.Serialize(after);
        var review = ProjectComparison.Create(before, after);
        review.Changes.Should().Contain(change => change.Path == "$.appVersion" && change.Category == ProjectChangeCategory.Identity && change.Before == "\"1.0\"" && change.After == "\"2.0\"");
        review.Changes.Should().Contain(change => change.Path == "$.defaultDirName" && change.Category == ProjectChangeCategory.Destinations);
        review.Changes.Should().Contain(change => change.Path == "$.outputFormat" && change.Category == ProjectChangeCategory.Package);
        review.Changes.Should().Contain(change => change.Path.StartsWith("$.windowsServices[") && change.Kind == ProjectChangeKind.Added);
        JsonSerializer.Serialize(before).Should().Be(beforeJson); JsonSerializer.Serialize(after).Should().Be(afterJson);
        after.AppVersion = "3.0"; review.Changes.Single(change => change.Path == "$.appVersion").After.Should().Be("\"2.0\"");
    }

    [Fact]
    public void CanonicalOrderingIgnoresListReorderingAndMissingValuesReportRemoval()
    {
        var before = Project(); var after = Project(); after.AppId = before.AppId;
        before.WindowsServices.Add(new() { Name = "B" }); before.WindowsServices.Add(new() { Name = "A" });
        after.WindowsServices.Add(new() { Name = "A" }); after.WindowsServices.Add(new() { Name = "B" });
        ProjectComparison.Create(before, after).Changes.Should().BeEmpty();
        after.WindowsServices.Clear();
        ProjectComparison.Create(before, after).Changes.Should().OnlyContain(change => change.Kind == ProjectChangeKind.Removed);
    }

    [Fact]
    public void CredentialsReferencesAndSensitivityDeclaredOnEitherSideAreExcluded()
    {
        var before = Project(); var after = Project(); after.AppId = before.AppId;
        before.CodeSignCertificatePassword = "original-pfx-secret"; after.CodeSignCertificatePassword = "changed-pfx-secret";
        before.CodeSignRemoteCredential = "env:ORIGINAL"; after.CodeSignRemoteCredential = "env:UPDATED";
        before.Resources.Add(new() { Id = "opaque", Type = "file.copy", Inputs = new() { ["opaque"] = "not-yet-marked-secret", ["destination"] = "old" } });
        after.Resources.Add(new() { Id = "opaque", Type = "file.copy", Inputs = new() { ["opaque"] = "replacement-secret", ["destination"] = "new" }, SensitiveInputs = new() { "opaque" } });
        before.WindowsServices.Add(new() { Name = "Service", Password = "service-secret" });
        after.WindowsServices.Add(new() { Name = "Service", Password = "dpapi:encrypted-secret", Description = "changed description" });
        var json = JsonSerializer.Serialize(ProjectComparison.Create(before, after));
        foreach (var credential in new[] { "original-pfx-secret", "changed-pfx-secret", "env:ORIGINAL", "env:UPDATED", "not-yet-marked-secret", "replacement-secret", "service-secret", "dpapi:encrypted-secret" })
            json.Should().NotContain(credential);
        json.Should().Contain("changed description").And.Contain("destination");
        ProjectComparison.Create(before, after).Changes.Should().NotContain(change => change.Path.StartsWith("$.codeSign"));
    }

    [Fact]
    public void ComparisonUsesTheExistingCanonicalExporterAndDiffContractForUnprotectedFields()
    {
        var before = Project(); var after = Project(); after.AppId = before.AppId; after.AppVersion = "2.0"; after.OutputDir = "release";
        var expected = ProjectAuthoringWorkspace.DiffCanonicalJson(ProjectCanonicalJsonExporter.ToJson(before), ProjectCanonicalJsonExporter.ToJson(after));
        var actual = ProjectComparison.Create(before, after);
        actual.Changes.Select(change => (change.Path, change.Before, change.After)).Should().Equal(expected.Select(change => (change.Path, change.CurrentValue, change.UpdatedValue)));
    }

    [Fact]
    public void ReaderUsesTheExistingSerializerAndDoesNotExposeItsErrorText()
    {
        var root = Path.Combine(Path.GetTempPath(), "p13_compare_reader_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "project.bsetup"); var project = Project(); project.WindowsServices.Add(new() { Name = "Service" });
            File.WriteAllText(path, InstallerScriptSerializer.Write(project));
            var read = ComparisonProjectReader.Read(path); read.Error.Should().Be(ComparisonProjectReadError.None);
            read.Project!.AppVersion.Should().Be(project.AppVersion); read.Project.WindowsServices.Should().ContainSingle();
            read.Project.SourceDirectory.Should().Be(Path.Combine(root, "src"));
            File.WriteAllText(path, "secret-invalid-project-content");
            var invalid = ComparisonProjectReader.Read(path); invalid.Project.Should().BeNull(); invalid.Error.Should().Be(ComparisonProjectReadError.Invalid);
            JsonSerializer.Serialize(invalid).Should().NotContain("secret-invalid-project-content");
            File.WriteAllText(path, ""); ComparisonProjectReader.Read(path).Error.Should().Be(ComparisonProjectReadError.Invalid);
            using (var stream = File.OpenWrite(path)) stream.SetLength(ComparisonProjectReader.MaximumBytes + 1L);
            ComparisonProjectReader.Read(path).Error.Should().Be(ComparisonProjectReadError.TooLarge);
            ComparisonProjectReader.Read(Path.Combine(root, "missing.bsetup")).Error.Should().Be(ComparisonProjectReadError.Unavailable);
        }
        finally { Directory.Delete(root, true); }
    }
}
