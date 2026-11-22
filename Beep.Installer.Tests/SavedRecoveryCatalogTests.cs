using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SavedRecoveryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SavedRecovery_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _sessions = new();
    private readonly List<InstallerController> _controllers = new();
    public SavedRecoveryCatalogTests() => Directory.CreateDirectory(_root);
    private InstallerController Controller()
    {
        var controller = new InstallerController(); controller.StopAutoSave(); _controllers.Add(controller); return controller;
    }
    public void Dispose()
    {
        foreach (var controller in _controllers) controller.StopAutoSave();
        foreach (var session in _sessions) AutoSave.ClearRecoveryFile(session);
        Directory.Delete(_root, true);
    }
    private string SavedDraft(bool missingOriginal = false)
    {
        var document = Path.Combine(_root, "app.bsetup");
        var project = InstallerProjectFactory.CreateNew("Saved project", "1.0.0", "Publisher", "relative-source");
        InstallerScriptSerializer.Save(project, document).ok.Should().BeTrue(); project.AppVersion = "2.0.0";
        InstallerScriptSerializer.Save(project, AutoSave.AutoSavePath(document)).ok.Should().BeTrue();
        File.SetLastWriteTimeUtc(AutoSave.AutoSavePath(document), File.GetLastWriteTimeUtc(document).AddSeconds(2));
        if (missingOriginal) File.Delete(document);
        return document;
    }

    [Fact]
    public void CatalogDeduplicatesSavedPathsAndOmitsOlderDrafts()
    {
        var document = SavedDraft();
        var entries = AutoSave.DiscoverDrafts(new[] { document, document.ToUpperInvariant() });
        entries.Where(entry => entry.DocumentPath?.Equals(document, StringComparison.OrdinalIgnoreCase) == true).Should().ContainSingle();
        File.SetLastWriteTimeUtc(AutoSave.AutoSavePath(document), File.GetLastWriteTimeUtc(document).AddSeconds(-2));
        AutoSave.DiscoverDrafts(new[] { document }).Should().NotContain(entry => entry.DocumentPath == document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDraftRecoveryRetainsOriginalIdentityAndResolvesPathsEvenWhenOriginalIsMissing(bool missing)
    {
        var document = SavedDraft(missing); var originalDraft = AutoSave.AutoSavePath(document);
        var controller = Controller();
        controller.RecoverDraft(AutoSave.DiscoverDrafts(new[] { document }).Single(entry => entry.DocumentPath == document)).ok.Should().BeTrue();
        var session = controller.SaveState.RecoveryPath; _sessions.Add(session);
        controller.FilePath.Should().Be(document); controller.IsDirty.Should().BeTrue();
        controller.Project.SourceDirectory.Should().Be(Path.Combine(_root, "relative-source"));
        session.Should().NotBe(originalDraft); File.Exists(originalDraft).Should().BeTrue();
        controller.Project.AppVersion = "3.0.0"; controller.WriteAutoSaveSnapshot();
        InstallerScriptSerializer.Load(originalDraft).project!.AppVersion.Should().Be("2.0.0");
        var discoveredSession = AutoSave.DiscoverDrafts(Array.Empty<string>()).Single(entry => entry.RecoveryPath == session);
        discoveredSession.DocumentPath.Should().Be(document);
        var restarted = Controller(); restarted.RecoverDraft(discoveredSession).ok.Should().BeTrue(); _sessions.Add(restarted.SaveState.RecoveryPath);
        restarted.FilePath.Should().Be(document); restarted.Project.AppVersion.Should().Be("3.0.0");
        restarted.Save().ok.Should().BeTrue();
        InstallerScriptSerializer.Load(document).project!.AppVersion.Should().Be("3.0.0");
        File.Exists(session).Should().BeFalse(); File.Exists(session + ".origin.json").Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData(null)]
    public void InvalidOriginMetadataIsDisclosedAndCannotChangeCurrentProject(string? invalidMetadata)
    {
        var document = SavedDraft(); var controller = Controller();
        controller.RecoverDraft(new(AutoSave.AutoSavePath(document), document)).ok.Should().BeTrue();
        var session = controller.SaveState.RecoveryPath; _sessions.Add(session);
        if (invalidMetadata is null) File.Delete(session + ".origin.json");
        else File.WriteAllText(session + ".origin.json", invalidMetadata);
        var entry = AutoSave.DiscoverDrafts(Array.Empty<string>()).Single(entry => entry.RecoveryPath == session);
        entry.DiscoveryError.Should().NotBeNullOrEmpty();
        var fresh = Controller(); var current = fresh.Project;
        fresh.RecoverDraft(entry).ok.Should().BeFalse(); fresh.Project.Should().BeSameAs(current);
        File.Exists(session).Should().BeTrue();
    }
}
