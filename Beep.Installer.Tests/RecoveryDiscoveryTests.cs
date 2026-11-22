using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class RecoveryDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepRecovery_" + Guid.NewGuid().ToString("N"));
    private readonly List<InstallerController> _controllers = new();
    private readonly List<string> _drafts = new();
    public RecoveryDiscoveryTests() => Directory.CreateDirectory(_root);
    private InstallerController Controller()
    {
        var controller = new InstallerController(); controller.StopAutoSave(); _controllers.Add(controller);
        _drafts.Add(controller.SaveState.RecoveryPath); return controller;
    }
    public void Dispose()
    {
        foreach (var controller in _controllers) controller.StopAutoSave();
        foreach (var path in _drafts.Distinct()) if (File.Exists(path)) File.Delete(path);
        Directory.Delete(_root, true);
    }

    [Fact]
    public void IndependentUntitledSessionsDoNotOverwriteAndAreDiscoverable()
    {
        var first = Controller(); var second = Controller();
        first.Project.AppName = "First draft"; second.Project.AppName = "Second draft";
        first.WriteAutoSaveSnapshot(); second.WriteAutoSaveSnapshot();
        first.SaveState.RecoveryPath.Should().NotBe(second.SaveState.RecoveryPath);
        AutoSave.DiscoverUntitledDrafts().Should().Contain(first.SaveState.RecoveryPath).And.Contain(second.SaveState.RecoveryPath);
        InstallerScriptSerializer.Load(first.SaveState.RecoveryPath).project!.AppName.Should().Be("First draft");
        InstallerScriptSerializer.Load(second.SaveState.RecoveryPath).project!.AppName.Should().Be("Second draft");
    }

    [Fact]
    public void FreshControllerRecoversUntitledAndOnlySuccessfulSaveClearsItsDraft()
    {
        var original = Controller(); original.Project.AppVersion = "9.0.0"; original.WriteAutoSaveSnapshot();
        var path = original.SaveState.RecoveryPath;
        var recovered = Controller(); recovered.RecoverUntitled(path).ok.Should().BeTrue();
        _drafts.Add(recovered.SaveState.RecoveryPath);
        recovered.SaveState.RecoveryPath.Should().NotBe(path);
        recovered.Project.AppVersion.Should().Be("9.0.0"); recovered.HasFilePath.Should().BeFalse();
        recovered.IsDirty.Should().BeTrue(); recovered.SaveState.IsRecovered.Should().BeTrue();
        var blocked = Path.Combine(_root, "blocked"); File.WriteAllText(blocked, "occupied");
        recovered.SaveAs(Path.Combine(blocked, "project.bsetup")).ok.Should().BeFalse();
        File.Exists(path).Should().BeTrue();
        recovered.SaveAs(Path.Combine(_root, "project.bsetup")).ok.Should().BeTrue();
        File.Exists(path).Should().BeFalse(); recovered.SaveState.IsRecovered.Should().BeFalse();
        recovered.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void OpeningSavedVersionPreservesNewerRecoveryDraft()
    {
        var controller = Controller(); var saved = Path.Combine(_root, "saved.bsetup");
        controller.SaveAs(saved).ok.Should().BeTrue();
        controller.Project.AppVersion = "9.0.0"; controller.WriteAutoSaveSnapshot();
        var draft = controller.SaveState.RecoveryPath;
        File.SetLastWriteTimeUtc(draft, File.GetLastWriteTimeUtc(saved).AddSeconds(2));
        controller.Open(saved, recoverDraft: false).ok.Should().BeTrue();
        controller.Project.AppVersion.Should().Be("1.0.0"); controller.IsDirty.Should().BeFalse();
        File.Exists(draft).Should().BeTrue();
        controller.Open(saved, recoverDraft: true).ok.Should().BeTrue();
        controller.Project.AppVersion.Should().Be("9.0.0"); controller.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void InvalidUntitledRecoveryPreservesCurrentProjectAndFile()
    {
        var controller = Controller(); var project = controller.Project;
        var path = Path.Combine(_root, "invalid.bsetup"); File.WriteAllText(path, "not a valid installer script");
        controller.RecoverUntitled(path).ok.Should().BeFalse();
        controller.Project.Should().BeSameAs(project); File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CleanupFailureReportsSavedStateAndRetainsDraft()
    {
        var controller = Controller(); controller.Project.AppVersion = "2.0.0"; controller.WriteAutoSaveSnapshot();
        var path = controller.SaveState.RecoveryPath;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            controller.SaveAs(Path.Combine(_root, "saved.bsetup")).ok.Should().BeTrue();
            controller.IsDirty.Should().BeFalse(); controller.SaveState.CleanupError.Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeTrue();
        }
        controller.Save().ok.Should().BeTrue();
        controller.SaveState.CleanupError.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void CleanupRetryPreservesDraftChangedAfterFailedDeletion()
    {
        var controller = Controller(); controller.Project.AppVersion = "2.0.0"; controller.WriteAutoSaveSnapshot();
        var path = controller.SaveState.RecoveryPath;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            controller.SaveAs(Path.Combine(_root, "saved.bsetup")).ok.Should().BeTrue();
        File.WriteAllText(path, "newer work from another session");
        controller.Save().ok.Should().BeTrue();
        controller.SaveState.CleanupError.Should().NotBeNullOrEmpty();
        File.ReadAllText(path).Should().Be("newer work from another session");
    }

    [Fact]
    public void SavingRecoveredSessionPreservesChangedSourceDraft()
    {
        var original = Controller(); original.Project.AppVersion = "2.0.0"; original.WriteAutoSaveSnapshot();
        var path = original.SaveState.RecoveryPath;
        var recovered = Controller(); recovered.RecoverUntitled(path).ok.Should().BeTrue();
        _drafts.Add(recovered.SaveState.RecoveryPath);
        original.Project.AppVersion = "3.0.0"; original.WriteAutoSaveSnapshot();
        recovered.SaveAs(Path.Combine(_root, "saved.bsetup")).ok.Should().BeTrue();
        InstallerScriptSerializer.Load(path).project!.AppVersion.Should().Be("3.0.0");
        recovered.SaveState.CleanupError.Should().NotBeNullOrEmpty();
        InstallerScriptSerializer.Load(recovered.FilePath!).project!.AppVersion.Should().Be("2.0.0");
    }
}
